#if AMZN_ANALYTICS_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    // Единственный владелец HTTP-канала /v1/events: first_open (paid/free) на инициализации
    // и cp_impression / cp_click из CrossPromoModule через делегаты в AmznGoDSDKCore.
    // Идемпотентность first_open и кэш device_id_hash через PlayerPrefs с историческими
    // ключами cp_* — это намеренно: апгрейд с хотфикса не должен слать дубли и терять
    // закешированный device_id.
    public class AnalyticsModule : ModuleBase
    {
        private const long MinTimestampMs = 1577836800000L;
        private const int DeviceIdMaxAttempts = 10;
        private const float DeviceIdRetryInterval = 2f;
        private const string Tag = "[Analytics]";

        // Значение device_id_hash для устройств без Fire ID. Пустая строка на бэкенде
        // неотличима от потерянного поля, поэтому отсутствие идентификатора кодируется явно.
        private const string UnattributedDeviceId = "unattributed";

        private string _baseUrl;
        private string _apiKey;
        private string _appType;
        private string _defaultPromotedAppId;
        private string _deviceIdHash;
        private bool _deviceIdResolved;
        private bool _flushing;
        private bool _firstOpenInFlight;

        private static string AppId => Application.identifier;

        // Отсутствующий Fire ID — легальное состояние: фолбэков на adid / android_id больше нет.
        // Готовность определяется фактом резолва, а не непустым хэшем, иначе такие устройства
        // молча перестали бы слать события; в payload вместо хэша уходит UnattributedDeviceId.
        public bool IsReady => Initialized && _deviceIdResolved;

        private string EventDeviceIdHash =>
            string.IsNullOrEmpty(_deviceIdHash) ? UnattributedDeviceId : _deviceIdHash;

        public void Construct(bool enable, string baseUrl, string apiKey, AnalyticsAppType appType, string defaultPromotedAppId)
        {
            Enabled = enable;
            _baseUrl = baseUrl?.TrimEnd('/');
            _apiKey = apiKey;
            _appType = appType == AnalyticsAppType.Free ? "free" : "paid";
            _defaultPromotedAppId = defaultPromotedAppId;

            if (Enabled && string.IsNullOrWhiteSpace(_baseUrl))
                Debug.LogWarning($"{Tag} Construct: baseUrl is empty — events will not be sent");
            if (Enabled && string.IsNullOrWhiteSpace(_apiKey))
                Debug.LogWarning($"{Tag} Construct: apiKey is empty — events will not be sent");

            Debug.Log($"{Tag} Constructed — enabled={Enabled}, baseUrl={_baseUrl}, appId={AppId}, appType={_appType}, defaultPromotedAppId={_defaultPromotedAppId}");
        }

        public override void Initialize()
        {
            StartCoroutine(InitializeRoutine());
        }

        public override void Cleanup() { }

        private IEnumerator InitializeRoutine()
        {
            yield return ResolveDeviceId();

            if (string.IsNullOrEmpty(_deviceIdHash))
                Debug.LogWarning($"{Tag} Fire ID unavailable — events will be sent with device_id_hash='{UnattributedDeviceId}'");
            else
                Debug.Log($"{Tag} Device ID resolved: {_deviceIdHash}");

            _firstOpenInFlight = true;
            yield return TrySendFirstOpen();
            _firstOpenInFlight = false;

            yield return FlushQueue();
        }

        private IEnumerator ResolveDeviceId()
        {
            for (int i = 0; i < DeviceIdMaxAttempts; i++)
            {
                Debug.Log($"{Tag} Resolving device ID, attempt {i + 1}/{DeviceIdMaxAttempts}...");

                // null — колбэк Adjust ещё не пришёл, ждём и пробуем снова.
                // "" — Fire ID подтверждённо недоступен, это финальный ответ: шлём пустой идентификатор.
                _deviceIdHash = DeviceIdProvider.TryResolveAndCache();
                if (_deviceIdHash != null)
                {
                    _deviceIdResolved = true;
                    yield break;
                }

                yield return new WaitForSeconds(DeviceIdRetryInterval);
            }

            // Колбэк так и не пришёл за отведённые попытки — считаем Fire ID недоступным,
            // но события всё равно отправляем, с идентификатором UnattributedDeviceId.
            Debug.LogWarning($"{Tag} No Fire ID after {DeviceIdMaxAttempts} attempts — falling back to device_id_hash='{UnattributedDeviceId}'");
            _deviceIdHash = string.Empty;
            _deviceIdResolved = true;
        }

        private IEnumerator TrySendFirstOpen()
        {
            string eventName = _appType == "free" ? "free_first_open" : "paid_first_open";
            string prefsKey = _appType == "free" ? "cp_free_first_open_sent" : "cp_paid_first_open_sent";

            if (PlayerPrefs.HasKey(prefsKey))
            {
                Debug.Log($"{Tag} {eventName} already sent (PlayerPrefs key '{prefsKey}' exists), skipping");
                yield break;
            }

            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
            {
                Debug.LogWarning($"{Tag} System clock appears incorrect (ts={ts}), skipping {eventName}");
                yield break;
            }

            string deviceIdHash = EventDeviceIdHash;
            string json = BuildFirstOpenJson(eventName, AppId, _appType, deviceIdHash, ts);
            Debug.Log($"{Tag} Sending {eventName}: app_id={AppId}, app_type={_appType}, device_id_hash={deviceIdHash}, ts={ts}");

            var outcome = SendOutcome.Retry;
            yield return SendEvent(json, o => outcome = o);

            switch (outcome)
            {
                case SendOutcome.Delivered:
                    // Флаг ставится ТОЛЬКО на подтверждённую доставку (HTTP 2xx). Раньше сюда
                    // попадал и любой 4xx — при ротации ключа бэкенд отдавал 401, событие
                    // считалось доставленным, и устройство навсегда выпадало из воронки.
                    PlayerPrefs.SetInt(prefsKey, 1);
                    PlayerPrefs.Save();
                    Debug.Log($"{Tag} {eventName} confirmed, saved PlayerPrefs key '{prefsKey}'");
                    break;

                case SendOutcome.Rejected:
                    // Бэкенд отверг событие по существу (400/404/422) — повтор не поможет.
                    // Флаг не ставим: если это окажется дефектом бэкенда, следующий запуск
                    // попробует снова, а не похоронит first_open навсегда.
                    Debug.LogError($"{Tag} {eventName} rejected by backend — not retrying this session, flag NOT set");
                    break;

                default:
                    // Транзиентный сбой. В общую очередь first_open НЕ кладём: он останется там
                    // и уйдёт при флаше, а следующий запуск (флаг-то не выставлен) отправит его
                    // ещё раз — на бэкенде получится дубль. Вместо этого просто пробуем снова
                    // при возврате фокуса и на следующем запуске.
                    Debug.LogWarning($"{Tag} {eventName} failed to send (transient) — will retry on focus / next launch");
                    break;
            }
        }

        public void TrackImpression(string paidAppId)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"{Tag} TrackImpression skipped — module not ready (initialized={Initialized}, deviceIdResolved={_deviceIdResolved})");
                return;
            }

            string resolvedPaidAppId = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            if (string.IsNullOrEmpty(resolvedPaidAppId))
            {
                Debug.LogWarning($"{Tag} TrackImpression skipped — no paid_app_id (param={paidAppId}, default={_defaultPromotedAppId})");
                return;
            }

            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            string deviceIdHash = EventDeviceIdHash;
            Debug.Log($"{Tag} >>> cp_impression: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, device_id_hash={deviceIdHash}, ts={ts}");

            string json = BuildCrossPromoEventJson("cp_impression", resolvedPaidAppId, AppId, deviceIdHash, ts);
            StartCoroutine(SendEventWithRetry(json));
        }

        public void TrackClick(string paidAppId)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"{Tag} TrackClick skipped — module not ready (initialized={Initialized}, deviceIdResolved={_deviceIdResolved})");
                return;
            }

            string resolvedPaidAppId = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            if (string.IsNullOrEmpty(resolvedPaidAppId))
            {
                Debug.LogWarning($"{Tag} TrackClick skipped — no paid_app_id (param={paidAppId}, default={_defaultPromotedAppId})");
                return;
            }

            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            string deviceIdHash = EventDeviceIdHash;
            Debug.Log($"{Tag} >>> cp_click: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, device_id_hash={deviceIdHash}, ts={ts}");

            string json = BuildCrossPromoEventJson("cp_click", resolvedPaidAppId, AppId, deviceIdHash, ts);
            StartCoroutine(SendEventWithRetry(json));
        }

        private IEnumerator SendEventWithRetry(string json)
        {
            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} No internet, queuing event");
                AnalyticsEventQueue.Enqueue(json);
                yield break;
            }

            var outcome = SendOutcome.Retry;
            yield return SendEvent(json, o => outcome = o);

            if (outcome == SendOutcome.Retry)
            {
                Debug.LogWarning($"{Tag} Send failed (transient), queuing event for retry");
                AnalyticsEventQueue.Enqueue(json);
            }
            else if (outcome == SendOutcome.Rejected)
            {
                Debug.LogError($"{Tag} Event rejected by backend, dropping: {json}");
            }
        }

        /// <summary>Что делать с событием после попытки отправки.</summary>
        private enum SendOutcome
        {
            /// <summary>HTTP 2xx — бэкенд принял событие.</summary>
            Delivered,

            /// <summary>Транзиентный сбой (сеть, 5xx, 401/403/408/429) — событие нужно повторить.</summary>
            Retry,

            /// <summary>Бэкенд отверг событие по существу (400/404/422) — повтор не поможет, дропаем.</summary>
            Rejected
        }

        private IEnumerator SendEvent(string jsonBody, Action<SendOutcome> onResult)
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                // Конфиг битый — повторы не помогут и только забьют очередь.
                Debug.LogError($"{Tag} SendEvent aborted — baseUrl or apiKey is empty");
                onResult?.Invoke(SendOutcome.Rejected);
                yield break;
            }

            Debug.Log($"{Tag} POST {_baseUrl}/v1/events — body: {jsonBody}");

            using (var request = new UnityWebRequest(_baseUrl + "/v1/events", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", _apiKey);

                yield return request.SendWebRequest();

                long code = request.responseCode;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"{Tag} <<< HTTP {code} OK: {request.downloadHandler.text}");
                    onResult?.Invoke(SendOutcome.Delivered);
                }
                else if (IsTransientHttpError(code))
                {
                    // 401/403 — почти всегда ротация или опечатка в API-ключе, чинится на бэкенде;
                    // 408/429 — таймаут и рейт-лимит. Всё это лечится повтором, поэтому НЕ считаем
                    // доставкой (иначе один сломанный ключ обнуляет воронку по всем устройствам).
                    Debug.LogWarning($"{Tag} <<< HTTP {code} (transient, will retry): {request.downloadHandler?.text}");
                    onResult?.Invoke(SendOutcome.Retry);
                }
                else if (code >= 400 && code < 500)
                {
                    Debug.LogWarning($"{Tag} <<< HTTP {code} (client error, not retrying): {request.downloadHandler?.text}");
                    onResult?.Invoke(SendOutcome.Rejected);
                }
                else
                {
                    Debug.LogWarning($"{Tag} <<< HTTP failed ({request.result}, code={code}): {request.error}");
                    onResult?.Invoke(SendOutcome.Retry);
                }
            }
        }

        private static bool IsTransientHttpError(long code) =>
            code == 401 || code == 403 || code == 408 || code == 429;

        private IEnumerator FlushQueue()
        {
            if (_flushing)
                yield break;

            _flushing = true;

            var events = AnalyticsEventQueue.DequeueAll();
            if (events == null || events.Count == 0)
            {
                _flushing = false;
                yield break;
            }

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} FlushQueue: no internet, {events.Count} events remain in queue");
                AnalyticsEventQueue.Requeue(events);
                _flushing = false;
                yield break;
            }

            Debug.Log($"{Tag} FlushQueue: sending {events.Count} queued events...");

            var remaining = new List<string>(events.Count);
            int delivered = 0;
            int rejected = 0;
            bool stopped = false;

            for (int i = 0; i < events.Count; i++)
            {
                // Первый транзиентный сбой останавливает флаш: сеть/бэкенд лежат, остальные
                // события возвращаются в очередь в исходном порядке.
                if (stopped)
                {
                    remaining.Add(events[i]);
                    continue;
                }

                var outcome = SendOutcome.Retry;
                yield return SendEvent(events[i], o => outcome = o);

                switch (outcome)
                {
                    case SendOutcome.Delivered:
                        delivered++;
                        break;
                    case SendOutcome.Rejected:
                        // Отвергнутое событие выкидываем, иначе оно навсегда заблокирует очередь.
                        rejected++;
                        break;
                    default:
                        stopped = true;
                        remaining.Add(events[i]);
                        break;
                }
            }

            Debug.Log($"{Tag} FlushQueue complete: {delivered} sent, {rejected} rejected, {remaining.Count} remaining");
            AnalyticsEventQueue.Requeue(remaining);
            _flushing = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && IsReady && IsInternetAvailable())
            {
                Debug.Log($"{Tag} App focused — retrying pending events");
                StartCoroutine(RetryPending());
            }
        }

        /// <summary>
        /// Досылает всё, что не ушло: first_open (если флаг так и не выставлен) и очередь событий.
        /// </summary>
        private IEnumerator RetryPending()
        {
            if (!_firstOpenInFlight)
            {
                _firstOpenInFlight = true;
                yield return TrySendFirstOpen();
                _firstOpenInFlight = false;
            }

            yield return FlushQueue();
        }

        private static bool IsInternetAvailable() =>
            Application.internetReachability != NetworkReachability.NotReachable;

        private static long GetTimestampMs() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string BuildFirstOpenJson(
            string eventName, string appId, string appType, string deviceIdHash, long ts)
        {
            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"app_id\":\"{EscapeJson(appId)}\"," +
                   $"\"app_type\":\"{EscapeJson(appType)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"ts\":{ts}" +
                   "}";
        }

        private static string BuildCrossPromoEventJson(
            string eventName, string paidAppId, string donorAppId, string deviceIdHash, long ts)
        {
            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"paid_app_id\":\"{EscapeJson(paidAppId)}\"," +
                   $"\"donor_app_id\":\"{EscapeJson(donorAppId)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"ts\":{ts}" +
                   "}";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
#endif
