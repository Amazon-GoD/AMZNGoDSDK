using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InternetConnectionSettingData : ModuleSettingData
    {
        public float CheckIntervalSeconds = 5f;
        // Reachability only proves a network interface is up; the HTTP probe confirms real
        // internet access. Probe success = any 2xx response from ProbeUrl within the timeout.
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

