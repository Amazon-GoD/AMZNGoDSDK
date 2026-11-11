using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class AppLovinSettingData : ModuleSettingData
    {
        public string SdkKey;
        public string AdMobAndroidAppId;
        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;
        public string BannerAdUnitId;
        public Runtime.AppLovinBannerPosition BannerPosition = Runtime.AppLovinBannerPosition.BottomCenter;
        public bool BannerFallbackDisabled;
        public string InterstitialAdPlacement = Runtime.AppLovinSettingData.DefaultInterstitialAdPlacement;
        public string RewardedAdPlacement = Runtime.AppLovinSettingData.DefaultRewardedAdPlacement;
        public bool VerboseLogging;
    }
}
