using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class CrossPromoSettingData : ModuleSettingData
    {
        public string ConfigUrl;
        public string AppodealSdkKey;
        public string MaxSdkKey;
        public string InterstitialId;
        public string RewardedId;
        public CrossPromoProviderType ProviderType = CrossPromoProviderType.Appodeal;
    }
}

