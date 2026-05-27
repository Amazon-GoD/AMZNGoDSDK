#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoTrackingService : MonoBehaviour
    {
        private const long MinTimestampMs = 1577836800000L;
        private const int DeviceIdMaxAttempts = 10;
        private const float DeviceIdRetryInterval = 2f;
        private const string Tag = "[CrossPromoTracking]";

        private string _baseUrl;
        private string _apiKey;
        private string _appType;
        private string _defaultPromotedAppId;
        private string _deviceIdHash;
        private bool _initialized;
        private bool _flushing;

        private static string AppId => Application.identifier;

        public bool IsReady => _initialized && !string.IsNullOrEmpty(_deviceIdHash);

        public void Construct(string baseUrl, string apiKey, CrossPromoAppType appType, string defaultPromotedAppId)
        {
            _baseUrl = baseUrl?.TrimEnd('/');
            _apiKey = apiKey;
            _appType = appType == CrossPromoAppType.Free ? "free" : "paid";
            _defaultPromotedAppId = defaultPromotedAppId;

            if (string.IsNullOrWhiteSpace(_baseUrl))
                Debug.LogWarning($"{Tag} Construct: baseUrl is empty — tracking will be disabled");
            if (string.IsNullOrWhiteSpace(_apiKey))
                Debug.LogWarning($"{Tag} Construct: apiKey is empty — tracking will be disabled");

            Debug.Log($"{Tag} Constructed — baseUrl={_baseUrl}, appId={AppId}, appType={_appType}, defaultPromotedAppId={_defaultPromotedAppId}");
        }

        public void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Debug.Log($"{Tag} Initializing tracking service...");
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            yield return ResolveDeviceId();

            if (string.IsNullOrEmpty(_deviceIdHash))
            {
                Debug.LogWarning($"{Tag} Device ID unavailable after {DeviceIdMaxAttempts} attempts, tracking disabled");
                yield break;
            }

            Debug.Log($"{Tag} Device ID resolved: {_deviceIdHash}");

            yield return TrySendFirstOpen();
            yield return FlushQueue();
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

        public void TrackImpression(string paidAppId)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"{Tag} TrackImpression skipped — service not ready (initialized={_initialized}, deviceIdHash={_deviceIdHash != null})");
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

            Debug.Log($"{Tag} >>> cp_impression: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, device_id_hash={_deviceIdHash}, ts={ts}");

            string json = BuildCrossPromoEventJson("cp_impression", resolvedPaidAppId, AppId, _deviceIdHash, ts);
            StartCoroutine(SendEventWithRetry(json));
        }

        public void TrackClick(string paidAppId)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"{Tag} TrackClick skipped — service not ready (initialized={_initialized}, deviceIdHash={_deviceIdHash != null})");
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

            Debug.Log($"{Tag} >>> cp_click: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, device_id_hash={_deviceIdHash}, ts={ts}");

            string json = BuildCrossPromoEventJson("cp_click", resolvedPaidAppId, AppId, _deviceIdHash, ts);
            StartCoroutine(SendEventWithRetry(json));
        }

        private IEnumerator SendEventWithRetry(string json)
        {
            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} No internet, queuing event");
                CrossPromoTrackingQueue.Enqueue(json);
                yield break;
            }

            bool success = false;
            yield return SendEvent(json, () => success = true);

            if (!success)
            {
                Debug.LogWarning($"{Tag} Send failed, queuing event for retry");
                CrossPromoTrackingQueue.Enqueue(json);
            }
        }

        private IEnumerator SendEvent(string jsonBody, Action onSuccess = null)
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning($"{Tag} SendEvent aborted — baseUrl or apiKey is empty");
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
                    Debug.LogWarning($"{Tag} <<< HTTP {request.responseCode} (client error, not retrying): {request.downloadHandler?.text}");
                    onSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"{Tag} <<< HTTP failed ({request.result}): {request.error}");
                }
            }
        }

        private IEnumerator FlushQueue()
        {
            if (_flushing)
                yield break;

            _flushing = true;

            var events = CrossPromoTrackingQueue.DequeueAll();
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

            int successCount = 0;
            for (int i = 0; i < events.Count; i++)
            {
                bool success = false;
                yield return SendEvent(events[i], () => success = true);
                if (success)
                    successCount++;
                else
                    break;
            }

            if (successCount > 0)
                events.RemoveRange(0, successCount);

            Debug.Log($"{Tag} FlushQueue complete: {successCount} sent, {events.Count} remaining");
            CrossPromoTrackingQueue.Requeue(events);
            _flushing = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && IsReady && IsInternetAvailable())
            {
                Debug.Log($"{Tag} App focused — flushing event queue");
                StartCoroutine(FlushQueue());
            }
        }

        private static bool IsInternetAvailable() =>
            Application.internetReachability != NetworkReachability.NotReachable;

        public static long GetTimestampMs() =>
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
