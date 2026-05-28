using System;

namespace AMZNGoDSDK.Runtime
{
    public enum AnalyticsAppType
    {
        Free = 0,
        Paid = 1
    }

    [Serializable]
    public class AnalyticsSettingData : ModuleSettingData
    {
        public string BaseUrl;
        public string ApiKey;
        public AnalyticsAppType AppType;
    }
}
