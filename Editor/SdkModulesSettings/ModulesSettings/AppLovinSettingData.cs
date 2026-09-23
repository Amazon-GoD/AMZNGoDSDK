using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class AppLovinSettingData : ModuleSettingData
    {
        public string SdkKey;
        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;
        public string InterstitialAdPlacement = Runtime.AppLovinSettingData.DefaultInterstitialAdPlacement;
        public string RewardedAdPlacement = Runtime.AppLovinSettingData.DefaultRewardedAdPlacement;
        public bool VerboseLogging;
    }
}
