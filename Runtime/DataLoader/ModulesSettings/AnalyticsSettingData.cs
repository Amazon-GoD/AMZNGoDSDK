using System;

namespace AMZNGoDSDK.Runtime
{
    public enum AnalyticsAppType
    {
        // None — значение по умолчанию (буквально 0), означающее «тип не выбран».
        // Сбрасывается в него при каждом старте Unity Editor, и билд с ним не собирается
        // (см. AnalyticsAppTypeSessionReset / AnalyticsAppTypeBuildGuard в Editor-сборке),
        // чтобы free/paid никогда не уезжал в аналитику по умолчанию.
        None = 0,
        Free = 1,
        Paid = 2
    }

    [Serializable]
    public class AnalyticsSettingData : ModuleSettingData
    {
        /// <summary>Analytics backend the SDK ships with. Not editable from the SDK Settings window.</summary>
        public const string DefaultBaseUrl = "https://amazon-cross-promo-backend.onrender.com";

        /// <summary>Ingest key for <see cref="DefaultBaseUrl"/>. Write-only ingest — it ships inside the client build.</summary>
        public const string DefaultApiKey = "0b3f17972ba10922c8bf95de0a6372e0b5e4174b6bc082fe1a5c19b8bdf1c876";

        public string BaseUrl = DefaultBaseUrl;
        public string ApiKey = DefaultApiKey;
        public AnalyticsAppType AppType;
    }
}
