#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    internal static class CrossPromoAnalytics
    {
        private const string BannerShowEvent = "crosspromo_banner_show";
        private const string BannerClickEvent = "crosspromo_banner_click";
        private const string VideoShowEvent = "crosspromo_video_show";
        private const string VideoClickEvent = "crosspromo_video_click";

        public static void ReportBannerShow(BannerData data) =>
            Report(BannerShowEvent, data, "banner_show");

        public static void ReportBannerClick(BannerData data) =>
            Report(BannerClickEvent, data, "banner_click");

        public static void ReportVideoShow(BannerData data) =>
            Report(VideoShowEvent, data, "video_show");

        public static void ReportVideoClick(BannerData data) =>
            Report(VideoClickEvent, data, "video_click");

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

            core.ReportEventAppMetrica(eventName, args);
            core.ReportEventAdjust(eventName, args);
        }
    }
}
#endif
