using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InternetConnectionSettingData : ModuleSettingData
    {
        public float CheckIntervalSeconds = 5f;
        public bool PauseGameWhenOffline = true;
        public bool ShowBanner = true;
        public string BannerMessage = "No internet connection...";
        public bool ShowRetryButton = true;
        public string RetryButtonLabel = "Try Again";
        public int BannerSortingOrder = 10000;
    }
}

