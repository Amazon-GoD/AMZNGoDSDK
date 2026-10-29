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

        /// <summary>
        /// Console ID из админки BI (Генератор ID), например F000042-0; уходит в first_open как
        /// <c>console_id</c> (см. <see cref="Runtime.ConsoleId"/>). Пусто у старых игр и в конфигах
        /// версий до 1.0.10.
        /// </summary>
        public string ConsoleId = string.Empty;

        /// <summary>
        /// Бандл (Android), для которого вводили <see cref="ConsoleId"/>: окно настроек проставляет его
        /// при каждом изменении ID. Build-гард сверяет его с бандлом сборки — так ловится копия
        /// шаблона, унёсшая чужой ID. Только для редактора, в рантайме не читается.
        /// </summary>
        public string ConsoleIdBundle = string.Empty;
    }
}
