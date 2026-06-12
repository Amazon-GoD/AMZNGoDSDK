#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using UnityEngine;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// C# bridge for the native ExoPlayer overlay (<c>com.amzngod.exoplayer.CrossPromoExoOverlay</c>),
    /// used when <see cref="VideoPlayerBackend.ExoPlayer"/> is selected.
    ///
    /// <para>The native side renders the video (ExoPlayer's own <c>StyledPlayerView</c>) plus the
    /// CTA / close buttons and countdown on top of the Unity surface. This bridge keeps all
    /// analytics, Adjust/tracking, redirect, cooldown and show-count logic on the C# side and
    /// only reacts to native callbacks delivered via <c>UnitySendMessage</c>.</para>
    /// </summary>
    public class CrossPromoExoNativeOverlay : MonoBehaviour
    {
        private const string NativeClass = "com.amzngod.exoplayer.CrossPromoExoOverlay";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _native;
#endif

        private PromoConfiguration _config;
        private Action _onClose;
        private Action _onCTA;
        private Action _onCompleted;
        private bool _isVisible;
        private bool _ctaClicked;

        public bool IsVisible => _isVisible;

        /// <summary>
        /// Shows the native overlay for the given promo. <paramref name="onCompleted"/> fires when
        /// the video reaches its end (used for rewarded). <paramref name="onClose"/> fires when the
        /// user closes the overlay (or on a non-recoverable error / unsupported platform).
        /// </summary>
        public void Show(PromoConfiguration config, Action onClose, Action onCTA, Action onCompleted)
        {
            if (config == null)
            {
                onClose?.Invoke();
                return;
            }

            string url = ResolveUrl(config);
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[CrossPromoExoNativeOverlay] No video URL in config.");
                onClose?.Invoke();
                return;
            }

            _config = config;
            _onClose = onClose;
            _onCTA = onCTA;
            _onCompleted = onCompleted;
            _isVisible = true;
            _ctaClicked = false;

            ReportImpression(config);

            string ctaText = !string.IsNullOrWhiteSpace(config.ButtonText) ? config.ButtonText : "Install";
            int ctaDelay = Mathf.Max(0, config.OverlayShowDelayInSeconds);

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _native = new AndroidJavaObject(NativeClass);
                _native.Call("init", gameObject.name);
                _native.Call("show", url, ctaText, ctaDelay, false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CrossPromoExoNativeOverlay] Failed to show native overlay: {e}");
                _isVisible = false;
                _onClose?.Invoke();
            }
#else
            Debug.LogWarning("[CrossPromoExoNativeOverlay] ExoPlayer backend is Android-only. Closing immediately.");
            _isVisible = false;
            _onClose?.Invoke();
#endif
        }

        public void SetMute(bool mute)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("setMuted", mute); }
            catch (Exception e) { Debug.LogWarning($"[CrossPromoExoNativeOverlay] setMuted failed: {e.Message}"); }
#endif
        }

        /// <summary>Best-effort dismiss of the native overlay and cleanup of bridge state.</summary>
        public void Hide()
        {
            if (!_isVisible) return;
            _isVisible = false;

            var cb = _onClose;
            _onClose = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("dismiss"); }
            catch (Exception e) { Debug.LogWarning($"[CrossPromoExoNativeOverlay] dismiss failed: {e.Message}"); }
            _native = null;
#endif

            cb?.Invoke();
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("dismiss"); } catch (Exception) { }
            _native = null;
#endif
        }

        // Когда поверх плеера открывается внешнее приложение (Amazon-стор по клику), Unity
        // уходит в фон → ставим видео на паузу; при возврате — возобновляем.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (!_isVisible) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (pauseStatus)
                {
                    _native?.Call("pause");
                }
                else
                {
                    _native?.Call("resume");
                    _ctaClicked = false;   // вернулись из стора — CTA снова кликабелен
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CrossPromoExoNativeOverlay] pause/resume failed: {e.Message}");
            }
#endif
        }

        #region Native callbacks (UnitySendMessage)

        // Invoked from CrossPromoExoOverlay.java via UnityPlayer.UnitySendMessage.

        private void OnExoOverlayCompleted(string _)
        {
            _onCompleted?.Invoke();
        }

        private void OnExoOverlayCta(string _)
        {
            // Полноэкранный клик-слой ловит повторные тапы — дедупим в пределах одного
            // фореграунд-сеанса (иначе случайный мультитап шлёт дубли
            // video_click/cp_click/Adjust/OpenURL). Флаг сбрасывается при возврате из стора
            // (OnApplicationPause(false)), поэтому после возврата клик снова доступен.
            if (_ctaClicked) return;
            _ctaClicked = true;

            if (_config != null)
                HandleCtaClick(_config);

            _onCTA?.Invoke();
        }

        private void OnExoOverlayClosed(string _)
        {
            // Native side already removed its views before sending this; Hide() just
            // clears bridge state and fires the close callback.
            Hide();
        }

        private void OnExoOverlayError(string message)
        {
            Debug.LogError($"[CrossPromoExoNativeOverlay] Native overlay error: {message}");
            Hide();
        }

        #endregion

        #region Analytics / Tracking (mirrors CrossPromoVideoOverlay)

        private void ReportImpression(PromoConfiguration config)
        {
            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;

            var data = new BannerData(config.Title, null, config.RedirectUrl, config.TrackingUrl, paidAppId);
            CrossPromoAnalytics.ReportVideoShow(data);
            CrossPromoModule.Instance?.TrackImpression(paidAppId);

            IncrementShowCount(config);

            string impressionUrl = CrossPromoAdjustTracking.BuildImpressionUrl(config);
            if (!string.IsNullOrWhiteSpace(impressionUrl))
                StartCoroutine(CrossPromoAdjustTracking.SendGet(impressionUrl));
        }

        private void HandleCtaClick(PromoConfiguration config)
        {
            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;

            var data = new BannerData(config.Title, null, config.RedirectUrl, config.TrackingUrl, paidAppId);
            CrossPromoAnalytics.ReportVideoClick(data);
            CrossPromoModule.Instance?.TrackClick(paidAppId);

            // Open the redirect first; tracking is fire-and-forget on the module (which
            // outlives this bridge) so a close triggered by the CTA can't kill it.
            if (!string.IsNullOrWhiteSpace(config.RedirectUrl))
                Application.OpenURL(config.RedirectUrl);

            var module = CrossPromoModule.Instance;
            if (module != null)
                module.StartCoroutine(SendClickTracking(config));
        }

        private static IEnumerator SendClickTracking(PromoConfiguration config)
        {
            yield return CrossPromoAdjustTracking.SendGet(CrossPromoAdjustTracking.BuildClickUrl(config));

            if (CrossPromoAdjustTracking.IsHttpUrl(config.TrackingUrl))
            {
                using var request = UnityEngine.Networking.UnityWebRequest.Get(config.TrackingUrl);
                yield return request.SendWebRequest();
            }
            else if (!string.IsNullOrWhiteSpace(config.TrackingUrl))
            {
                Debug.LogWarning($"[CrossPromoExoNativeOverlay] Skipping non-http(s) TrackingUrl: {config.TrackingUrl}");
            }
        }

        private static void IncrementShowCount(PromoConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.Title)) return;

            int count = PlayerPrefs.GetInt(config.Title, 0);
            PlayerPrefs.SetInt(config.Title, count + 1);
            PlayerPrefs.Save();
            VideoCooldownRegistry.RecordShown(config.Title);
        }

        #endregion

        #region URL resolution (mirrors CrossPromoModule.ResolvePromoUrl)

        private static string ResolveUrl(PromoConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.VideoUrl))
                return config.VideoUrl;

            if (!string.IsNullOrWhiteSpace(config.FileName))
            {
                string ext = config.FileExtension.ToString();
                string fileName = config.FileName.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
                    ? config.FileName
                    : $"{config.FileName}.{ext}";
                return System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            }

            return null;
        }

        #endregion
    }
}
#endif
