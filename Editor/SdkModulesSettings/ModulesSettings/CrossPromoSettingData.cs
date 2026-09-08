using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        [UnityEngine.Tooltip("URL мастер JSON (Packages: PackageName → ConfigUrl) или прямого JSON с креативами. Загружается при запуске игры.")]
        public string ConfigUrl;

        // Video-бэкенд в настройках больше не выбирается — всегда ExoPlayer.
        // DefaultPromotedAppId убран из настроек (всегда дефолтный/пустой на рантайме).
        public VideoPlayerBackend VideoBackend = VideoPlayerBackend.ExoPlayer;
    }
}
