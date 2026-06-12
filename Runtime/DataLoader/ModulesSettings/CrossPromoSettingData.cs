using System;

namespace AMZNGoDSDK.Runtime
{
    public enum VideoPlayerBackend
    {
        UnityVideoPlayer = 0,
        ExoPlayer = 1
    }

    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;

        [UnityEngine.Tooltip("Fallback promoted app ID for tracking events when a specific promo doesn't provide its own AppPackageName.")]
        public string DefaultPromotedAppId;

        public VideoPlayerBackend VideoBackend;
    }
}
