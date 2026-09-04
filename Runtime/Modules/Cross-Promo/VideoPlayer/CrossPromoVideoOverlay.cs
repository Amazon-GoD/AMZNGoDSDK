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
        /// <summary>Потолок ожидания отправки клика перед открытием стора.</summary>
        private const float ClickTrackingTimeoutSeconds = 1.5f;

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
        private string _placement;
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
        /// Replaces the overlay's video player with a preloaded one.
        /// The previous player (if any and different) is stopped and destroyed.
        /// </summary>
        public void SwapVideoPlayer(CrossPromoVideoPlayer preloadedPlayer)
        {
            if (preloadedPlayer == null)
            {
                Debug.LogWarning("[CrossPromoVideoOverlay] SwapVideoPlayer called with null, ignoring.");
                return;
            }
            if (preloadedPlayer == _videoPlayer)
                return;

            var previous = _videoPlayer;

            // Unsubscribe from the previous player BEFORE swapping — UnsubscribeVideoEvents
            // uses _videoPlayer as the target.
            UnsubscribeVideoEvents();

            // Assign the new player. We do NOT reparent the preload player's GameObject —
            // reparenting a Unity VideoPlayer on Android causes the underlying MediaCodec /
            // render surface to be re-created, which burns ~700ms to 1s of black on first show.
            // The preload player lives on its own DontDestroyOnLoad host; leaving it there
            // preserves its decoder state. The overlay only needs the RT for the RawImage.
            _videoPlayer = preloadedPlayer;

            if (_videoDisplay != null)
            {
                if (_videoPlayer.CurrentTexture == null)
                    _videoDisplay.texture = null;
                _videoPlayer.SetTargetRawImage(_videoDisplay);
            }

            if (previous != null && previous != _videoPlayer)
            {
                previous.Stop();

                var prevGo = previous.gameObject;
                bool playerOnOverlayRoot = prevGo == gameObject;
                bool playerOnVideoDisplay = _videoDisplay != null && prevGo == _videoDisplay.gameObject;

                if (playerOnOverlayRoot || playerOnVideoDisplay)
                    Destroy(previous);
                else
                    Destroy(prevGo);
            }

            Debug.Log($"[CrossPromoVideoOverlay][T={Time.realtimeSinceStartup:F2}] Swapped to preloaded player (no reparent; newState={_videoPlayer.State}, hasTexture={_videoPlayer.CurrentTexture != null}, playerGO={_videoPlayer.gameObject.name}, active={_videoPlayer.gameObject.activeInHierarchy})");
        }

        /// <summary>
        /// Shows the overlay with the given promo configuration.
        /// If the video was preloaded, playback starts instantly.
        /// </summary>
        public void Show(PromoConfiguration config, string placement = null, Action onClose = null, Action onCTA = null)
        {
            _placement = placement;

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

            // Обе кнопки стартуют скрытыми; DelayedUICoroutine ниже активирует их
            // после соответствующих задержек из конфига (OverlayShowDelayInSeconds для
            // CTA, CloseShowDelayInSeconds для крестика). Иначе игрок может случайно
            // тапнуть CTA в первый момент показа (открывая внешнюю Amazon-страницу
            // во время активного игрового тапа) — UX-баг.
            if (_closeButton != null) _closeButton.gameObject.SetActive(false);
            if (_ctaButton != null) _ctaButton.gameObject.SetActive(false);

            if (_progressSlider != null)
            {
                _progressSlider.gameObject.SetActive(true);
                _progressSlider.SetValueWithoutNotify(0f);
            }

            // Таймер крестика теперь привязан к времени видео, а не к
            // CloseShowDelayInSeconds из конфига: крестик появляется только
            // когда ролик доиграл до конца — юзер обязан досмотреть.
            // CloseCountdownCoroutine читает CurrentTime/Duration напрямую,
            // поэтому он автоматически встаёт на паузе, когда видео паузится
            // (свернули приложение в Amazon-попап — VideoPlayer.Pause()
            // приходит из OnApplicationPause, CurrentTime замирает).
            if (_closeCountdownText != null)
                _closeCountdownText.gameObject.SetActive(true);

            _countdownCoroutine = StartCoroutine(CloseCountdownCoroutine());

            Debug.Log($"[CrossPromoVideoOverlay][T={Time.realtimeSinceStartup:F2}] Show() entry: playerState={_videoPlayer.State}, isPreloaded={_videoPlayer.IsPreloaded}, preloadedUrl='{_videoPlayer.PreloadedUrl}', requestedUrl='{videoUrl}'");
            bool usedPreload = _videoPlayer.PlayPreloaded(videoUrl);
            Debug.Log($"[CrossPromoVideoOverlay][T={Time.realtimeSinceStartup:F2}] Show() after PlayPreloaded: usedPreload={usedPreload}, state={_videoPlayer.State}");
            if (usedPreload)
            {
                ShowLoading(_videoPlayer.State != CrossPromoVideoPlayer.PlaybackState.Playing);
            }
            else
            {
                Debug.LogWarning($"[CrossPromoVideoOverlay][T={Time.realtimeSinceStartup:F2}] FALLBACK: overlay's own player starting fresh Load — black screen until Prepare completes");
                if (_videoDisplay != null)
                    _videoDisplay.texture = null;

                ShowLoading(true);
                _videoPlayer.AutoPlay = true;
                _videoPlayer.Loop = false;
                _videoPlayer.Load(videoUrl);
            }

            IncrementShowCount(config);
            ReportImpression(config);

            var adjustImpression = CrossPromoAdjustTracking.BuildImpressionUrl(config);
            if (!string.IsNullOrWhiteSpace(adjustImpression))
                StartCoroutine(CrossPromoAdjustTracking.SendGet(adjustImpression));

            _showCoroutine = StartCoroutine(DelayedUICoroutine(config));
            _updateCoroutine = StartCoroutine(UpdateProgressCoroutine());
        }

        /// <summary>Hides the overlay, stops the video, invokes the close callback.</summary>
        public void Hide()
        {
            if (!_isVisible) return;

            // 1) Снимаем интерактивность немедленно — игрок видит реакцию на
            //    крестик даже если native Stop() ниже залипнет на блокировке
            //    MediaCodec.release (см. фриз на MTK MT8169, Log MGH 2805).
            _isVisible = false;
            SetVisible(false);

            // 2) Отдаём управление игре ДО потенциально блокирующего Stop().
            //    Раньше Stop() стоял до callback'а — при native-блокировке
            //    крестик не давал ни визуальной реакции, ни возврата в игру.
            OnClose?.Invoke();
            var cb = _callbackOnClose;
            _callbackOnClose = null;
            _callbackOnCTA = null;
            cb?.Invoke();

            // 3) Чистим внутреннее состояние.
            StopAllOverlayCoroutines();
            UnsubscribeVideoEvents();

            // 4) Best-effort остановка плеера. Если native-стек залип — экран
            //    уже свободен, игра уже получила управление.
            try { _videoPlayer?.Stop(); }
            catch (Exception e) { Debug.LogWarning($"[CrossPromoVideoOverlay] Stop() threw: {e.Message}"); }
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
            // Крестик здесь больше НЕ активируется — он появляется только когда
            // видео доиграло (см. CloseCountdownCoroutine + HandleVideoCompleted).
            // CloseShowDelayInSeconds из конфига игнорится сознательно.
            float ctaDelay = Mathf.Max(0, config.OverlayShowDelayInSeconds);
            if (ctaDelay > 0)
                yield return new WaitForSecondsRealtime(ctaDelay);

            if (_ctaButton != null) _ctaButton.gameObject.SetActive(true);
        }

        private IEnumerator UpdateProgressCoroutine()
        {
            while (_isVisible)
            {
                UpdateProgressUI();
                yield return null;
            }
        }

        private IEnumerator CloseCountdownCoroutine()
        {
            // Тик по фактическому времени видео, а не по wall-clock. Решает два
            // кейса разом:
            //   1) Видео на паузе (приложение свёрнуто в Amazon-попап) →
            //      CurrentTime замирает → таймер автоматически встаёт.
            //   2) Длина таймера = длине ролика: когда CurrentTime ≥ Duration,
            //      крестик активируется. Юзер обязан досмотреть.
            while (_videoPlayer != null && _isVisible)
            {
                double duration = _videoPlayer.Duration;
                if (duration > 0)
                {
                    int remaining = Mathf.Max(0, Mathf.CeilToInt((float)(duration - _videoPlayer.CurrentTime)));
                    if (_closeCountdownText != null)
                        _closeCountdownText.text = remaining.ToString();

                    if (remaining <= 0)
                        break;
                }

                yield return null;
            }

            if (_closeCountdownText != null)
                _closeCountdownText.gameObject.SetActive(false);

            if (_closeButton != null)
                _closeButton.gameObject.SetActive(true);

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
            // Intentionally not hiding the loading indicator here.
            // "Ready" means the video is buffered, but the RenderTexture is still black
            // until the first decoded frame arrives. The loading indicator will be hidden
            // by HandleVideoStateChanged once the Playing state fires (after first frame).
        }

        private void HandleVideoCompleted()
        {
            OnVideoCompleted?.Invoke();

            if (_ctaButton != null) _ctaButton.gameObject.SetActive(true);
            if (_closeButton != null) _closeButton.gameObject.SetActive(true);

            // Прячем countdown-текст: видео доиграло, крестик уже активен.
            // CloseCountdownCoroutine обычно делает это сама на остатке=0, но
            // если корутина ещё не успела дотикать (округление, частота фрейма),
            // подстраховываемся здесь.
            if (_closeCountdownText != null)
                _closeCountdownText.gameObject.SetActive(false);

            // Сразу освобождаем хардварный декодер на Completed. Без этого
            // следующий фоновый Preload откроет второй параллельный MediaCodec,
            // а на MTK MT8169 (Amazon Fire) одновременно два H.264-сессии
            // упираются в ограниченный пул декодеров — Prepare второго встаёт
            // в native-call и через ResourceManager блокирует main-поток Unity
            // (фриз ~3 минуты на циклах Show+CTA, см. лог Log MGH 2805).
            // Последний кадр остаётся на RawImage — texture уже скопирована.
            _videoPlayer?.Stop();
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
            Debug.Log($"[CrossPromoVideoOverlay][T={Time.realtimeSinceStartup:F2}] VideoState {prev} -> {next}");
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

            var config = _config;
            var module = CrossPromoModule.Instance;

            // И трекинг, и OpenURL уводим на модуль: он переживает Hide() оверлея, который
            // может прилететь из внешнего OnCTAClick-колбэка ниже. Раньше из-за этого OpenURL
            // приходилось звать первым — и клик уходил уже после того, как приложение
            // свернулось в стор.
            if (module != null)
            {
                module.StartCoroutine(TrackThenOpen(config));
            }
            else if (!string.IsNullOrWhiteSpace(config.RedirectUrl))
            {
                // Модуля нет — тап всё равно не должен пропасть.
                Application.OpenURL(config.RedirectUrl);
            }

            OnCTAClick?.Invoke();
            _callbackOnCTA?.Invoke();
        }

        /// <summary>
        /// Отправляет клик ДО перехода в стор (как это делает баннер): после OpenURL приложение
        /// сворачивается, Unity встаёт на паузу, и незавершённый запрос доезжает в лучшем случае
        /// при возврате. Ждём отправку, но не дольше <see cref="ClickTrackingTimeoutSeconds"/> —
        /// иначе тап по CTA ощущается зависшим; недоехавшие ретраи продолжатся в фоне на модуле.
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
                Debug.LogWarning($"[CrossPromoVideoOverlay] Click tracking still in flight after {ClickTrackingTimeoutSeconds}s — opening store, tracking continues in background");

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
                using var request = UnityWebRequest.Get(config.TrackingUrl);
                yield return request.SendWebRequest();
            }
            else if (!string.IsNullOrWhiteSpace(config.TrackingUrl))
            {
                Debug.LogWarning($"[CrossPromoVideoOverlay] Skipping non-http(s) TrackingUrl: {config.TrackingUrl}");
            }
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
            // Показ в AppMetrica (inter/reward_displayed) для Unity-пути репортит модуль в
            // ShowVideoInternalCoroutine. Здесь остаётся только показ на наш бэкенд
            // (cp_impression); отдельный crosspromo_video_show убран как дубль.
            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;
            CrossPromoModule.Instance?.TrackImpression(paidAppId);
        }

        private void ReportClick(PromoConfiguration config)
        {
            var data = new BannerData(
                config.Title, null, config.RedirectUrl, config.TrackingUrl,
                config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null);

            // Клик в AppMetrica + Adjust по плейсменту (inter/reward_clicked). Отдельный
            // crosspromo_video_click заменён на них.
            CrossPromoAnalytics.ReportClicked(_placement, data);

            string paidAppId = config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null;
            CrossPromoModule.Instance?.TrackClick(paidAppId);
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
