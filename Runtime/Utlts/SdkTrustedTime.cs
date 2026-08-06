using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Доверенное время для расчёта оплаченных периодов подписки (ТЗ IAP-14).
    ///
    /// Часы устройства игрок может перевести и «намайнить» периодов, поэтому опираемся на
    /// заголовок Date HTTP-ответов нашего бэкенда (он есть в любом ответе по RFC, включая
    /// ошибочные). Правила:
    ///  • храповик — значение меньше максимального когда-либо виденного не откатывает время;
    ///  • между наблюдениями время двигают только монотонные часы процесса
    ///    (Time.realtimeSinceStartup), никогда системные;
    ///  • пока в ЭТОЙ сессии не было ни одного наблюдения, UtcNow == null — периоды не
    ///    начисляются и ждут сети (ничего не теряется: при первом онлайне выдадим все разом).
    ///
    /// Источники: собственный лёгкий GET (FetchOnce — существующие запросы аналитики уходят
    /// слишком редко: first_open один раз за установку) плюс попутные ответы AnalyticsModule.
    /// </summary>
    public static class SdkTrustedTime
    {
        private const string PrefsKey = "AMZN_TrustedTimeUtc";

        private static bool _loaded;
        private static DateTime _maxSeenUtc;       // максимум наблюдений за всю жизнь установки (персистится)
        private static bool _hasFresh;             // было ли наблюдение в этой сессии
        private static double _observedAtRealtime; // Time.realtimeSinceStartup в момент наблюдения

        /// <summary>Первое наблюдение в сессии: подписчик доначисляет периоды, ждавшие времени.</summary>
        public static event Action OnFirstFreshTime;

        public static bool HasFreshTime => _hasFresh;

        /// <summary>Текущее доверенное время (UTC) или null, пока в сессии не было наблюдений.</summary>
        public static DateTime? UtcNow
        {
            get
            {
                if (!_hasFresh)
                    return null;
                return _maxSeenUtc.AddSeconds(Time.realtimeSinceStartup - _observedAtRealtime);
            }
        }

        /// <summary>
        /// Принимает значение заголовка Date любого HTTP-ответа нашего бэкенда.
        /// Наблюдение засчитывается, даже если значение не двинуло храповик: сеть была,
        /// а эффективное «сейчас» — максимум из когда-либо виденного.
        /// </summary>
        public static void OfferHttpDate(string httpDateHeader)
        {
            if (string.IsNullOrWhiteSpace(httpDateHeader))
                return;

            if (!DateTime.TryParse(httpDateHeader, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var observed))
            {
                Debug.LogWarning($"[SdkTrustedTime] Unparsable Date header: '{httpDateHeader}'");
                return;
            }

            EnsureLoaded();

            bool first = !_hasFresh;

            if (observed > _maxSeenUtc)
            {
                _maxSeenUtc = observed;
                PlayerPrefs.SetString(PrefsKey, _maxSeenUtc.ToString("o"));
                PlayerPrefs.Save();
            }

            _observedAtRealtime = Time.realtimeSinceStartup;
            _hasFresh = true;

            if (first)
            {
                try { OnFirstFreshTime?.Invoke(); }
                catch (Exception e) { Debug.LogError($"[SdkTrustedTime] OnFirstFreshTime handler threw: {e}"); }
            }
        }

        /// <summary>
        /// Лёгкий запрос за временем: интересен ТОЛЬКО заголовок Date, тело и код ответа
        /// не важны (бэкенд не трогаем — корень может отвечать чем угодно). No-op, если
        /// наблюдение в сессии уже было.
        /// </summary>
        public static IEnumerator FetchOnce()
        {
            if (_hasFresh)
                yield break;

            using (var request = UnityWebRequest.Get(AnalyticsSettingData.DefaultBaseUrl + "/"))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                OfferHttpDate(request.GetResponseHeader("Date"));
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;

            var stored = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(stored))
                return;

            if (DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v))
                _maxSeenUtc = v.ToUniversalTime();
            else
                Debug.LogWarning($"[SdkTrustedTime] Corrupt persisted value: '{stored}'");
        }
    }
}
