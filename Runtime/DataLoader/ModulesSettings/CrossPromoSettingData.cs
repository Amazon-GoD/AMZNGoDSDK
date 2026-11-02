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
        public const string DefaultConfigUrl = "https://amzngod.space/master.json";
        public const int CurrentConfigUrlMigrationVersion = 1;

        // Без initializer: старые JSON без этого поля должны проходить миграцию.
        [UnityEngine.HideInInspector]
        public int ConfigUrlMigrationVersion;

        [UnityEngine.Tooltip("URL of a master JSON (Packages: PackageName → ConfigUrl) or a direct creative JSON. Resolved at game startup.")]
        public string ConfigUrl = DefaultConfigUrl;

        [UnityEngine.Tooltip("Fallback promoted app ID for tracking events when a specific promo doesn't provide its own AppPackageName.")]
        public string DefaultPromotedAppId;

        // Бэкенд плеера больше не выбирается в настройках — всегда ExoPlayer (Android).
        // UnityVideoPlayer-путь оставлен в коде как fallback, но из UI убран.
        public VideoPlayerBackend VideoBackend = VideoPlayerBackend.ExoPlayer;
    }
}
