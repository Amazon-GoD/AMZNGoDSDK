using System;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Screen anchor of the native MAX banner. Values are serialized into the SDK config:
    /// do not renumber. 0 keeps the bottom-center banner for configs saved without the field.
    /// </summary>
    public enum AppLovinBannerPosition
    {
        BottomCenter = 0,
        TopCenter = 1,
        BottomLeft = 2,
        BottomRight = 3,
        TopLeft = 4,
        TopRight = 5
    }

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

        /// <summary>Default MAX banner anchor; the game can change it at runtime through the facade.</summary>
        public AppLovinBannerPosition BannerPosition = AppLovinBannerPosition.BottomCenter;

        /// <summary>
        /// Keeps the cross-promo banner after the caps are exhausted instead of switching to MAX.
        /// Inverted on purpose: a config saved before this field existed must keep the fallback on.
        /// </summary>
        public bool BannerFallbackDisabled;

        public string InterstitialAdPlacement = DefaultInterstitialAdPlacement;
        public string RewardedAdPlacement = DefaultRewardedAdPlacement;

        /// <summary>Подробный лог MAX. Держать выключенным в релизных сборках.</summary>
        public bool VerboseLogging;

        /// <summary>Старые конфиги и пустые поля сохраняют прежние названия плейсментов.</summary>
        public static string NormalizeAdPlacement(string placement, string defaultPlacement) =>
            string.IsNullOrWhiteSpace(placement) ? defaultPlacement : placement.Trim();
    }
}
