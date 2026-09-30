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
    /// <para>Зачем: роутер фасада SDK отдаёт показ в медиацию,
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

        private const string BannerDisplayedEvent = "mediation_banner_displayed";
        private const string BannerClickedEvent = "mediation_banner_clicked";
        private const string BannerLoadFailedEvent = "mediation_banner_load_failed";

        /// <summary>
        /// Показ запрошен, но готового ad'а не было. Отдельное событие: в инвариант
        /// «запросов = показов + ошибок показа» такой отказ не входит — показа не начиналось,
        /// и роутер может продолжить запрос через JSON-фолбэк.
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

        #region Показы

        public static void ReportInterRequested(string placement) =>
            ReportSimple(InterRequestedEvent, placement, AdAnalyticsFormat.Interstitial);

        public static void ReportRewardRequested(string placement) =>
            ReportSimple(RewardRequestedEvent, placement, AdAnalyticsFormat.Rewarded);

        /// <summary>MAX не принял показ: готового ad'а не было.</summary>
        public static void ReportNoFill(string placement, bool sdkInitialized, string adFormat)
        {
            var args = BuildArgs(placement, null, adFormat);
            args["sdk_initialized"] = sdkInitialized ? "1" : "0";

            Report(NoFillEvent, args, alsoAdjust: false);
        }

        /// <summary>
        /// Показ состоялся. Уходит и в Adjust — как у кросс-промо: показ это ключевое событие
        /// воронки, по нему считаются когорты.
        /// </summary>
        public static void ReportDisplayed(string placement, MaxSdkBase.AdInfo adInfo, bool isRewarded)
        {
            string eventName = isRewarded ? RewardDisplayedEvent : InterDisplayedEvent;
            Report(eventName, BuildArgs(placement, adInfo, FullscreenFormat(isRewarded)), alsoAdjust: true);
        }

        public static void ReportDisplayFailed(string placement, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo,
            bool isRewarded)
        {
            string eventName = isRewarded ? RewardDisplayFailedEvent : InterDisplayFailedEvent;

            var args = BuildArgs(placement, adInfo, FullscreenFormat(isRewarded));
            args["reason"] = errorInfo != null && !string.IsNullOrEmpty(errorInfo.Message)
                ? errorInfo.Message
                : "unknown";

            if (errorInfo != null)
                args["error_code"] = ((int)errorInfo.Code).ToString(CultureInfo.InvariantCulture);

            Report(eventName, args, alsoAdjust: false);
        }

        /// <summary>Клик по рекламе — как и показ, уходит в оба трекера.</summary>
        public static void ReportClicked(string placement, MaxSdkBase.AdInfo adInfo, bool isRewarded)
        {
            string eventName = isRewarded ? RewardClickedEvent : InterClickedEvent;
            ReportClickedInternal(eventName, placement, adInfo, FullscreenFormat(isRewarded));
        }

        public static void ReportBannerClicked(string placement, MaxSdkBase.AdInfo adInfo) =>
            ReportClickedInternal(BannerClickedEvent, placement, adInfo, AdAnalyticsFormat.Banner);

        private static void ReportClickedInternal(string eventName, string placement, MaxSdkBase.AdInfo adInfo, string adFormat)
        {
            Report(eventName, BuildArgs(placement, adInfo, adFormat), alsoAdjust: true);

            // Собственный бэкенд: mediation_click. Отдельный тип события, потому что cp_click
            // требует paid_app_id, которого у показа медиации нет.
#if AMZN_ANALYTICS_ENABLED
            var analytics = SdkModuleRegistry.Get<AnalyticsModule>();
            if (analytics == null || adInfo == null)
                return;

            try
            {
                analytics.TrackMediationClick(adInfo.NetworkName, adInfo.AdUnitIdentifier, placement, adFormat);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] backend mediation_click failed: {ex.Message}");
            }
#endif
        }

        public static void ReportHidden(string placement, MaxSdkBase.AdInfo adInfo, bool isRewarded)
        {
            string eventName = isRewarded ? RewardHiddenEvent : InterHiddenEvent;
            Report(eventName, BuildArgs(placement, adInfo, FullscreenFormat(isRewarded)), alsoAdjust: false);
        }

        /// <summary>Награда за rewarded выдана — отдельное событие для экономики.</summary>
        public static void ReportRewardEarned(string placement, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            var args = BuildArgs(placement, adInfo, AdAnalyticsFormat.Rewarded);

            // MaxSdkBase.Reward — struct, а не класс: проверка на null тут невозможна
            // (CS0019). Пустой Label при этом штатен — сеть может не прислать подпись награды.
            args["reward_label"] = reward.Label ?? string.Empty;
            args["reward_amount"] = reward.Amount.ToString(CultureInfo.InvariantCulture);

            Report(RewardEarnedEvent, args, alsoAdjust: true);
        }

        public static void ReportBannerLoadFailed(string placement, string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            var args = BuildArgs(placement, null, AdAnalyticsFormat.Banner);
            args["ad_unit"] = adUnitId ?? string.Empty;
            args["reason"] = errorInfo != null && !string.IsNullOrEmpty(errorInfo.Message)
                ? errorInfo.Message
                : "unknown";
            if (errorInfo != null)
                args["error_code"] = ((int)errorInfo.Code).ToString(CultureInfo.InvariantCulture);
            Report(BannerLoadFailedEvent, args, alsoAdjust: false);
        }

        /// <summary>MAX has no banner displayed callback; revenue confirms each impression, including refreshes.</summary>
        public static void ReportBannerRevenuePaid(string placement, MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null)
                return;
            Report(BannerDisplayedEvent, BuildArgs(placement, adInfo, AdAnalyticsFormat.Banner), alsoAdjust: true);
            ReportAdRevenue(placement, adInfo, AdAnalyticsFormat.Banner);
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
        public static void ReportAdRevenue(string placement, MaxSdkBase.AdInfo adInfo, string adFormat)
        {
            if (adInfo == null)
                return;

            ReportAdRevenueToAdjust(placement, adInfo, adFormat);
            ReportAdRevenueToAppMetrica(placement, adInfo, adFormat);

            // Собственный бэкенд (/v1/events, событие mediation_impression). Шлём именно здесь,
            // а не по OnAdDisplayedEvent: MAX отдаёт OnAdRevenuePaidEvent ровно один раз на показ,
            // и только в нём есть выручка — иначе понадобился бы второй запрос ради суммы.
#if AMZN_ANALYTICS_ENABLED
            var analytics = SdkModuleRegistry.Get<AnalyticsModule>();
            if (analytics != null)
            {
                try
                {
                    analytics.TrackMediationImpression(
                        adInfo.NetworkName,
                        adInfo.AdUnitIdentifier,
                        placement,
                        adInfo.Revenue,
                        adInfo.RevenuePrecision,
                        adFormat);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AppLovinAnalytics] backend mediation_impression failed: {ex.Message}");
                }
            }
#endif

            // Плюс плоское событие в AppMetrica — там сумма показа нужна рядом с остальной воронкой.
            Report("mediation_ad_revenue", BuildArgs(placement, adInfo, adFormat), alsoAdjust: false);
        }

        private static void ReportAdRevenueToAdjust(string placement, MaxSdkBase.AdInfo adInfo, string adFormat)
        {
#if AMZN_ADJUST_ENABLED
            var adjust = SdkModuleRegistry.Get<AdjustModule>();
            if (adjust == null || !adjust.Enabled || !adjust.IsSdkInitialized) return;
            try
            {
                var adRevenue = new AdjustAdRevenue(AdjustAdRevenueSource);
                adRevenue.SetRevenue(adInfo.Revenue, "USD");   // MAX всегда отдаёт выручку в USD
                adRevenue.AdRevenueNetwork = adInfo.NetworkName;
                adRevenue.AdRevenueUnit = adInfo.AdUnitIdentifier;
                adRevenue.AdRevenuePlacement = placement;
                string knownFormat = AdAnalyticsFormat.KnownOrNull(adFormat);
                if (knownFormat != null)
                    adRevenue.AddCallbackParameter("ad_format", knownFormat);

                Adjust.TrackAdRevenue(adRevenue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] Adjust ad revenue failed: {ex.Message}");
            }
#endif
        }

        private static void ReportAdRevenueToAppMetrica(string placement, MaxSdkBase.AdInfo adInfo, string adFormat)
        {
#if AMZN_APPMETRICA_ENABLED
            try
            {
                var adRevenue = new AdRevenue(adInfo.Revenue, "USD")
                {
                    AdNetwork = adInfo.NetworkName,
                    AdUnitId = adInfo.AdUnitIdentifier,
                    AdPlacementName = placement,
                    AdType = adFormat == AdAnalyticsFormat.Banner ? AdType.Banner
                        : adFormat == AdAnalyticsFormat.Rewarded ? AdType.Rewarded
                        : adFormat == AdAnalyticsFormat.Interstitial ? AdType.Interstitial : (AdType?)null,

                    // Precision — насколько точна сумма (exact / estimated / publisher_defined /
                    // undisclosed). Без неё выручку нельзя корректно агрегировать.
                    Precision = adInfo.RevenuePrecision
                };

                string knownFormat = AdAnalyticsFormat.KnownOrNull(adFormat);
                if (knownFormat != null)
                    adRevenue.Payload = new Dictionary<string, string> { ["ad_format"] = knownFormat };

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

        private static string FullscreenFormat(bool isRewarded) =>
            isRewarded ? AdAnalyticsFormat.Rewarded : AdAnalyticsFormat.Interstitial;

        /// <summary>
        /// Поля показа из <c>adInfo</c>. Пустой adInfo допустим: в display_failed MAX может
        /// прислать его без сети и без выручки, и терять из-за этого само событие нельзя.
        /// </summary>
        private static Dictionary<string, string> BuildArgs(string placement, MaxSdkBase.AdInfo adInfo, string adFormat)
        {
            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty
            };

            string knownFormat = AdAnalyticsFormat.KnownOrNull(adFormat);
            if (knownFormat != null)
                args["ad_format"] = knownFormat;

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

        private static void ReportSimple(string eventName, string placement, string adFormat)
        {
            Report(eventName, BuildArgs(placement, null, adFormat), alsoAdjust: false);
        }

        private static void Report(string eventName, Dictionary<string, string> args, bool alsoAdjust)
        {
#if AMZN_APPMETRICA_ENABLED
            try
            {
                var appMetrica = SdkModuleRegistry.Get<AppMetricaModule>();
                if (appMetrica != null && appMetrica.Enabled && appMetrica.Initialized)
                    appMetrica.ReportEvent(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] AppMetrica report failed for '{eventName}': {ex.Message}");
            }
#endif

            if (!alsoAdjust)
                return;

#if AMZN_ADJUST_ENABLED
            try
            {
                var adjust = SdkModuleRegistry.Get<AdjustModule>();
                if (adjust != null && adjust.Enabled)
                    adjust.ReportEvent(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinAnalytics] Adjust report failed for '{eventName}': {ex.Message}");
            }
#endif
        }

        #endregion
    }
}
#endif
