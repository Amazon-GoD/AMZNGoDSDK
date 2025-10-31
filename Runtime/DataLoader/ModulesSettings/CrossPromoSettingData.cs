using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;
        public string MaxSdkKey;
        public string InterstitialId;
        public string RewardedId;
    }
}

