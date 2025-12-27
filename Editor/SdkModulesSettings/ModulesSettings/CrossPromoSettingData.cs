using System;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
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

