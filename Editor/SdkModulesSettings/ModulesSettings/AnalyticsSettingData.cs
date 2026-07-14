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
        // Единственный источник правды — константы в runtime-классе, чтобы зеркала не разъехались.
        public string BaseUrl = Runtime.AnalyticsSettingData.DefaultBaseUrl;
        public string ApiKey = Runtime.AnalyticsSettingData.DefaultApiKey;
        public AnalyticsAppType AppType;
    }
}
