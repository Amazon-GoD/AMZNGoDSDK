using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class FirebaseSettingData : ModuleSettingData
    {
        public const int DefaultFetchTimeoutSeconds = 10;
        public const int DefaultMinimumFetchIntervalSeconds = 43200;

        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
        // Opt-in: upgrading existing projects does not introduce network fetches.
        public bool EnableRemoteConfig;
        public int RemoteConfigFetchTimeoutSeconds = DefaultFetchTimeoutSeconds;
        public int RemoteConfigMinimumFetchIntervalSeconds = DefaultMinimumFetchIntervalSeconds;
    }
}

