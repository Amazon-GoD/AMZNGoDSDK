using System;

namespace AMZNGoDSDK.Editor
{
    // Зеркало Runtime.AnalyticsAppType — числовые значения обязаны совпадать.
    public enum AnalyticsAppType
    {
        None = 0,
        Free = 1,
        Paid = 2
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
