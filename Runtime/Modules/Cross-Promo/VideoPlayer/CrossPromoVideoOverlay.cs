#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Full-screen video promo overlay. Loads a video from <see cref="PromoConfiguration"/>,
    /// manages CTA / close button delays, progress bar, mute toggle, tracking, and analytics.
    /// </summary>
    public class CrossPromoVideoOverlay : MonoBehaviour
    {
        #region Serialized References

        [Header("Video")]
        [SerializeField] private CrossPromoVideoPlayer _videoPlayer;
        [SerializeField] private RawImage _videoDisplay;

        [Header("UI — Buttons")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _ctaButton;
        [SerializeField] private Button _muteButton;

        [Header("UI — Progress")]
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private Text _currentTimeText;
        [SerializeField] private Text _durationText;

        [Header("UI — Labels")]
        [SerializeField] private Text _ctaText;
        [SerializeField] private Text _titleText;

        [Header("UI — Countdown")]
        [Tooltip("Text label shown in place of the close button while the countdown is active. Displays remaining seconds.")]
        [SerializeField] private Text _closeCountdownText;

        [Header("UI — Misc")]
        [SerializeField] private GameObject _loadingIndicator;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _muteIcon;
        [SerializeField] private Sprite _muteOnSprite;
        [SerializeField] private Sprite _muteOffSprite;

        [Header("Animation")]
        [SerializeField] private float _fadeDuration = 0.25f;

        #endregion

        #region Events

        public event Action OnClose;
        public event Action OnCTAClick;
        public event Action OnVideoCompleted;

        #endregion

        #region Private State

        private PromoConfiguration _config;
        private Action _callbackOnClose;
        private Action _callbackOnCTA;
        private Coroutine _showCoroutine;
        private Coroutine _updateCoroutine;
        private Coroutine _countdownCoroutine;
        private bool _isVisible;
        private bool _isMuted;

        #endregion

        #region Properties

        public bool IsVisible => _isVisible;
        public CrossPromoVideoPlayer VideoPlayer => _videoPlayer;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(HandleCloseClick);
            if (_ctaButton != null) _ctaButton.onClick.AddListener(HandleCTAClick);
            if (_muteButton != null) _muteButton.onClick.AddListener(HandleMuteClick);
            if (_progressSlider != null) _progressSlider.onValueChanged.AddListener(HandleSeek);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null) _closeButton.onClick.RemoveListener(HandleCloseClick);
            if (_ctaButton != null) _ctaButton.onClick.RemoveListener(HandleCTAClick);
            if (_muteButton != null) _muteButton.onClick.RemoveListener(HandleMuteClick);
            if (_progressSlider != null) _progressSlider.onValueChanged.RemoveListener(HandleSeek);

            UnsubscribeVideoEvents();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Preloads (buffers) a video for the given config without showing the overlay.
        /// Call <see cref="Show"/> with the same config afterwards for instant playback.
        /// </summary>
        /// <summary>
        /// Replaces the overlay's video player with a preloaded one.
        /// The previous player is stopped and its target cleared.
        /// </summary>
        public void SwapVideoPlayer(CrossPromoVideoPlayer preloadedPlayer)
        {
            if (preloadedPlayer == null) return;

            var previous = _videoPlayer;
            if (previous != null && previous != preloadedPlayer)
            {
                UnsubscribeVideoEvents();
                previous.Stop();

                // Never destroy the overlay root or the RawImage host: the player often lives on
                // the same GameObject as this overlay / video display (e.g. "CrossAdPanel").
                var prevGo = previous.gameObject;
                bool playerOnOverlayRoot = prevGo == gameObject;
                bool playerOnVideoDisplay = _videoDisplay != null && prevGo == _videoDisplay.gameObject;

                if (playerOnOverlayRoot || playerOnVideoDisplay)
                    Destroy(previous);
                else
                    Destroy(prevGo);
            }

            _videoPlayer = preloadedPlayer;
            _videoPlayer.transform.SetParent(transform, false);

            if (_videoDisplay != null)
                _videoPlayer.SetTargetRawImage(_videoDisplay);

            Debug.Log("[CrossPromoVideoOverlay] Swapped to preloaded player.");
        }

        public void Preload(PromoConfiguration config)
        {
            if (config == null) return;

            string videoUrl = ResolveVideoUrl(config);
            if (string.IsNullOrWhiteSpace(videoUrl)) return;

            EnsureVideoPlayer();

            bool wasActive = gameObject.activeSelf;
            if (!wasActive)
            {
                gameObject.SetActive(true);
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 0f;
                    _canvasGroup.interactable = false;
                    _canvasGroup.blocksRaycasts = false;
                }
            }

            _videoPlayer.Loop = false;
            _videoPlayer.Preload(videoUrl);

            if (!wasActive)
                StartCoroutine(HideAfterPreloadReady());

            Debug.Log($"[CrossPromoVideoOverlay] Preloading video: {videoUrl}");
        }

        private IEnumerator HideAfterPreloadReady()
        {
            yield return null;
            while (_videoPlayer != null && _videoPlayer.State == CrossPromoVideoPlayer.PlaybackState.Loading)
                yield return null;

            if (!_isVisible)
                gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the overlay with the given promo configuration.
        /// If the video was preloaded, playback starts instantly.
        /// </summary>
        public void Show(PromoConfiguration config, Action onClose = null, Action onCTA = null)
        {
            if (config == null)
            {
                Debug.LogWarning("[CrossPromoVideoOverlay] Cannot show: config is null.");
                onClose?.Invoke();
                return;
            }

            string videoUrl = ResolveVideoUrl(config);
            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                Debug.LogWarning("[CrossPromoVideoOverlay] Cannot show: no video URL in config.");
                onClose?.Invoke();
                return;
            }

            _config = config;
            _callbackOnClose = onClose;
            _callbackOnCTA = onCTA;

            StopAllOverlayCoroutines();
            EnsureVideoPlayer();

            _isMuted = false;
            ApplyConfigToUI(config);
            SubscribeVideoEvents();

            SetVisible(true);

            if (_closeButton != null) _closeButton.gameObject.SetActive(false);
            if (_ctaButton != null) _ctaButton.gameObject.SetActive(false);

            if (_progressSlider != null)
            {
                _progressSlider.gameObject.SetActive(true);
                _progressSlider.SetValueWithoutNotify(0f);
            }

            int closeDelay = Mathf.Max(0, config.CloseShowDelayInSeconds);
            if (_closeCountdownText != null)
            {
                _closeCountdownText.gameObject.SetActive(closeDelay > 0);
                _closeCountdownText.text = closeDelay.ToString();
            }

            if (closeDelay > 0)
                _countdownCoroutine = StartCoroutine(CloseCountdownCoroutine(closeDelay));

            bool usedPreload = _videoPlayer.PlayPreloaded(videoUrl);
            if (usedPreload)
            {
                ShowLoading(false);
            }
            else
            {
                if (_videoDisplay != null)
                    _videoDisplay.texture = null;

                ShowLoading(true);
                _videoPlayer.AutoPlay = true;
                _videoPlayer.Loop = false;
                _videoPlayer.Load(videoUrl);
            }

            IncrementShowCount(config);
            ReportImpression(config);

            _showCoroutine = StartCoroutine(DelayedUICoroutine(config));
            _updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
        }

        /// <summary>Hides the overlay, stops the video, invokes the close callback.</summary>
        public void Hide()
        {
            if (!_isVisible) return;

            StopAllOverlayCoroutines();
            UnsubscribeVideoEvents();

            _videoPlayer?.Stop();
            SetVisible(false);

            _isVisible = false;
            OnClose?.Invoke();
            _callbackOnClose?.Invoke();
            _callbackOnClose = null;
            _callbackOnCTA = null;
        }

        #endregion

        #region Internal — UI Helpers

        private void ApplyConfigToUI(PromoConfiguration config)
        {
            if (_titleText != null)
                _titleText.text = config.Title ?? string.Empty;

            if (_ctaText != null)
                _ctaText.text = !string.IsNullOrWhiteSpace(config.ButtonText)
                    ? config.ButtonText
                    : "Install";

            UpdateMuteIcon();
        }

        private void ShowLoading(bool show)
        {
            if (_loadingIndicator != null)
                _loadingIndicator.SetActive(show);
        }

        private void UpdateMuteIcon()
        {
            if (_muteIcon == null || _muteOnSprite == null || _muteOffSprite == null) return;

            _muteIcon.sprite = _isMuted ? _muteOffSprite : _muteOnSprite;
        }

        private void UpdateProgressUI()
        {
            if (_videoPlayer == null) return;

            if (_progressSlider != null)
            {
                _progressSlider.SetValueWithoutNotify(_videoPlayer.Progress);
            }

            if (_currentTimeText != null)
                _currentTimeText.text = CrossPromoVideoPlayer.FormatTime(_videoPlayer.CurrentTime);

            if (_durationText != null)
                _durationText.text = CrossPromoVideoPlayer.FormatTime(_videoPlayer.Duration);
        }

        #endregion

        #region Internal — Visibility & Fade

        private void SetVisible(bool visible, bool immediate = false)
        {
            _isVisible = visible;

            if (_canvasGroup != null)
            {
                if (visible)
                    gameObject.SetActive(true);

                if (immediate || _fadeDuration <= 0)
                {
                    _canvasGroup.alpha = visible ? 1f : 0f;
                    _canvasGroup.interactable = visible;
                    _canvasGroup.blocksRaycasts = visible;
                }
                else
                {
                    StartCoroutine(FadeCoroutine(visible));
                }
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }

        private IEnumerator FadeCoroutine(bool fadeIn)
        {
            float start = _canvasGroup.alpha;
            float end = fadeIn ? 1f : 0f;
            float elapsed = 0f;

            _canvasGroup.interactable = fadeIn;
            _canvasGroup.blocksRaycasts = fadeIn;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, end, elapsed / _fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = end;
        }

        #endregion

        #region Internal — Delayed UI Elements

        private IEnumerator DelayedUICoroutine(PromoConfiguration config)
        {
            float ctaDelay = Mathf.Max(0, config.OverlayShowDelayInSeconds);
            float closeDelay = Mathf.Max(0, config.CloseShowDelayInSeconds);

            float minDelay = Mathf.Min(ctaDelay, closeDelay);
            float maxDelay = Mathf.Max(ctaDelay, closeDelay);
            bool ctaFirst = ctaDelay <= closeDelay;

            if (minDelay > 0)
                yield return new WaitForSecondsRealtime(minDelay);

            if (ctaFirst)
            {
                if (_ctaButton != null) _ctaButton.gameObject.SetActive(true);
            }
            else
            {
                if (_closeButton != null) _closeButton.gameObject.SetActive(true);
                if (_closeCountdownText != null)
                    _closeCountdownText.gameObject.SetActive(false);
            }

            float remaining = maxDelay - minDelay;
            if (remaining > 0)
                yield return new WaitForSecondsRealtime(remaining);

            if (ctaFirst)
            {
                if (_closeButton != null) _closeButton.gameObject.SetActive(true);
                if (_closeCountdownText != null)
                    _closeCountdownText.gameObject.SetActive(false);
            }
            else
            {
                if (_ctaButton != null) _ctaButton.gameObject.SetActive(true);
            }
        }

        private IEnumerator UpdateProgressCoroutine()
        {
            while (_isVisible)
            {
                UpdateProgressUI();
                yield return null;
            }
        }

        private IEnumerator CloseCountdownCoroutine(int totalSeconds)
        {
            int remaining = totalSeconds;

            while (remaining > 0)
            {
                if (_closeCountdownText != null)
                    _closeCountdownText.text = remaining.ToString();

                yield return new WaitForSecondsRealtime(1f);
                remaining--;
            }

            if (_closeCountdownText != null)
                _closeCountdownText.gameObject.SetActive(false);

            _countdownCoroutine = null;
        }

        #endregion

        #region Internal — Video Events

        private void EnsureVideoPlayer()
        {
            if (_videoPlayer != null) return;

            _videoPlayer = GetComponentInChildren<CrossPromoVideoPlayer>();
            if (_videoPlayer == null)
            {
                var go = new GameObject("VideoPlayer");
                go.transform.SetParent(transform, false);
                _videoPlayer = go.AddComponent<CrossPromoVideoPlayer>();
            }

            if (_videoDisplay != null)
                _videoPlayer.SetTargetRawImage(_videoDisplay);
        }

        private void SubscribeVideoEvents()
        {
            if (_videoPlayer == null) return;

            _videoPlayer.OnReady += HandleVideoReady;
            _videoPlayer.OnCompleted += HandleVideoCompleted;
            _videoPlayer.OnError += HandleVideoError;
            _videoPlayer.OnStateChanged += HandleVideoStateChanged;
        }

        private void UnsubscribeVideoEvents()
        {
            if (_videoPlayer == null) return;

            _videoPlayer.OnReady -= HandleVideoReady;
            _videoPlayer.OnCompleted -= HandleVideoCompleted;
            _videoPlayer.OnError -= HandleVideoError;
            _videoPlayer.OnStateChanged -= HandleVideoStateChanged;
        }

        private void HandleVideoReady()
        {
            ShowLoading(false);
        }

        private void HandleVideoCompleted()
        {
            OnVideoCompleted?.Invoke();

            if (_ctaButton != null) _ctaButton.gameObject.SetActive(true);
            if (_closeButton != null) _closeButton.gameObject.SetActive(true);
        }

        private void HandleVideoError(string error)
        {
            Debug.LogError($"[CrossPromoVideoOverlay] Video error: {error}");
            ShowLoading(false);

            if (_closeButton != null) _closeButton.gameObject.SetActive(true);
        }

        private void HandleVideoStateChanged(
            CrossPromoVideoPlayer.PlaybackState prev,
            CrossPromoVideoPlayer.PlaybackState next)
        {
            if (next == CrossPromoVideoPlayer.PlaybackState.Playing)
                ShowLoading(false);
        }

        #endregion

        #region Internal — Button Handlers

        private void HandleCloseClick()
        {
            Hide();
        }

        private void HandleCTAClick()
        {
            if (_config == null) return;

            ReportClick(_config);
            StartCoroutine(TrackAndRedirect(_config));

            OnCTAClick?.Invoke();
            _callbackOnCTA?.Invoke();
        }

        private void HandleMuteClick()
        {
            _isMuted = !_isMuted;

            if (_videoPlayer != null)
                _videoPlayer.SetMute(_isMuted);

            UpdateMuteIcon();
        }

        private void HandleSeek(float value)
        {
            _videoPlayer?.SeekNormalized(value);
        }

        #endregion

        #region Internal — Tracking & Analytics

        private static void ReportImpression(PromoConfiguration config)
        {
            var data = new BannerData(
                config.Title, null, config.RedirectUrl, config.TrackingUrl,
                config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null);

            CrossPromoAnalytics.ReportVideoShow(data);

            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;
            CrossPromoModule.Instance?.TrackImpression(paidAppId);
        }

        private static void ReportClick(PromoConfiguration config)
        {
            var data = new BannerData(
                config.Title, null, config.RedirectUrl, config.TrackingUrl,
                config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null);

            CrossPromoAnalytics.ReportVideoClick(data);

            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;
            CrossPromoModule.Instance?.TrackClick(paidAppId);
        }

        private IEnumerator TrackAndRedirect(PromoConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.TrackingUrl))
            {
                using var request = UnityWebRequest.Get(config.TrackingUrl);
                yield return request.SendWebRequest();
            }

            if (!string.IsNullOrWhiteSpace(config.RedirectUrl))
                Application.OpenURL(config.RedirectUrl);
        }

        private static void IncrementShowCount(PromoConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.Title)) return;

            int count = PlayerPrefs.GetInt(config.Title, 0);
            PlayerPrefs.SetInt(config.Title, count + 1);
            PlayerPrefs.Save();
        }

        #endregion

        #region Internal — Coroutine Management

        private void StopAllOverlayCoroutines()
        {
            if (_showCoroutine != null)
            {
                StopCoroutine(_showCoroutine);
                _showCoroutine = null;
            }

            if (_updateCoroutine != null)
            {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }

            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
            }
        }

        #endregion

        #region Internal — URL Resolution

        private static string ResolveVideoUrl(PromoConfiguration config)
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
