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

        // Потолок запроса: без него зависший сокет висит вечно.
        private const int HttpTimeoutSeconds = 15;

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

            // Device identifier for attribution. DeviceIdProvider живёт в Analytics
            // модуле — при отключённом Analytics его нет в компиляции, атрибуция
            // тогда идёт без device id (Adjust матчит по fingerprint/prob-match).
#if AMZN_ANALYTICS_ENABLED
            string rawId = DeviceIdProvider.RawDeviceId;
            string paramName = DeviceIdProvider.DeviceIdParamName;
            if (!string.IsNullOrEmpty(rawId) && !string.IsNullOrEmpty(paramName))
                AppendQuery(parts, paramName, rawId);
#endif

            // Публичный IP устройства для атрибуции. При отправке через прокси (Infatica)
            // Adjust видит IP прокси, поэтому передаём реальный IP явно. Если ещё не
            // резолвился — параметр просто опускается, следующие показы/клики его подхватят.
            string publicIp = CrossPromoPublicIp.Value;
            if (!string.IsNullOrEmpty(publicIp))
                AppendQuery(parts, "ip_address", publicIp);

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

        /// <summary>Задержки между попытками; их количество и задаёт число ретраев.</summary>
        private static readonly float[] RetryDelaysSeconds = { 1f, 3f };

        /// <summary>
        /// Шлёт трекер-GET с ретраями. Раньше это был единственный выстрел без повторов:
        /// любой транзиентный сбой (а он особенно вероятен ровно в момент клика, когда
        /// приложение уходит в стор) молча терял конверсию.
        /// </summary>
        internal static IEnumerator SendGet(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                // НЕ тихий выход: иначе по логу невозможно отличить «ссылка не задана» от
                // «ссылка успешно дёрнулась». На этом мы уже теряли неделю.
                Debug.LogError("[CrossPromoAdjust] SendGet: tracker URL пуст — ничего не отправлено. Проверьте adjust_click_url / adjust_impression_url и поля campaign/adgroup/creative в конфиге.");
                yield break;
            }

            if (!IsHttpUrl(url))
            {
                Debug.LogWarning($"[CrossPromoAdjust] Skipping non-http(s) tracker URL: {url}");
                yield break;
            }

            int maxAttempts = RetryDelaysSeconds.Length + 1;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                long code;
                string error;

                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = HttpTimeoutSeconds;
                    yield return request.SendWebRequest();

                    // Логируем ТЕЛО ответа, а не только код: Adjust на отклонённый клик отвечает
                    // HTTP 200 и пишет ошибку в тело. «200 OK» ещё не значит, что клик засчитан.
                    string body = request.downloadHandler != null ? request.downloadHandler.text : null;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[CrossPromoAdjust] GET {request.responseCode} (attempt {attempt}): {url} — body: {body}");
                        yield break;
                    }

                    code = request.responseCode;
                    error = request.error;
                    if (!string.IsNullOrEmpty(body))
                        Debug.LogWarning($"[CrossPromoAdjust] GET error body: {body}");
                }

                // 4xx (кроме таймаута и рейт-лимита) — трекер отверг ссылку по существу,
                // повтор ничего не изменит.
                if (code >= 400 && code < 500 && code != 408 && code != 429)
                {
                    Debug.LogWarning($"[CrossPromoAdjust] GET rejected ({code}), not retrying: {url} — {error}");
                    yield break;
                }

                Debug.LogWarning($"[CrossPromoAdjust] GET failed (attempt {attempt}/{maxAttempts}, code={code}): {url} — {error}");

                if (attempt < maxAttempts)
                    yield return new WaitForSecondsRealtime(RetryDelaysSeconds[attempt - 1]);
            }

            Debug.LogWarning($"[CrossPromoAdjust] GET giving up after {maxAttempts} attempts: {url}");
        }

        /// <summary>
        /// Проверяет креатив на старте и громко пишет в лог, чего не хватает для атрибуции:
        /// ссылок Adjust или полей campaign/adgroup/creative. Ничего не отправляет — только логи.
        /// </summary>
        internal static void LogConfigWarnings(PromoConfiguration config)
        {
            if (config == null) return;

            string title = string.IsNullOrWhiteSpace(config.Title) ? "<no title>" : config.Title;

            if (string.IsNullOrWhiteSpace(config.adjust_click_url))
                Debug.LogError($"[CrossPromoAdjust] Config '{title}': НЕТ adjust_click_url — клики в Adjust не уйдут, установка припишется к органике.");
            if (string.IsNullOrWhiteSpace(config.adjust_impression_url))
                Debug.LogWarning($"[CrossPromoAdjust] Config '{title}': нет adjust_impression_url — показы в Adjust не уйдут.");
            if (string.IsNullOrWhiteSpace(config.campaign))
                Debug.LogWarning($"[CrossPromoAdjust] Config '{title}': пустое поле campaign.");
            if (string.IsNullOrWhiteSpace(config.adgroup))
                Debug.LogWarning($"[CrossPromoAdjust] Config '{title}': пустое поле adgroup.");
            if (string.IsNullOrWhiteSpace(config.creative))
                Debug.LogWarning($"[CrossPromoAdjust] Config '{title}': пустое поле creative.");
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
