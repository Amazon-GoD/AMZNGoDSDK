using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;

        // Video-бэкенд в настройках больше не выбирается — всегда ExoPlayer.
        // DefaultPromotedAppId убран из настроек (всегда дефолтный/пустой на рантайме).
        public VideoPlayerBackend VideoBackend = VideoPlayerBackend.ExoPlayer;
    }
}
