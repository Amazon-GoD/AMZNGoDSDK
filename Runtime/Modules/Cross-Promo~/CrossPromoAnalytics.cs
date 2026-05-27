#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    internal static class CrossPromoAnalytics
    {
        private const string BannerShowEvent = "crosspromo_banner_show";
        private const string BannerClickEvent = "crosspromo_banner_click";
        private const string VideoShowEvent = "crosspromo_video_show";
        private const string VideoClickEvent = "crosspromo_video_click";

        private const string InterRequestedEvent = "crosspromo_inter_requested";
        private const string InterDisplayedEvent = "crosspromo_inter_displayed";
        private const string RewardRequestedEvent = "crosspromo_reward_requested";
        private const string RewardDisplayedEvent = "crosspromo_reward_displayed";

        public static void ReportBannerShow(BannerData data) =>
            Report(BannerShowEvent, data, "banner_show");

        public static void ReportBannerClick(BannerData data) =>
            Report(BannerClickEvent, data, "banner_click");

        public static void ReportVideoShow(BannerData data) =>
            Report(VideoShowEvent, data, "video_show");

        public static void ReportVideoClick(BannerData data) =>
            Report(VideoClickEvent, data, "video_click");

        public static void ReportInterRequested(string placement) =>
            ReportSimple(InterRequestedEvent, placement);

        public static void ReportInterDisplayed(BannerData data, string placement) =>
            Report(InterDisplayedEvent, data, placement);

        public static void ReportRewardRequested(string placement) =>
            ReportSimple(RewardRequestedEvent, placement);

        public static void ReportRewardDisplayed(BannerData data, string placement) =>
            Report(RewardDisplayedEvent, data, placement);

        private static void Report(string eventName, BannerData data, string placement)
        {
            if (data == null)
                return;

            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            var args = new Dictionary<string, string>
            {
                ["title"] = data.title ?? string.Empty,
                ["placement"] = placement
            };

            if (!string.IsNullOrWhiteSpace(data.redirectUrl))
            {
                args["redirectUrl"] = data.redirectUrl;
            }

            if (!string.IsNullOrWhiteSpace(data.trackingUrl))
            {
                args["trackingUrl"] = data.trackingUrl;
            }

            if (!string.IsNullOrWhiteSpace(data.paidAppId))
            {
                args["app_id"] = data.paidAppId;
            }

            SafeReport(core, eventName, args);
        }

        private static void ReportSimple(string eventName, string placement)
        {
            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                return;

            var args = new Dictionary<string, string>
            {
                ["placement"] = placement ?? string.Empty
            };

            SafeReport(core, eventName, args);
        }

        private static void SafeReport(AmznGoDSDKCore core, string eventName, Dictionary<string, string> args)
        {
            try
            {
                core.ReportEventAppMetrica(eventName, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CrossPromoAnalytics] AppMetrica report failed for '{eventName}': {ex.Message}");
            }

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
