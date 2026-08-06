#if AMZN_IAP_ENABLED
using System;
using System.Collections;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Повторы с бэкоффом для асинхронных операций Amazon (ТЗ IAP-03): три попытки через
    /// 2 / 8 / 30 секунд, после исчерпания — защёлка «повторить при следующем возврате из
    /// фона». Заодно single-flight: у слушателя Amazon нет привязки к запросу (RequestId не
    /// читается), поэтому параллельные прогоны одной операции запрещены одним флагом —
    /// после IAP-11 источников запросов сверки три (старт, ручной Restore, форграунд).
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

        private int _failedAttempts;
        private bool _inFlight;
        private float _inFlightSince;
        private bool _retryOnForeground;
        private Coroutine _pendingRetry;

        public bool InFlight => InFlightFresh;

        private bool InFlightFresh =>
            _inFlight && Time.realtimeSinceStartup - _inFlightSince < InFlightTimeoutSeconds;

        public IapRetryScheduler(MonoBehaviour host, Action retryAction, Action onExhausted)
        {
            _host = host;
            _retryAction = retryAction;
            _onExhausted = onExhausted;
        }

        /// <summary>false — операция уже в полёте, второй запуск запрещён. Полёт старше
        /// ватчдога считается мёртвым: новый запуск разрешается, поздний ответ мёртвого
        /// прогона отбрасывается на стороне вызывающего (сессия уже другая).</summary>
        public bool TryBegin()
        {
            if (InFlightFresh)
                return false;
            if (_inFlight)
                Debug.LogWarning("[AMZNGoDSDK] In-flight IAP operation timed out with no response — starting a new run");
            CancelPendingRetry();
            _inFlight = true;
            _inFlightSince = Time.realtimeSinceStartup;
            return true;
        }

        public void OnSuccess()
        {
            _inFlight = false;
            _failedAttempts = 0;
            _retryOnForeground = false;
        }

        public void OnFailure()
        {
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
            if (!_retryOnForeground || InFlightFresh)
                return;
            _retryOnForeground = false;
            _failedAttempts = 0;
            _retryAction?.Invoke();
        }

        private IEnumerator RetryAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _pendingRetry = null;
            _retryAction?.Invoke();
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
