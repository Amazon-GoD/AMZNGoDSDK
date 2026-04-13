using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;

        public string TrackingBaseUrl;
        public string TrackingApiKey;
        public CrossPromoAppType AppType;
        public string DefaultPromotedAppId;
        public VideoPlayerBackend VideoBackend;
    }
}
