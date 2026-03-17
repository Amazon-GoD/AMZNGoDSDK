using System;

namespace AMZNGoDSDK.Runtime
{
    public enum CrossPromoAppType
    {
        Free = 0,
        Paid = 1
    }

    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;
        public string AppodealSdkKey;

        public string TrackingBaseUrl;
        public string TrackingApiKey;
        public CrossPromoAppType AppType;

        [UnityEngine.Tooltip("Fallback promoted app ID for tracking events when a specific promo doesn't provide its own AppPackageName (e.g. Appodeal ads).")]
        public string DefaultPromotedAppId;
    }
}
