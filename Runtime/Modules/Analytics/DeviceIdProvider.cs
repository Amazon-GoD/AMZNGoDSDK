using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Resolves the device identifier used for analytics events and Adjust attribution.
    /// Only the Amazon Fire advertising ID is accepted — there is deliberately NO fallback to
    /// Adjust's adid or SystemInfo.deviceUniqueIdentifier. When the Fire ID is unavailable this
    /// class reports an empty string rather than a hash derived from another source; callers
    /// decide how to encode that (AnalyticsModule sends device_id_hash="unattributed", while the
    /// Adjust tracker URL simply omits the fire_adid parameter).
    /// </summary>
    public static class DeviceIdProvider
    {
        private const string CacheKey = "cp_device_id_hash";
        private const string CacheKeyRaw = "cp_device_id_raw";
        private const string CacheKeyParam = "cp_device_id_param";
        private const string Tag = "[CrossPromoTracking:DeviceId]";
        private const string ZeroFireAdId = "00000000-0000-0000-0000-000000000000";

        /// <summary>The only accepted identifier source (also the Adjust URL parameter name).</summary>
        private const string FireAdIdParam = "fire_adid";

        /// <summary>
        /// Сколько ждать колбэк Adjust, прежде чем считать запрос потерянным и послать новый.
        /// Меньше, чем интервал ретраев в AnalyticsModule, поэтому каждая его попытка резолва
        /// приводит к свежему запросу.
        /// </summary>
        private const float RequestTimeoutSeconds = 1.5f;

        private static string _pendingRawId;
        private static bool _requestInFlight;
        private static float _lastRequestTime = float.NegativeInfinity;

#if !AMZN_ADJUST_ENABLED
        // Только для сборок без Adjust: если системное чтение не удалось, фолбэка нет.
        // С включённым Adjust его ретраи завершает AnalyticsModule, а не единичный null-колбэк.
        private static bool _resolvedWithoutFireId;
#endif

        /// <summary>Raw (unhashed) Fire ID for Adjust tracker URLs. Empty when unavailable.</summary>
        public static string RawDeviceId =>
            !string.IsNullOrEmpty(GetCachedDeviceIdHash())
                ? PlayerPrefs.GetString(CacheKeyRaw, "")
                : _pendingRawId;

        /// <summary>Adjust URL parameter name. Always "fire_adid" — no other source is used.</summary>
        public static string DeviceIdParamName => FireAdIdParam;

        public static string GetCachedDeviceIdHash()
        {
            if (!PlayerPrefs.HasKey(CacheKey))
                return null;

            // На устройствах, обновившихся с версии с фолбэками, в кэше может лежать хэш,
            // посчитанный из adid или android_id. Такой идентификатор больше не годится —
            // выбрасываем его и перезапрашиваем Fire ID.
            string cachedParam = PlayerPrefs.GetString(CacheKeyParam, "");
            if (cachedParam != FireAdIdParam)
            {
                Debug.Log($"{Tag} Discarding legacy cached device id (param='{cachedParam}') — only {FireAdIdParam} is accepted.");
                PlayerPrefs.DeleteKey(CacheKey);
                PlayerPrefs.DeleteKey(CacheKeyRaw);
                PlayerPrefs.DeleteKey(CacheKeyParam);
                PlayerPrefs.Save();
                return null;
            }

            return PlayerPrefs.GetString(CacheKey);
        }

        /// <summary>
        /// Resolves the Fire ID hash.
        /// </summary>
        /// <returns>
        /// SHA-256 of the Fire ID; <see cref="string.Empty"/> when the system reports no usable ID
        /// or system access failed and the Adjust fallback is disabled; <c>null</c> while waiting
        /// for the Adjust fallback — the caller must retry and decide for itself when to give up.
        /// A single null callback from Adjust is NOT treated as "no Fire ID": it usually just
        /// means the native SDK isn't up yet.
        /// </returns>
        public static string TryResolveAndCache()
        {
            var cached = GetCachedDeviceIdHash();
            if (!string.IsNullOrEmpty(cached))
            {
                Debug.Log($"{Tag} Using cached device_id_hash: {cached}");
                return cached;
            }

            // Fire OS отвечает синхронно и без Adjust, в том числе без AMZN_ADJUST_ENABLED.
            if (TryReadFireAdIdFromSystem(out var systemId))
                return systemId != null ? CacheAndReturn(systemId) : string.Empty;

            // Adjust используется только как фолбэк, если системное чтение не удалось.
            if (!string.IsNullOrEmpty(_pendingRawId))
                return CacheAndReturn(_pendingRawId);

#if !AMZN_ADJUST_ENABLED
            if (_resolvedWithoutFireId)
                return string.Empty;
#endif

            Debug.Log($"{Tag} System Fire ID access unavailable, trying Adjust fallback...");
            RequestDeviceId();
            return null;
        }

        /// <summary>
        /// Читает Fire ID напрямую из настроек Fire OS тем же вызовом, что Adjust.GetAmazonAdId.
        /// true — система ответила: rawId содержит ID либо null, если пригодного ID нет.
        /// false — чтение не удалось или платформа не поддерживается; нужен фолбэк через Adjust.
        /// </summary>
        private static bool TryReadFireAdIdFromSystem(out string rawId)
        {
            rawId = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var secure = new AndroidJavaClass("android.provider.Settings$Secure"))
                {
                    // Amazon требует читать opt-out перед ID; учёт конверсий и отчётность
                    // разрешены и при ограничении персонализации, поэтому ID не отбрасываем.
                    // -1 означает неизвестную настройку и не вызывает исключение на обычном Android.
                    int limitAdTracking = secure.CallStatic<int>("getInt", resolver, "limit_ad_tracking", -1);
                    string trackingStatus = limitAdTracking == 0 ? "not limited"
                        : limitAdTracking == 1 ? "limited" : "unknown";
                    Debug.Log($"{Tag} Ad tracking preference: {trackingStatus}. Fire ID usage is limited to conversion tracking and reporting.");

                    string id = secure.CallStatic<string>("getString", resolver, "advertising_id");
                    id = id?.Trim();

                    if (string.IsNullOrEmpty(id))
                        Debug.Log($"{Tag} advertising_id is absent — no Fire ID on this device.");
                    else if (id == ZeroFireAdId)
                        Debug.Log($"{Tag} advertising_id is all zeros (child profile) — no usable Fire ID.");
                    else
                        rawId = id;

                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Could not read Fire advertising settings from Settings.Secure: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        private static string CacheAndReturn(string rawId)
        {
            Debug.Log($"{Tag} Fire ID received: {rawId}");
            var hash = HashDeviceId(rawId);
            if (string.IsNullOrEmpty(hash))
                return null;

            PlayerPrefs.SetString(CacheKey, hash);
            PlayerPrefs.SetString(CacheKeyRaw, rawId);
            PlayerPrefs.SetString(CacheKeyParam, FireAdIdParam);
            PlayerPrefs.Save();
            Debug.Log($"{Tag} Cached device_id_hash={hash}, param={FireAdIdParam}");
            return hash;
        }

        public static string HashDeviceId(string rawId)
        {
            if (string.IsNullOrEmpty(rawId))
                return null;

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(rawId.Trim().ToLowerInvariant());
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void RequestDeviceId()
        {
#if AMZN_ADJUST_ENABLED
            // Запрос НЕ one-shot. Раньше он делался ровно один раз за сессию, и если нативный
            // Adjust на тот момент был ещё не поднят, его колбэк приходил с null — устройство
            // навсегда оставалось без Fire ID. Теперь потерянный (не ответивший за
            // RequestTimeoutSeconds) запрос повторяется на следующей попытке резолва.
            float now = Time.realtimeSinceStartup;
            if (_requestInFlight && now - _lastRequestTime < RequestTimeoutSeconds)
                return;

            _requestInFlight = true;
            _lastRequestTime = now;
            Debug.Log($"{Tag} Requesting AmazonAdId from Adjust SDK fallback...");

            try
            {
                AdjustSdk.Adjust.GetAmazonAdId(amazonAdId =>
                {
                    _requestInFlight = false;

                    if (!string.IsNullOrWhiteSpace(amazonAdId) && amazonAdId.Trim() != ZeroFireAdId)
                    {
                        Debug.Log($"{Tag} Adjust.GetAmazonAdId callback received: {amazonAdId}");
                        _pendingRawId = amazonAdId;
                        return;
                    }

                    // null здесь НЕ означает «Fire ID недоступен» — чаще это значит, что нативный
                    // Adjust ещё не готов. Финальный вердикт выносит цикл ретраев в AnalyticsModule:
                    // исчерпал попытки — значит идентификатора нет. Никаких фолбэков на adid /
                    // android_id при этом всё равно не делается.
                    Debug.LogWarning($"{Tag} Adjust fallback returned no usable AmazonAdId (empty or all zeros) — will retry.");
                });
            }
            catch (Exception e)
            {
                _requestInFlight = false;
                Debug.LogWarning($"{Tag} Failed to request AmazonAdId from Adjust: {e.Message}");
            }
#else
            if (_resolvedWithoutFireId)
                return;

            // Системное чтение не удалось, а Adjust отключён — другого источника Fire ID нет.
            _resolvedWithoutFireId = true;
            Debug.LogWarning($"{Tag} System Fire ID access unavailable and Adjust SDK fallback is disabled — identifier will be reported as unattributed.");
#endif
        }
    }
}
