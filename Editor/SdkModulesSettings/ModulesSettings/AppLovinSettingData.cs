using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class AppLovinSettingData : ModuleSettingData
    {
        public string SdkKey;
        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;
        public bool VerboseLogging;
    }
}
