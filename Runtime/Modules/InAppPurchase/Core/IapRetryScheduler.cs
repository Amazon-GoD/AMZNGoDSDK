#if AMZN_IAP_ENABLED
using System;
using System.Collections;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Повторы с бэкоффом для асинхронных операций Amazon (ТЗ IAP-03): три попытки через
    /// 2 / 8 / 30 секунд, после исчерпания — защёлка «повторить при следующем возврате из
    /// фона». Заодно single-flight для полного прогона сверки (старт, ручной Restore,
    /// форграунд) и активный таймаут: отсутствие ответа завершает прогон через обработчик
    /// вызывающего, а поздние ответы отбрасываются им по RequestId.
    /// </summary>
    internal sealed class IapRetryScheduler
    {
        private static readonly float[] Delays = { 2f, 8f, 30f };

        // Ватчдог: ответа может не быть вовсе (мост умер, процесс диалога убит) — без
        // таймаута single-flight заблокировал бы операцию до перезапуска приложения.
        private const float InFlightTimeoutSeconds = 120f;

        private readonly MonoBehaviour _host;
        private readonly Action _retryAction;
        private readonly Action _onExhausted;
        private readonly Action _onTimeout;

        private int _failedAttempts;
        private bool _inFlight;
        private bool _retryOnForeground;
        private Coroutine _pendingRetry;
        private Coroutine _watchdog;

        public bool InFlight => _inFlight;

        public IapRetryScheduler(MonoBehaviour host, Action retryAction, Action onExhausted, Action onTimeout = null)
        {
            _host = host;
            _retryAction = retryAction;
            _onExhausted = onExhausted;
            _onTimeout = onTimeout;
        }

        /// <summary>false — операция уже в полёте, второй запуск запрещён до её завершения,
        /// включая обработку таймаута. Ватчдог ограничивает весь прогон, а не одну страницу.</summary>
        public bool TryBegin()
        {
            if (_inFlight)
                return false;
            CancelPendingRetry();
            _inFlight = true;
            _watchdog = _host.StartCoroutine(Watchdog());
            return true;
        }

        public void OnSuccess()
        {
            CancelWatchdog();
            _inFlight = false;
            _failedAttempts = 0;
            _retryOnForeground = false;
        }

        public void OnFailure()
        {
            CancelWatchdog();
            _inFlight = false;

            // Каталог шлётся батчами и НЕ ходит через TryBegin: одна волна сбоя даёт
            // OnFailure на каждый батч. Пока ретрай уже назначен, дубли не копим — иначе
            // попытки исчерпываются одной волной, а корутины плодятся параллельно.
            if (_pendingRetry != null)
                return;

            if (_failedAttempts >= Delays.Length)
            {
                // Попытки исчерпаны: состояние остаётся Unknown, новая попытка — при
                // следующем возврате из фона (OnForeground).
                _retryOnForeground = true;
                _onExhausted?.Invoke();
                return;
            }

            float delay = Delays[_failedAttempts];
            _failedAttempts++;
            _pendingRetry = _host.StartCoroutine(RetryAfter(delay));
        }

        public void OnForeground()
        {
            if (!_retryOnForeground || _inFlight)
                return;
            _retryOnForeground = false;
            _failedAttempts = 0;
            _retryAction?.Invoke();
        }

        public void Cancel()
        {
            CancelWatchdog();
            CancelPendingRetry();
            _inFlight = false;
            _failedAttempts = 0;
            _retryOnForeground = false;
        }

        private IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(InFlightTimeoutSeconds);
            _watchdog = null;

            if (!_inFlight)
                yield break;

            Debug.LogWarning("[AMZNGoDSDK] In-flight IAP operation timed out with no response — failing the run");
            if (_onTimeout != null)
                _onTimeout();
            else
                OnFailure();
        }

        private IEnumerator RetryAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _pendingRetry = null;
            _retryAction?.Invoke();
        }

        private void CancelWatchdog()
        {
            if (_watchdog == null)
                return;
            _host.StopCoroutine(_watchdog);
            _watchdog = null;
        }

        private void CancelPendingRetry()
        {
            if (_pendingRetry == null)
                return;
            _host.StopCoroutine(_pendingRetry);
            _pendingRetry = null;
        }
    }
}
#endif
