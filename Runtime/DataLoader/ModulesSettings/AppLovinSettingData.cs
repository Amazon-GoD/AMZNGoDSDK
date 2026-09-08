using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class AppLovinSettingData : ModuleSettingData
    {
        /// <summary>
        /// Дублирует ключ из AppLovin Integration Manager. Пустая строка — значит берём
        /// значение, которое Integration Manager уже положил в AppLovinSettings.
        /// </summary>
        public string SdkKey;

        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;

        /// <summary>Подробный лог MAX. Держать выключенным в релизных сборках.</summary>
        public bool VerboseLogging;
    }
}
