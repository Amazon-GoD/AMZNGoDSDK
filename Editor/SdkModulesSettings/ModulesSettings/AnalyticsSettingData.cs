using System;

namespace AMZNGoDSDK.Editor
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
