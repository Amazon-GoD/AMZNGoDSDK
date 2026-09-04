#if AMZN_APPLOVIN_ENABLED
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if AMZN_ADJUST_ENABLED
using AdjustSdk;
#endif
#if AMZN_APPMETRICA_ENABLED
using Io.AppMetrica;
#endif

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Отправка аналитики по показам медиации AppLovin — зеркало <see cref="CrossPromoAnalytics"/>
    /// для второго источника рекламы.
    ///
    /// <para>Зачем: роутер <see cref="AmznGoDSDKCore.ShowVideoPromo"/> отдаёт показ в медиацию,
    /// как только кросс-промо выбрало капы. Без этих событий воронка в отчётах обрывается ровно
    /// на переключении: показы идут, а в аналитике их нет, и «показа не было» не отличить от
    /// «показ ушёл в медиацию».</para>
    ///
    /// <para><b>Ad revenue</b> — отдельный и главный канал. MAX отдаёт выручку по каждому показу
    /// событием <c>OnAdRevenuePaidEvent</c>; без его ретрансляции в Adjust и AppMetrica ROAS и LTV
    /// по рекламе не считаются вообще. Печатать <c>adInfo.Revenue</c> в лог, как было раньше,
    /// для этого недостаточно.</para>
    ///
    /// <para>Все вызовы защищены: модуль-получатель может быть выключен (у core свои гарды на
    /// <c>Enabled</c>/<c>Initialized</c>), а сам SDK — ещё не подняться. Аналитика не имеет права
    /// ронять показ рекламы, поэтому каждая отправка обёрнута в try/catch.</para>
    /// </summary>
    internal static class AppLovinAnalytics
    {
        private const string InterRequestedEvent = "mediation_inter_requested";
        private const string InterDisplayedEvent = "mediation_inter_displayed";
        private const string InterDisplayFailedEvent = "mediation_inter_display_failed";
        private const string InterClickedEvent = "mediation_inter_clicked";
        private const string InterHiddenEvent = "mediation_inter_hidden";

        private const string RewardRequestedEvent = "mediation_reward_requested";
        private const string RewardDisplayedEvent = "mediation_reward_displayed";
        private const string RewardDisplayFailedEvent = "mediation_reward_display_failed";
        private const string RewardClickedEvent = "mediation_reward_clicked";
        private const string RewardHiddenEvent = "mediation_reward_hidden";
        private const string RewardEarnedEvent = "mediation_reward_earned";

        /// <summary>
        /// Показ запрошен, но готового ad'а не было. Отдельное событие: в инвариант
        /// «запросов = показов + ошибок показа» такой отказ не входит — показа не начиналось,
        /// и роутер просто закрывает запрос без рекламы.
        /// </summary>
        private const string NoFillEvent = "mediation_no_fill";

        /// <summary>
        /// Значение <c>source</c> для <c>AdjustAdRevenue</c>, которым Adjust опознаёт выручку
        /// от медиации MAX.
        /// <para>
        /// ⚠️ Строка задана по документации Adjust по интеграции с AppLovin MAX; в вендоренном
        /// Adjust SDK констант для источников нет, поэтому подтвердить её по коду репозитория
        /// нельзя. Если значение разойдётся с ожидаемым, выручка не потеряется — она попадёт в
        /// Adjust под неизвестным источником, и это будет видно в дашборде. Проверять там же.
        /// </para>
        /// </summary>
        private const string AdjustAdRevenueSource = "applovin_max_sdk";

        private const string InterstitialPlacement = "interstitial";
        private const string RewardedPlacement = "rewarded";

        #region Показы

        public static void ReportInterRequested(string placement) =>
            ReportSimple(InterRequestedEvent, placement);

        public static void ReportRewardRequested(string placement) =>
            ReportSimple(RewardRequestedEvent, placement);

        /// <summary>Запрос закрыт без рекламы: готового ad'а не было.</summary>
        public static void ReportNoFill(string placement, bool sdkInitialized)
        {
            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty,
                ["sdk_initialized"] = sdkInitialized ? "1" : "0"
            };

            Report(NoFillEvent, args, alsoAdjust: false);
        }

        /// <summary>
        /// Показ состоялся. Уходит и в Adjust — как у кросс-промо: показ это ключевое событие
        /// воронки, по нему считаются когорты.
        /// </summary>
        public static void ReportDisplayed(string placement, MaxSdkBase.AdInfo adInfo)
        {
            string eventName = placement == RewardedPlacement ? RewardDisplayedEvent : InterDisplayedEvent;
            Report(eventName, BuildArgs(placement, adInfo), alsoAdjust: true);
        }

        public static void ReportDisplayFailed(string placement, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            string eventName = placement == RewardedPlacement ? RewardDisplayFailedEvent : InterDisplayFailedEvent;

            var args = BuildArgs(placement, adInfo);
            args["reason"] = errorInfo != null && !string.IsNullOrEmpty(errorInfo.Message)
                ? errorInfo.Message
                : "unknown";

            if (errorInfo != null)
                args["error_code"] = ((int)errorInfo.Code).ToString(CultureInfo.InvariantCulture);

            Report(eventName, args, alsoAdjust: false);
        }

        /// <summary>Клик по рекламе — как и показ, уходит в оба трекера.</summary>
        public static void ReportClicked(string placement, MaxSdkBase.AdInfo adInfo)
        {
            string eventName = placement == RewardedPlacement ? RewardClickedEvent : InterClickedEvent;
            Report(eventName, BuildArgs(placement, adInfo), alsoAdjust: true);

            // Собственный бэкенд: mediation_click. Отдельный тип события, потому что cp_click
            // требует paid_app_id, которого у показа медиации нет.
            var core = AmznGoDSDKCore.Instance;
            if (core == null || adInfo == null)
                return;

            try
            {
                core.TrackAnalyticsMediationClick(adInfo.NetworkName, adInfo.AdUnitIdentifier, placement);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] backend mediation_click failed: {ex.Message}");
            }
        }

        public static void ReportHidden(string placement, MaxSdkBase.AdInfo adInfo)
        {
            string eventName = placement == RewardedPlacement ? RewardHiddenEvent : InterHiddenEvent;
            Report(eventName, BuildArgs(placement, adInfo), alsoAdjust: false);
        }

        /// <summary>Награда за rewarded выдана — отдельное событие для экономики.</summary>
        public static void ReportRewardEarned(MaxSdkBase.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            var args = BuildArgs(RewardedPlacement, adInfo);

            // MaxSdkBase.Reward — struct, а не класс: проверка на null тут невозможна
            // (CS0019). Пустой Label при этом штатен — сеть может не прислать подпись награды.
            args["reward_label"] = reward.Label ?? string.Empty;
            args["reward_amount"] = reward.Amount.ToString(CultureInfo.InvariantCulture);

            Report(RewardEarnedEvent, args, alsoAdjust: true);
        }

        #endregion

        #region Ad revenue

        /// <summary>
        /// Ретранслирует impression-level revenue из MAX в трекеры.
        ///
        /// <para>Adjust и AppMetrica получают выручку СВОИМИ типами (<c>AdjustAdRevenue</c> /
        /// <c>AdRevenue</c>), а не обычным событием: только так она попадает в отчёты по ROAS,
        /// а не в общую ленту событий.</para>
        ///
        /// <para>Вызывается из <c>OnAdRevenuePaidEvent</c>, то есть по одному разу на показ.
        /// Пропущенный вызов — это молча потерянные деньги в отчётности, поэтому исключения
        /// глушатся по отдельности: сбой одного трекера не должен отменять отправку в другой.</para>
        /// </summary>
        public static void ReportAdRevenue(string placement, MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null)
                return;

            ReportAdRevenueToAdjust(placement, adInfo);
            ReportAdRevenueToAppMetrica(placement, adInfo);

            // Собственный бэкенд (/v1/events, событие mediation_impression). Шлём именно здесь,
            // а не по OnAdDisplayedEvent: MAX отдаёт OnAdRevenuePaidEvent ровно один раз на показ,
            // и только в нём есть выручка — иначе понадобился бы второй запрос ради суммы.
            var core = AmznGoDSDKCore.Instance;
            if (core != null)
            {
                try
                {
                    core.TrackAnalyticsMediationImpression(
                        adInfo.NetworkName,
                        adInfo.AdUnitIdentifier,
                        placement,
                        adInfo.Revenue,
                        adInfo.RevenuePrecision);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AppLovinAnalytics] backend mediation_impression failed: {ex.Message}");
                }
            }

            // Плюс плоское событие в AppMetrica — там сумма показа нужна рядом с остальной воронкой.
            Report("mediation_ad_revenue", BuildArgs(placement, adInfo), alsoAdjust: false);
        }

        private static void ReportAdRevenueToAdjust(string placement, MaxSdkBase.AdInfo adInfo)
        {
#if AMZN_ADJUST_ENABLED
            try
            {
                var adRevenue = new AdjustAdRevenue(AdjustAdRevenueSource);
                adRevenue.SetRevenue(adInfo.Revenue, "USD");   // MAX всегда отдаёт выручку в USD
                adRevenue.AdRevenueNetwork = adInfo.NetworkName;
                adRevenue.AdRevenueUnit = adInfo.AdUnitIdentifier;
                adRevenue.AdRevenuePlacement = placement;

                Adjust.TrackAdRevenue(adRevenue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] Adjust ad revenue failed: {ex.Message}");
            }
#endif
        }

        private static void ReportAdRevenueToAppMetrica(string placement, MaxSdkBase.AdInfo adInfo)
        {
#if AMZN_APPMETRICA_ENABLED
            try
            {
                var adRevenue = new AdRevenue(adInfo.Revenue, "USD")
                {
                    AdNetwork = adInfo.NetworkName,
                    AdUnitId = adInfo.AdUnitIdentifier,
                    AdPlacementName = placement,
                    AdType = placement == RewardedPlacement ? AdType.Rewarded : AdType.Interstitial,

                    // Precision — насколько точна сумма (exact / estimated / publisher_defined /
                    // undisclosed). Без неё выручку нельзя корректно агрегировать.
                    Precision = adInfo.RevenuePrecision
                };

                AppMetrica.ReportAdRevenue(adRevenue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] AppMetrica ad revenue failed: {ex.Message}");
            }
#endif
        }

        #endregion

        #region Внутреннее

        /// <summary>
        /// Поля показа из <c>adInfo</c>. Пустой adInfo допустим: в display_failed MAX может
        /// прислать его без сети и без выручки, и терять из-за этого само событие нельзя.
        /// </summary>
        private static Dictionary<string, string> BuildArgs(string placement, MaxSdkBase.AdInfo adInfo)
        {
            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty
            };

            if (adInfo == null)
                return args;

            if (!string.IsNullOrEmpty(adInfo.NetworkName))
                args["network"] = adInfo.NetworkName;

            if (!string.IsNullOrEmpty(adInfo.AdUnitIdentifier))
                args["ad_unit"] = adInfo.AdUnitIdentifier;

            if (!string.IsNullOrEmpty(adInfo.NetworkPlacement))
                args["network_placement"] = adInfo.NetworkPlacement;

            // Инвариантная культура: на локали с запятой в разделителе значение уехало бы
            // в аналитику как "0,0123" и разобралось бы как другое число.
            args["revenue"] = adInfo.Revenue.ToString("F6", CultureInfo.InvariantCulture);

            if (!string.IsNullOrEmpty(adInfo.RevenuePrecision))
                args["revenue_precision"] = adInfo.RevenuePrecision;

            return args;
        }

        private static void ReportSimple(string eventName, string placement)
        {
            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty
            };

            Report(eventName, args, alsoAdjust: false);
        }

        private static void Report(string eventName, Dictionary<string, string> args, bool alsoAdjust)
        {
            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            try
            {
                core.ReportEventAppMetrica(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] AppMetrica report failed for '{eventName}': {ex.Message}");
            }

            if (!alsoAdjust)
                return;

            try
            {
                core.ReportEventAdjust(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] Adjust report failed for '{eventName}': {ex.Message}");
            }
        }

        #endregion
    }
}
#endif
