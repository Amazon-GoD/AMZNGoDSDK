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

        // Потолок HTTP-запроса. Без него зависший сокет висит вечно и держит корутину.
        private const int HttpTimeoutSeconds = 15;

        // Сколько ждём резолва device_id перед отправкой события, попавшего в первые
        // мгновения после старта (до того как пришёл колбэк Fire ID). Обычно резолв
        // происходит за десятки миллисекунд, поэтому клик по CTA этим не тормозится.
        private const float DeviceIdWaitForEventSeconds = 6f;

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
            _appType = appType == AnalyticsAppType.Paid ? "paid" : "free";
            _defaultPromotedAppId = defaultPromotedAppId;

            // В билд None попасть не может — его отсекает AnalyticsAppTypeBuildGuard. Остаётся
            // Play mode после старта редактора, когда тип ещё сброшен: слать события с
            // додуманным free/paid нельзя, они навсегда испортят разбивку на бэкенде.
            if (Enabled && appType == AnalyticsAppType.None)
            {
                Enabled = false;
                Debug.LogError($"{Tag} App Type не выбран — аналитика отключена для этого запуска. " +
                               "Выставь App Type в AMZN GoD/SDK Settings → Analytics.");
                return;
            }

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

                // WaitForSecondsRealtime, а не WaitForSeconds: игра ставит timeScale=0 на
                // интерстишеле, и обычный WaitForSeconds там встал бы навсегда — device_id
                // не резолвился бы НИКОГДА, и все события молча выбрасывались.
                yield return new WaitForSecondsRealtime(DeviceIdRetryInterval);
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
            string eventId = NewEventId();
            string json = BuildFirstOpenJson(eventName, AppId, _appType, deviceIdHash, ts, eventId);
            Debug.Log($"{Tag} Sending {eventName}: app_id={AppId}, app_type={_appType}, device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

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
            // ts фиксируем СРАЗУ, в момент показа: если device_id ещё резолвится, отправка
            // подождёт его пару кадров, но время события останется настоящим, а не поздним.
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;
            StartCoroutine(TrackCrossPromoEvent("cp_impression", paidAppId, ts));
        }

        public void TrackClick(string paidAppId)
        {
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;
            StartCoroutine(TrackCrossPromoEvent("cp_click", paidAppId, ts));
        }

        /// <summary>
        /// Awaitable-версия отправки клика — её ждёт оверлей перед открытием стора, чтобы
        /// GET клика успел уйти до сворачивания приложения.
        /// </summary>
        public IEnumerator TrackClickRoutine(string paidAppId)
        {
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                yield break;
            yield return TrackCrossPromoEvent("cp_click", paidAppId, ts);
        }

        private IEnumerator TrackCrossPromoEvent(string eventName, string paidAppId, long ts)
        {
            // Раньше здесь был ранний return при !IsReady — событие пропадало навсегда,
            // хотя device_id резолвился буквально десятки миллисекунд спустя. Теперь ждём
            // резолва (ограниченно), не теряя событие.
            yield return WaitUntilDeviceIdResolved();

            string resolvedPaidAppId = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            if (string.IsNullOrEmpty(resolvedPaidAppId))
            {
                Debug.LogWarning($"{Tag} {eventName} skipped — no paid_app_id (param={paidAppId}, default={_defaultPromotedAppId})");
                yield break;
            }

            string deviceIdHash = EventDeviceIdHash;
            string eventId = NewEventId();
            Debug.Log($"{Tag} >>> {eventName}: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

            string json = BuildCrossPromoEventJson(eventName, resolvedPaidAppId, AppId, deviceIdHash, ts, eventId);
            yield return SendEventWithRetry(json, eventId);
        }

        /// <summary>
        /// Ждёт резолва device_id (не дольше <see cref="DeviceIdWaitForEventSeconds"/>). Если так
        /// и не резолвился — событие всё равно уйдёт, но с device_id_hash='unattributed': это
        /// лучше, чем потерять его целиком.
        /// </summary>
        private IEnumerator WaitUntilDeviceIdResolved()
        {
            if (_deviceIdResolved)
                yield break;

            float deadline = Time.realtimeSinceStartup + DeviceIdWaitForEventSeconds;
            while (!_deviceIdResolved && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!_deviceIdResolved)
                Debug.LogWarning($"{Tag} device_id not resolved after {DeviceIdWaitForEventSeconds}s — sending event with '{UnattributedDeviceId}'");
        }

        private IEnumerator SendEventWithRetry(string json, string eventId)
        {
            // Сначала кладём на диск, потом отправляем. Тогда, чем бы отправка ни закончилась
            // — в том числе если приложение убьют на середине запроса (игрок ушёл в стор) —
            // событие не пропадёт: оно уже в очереди и уйдёт при следующем запуске.
            // Дубли на бэкенде отсекаются по event_id.
            AnalyticsEventQueue.Enqueue(json);

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} No internet, event kept in queue (event_id={eventId})");
                yield break;
            }

            var outcome = SendOutcome.Retry;
            yield return SendEvent(json, o => outcome = o);

            if (outcome == SendOutcome.Delivered || outcome == SendOutcome.Rejected)
            {
                // Удаляем из очереди ТОЛЬКО когда точно решено: доставлено (2xx) или
                // отвергнуто по существу (повтор не поможет). Транзиентный сбой оставляет
                // событие в очереди для ретрая при следующем флаше.
                AnalyticsEventQueue.Remove(eventId);
                if (outcome == SendOutcome.Rejected)
                    Debug.LogError($"{Tag} Event rejected by backend, dropped from queue (event_id={eventId}): {json}");
            }
            else
            {
                Debug.LogWarning($"{Tag} Send failed (transient), event stays in queue for retry (event_id={eventId})");
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
                request.timeout = HttpTimeoutSeconds;   // иначе зависший сокет держит корутину вечно

                yield return request.SendWebRequest();

                // Попутное наблюдение доверенного времени для периодов подписки (ТЗ IAP-14):
                // заголовок Date есть в любом ответе по RFC, включая ошибочные.
                SdkTrustedTime.OfferHttpDate(request.GetResponseHeader("Date"));

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

            // Сначала ЧИТАЕМ очередь, не очищая её. Событие удаляется поштучно и только
            // после подтверждённой доставки/отказа. Раньше очередь очищалась ДО отправки
            // (DequeueAll) — если приложение убивали в середине флаша, пропадала вся пачка.
            var events = AnalyticsEventQueue.Peek();
            if (events == null || events.Count == 0)
            {
                _flushing = false;
                yield break;
            }

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} FlushQueue: no internet, {events.Count} events remain in queue");
                _flushing = false;
                yield break;
            }

            Debug.Log($"{Tag} FlushQueue: sending {events.Count} queued events...");

            int delivered = 0;
            int rejected = 0;
            int remaining = 0;

            for (int i = 0; i < events.Count; i++)
            {
                var outcome = SendOutcome.Retry;
                yield return SendEvent(events[i], o => outcome = o);

                switch (outcome)
                {
                    case SendOutcome.Delivered:
                        AnalyticsEventQueue.RemoveExact(events[i]);
                        delivered++;
                        break;
                    case SendOutcome.Rejected:
                        // Отвергнутое по существу выкидываем, иначе навсегда заблокирует очередь.
                        AnalyticsEventQueue.RemoveExact(events[i]);
                        rejected++;
                        break;
                    default:
                        // Первый транзиентный сбой останавливает флаш: сеть/бэкенд лежат.
                        // Остальные события остаются в очереди нетронутыми.
                        remaining = events.Count - i;
                        Debug.Log($"{Tag} FlushQueue stopped (transient): {delivered} sent, {rejected} rejected, {remaining} remaining");
                        _flushing = false;
                        yield break;
                }
            }

            Debug.Log($"{Tag} FlushQueue complete: {delivered} sent, {rejected} rejected, 0 remaining");
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

        private void OnApplicationPause(bool pauseStatus)
        {
            // Игрок уходит в стор → Android шлёт OnApplicationPause(true). Пробуем дослать
            // очередь прямо сейчас: возврата (focus) можно не дождаться — ушедший в стор
            // часто не возвращается. Даже если запрос не успеет — событие уже на диске
            // (enqueue-first), так что не потеряется.
            if (pauseStatus && IsReady && IsInternetAvailable())
            {
                Debug.Log($"{Tag} App paused — flushing queued events");
                StartCoroutine(FlushQueue());
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

        /// <summary>Случайный идентификатор события: и для удаления из очереди, и для дедупа
        /// на бэкенде (в т.ч. чтобы enqueue-first не породил дубли при повторной отправке).</summary>
        private static string NewEventId() => Guid.NewGuid().ToString("N");

        private static string BuildFirstOpenJson(
            string eventName, string appId, string appType, string deviceIdHash, long ts, string eventId)
        {
            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"app_id\":\"{EscapeJson(appId)}\"," +
                   $"\"app_type\":\"{EscapeJson(appType)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"ts\":{ts}" +
                   "}";
        }

        private static string BuildCrossPromoEventJson(
            string eventName, string paidAppId, string donorAppId, string deviceIdHash, long ts, string eventId)
        {
            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
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
