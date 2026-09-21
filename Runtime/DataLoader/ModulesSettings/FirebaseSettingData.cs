using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class FirebaseSettingData : ModuleSettingData
    {
        public const int DefaultFetchTimeoutSeconds = 10;
        public const int DefaultMinimumFetchIntervalSeconds = 43200;
        public const string DefaultABTestConstantsPath = "Assets/AMZNGoDSDK/Runtime/Generated/ABTestConstants.cs";
        public const string AdjustEnableTestId = "adjust_enable";
        public const string AdjustEnabledGroup = "true";
        public const string AdjustDisabledGroup = "false";

        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
        // Opt-in: upgrading existing projects does not introduce network fetches.
        public bool EnableRemoteConfig;
        public int RemoteConfigFetchTimeoutSeconds = DefaultFetchTimeoutSeconds;
        public int RemoteConfigMinimumFetchIntervalSeconds = DefaultMinimumFetchIntervalSeconds;
        public string ABTestConstantsPath = DefaultABTestConstantsPath;
        public List<ABTestEntry> ABTests = new List<ABTestEntry>();
    }

    [Serializable]
    public class ABTestEntry
    {
        public string TestName = "NewTest";
        public string TestId = "test_new";
        public List<string> GroupNames = new List<string> { "GroupA", "GroupB" };
        // Null/empty supports configs from the original package, which had no explicit default.
        public string DefaultGroup;

        public string GetDefaultGroup() => !string.IsNullOrEmpty(DefaultGroup) ? DefaultGroup
            : GroupNames != null && GroupNames.Count > 0 ? GroupNames[0] : null;
    }
}

