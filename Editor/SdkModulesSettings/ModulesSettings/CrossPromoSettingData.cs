using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;
        public string DefaultPromotedAppId;
        public VideoPlayerBackend VideoBackend;
    }
}
