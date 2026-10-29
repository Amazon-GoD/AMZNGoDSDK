#if AMZN_IAP_ENABLED
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Вью баннера подписки: задник, кнопка «купить» и крестик. Висит на корне префабов
    /// SubscriptionBanner (Horizontal/Vertical). Показом, покупкой и закрытием управляет
    /// <see cref="SubscriptionBannerPresenter"/> — вью только рисует и сообщает о нажатиях.
    ///
    /// Крестик появляется через <see cref="_closeButtonDelay"/> секунд, кнопка «купить» плавно
    /// пульсирует (<see cref="_pulseScale"/>, <see cref="_pulsePeriod"/>). Обе анимации — на
    /// корутинах по нескейленному времени: игра может стоять на паузе через Time.timeScale.
    /// </summary>
    [DisallowMultipleComponent]
    public class SubscriptionBanner : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Button _cross;
        [SerializeField] private Button _continue;

        [Tooltip("Через сколько секунд после показа появляется крестик.")]
        [SerializeField, Min(0f)] private float _closeButtonDelay = 4f;

        [Header("Пульсация кнопки «купить»")]
        [Tooltip("Во сколько раз кнопка увеличивается на пике. 1 — без пульсации.")]
        [SerializeField, Min(1f)] private float _pulseScale = 1.06f;

        [Tooltip("Длительность одного цикла (увеличение и возврат), секунды.")]
        [SerializeField, Min(0.1f)] private float _pulsePeriod = 1.2f;

        private Coroutine _closeButtonRoutine;
        private Coroutine _pulseRoutine;
        private Vector3 _continueBaseScale = Vector3.one;

        /// <summary>Нажата кнопка «купить».</summary>
        public event Action ContinueClicked;

        /// <summary>Нажат крестик.</summary>
        public event Action CloseClicked;

        private void Awake()
        {
            if (_continue != null)
            {
                _continueBaseScale = _continue.transform.localScale;
                _continue.onClick.AddListener(HandleContinueClick);
            }

            if (_cross != null)
                _cross.onClick.AddListener(HandleCloseClick);
        }

        private void OnDestroy()
        {
            if (_continue != null)
                _continue.onClick.RemoveListener(HandleContinueClick);

            if (_cross != null)
                _cross.onClick.RemoveListener(HandleCloseClick);
        }

        /// <summary>Ставит задник и запускает отсчёт до появления крестика.</summary>
        public void Show(Sprite background)
        {
            if (_background != null)
                _background.sprite = background;

            // Крестик прячем на время отсчёта, чтобы баннер не закрыли вслепую.
            SetCloseButtonVisible(false);

            if (_closeButtonRoutine != null)
                StopCoroutine(_closeButtonRoutine);

            _closeButtonRoutine = StartCoroutine(ShowCloseButtonDelayed());

            StartPulse();
        }

        private void StartPulse()
        {
            if (_continue == null || _pulseScale <= 1f)
                return;

            if (_pulseRoutine != null)
                StopCoroutine(_pulseRoutine);

            _continue.transform.localScale = _continueBaseScale;
            _pulseRoutine = StartCoroutine(Pulse());
        }

        // Нескейленное время, как у крестика: игра может стоять на паузе через Time.timeScale.
        // Корутина живёт, пока жив баннер: при закрытии объект уничтожается вместе с ней.
        private IEnumerator Pulse()
        {
            Transform target = _continue.transform;
            float phase = 0f;

            while (true)
            {
                phase = (phase + Time.unscaledDeltaTime / _pulsePeriod) % 1f;

                // 0 → 1 → 0 за цикл по косинусу: плавный разгон и возврат без рывка на стыке циклов.
                float t = (1f - Mathf.Cos(phase * 2f * Mathf.PI)) * 0.5f;
                target.localScale = _continueBaseScale * Mathf.Lerp(1f, _pulseScale, t);

                yield return null;
            }
        }

        private IEnumerator ShowCloseButtonDelayed()
        {
            if (_closeButtonDelay > 0f)
                yield return new WaitForSecondsRealtime(_closeButtonDelay);

            SetCloseButtonVisible(true);
            _closeButtonRoutine = null;
        }

        private void SetCloseButtonVisible(bool visible)
        {
            if (_cross != null)
                _cross.gameObject.SetActive(visible);
        }

        private void HandleContinueClick() => ContinueClicked?.Invoke();

        private void HandleCloseClick() => CloseClicked?.Invoke();
    }
}
#endif
