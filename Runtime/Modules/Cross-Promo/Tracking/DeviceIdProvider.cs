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
        private const string Tag = "[CrossPromoTracking:DeviceId]";

        private static string _pendingRawId;
        private static bool _requested;

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
                Debug.Log($"{Tag} Raw ID received from Adjust callback: {_pendingRawId}");
                var hash = HashDeviceId(_pendingRawId);
                if (!string.IsNullOrEmpty(hash))
                {
                    PlayerPrefs.SetString(CacheKey, hash);
                    PlayerPrefs.Save();
                    Debug.Log($"{Tag} Hashed and cached device_id_hash: {hash}");
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
                        return;
                    }

                    Debug.Log($"{Tag} AmazonAdId is null, falling back to Adjust.GetAdid...");
                    AdjustSdk.Adjust.GetAdid(adid =>
                    {
                        if (!string.IsNullOrEmpty(adid))
                        {
                            Debug.Log($"{Tag} Adjust.GetAdid callback received: {adid}");
                            _pendingRawId = adid;
                        }
                        else
                        {
                            Debug.LogWarning($"{Tag} Both AmazonAdId and Adid are null");
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
            Debug.LogWarning($"{Tag} AMZN_ADJUST_ENABLED is not defined, cannot get device ID");
#endif
        }
    }
}
#endif
