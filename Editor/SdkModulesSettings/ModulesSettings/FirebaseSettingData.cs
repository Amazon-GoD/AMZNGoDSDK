using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class FirebaseSettingData : ModuleSettingData
    {
        public const int DefaultFetchTimeoutSeconds = Runtime.FirebaseSettingData.DefaultFetchTimeoutSeconds;
        public const int DefaultMinimumFetchIntervalSeconds = Runtime.FirebaseSettingData.DefaultMinimumFetchIntervalSeconds;

        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
        public bool EnableRemoteConfig;
        public int RemoteConfigFetchTimeoutSeconds = DefaultFetchTimeoutSeconds;
        public int RemoteConfigMinimumFetchIntervalSeconds = DefaultMinimumFetchIntervalSeconds;
    }
}

