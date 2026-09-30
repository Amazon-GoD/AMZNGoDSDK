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

        /// <summary>
        /// Android App ID AdMob для манифеста сборки. Пустое значение сохраняет
        /// App ID, заданный в MAX Integration Manager.
        /// </summary>
        public string AdMobAndroidAppId;

        public string InterstitialAdUnitId;
        public string RewardedAdUnitId;

        /// <summary>Пустое значение сохраняет баннер кросс-промо после исчерпания капов.</summary>
        public string BannerAdUnitId;

        public string InterstitialAdPlacement = DefaultInterstitialAdPlacement;
        public string RewardedAdPlacement = DefaultRewardedAdPlacement;

        /// <summary>Подробный лог MAX. Держать выключенным в релизных сборках.</summary>
        public bool VerboseLogging;

        /// <summary>Старые конфиги и пустые поля сохраняют прежние названия плейсментов.</summary>
        public static string NormalizeAdPlacement(string placement, string defaultPlacement) =>
            string.IsNullOrWhiteSpace(placement) ? defaultPlacement : placement.Trim();
    }
}
