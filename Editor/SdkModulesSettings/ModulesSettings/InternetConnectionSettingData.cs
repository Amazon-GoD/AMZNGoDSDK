using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class InternetConnectionSettingData : ModuleSettingData
    {
        public float CheckIntervalSeconds = 5f;
        public bool UseHttpProbe = true;
        public string ProbeUrl = "https://clients3.google.com/generate_204";
        public float ProbeTimeoutSeconds = 5f;
        public bool PauseGameWhenOffline = true;
        public bool ShowBanner = true;
        public string BannerMessage = "No internet connection...";
        public bool ShowRetryButton = true;
        public string RetryButtonLabel = "Try Again";
        public int BannerSortingOrder = 10000;
    }
}

