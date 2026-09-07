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
        [UnityEngine.Tooltip("URL of a master JSON (Packages: PackageName → ConfigUrl) or a direct creative JSON. Resolved at game startup.")]
        public string ConfigUrl;

        [UnityEngine.Tooltip("Fallback promoted app ID for tracking events when a specific promo doesn't provide its own AppPackageName.")]
        public string DefaultPromotedAppId;

        // Бэкенд плеера больше не выбирается в настройках — всегда ExoPlayer (Android).
        // UnityVideoPlayer-путь оставлен в коде как fallback, но из UI убран.
        public VideoPlayerBackend VideoBackend = VideoPlayerBackend.ExoPlayer;
    }
}
