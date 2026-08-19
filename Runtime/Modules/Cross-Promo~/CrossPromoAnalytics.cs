#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    internal static class CrossPromoAnalytics
    {
        private const string BannerClickEvent = "crosspromo_banner_click";

        private const string InterRequestedEvent = "crosspromo_inter_requested";
        private const string InterDisplayedEvent = "crosspromo_inter_displayed";
        private const string InterClickedEvent = "crosspromo_inter_clicked";
        private const string InterDisplayFailedEvent = "crosspromo_inter_display_failed";

        private const string RewardRequestedEvent = "crosspromo_reward_requested";
        private const string RewardDisplayedEvent = "crosspromo_reward_displayed";
        private const string RewardClickedEvent = "crosspromo_reward_clicked";
        private const string RewardDisplayFailedEvent = "crosspromo_reward_display_failed";

        // Показ отклонён, потому что реклама уже на экране. Это НЕ ошибка показа: запрос для
        // такого вызова вообще не отправляется (см. CrossPromoModule.ShowVideoInternalCoroutine),
        // поэтому событие отдельное и в инвариант «запросов = показов + ошибок» не входит.
        private const string RejectedBusyEvent = "crosspromo_rejected_busy";

        // Плейсменты.
        private const string Interstitial = "interstitial";
        private const string Rewarded = "rewarded";

        // ---- Banner (AppMetrica only). Показ баннера НЕ репортится: он крутится каждые
        //      8 секунд и засорял бы аналитику. Уходит только клик. ----

        public static void ReportBannerClick(BannerData data) =>
            ReportAppMetrica(BannerClickEvent, data, "banner_click");

        // ---- Запрос (AppMetrica only) ----

        public static void ReportInterRequested(string placement) =>
            ReportSimpleAppMetrica(InterRequestedEvent, placement);

        public static void ReportRewardRequested(string placement) =>
            ReportSimpleAppMetrica(RewardRequestedEvent, placement);

        // ---- Показ (AppMetrica + Adjust). Бэкенд-показ (cp_impression) шлётся отдельно
        //      через CrossPromoModule.TrackImpression на первом кадре. ----

        public static void ReportInterDisplayed(BannerData data, string placement) =>
            ReportAppMetricaAndAdjust(InterDisplayedEvent, data, placement);

        public static void ReportRewardDisplayed(BannerData data, string placement) =>
            ReportAppMetricaAndAdjust(RewardDisplayedEvent, data, placement);

        /// <summary>Диспетчер по плейсменту — для оверлеев, которые знают только placement.</summary>
        public static void ReportDisplayed(string placement, BannerData data)
        {
            if (placement == Rewarded) ReportRewardDisplayed(data, placement);
            else if (placement == Interstitial) ReportInterDisplayed(data, placement);
        }

        // ---- Клик (AppMetrica + Adjust). Бэкенд-клик (cp_click) и Adjust-click-URL
        //      шлёт оверлей отдельно и ЖДЁТ их перед открытием стора. ----

        public static void ReportInterClicked(BannerData data, string placement) =>
            ReportAppMetricaAndAdjust(InterClickedEvent, data, placement);

        public static void ReportRewardClicked(BannerData data, string placement) =>
            ReportAppMetricaAndAdjust(RewardClickedEvent, data, placement);

        public static void ReportClicked(string placement, BannerData data)
        {
            if (placement == Rewarded) ReportRewardClicked(data, placement);
            else if (placement == Interstitial) ReportInterClicked(data, placement);
        }

        // ---- Ошибка показа (AppMetrica only) ----

        public static void ReportInterDisplayFailed(string placement, string reason, string errorCode, BannerData data) =>
            ReportDisplayFailedInternal(InterDisplayFailedEvent, placement, reason, errorCode, data);

        public static void ReportRewardDisplayFailed(string placement, string reason, string errorCode, BannerData data) =>
            ReportDisplayFailedInternal(RewardDisplayFailedEvent, placement, reason, errorCode, data);

        /// <summary>
        /// Диспетчер ошибки показа по плейсменту. Для placement, не относящегося к inter/reward
        /// (например прямой ShowVideoPromo без плейсмента), событие не шлётся: разделить его
        /// по воронкам всё равно нельзя, а «одно событие на всё» мы как раз убираем.
        /// </summary>
        public static void ReportDisplayFailed(string placement, string reason, string errorCode, BannerData data)
        {
            if (placement == Rewarded) ReportRewardDisplayFailed(placement, reason, errorCode, data);
            else if (placement == Interstitial) ReportInterDisplayFailed(placement, reason, errorCode, data);
        }

        // ---- Показ отклонён: реклама уже на экране (AppMetrica only) ----

        public static void ReportRejectedBusy(string placement) =>
            ReportSimpleAppMetrica(RejectedBusyEvent, placement);

        // ------------------------------------------------------------------

        private static void ReportDisplayFailedInternal(
            string eventName, string placement, string reason, string errorCode, BannerData data)
        {
            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty,
                ["reason"] = string.IsNullOrEmpty(reason) ? "unknown" : reason
            };

            if (data != null)
            {
                args["title"] = data.title ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(data.paidAppId))
                    args["app_id"] = data.paidAppId;
            }

            if (!string.IsNullOrWhiteSpace(errorCode))
                args["error_code"] = errorCode;

            SafeReportAppMetrica(core, eventName, args);
        }

        private static void ReportAppMetrica(string eventName, BannerData data, string placement)
        {
            if (data == null)
                return;

            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            SafeReportAppMetrica(core, eventName, BuildArgs(data, placement));
        }

        private static void ReportAppMetricaAndAdjust(string eventName, BannerData data, string placement)
        {
            if (data == null)
                return;

            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            var args = BuildArgs(data, placement);
            SafeReportAppMetrica(core, eventName, args);
            SafeReportAdjust(core, eventName, args);
        }

        private static Dictionary<string, string> BuildArgs(BannerData data, string placement)
        {
            var args = new Dictionary<string, string>
            {
                ["title"] = data.title ?? string.Empty,
                ["placement"] = placement
            };

            if (!string.IsNullOrWhiteSpace(data.redirectUrl))
                args["redirectUrl"] = data.redirectUrl;

            if (!string.IsNullOrWhiteSpace(data.trackingUrl))
                args["trackingUrl"] = data.trackingUrl;

            if (!string.IsNullOrWhiteSpace(data.paidAppId))
                args["app_id"] = data.paidAppId;

            return args;
        }

        private static void ReportSimpleAppMetrica(string eventName, string placement)
        {
            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty
            };

            SafeReportAppMetrica(core, eventName, args);
        }

        private static void SafeReportAppMetrica(AmznGoDSDKCore core, string eventName, Dictionary<string, string> args)
        {
            try
            {
                core.ReportEventAppMetrica(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CrossPromoAnalytics] AppMetrica report failed for '{eventName}': {ex.Message}");
            }
        }

        private static void SafeReportAdjust(AmznGoDSDKCore core, string eventName, Dictionary<string, string> args)
        {
            try
            {
                core.ReportEventAdjust(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CrossPromoAnalytics] Adjust report failed for '{eventName}': {ex.Message}");
            }
        }
    }
}
#endif
