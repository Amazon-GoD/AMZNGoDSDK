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

        /// <summary>Потолок ожидания отправки клика перед открытием стора.</summary>
        private const float ClickTrackingTimeoutSeconds = 1.5f;

        /// <summary>
        /// Сколько ждём первого кадра (нативный <c>OnExoOverlayStarted</c>) после запроса показа.
        /// Не дождались — считаем показ провалившимся с reason=load_timeout. Плеер в этом случае
        /// эксепшн не кидает (просто молчит), поэтому детектим только по таймеру.
        /// </summary>
        private const float LoadTimeoutSeconds = 10f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _native;
#endif

        private PromoConfiguration _config;
        private Action _onClose;
        private Action _onCTA;
        private Action _onCompleted;
        private bool _isVisible;
        private bool _ctaClicked;

        // Первый кадр отрисован (нативный OnExoOverlayStarted) — гасит load-таймаут.
        private bool _started;
        // Один error-ивент на показ: защита от гонки «таймаут vs onPlayerError» и повторных ошибок.
        private bool _errorReported;
        private Coroutine _loadTimeoutCo;

        // Универсальный мут звука игры на время показа рекламы.
        private bool _gameAudioMuted;
        private float _savedAudioVolume;

        public bool IsVisible => _isVisible;

        /// <summary>
        /// Shows the native overlay for the given promo. <paramref name="onCompleted"/> fires when
        /// the video reaches its end (used for rewarded). <paramref name="onClose"/> fires when the
        /// user closes the overlay (or on a non-recoverable error / unsupported platform).
        /// </summary>
        public void Show(PromoConfiguration config, Action onClose, Action onCTA, Action onCompleted)
        {
            _started = false;
            _errorReported = false;

            if (config == null)
            {
                ReportError("no_config", null, null);
                onClose?.Invoke();
                return;
            }

            string url = ResolveUrl(config);
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[CrossPromoExoNativeOverlay] No video URL in config.");
                ReportError("no_url", null, BuildBannerData(config));
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
                // Глушим звук игры до показа: ролик звучит через нативный ExoPlayer (мимо
                // AudioListener), поэтому реклама слышна, а игра — нет.
                MuteGameAudio();

                _native = new AndroidJavaObject(NativeClass);
                _native.Call("init", gameObject.name);
                _native.Call("show", url, ctaText, ctaDelay, false);

                // Сторож зависшего показа: ждём первого кадра, иначе load_timeout.
                StartLoadTimeout();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CrossPromoExoNativeOverlay] Failed to show native overlay: {e}");
                RestoreGameAudio();
                _isVisible = false;
                ReportError("native_init_exception", null, BuildBannerData(config));
                _onClose?.Invoke();
            }
#else
            Debug.LogWarning("[CrossPromoExoNativeOverlay] ExoPlayer backend is Android-only. Closing immediately.");
            _isVisible = false;
            ReportError("unsupported_platform", null, BuildBannerData(config));
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

            CancelLoadTimeout();
            RestoreGameAudio();

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
            // Safety net: never leave the game muted if we're torn down without a clean Hide().
            CancelLoadTimeout();
            RestoreGameAudio();
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("dismiss"); } catch (Exception) { }
            _native = null;
#endif
        }

        /// <summary>
        /// Universal, engine-level game mute for the duration of the ad: silences everything the
        /// Unity <see cref="AudioListener"/> hears, independent of how the host game manages its
        /// own audio. The ad video keeps its sound (it plays through the native ExoPlayer, not the
        /// Unity listener). Idempotent — the original volume is captured only on the first call.
        /// </summary>
        private void MuteGameAudio()
        {
            if (_gameAudioMuted) return;
            _savedAudioVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            _gameAudioMuted = true;
        }

        /// <summary>Restores the game volume saved by <see cref="MuteGameAudio"/>. Idempotent.</summary>
        private void RestoreGameAudio()
        {
            if (!_gameAudioMuted) return;
            AudioListener.volume = _savedAudioVolume;
            _gameAudioMuted = false;
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

        // Первый кадр отрисован — показ состоялся, load-таймаут больше не нужен.
        private void OnExoOverlayStarted(string _)
        {
            _started = true;
            CancelLoadTimeout();
        }

        private void OnExoOverlayCompleted(string _)
        {
            CancelLoadTimeout();
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

            // Пользователь тапнул по ролику — показ явно состоялся, таймаут снимаем.
            CancelLoadTimeout();

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

            ParseError(message, out string reason, out string errorCode);
            ReportError(reason, errorCode, BuildBannerData(_config));

            Hide();
        }

        #endregion

        #region Error reporting

        /// <summary>
        /// Единая точка отправки события проблемы показа. Идемпотентна в пределах одного показа
        /// (<see cref="_errorReported"/>) и всегда снимает load-таймаут.
        /// </summary>
        private void ReportError(string reason, string errorCode, BannerData data)
        {
            if (_errorReported) return;
            _errorReported = true;

            CancelLoadTimeout();
            CrossPromoAnalytics.ReportVideoError(reason, errorCode, data);
        }

        /// <summary>
        /// Разбирает payload нативного OnExoOverlayError формата "errorCode|errorCodeName".
        /// Непарсируемый payload (например "No current activity") трактуем как playback_other
        /// без error_code.
        /// </summary>
        private static void ParseError(string payload, out string reason, out string errorCode)
        {
            errorCode = null;
            reason = "playback_other";

            if (string.IsNullOrEmpty(payload))
                return;

            string[] parts = payload.Split('|');
            if (!int.TryParse(parts[0], out int code))
                return;

            errorCode = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                ? $"{code}:{parts[1]}"
                : code.ToString();
            reason = MapReason(code);
        }

        /// <summary>Маппинг диапазонов ExoPlayer <c>PlaybackException.errorCode</c> в ограниченный enum.</summary>
        private static string MapReason(int code)
        {
            if (code == 1004) return "playback_timeout";
            if (code == 2001 || code == 2002) return "io_network";
            if (code == 2004 || code == 2005) return "io_not_found";
            if (code >= 2000 && code < 3000) return "io_other";
            if (code >= 3000 && code < 4000) return "parse";
            if (code >= 4000 && code < 5000) return "decode";
            return "playback_other";
        }

        private void StartLoadTimeout()
        {
            CancelLoadTimeout();
            _loadTimeoutCo = StartCoroutine(LoadTimeoutRoutine());
        }

        private void CancelLoadTimeout()
        {
            if (_loadTimeoutCo != null)
            {
                StopCoroutine(_loadTimeoutCo);
                _loadTimeoutCo = null;
            }
        }

        private IEnumerator LoadTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(LoadTimeoutSeconds);
            _loadTimeoutCo = null;

            if (_started || !_isVisible)
                yield break;

            Debug.LogWarning($"[CrossPromoExoNativeOverlay] Load timeout — no first frame after {LoadTimeoutSeconds}s");
            ReportError("load_timeout", null, BuildBannerData(_config));
            Hide();
        }

        private static BannerData BuildBannerData(PromoConfiguration config)
        {
            if (config == null)
                return null;

            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;
            return new BannerData(config.Title, null, config.RedirectUrl, config.TrackingUrl, paidAppId);
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

            // Клик уходит ДО редиректа, а сам редирект — на модуле (он переживает закрытие
            // оверлея по CTA). Открывать стор первым, как раньше, нельзя: приложение
            // сворачивается, Unity встаёт на паузу и GET клика может не доехать никогда.
            var module = CrossPromoModule.Instance;
            if (module != null)
            {
                module.StartCoroutine(TrackThenOpen(config));
            }
            else if (!string.IsNullOrWhiteSpace(config.RedirectUrl))
            {
                Application.OpenURL(config.RedirectUrl);
            }
        }

        /// <summary>
        /// Ждёт отправку клика (но не дольше <see cref="ClickTrackingTimeoutSeconds"/>, чтобы тап
        /// по CTA не ощущался зависшим), затем открывает стор. Незавершённые ретраи продолжаются
        /// в фоне на модуле.
        /// </summary>
        private static IEnumerator TrackThenOpen(PromoConfiguration config)
        {
            var module = CrossPromoModule.Instance;
            bool trackingDone = false;

            if (module != null)
                module.StartCoroutine(RunClickTracking(config, () => trackingDone = true));
            else
                trackingDone = true;

            float deadline = Time.realtimeSinceStartup + ClickTrackingTimeoutSeconds;
            while (!trackingDone && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!trackingDone)
                Debug.LogWarning($"[CrossPromoExoNativeOverlay] Click tracking still in flight after {ClickTrackingTimeoutSeconds}s — opening store, tracking continues in background");

            if (!string.IsNullOrWhiteSpace(config.RedirectUrl))
                Application.OpenURL(config.RedirectUrl);
        }

        private static IEnumerator RunClickTracking(PromoConfiguration config, Action onDone)
        {
            yield return SendClickTracking(config);
            onDone?.Invoke();
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
