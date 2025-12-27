using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    internal static class CrossPromoAnalytics
    {
        private const string BannerShowEvent = "crosspromo_banner_show";
        private const string BannerClickEvent = "crosspromo_banner_click";

        public static void ReportBannerShow(BannerData data) =>
            Report(BannerShowEvent, data, "banner_show");

        public static void ReportBannerClick(BannerData data) =>
            Report(BannerClickEvent, data, "banner_click");

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

            core.ReportEventAppMetrica(eventName, args);
            core.ReportEventAdjust(eventName, args);
        }
    }
}

