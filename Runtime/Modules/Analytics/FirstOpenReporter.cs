using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    // Hotfix: отправляет paid_first_open / free_first_open на кастомный бэк,
    // не завися от CrossPromoModule (он может быть выключен и физически вырезан
    // из сборки вместе с CrossPromoTrackingService). Параллельно с CrossPromo
    // не работает — Core разводит их по gating-условию, плюс используются те же
    // PlayerPrefs-ключи (cp_free_first_open_sent / cp_paid_first_open_sent),
    // что даёт второй контур защиты от двойной отправки.
    public class FirstOpenReporter : MonoBehaviour
    {
        private const long MinTimestampMs = 1577836800000L;
        private const int DeviceIdMaxAttempts = 10;
        private const float DeviceIdRetryInterval = 2f;
        private const string Tag = "[FirstOpenReporter]";

        private string _baseUrl;
        private string _apiKey;
        private string _appType;
        private string _deviceIdHash;

        private static string AppId => Application.identifier;

        public void Construct(string baseUrl, string apiKey, FirstOpenAppType appType)
        {
            _baseUrl = baseUrl?.TrimEnd('/');
            _apiKey = apiKey;
            _appType = appType == FirstOpenAppType.Free ? "free" : "paid";
        }

        public void Initialize()
        {
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            yield return ResolveDeviceId();

            if (string.IsNullOrEmpty(_deviceIdHash))
            {
                Debug.LogWarning($"{Tag} Device ID unavailable after {DeviceIdMaxAttempts} attempts, skipping first_open");
                yield break;
            }

            Debug.Log($"{Tag} Device ID resolved: {_deviceIdHash}");

            yield return TrySendFirstOpen();
        }

        private IEnumerator ResolveDeviceId()
        {
            for (int i = 0; i < DeviceIdMaxAttempts; i++)
            {
                Debug.Log($"{Tag} Resolving device ID, attempt {i + 1}/{DeviceIdMaxAttempts}...");
                _deviceIdHash = DeviceIdProvider.TryResolveAndCache();
                if (!string.IsNullOrEmpty(_deviceIdHash))
                    yield break;

                yield return new WaitForSeconds(DeviceIdRetryInterval);
            }
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

            string json = BuildFirstOpenJson(eventName, AppId, _appType, _deviceIdHash, ts);
            Debug.Log($"{Tag} Sending {eventName}: app_id={AppId}, app_type={_appType}, device_id_hash={_deviceIdHash}, ts={ts}");

            bool success = false;
            yield return SendEvent(json, () => success = true);

            if (success)
            {
                PlayerPrefs.SetInt(prefsKey, 1);
                PlayerPrefs.Save();
                Debug.Log($"{Tag} {eventName} confirmed, saved PlayerPrefs key '{prefsKey}'");
            }
            else
            {
                Debug.LogWarning($"{Tag} {eventName} failed to send, queuing for retry");
                CrossPromoTrackingQueue.Enqueue(json);
            }
        }

        private IEnumerator SendEvent(string jsonBody, Action onSuccess)
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning($"{Tag} SendEvent aborted — baseUrl or apiKey is empty");
                yield break;
            }

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} No internet, will queue event");
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

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"{Tag} <<< HTTP 200 OK: {request.downloadHandler.text}");
                    onSuccess?.Invoke();
                }
                else if (request.responseCode >= 400 && request.responseCode < 500)
                {
                    // 4xx — клиентская ошибка, ретраить бессмысленно. Считаем доставленным,
                    // чтобы не плодить дубли в очереди и не блокировать выставление PlayerPrefs.
                    Debug.LogWarning($"{Tag} <<< HTTP {request.responseCode} (client error, not retrying): {request.downloadHandler?.text}");
                    onSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"{Tag} <<< HTTP failed ({request.result}): {request.error}");
                }
            }
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
