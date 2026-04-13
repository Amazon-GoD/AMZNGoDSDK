#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Builds Adjust impression/click URLs from remote config and sends fire-and-forget GET requests.
    /// </summary>
    internal static class CrossPromoAdjustTracking
    {
        private const string DonorAppPlaceholder = "{donor_app}";

        internal static string BuildImpressionUrl(PromoConfiguration config) =>
            config == null ? null : BuildAdjustUrl(config.adjust_impression_url, config.campaign, config.adgroup, config.creative);

        internal static string BuildClickUrl(PromoConfiguration config) =>
            config == null ? null : BuildAdjustUrl(config.adjust_click_url, config.campaign, config.adgroup, config.creative);

        /// <summary>
        /// Replaces <c>{donor_app}</c> with the host app bundle id, then appends <c>campaign</c>, <c>adgroup</c>, <c>creative</c> query parameters.
        /// </summary>
        internal static string BuildAdjustUrl(string baseUrl, string campaign, string adgroup, string creative)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            var url = baseUrl.Trim();
            if (url.IndexOf(DonorAppPlaceholder, StringComparison.Ordinal) >= 0)
            {
                string donor = string.IsNullOrEmpty(Application.identifier) ? string.Empty : Uri.EscapeDataString(Application.identifier);
                url = url.Replace(DonorAppPlaceholder, donor);
            }

            var parts = new List<string>(8);

            // S2S flag — required for server-side / non-browser requests
            parts.Add("s2s=1");

            // Device identifier for attribution
            string rawId = DeviceIdProvider.RawDeviceId;
            string paramName = DeviceIdProvider.DeviceIdParamName;
            if (!string.IsNullOrEmpty(rawId) && !string.IsNullOrEmpty(paramName))
                AppendQuery(parts, paramName, rawId);

            AppendQuery(parts, "campaign", campaign);
            AppendQuery(parts, "adgroup", adgroup);
            AppendQuery(parts, "creative", creative);

            char sep = url.IndexOf('?') >= 0 ? '&' : '?';
            var sb = new StringBuilder(url, url.Length + parts.Count * 32);
            sb.Append(sep);
            sb.Append(string.Join("&", parts));
            return sb.ToString();
        }

        private static void AppendQuery(List<string> parts, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }

        internal static IEnumerator SendGet(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                yield break;

            if (!IsHttpUrl(url))
            {
                Debug.LogWarning($"[CrossPromoAdjust] Skipping non-http(s) tracker URL: {url}");
                yield break;
            }

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[CrossPromoAdjust] GET failed ({request.responseCode}): {url} — {request.error}");
        }

        internal static bool IsHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
