#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class DeviceIdProvider
    {
        private const string CacheKey = "cp_device_id_hash";
        private const string CacheKeyRaw = "cp_device_id_raw";
        private const string CacheKeyParam = "cp_device_id_param";
        private const string Tag = "[CrossPromoTracking:DeviceId]";

        private static string _pendingRawId;
        private static string _pendingParamName;
        private static bool _requested;

        /// <summary>Raw (unhashed) device ID for Adjust tracker URLs.</summary>
        public static string RawDeviceId =>
            !string.IsNullOrEmpty(PlayerPrefs.GetString(CacheKeyRaw, ""))
                ? PlayerPrefs.GetString(CacheKeyRaw)
                : _pendingRawId;

        /// <summary>Adjust URL parameter name: "fire_adid", "adid", or "android_id".</summary>
        public static string DeviceIdParamName =>
            !string.IsNullOrEmpty(PlayerPrefs.GetString(CacheKeyParam, ""))
                ? PlayerPrefs.GetString(CacheKeyParam)
                : _pendingParamName;

        public static string GetCachedDeviceIdHash()
        {
            return PlayerPrefs.HasKey(CacheKey) ? PlayerPrefs.GetString(CacheKey) : null;
        }

        public static string TryResolveAndCache()
        {
            var cached = GetCachedDeviceIdHash();
            if (!string.IsNullOrEmpty(cached))
            {
                Debug.Log($"{Tag} Using cached device_id_hash: {cached}");
                return cached;
            }

            if (!string.IsNullOrEmpty(_pendingRawId))
            {
                Debug.Log($"{Tag} Raw ID received: {_pendingRawId} (param={_pendingParamName})");
                var hash = HashDeviceId(_pendingRawId);
                if (!string.IsNullOrEmpty(hash))
                {
                    PlayerPrefs.SetString(CacheKey, hash);
                    PlayerPrefs.SetString(CacheKeyRaw, _pendingRawId);
                    PlayerPrefs.SetString(CacheKeyParam, _pendingParamName ?? "android_id");
                    PlayerPrefs.Save();
                    Debug.Log($"{Tag} Cached device_id_hash={hash}, param={_pendingParamName}");
                    return hash;
                }
            }

            Debug.Log($"{Tag} Device ID not yet available, requesting from Adjust...");
            RequestDeviceId();
            return null;
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
            if (_requested)
                return;

            _requested = true;
            Debug.Log($"{Tag} Requesting AmazonAdId from Adjust SDK...");

            try
            {
                AdjustSdk.Adjust.GetAmazonAdId(amazonAdId =>
                {
                    if (!string.IsNullOrEmpty(amazonAdId))
                    {
                        Debug.Log($"{Tag} Adjust.GetAmazonAdId callback received: {amazonAdId}");
                        _pendingRawId = amazonAdId;
                        _pendingParamName = "fire_adid";
                        return;
                    }

                    Debug.Log($"{Tag} AmazonAdId is null, falling back to Adjust.GetAdid...");
                    AdjustSdk.Adjust.GetAdid(adid =>
                    {
                        if (!string.IsNullOrEmpty(adid))
                        {
                            Debug.Log($"{Tag} Adjust.GetAdid callback received: {adid}");
                            _pendingRawId = adid;
                            _pendingParamName = "adid";
                        }
                        else
                        {
                            Debug.LogWarning($"{Tag} Both AmazonAdId and Adid are null, using SystemInfo fallback");
                            _pendingRawId = SystemInfo.deviceUniqueIdentifier;
                            _pendingParamName = "android_id";
                        }
                    });
                });
            }
            catch (Exception e)
            {
                _requested = false;
                Debug.LogWarning($"{Tag} Failed to request device ID from Adjust: {e.Message}");
            }
#else
            if (_requested)
                return;

            _requested = true;
            _pendingRawId = SystemInfo.deviceUniqueIdentifier;
            _pendingParamName = "android_id";
            Debug.Log($"{Tag} Adjust not available, using SystemInfo.deviceUniqueIdentifier as fallback");
#endif
        }
    }
}
#endif
