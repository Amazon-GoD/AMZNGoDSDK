using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        // Сохраняется также в открытом окне при domain reload после обновления SDK.
        [UnityEngine.HideInInspector]
        public int ConfigUrlMigrationVersion;

        [UnityEngine.Tooltip("URL мастер JSON (Packages: PackageName → ConfigUrl) или прямого JSON с креативами. Загружается при запуске игры.")]
        public string ConfigUrl = Runtime.CrossPromoSettingData.DefaultConfigUrl;

        // Video-бэкенд в настройках больше не выбирается — всегда ExoPlayer.
        // DefaultPromotedAppId убран из настроек (всегда дефолтный/пустой на рантайме).
        public VideoPlayerBackend VideoBackend = VideoPlayerBackend.ExoPlayer;
    }
}
