using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InternetConnectionSettingData : ModuleSettingData
    {
        public float CheckIntervalSeconds = 5f;
        public bool PauseGameWhenOffline = true;
        public bool ShowBanner = true;
    }
}

