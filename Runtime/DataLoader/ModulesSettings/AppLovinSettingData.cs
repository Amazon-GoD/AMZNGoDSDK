using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class AppLovinSettingData : ModuleSettingData
    {
        public const string DefaultInterstitialAdPlacement = "interstitial";
        public const string DefaultRewardedAdPlacement = "rewarded";

        /// <summary>
        /// Дублирует ключ из AppLovin Integration Manager. Пустая строка — значит берём
        /// значение, которое Integration Manager уже положил в AppLovinSettings.
        /// </summary>
        public string SdkKey;

        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;
        public string InterstitialAdPlacement = DefaultInterstitialAdPlacement;
        public string RewardedAdPlacement = DefaultRewardedAdPlacement;

        /// <summary>Подробный лог MAX. Держать выключенным в релизных сборках.</summary>
        public bool VerboseLogging;

        /// <summary>Старые конфиги и пустые поля сохраняют прежние названия плейсментов.</summary>
        public static string NormalizeAdPlacement(string placement, string defaultPlacement) =>
            string.IsNullOrWhiteSpace(placement) ? defaultPlacement : placement.Trim();
    }
}
