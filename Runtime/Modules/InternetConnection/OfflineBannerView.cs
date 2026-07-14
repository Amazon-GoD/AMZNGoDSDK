#if AMZN_INTERNETCONNECTION_ENABLED
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Full-screen banner shown by <see cref="InternetConnectionModule"/> while the device is offline.
    /// Fades in/out on unscaled time, because the module may set <c>Time.timeScale</c> to 0 while offline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OfflineBannerView : MonoBehaviour
    {
        private const string Tag = "[OfflineBanner]";

        [Header("Layout")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Content")]
        [SerializeField] private Image _icon;
        [SerializeField] private Text _messageText;

        [Header("Retry")]
        [SerializeField] private GameObject _retryButtonRoot;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Text _retryButtonText;

        [Header("Animation")]
        [Tooltip("Fade duration in seconds. 0 = instant.")]
        [SerializeField] private float _fadeDuration = 0.2f;

        private Coroutine _fadeCoroutine;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        /// <summary>Raised when the player taps the retry button.</summary>
        public event Action OnRetry;

        private void Awake()
        {
            if (_canvas == null)
                _canvas = GetComponent<Canvas>();

            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();

            if (_retryButton != null)
                _retryButton.onClick.AddListener(HandleRetryClick);

            ApplyState(0f, false);
        }

        private void OnDestroy()
        {
            if (_retryButton != null)
                _retryButton.onClick.RemoveListener(HandleRetryClick);
        }

        /// <summary>Applies the module settings (texts, retry button, sorting order) to the UI.</summary>
        public void Configure(InternetConnectionSettingData settings)
        {
            if (settings == null)
                return;

            if (_messageText != null && !string.IsNullOrEmpty(settings.BannerMessage))
                _messageText.text = settings.BannerMessage;

            if (_retryButtonText != null && !string.IsNullOrEmpty(settings.RetryButtonLabel))
                _retryButtonText.text = settings.RetryButtonLabel;

            if (_retryButtonRoot != null)
                _retryButtonRoot.SetActive(settings.ShowRetryButton);

            if (_canvas != null)
                _canvas.sortingOrder = settings.BannerSortingOrder;
        }

        public void Show()
        {
            if (_isVisible)
                return;

            // Activating the object may run Awake(), which resets the alpha — so flip the flag afterwards.
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            _isVisible = true;
            StartFade(1f);
        }

        public void Hide(bool immediate = false)
        {
            if (!_isVisible && !gameObject.activeSelf)
                return;

            _isVisible = false;

            if (immediate)
            {
                StopFade();
                ApplyState(0f, false);
                gameObject.SetActive(false);
                return;
            }

            StartFade(0f);
        }

        private void StartFade(float targetAlpha)
        {
            StopFade();

            bool canAnimate = _canvasGroup != null && _fadeDuration > 0f && isActiveAndEnabled;
            if (!canAnimate)
            {
                bool visible = targetAlpha > 0f;
                ApplyState(targetAlpha, visible);
                if (!visible)
                    gameObject.SetActive(false);
                return;
            }

            _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha));
        }

        private void StopFade()
        {
            if (_fadeCoroutine == null)
                return;

            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        private IEnumerator FadeRoutine(float targetAlpha)
        {
            bool fadingIn = targetAlpha > 0f;
            float startAlpha = _canvasGroup.alpha;

            // Swallow input for the whole fade, so a tap can't slip through to the game underneath.
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = fadingIn;

            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / _fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            _fadeCoroutine = null;

            if (!fadingIn)
            {
                ApplyState(0f, false);
                gameObject.SetActive(false);
            }
        }

        private void ApplyState(float alpha, bool interactive)
        {
            if (_canvasGroup == null)
                return;

            _canvasGroup.alpha = alpha;
            _canvasGroup.interactable = interactive;
            _canvasGroup.blocksRaycasts = interactive;
        }

        private void HandleRetryClick()
        {
            Debug.Log($"{Tag} Retry pressed.");
            OnRetry?.Invoke();
        }
    }
}
#endif
