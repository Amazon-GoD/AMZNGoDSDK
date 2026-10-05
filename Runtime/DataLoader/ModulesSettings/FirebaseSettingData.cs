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
        // Default Remote Config key; retained for compatibility with existing integrations.
        public const string AdjustEnableTestId = "adjust_enable";
        public const string AdjustEnabledGroup = "true";
        public const string AdjustDisabledGroup = "false";

        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
        // New installations resolve the built-in Adjust flag without extra setup.
        public bool EnableRemoteConfig = true;
        public string AdjustEnableRemoteConfigKey = AdjustEnableTestId;
        public int RemoteConfigFetchTimeoutSeconds = DefaultFetchTimeoutSeconds;
        public int RemoteConfigMinimumFetchIntervalSeconds = DefaultMinimumFetchIntervalSeconds;
        public string ABTestConstantsPath = DefaultABTestConstantsPath;
        public List<ABTestEntry> ABTests = new List<ABTestEntry>();

        public static bool IsValidRemoteConfigKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 100) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')
                    || character == '_' || (i > 0 && character >= '0' && character <= '9')) continue;
                return false;
            }
            return true;
        }

        public static string NormalizeAdjustEnableRemoteConfigKey(string value)
        {
            string key = value?.Trim();
            return IsValidRemoteConfigKey(key) ? key : AdjustEnableTestId;
        }
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

