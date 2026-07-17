#if AMZN_CROSSPROMO_ENABLED
using System.Collections;
using System.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Резолвит публичный IP устройства через echo-сервис и кэширует его на сессию.
    /// Нужен как параметр <c>ip_address</c> в Adjust S2S-ссылках: при отправке трафика через
    /// прокси (Infatica) Adjust видит IP прокси, а не устройства, и атрибуция ломается —
    /// поэтому реальный публичный IP надо передавать явно.
    /// </summary>
    internal static class CrossPromoPublicIp
    {
        // Возвращает публичный IP устройства простым текстом (IPv4).
        private const string EchoUrl = "https://api.ipify.org";
        private const int TimeoutSeconds = 10;

        private static string _cachedIp;
        private static bool _fetching;

        /// <summary>Кэшированный публичный IP или null, если ещё не резолвился.</summary>
        internal static string Value => _cachedIp;

        /// <summary>
        /// Запускает резолв, если IP ещё не получен и не резолвится прямо сейчас. Безопасно
        /// вызывать многократно — внутренний guard не плодит дублирующие запросы. При неудаче
        /// оставляет кэш пустым, чтобы следующий вызов повторил попытку.
        /// </summary>
        internal static IEnumerator Prefetch()
        {
            if (!string.IsNullOrEmpty(_cachedIp) || _fetching)
                yield break;

            _fetching = true;

            using (var request = UnityWebRequest.Get(EchoUrl))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string ip = request.downloadHandler?.text?.Trim();
                    if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out _))
                    {
                        _cachedIp = ip;
                        Debug.Log($"[CrossPromoPublicIp] resolved public IP: {ip}");
                    }
                    else
                    {
                        Debug.LogWarning($"[CrossPromoPublicIp] echo returned non-IP response: '{ip}'");
                    }
                }
                else
                {
                    Debug.LogWarning($"[CrossPromoPublicIp] fetch failed ({request.responseCode}): {request.error}");
                }
            }

            _fetching = false;
        }
    }
}
#endif
