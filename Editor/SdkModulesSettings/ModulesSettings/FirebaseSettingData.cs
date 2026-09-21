using System;
using System.Collections.Generic;

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
        public string ABTestConstantsPath = Runtime.FirebaseSettingData.DefaultABTestConstantsPath;
        public List<Runtime.ABTestEntry> ABTests = new List<Runtime.ABTestEntry>();
    }
}

