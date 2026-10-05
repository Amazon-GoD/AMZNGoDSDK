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
        private bool _waitingForResponse;
        private bool _retryOnForeground;
        private Coroutine _pendingRetry;
        private Coroutine _watchdog;
        private double _inFlightDeadline;
        private double _retryDeadline;
        private bool _retryScheduled;

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
            ExpireIfNeeded();
            if (_inFlight)
                return false;
            if (!HostIsActive)
            {
                // Restore may be requested while the SDK object is inactive. Keep the
                // request pending without trying to start a coroutine on an inactive host.
                _retryScheduled = true;
                _retryDeadline = Time.realtimeSinceStartupAsDouble;
                return false;
            }
            CancelPendingRetry();
            _inFlight = true;
            _waitingForResponse = true;
            _inFlightDeadline = Time.realtimeSinceStartupAsDouble + InFlightTimeoutSeconds;
            _watchdog = _host.StartCoroutine(Watchdog());
            return true;
        }

        public void OnSuccess()
        {
            CancelWatchdog();
            _inFlight = false;
            _waitingForResponse = false;
            _failedAttempts = 0;
            _retryOnForeground = false;
        }

        public void OnFailure()
        {
            CancelWatchdog();
            _inFlight = false;
            _waitingForResponse = false;

            // Каталог шлётся батчами и НЕ ходит через TryBegin: одна волна сбоя даёт
            // OnFailure на каждый батч. Пока ретрай уже назначен, дубли не копим — иначе
            // попытки исчерпываются одной волной, а корутины плодятся параллельно.
            if (_retryScheduled)
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
            _retryScheduled = true;
            _retryDeadline = Time.realtimeSinceStartupAsDouble + delay;
            ResumePendingRetry();
        }

        public void OnForeground()
        {
            ResumeAfterActivation();
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
            _waitingForResponse = false;
            _failedAttempts = 0;
            _retryOnForeground = false;
        }

        private bool HostIsActive => _host != null && _host.gameObject.activeInHierarchy;

        /// <summary>Preserve deadlines when Unity stops this host's coroutines.</summary>
        public void SuspendForDeactivation()
        {
            CancelWatchdog();
            StopPendingRetryCoroutine();
        }

        public void ResumeAfterActivation()
        {
            if (!HostIsActive)
                return;
            ExpireIfNeeded();
            if (_inFlight && _waitingForResponse && _watchdog == null)
                _watchdog = _host.StartCoroutine(Watchdog());
            ResumePendingRetry();
        }

        /// <summary>The full response arrived; keep single-flight while game callbacks run.</summary>
        public void FinishWaitingForResponse()
        {
            _waitingForResponse = false;
            CancelWatchdog();
        }

        public void ExpireIfNeeded()
        {
            if (!_inFlight || !_waitingForResponse || Time.realtimeSinceStartupAsDouble < _inFlightDeadline)
                return;
            CancelWatchdog();
            Debug.LogWarning("[AMZNGoDSDK] In-flight IAP operation timed out with no response — failing the run");
            // The owner invalidates its session/RequestIds and completes existing restore
            // callbacks before TryBegin can admit a replacement run.
            if (_onTimeout != null)
                _onTimeout();
            else
                OnFailure();
        }

        private IEnumerator Watchdog()
        {
            while (_inFlight && _waitingForResponse && Time.realtimeSinceStartupAsDouble < _inFlightDeadline)
                yield return new WaitForSecondsRealtime((float)Math.Max(0,
                    _inFlightDeadline - Time.realtimeSinceStartupAsDouble));
            _watchdog = null;
            ExpireIfNeeded();
        }

        private void ResumePendingRetry()
        {
            if (_retryScheduled && _pendingRetry == null && HostIsActive)
                _pendingRetry = _host.StartCoroutine(RetryAfter());
        }

        private IEnumerator RetryAfter()
        {
            yield return new WaitForSecondsRealtime((float)Math.Max(0,
                _retryDeadline - Time.realtimeSinceStartupAsDouble));
            _pendingRetry = null;
            _retryScheduled = false;
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
            _retryScheduled = false;
            StopPendingRetryCoroutine();
        }

        private void StopPendingRetryCoroutine()
        {
            if (_pendingRetry == null)
                return;
            _host.StopCoroutine(_pendingRetry);
            _pendingRetry = null;
        }
    }
}
#endif
