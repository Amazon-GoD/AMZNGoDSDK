namespace AMZNGoDSDK.Runtime
{
    /// <summary>What currently occupies the cross-promo banner slot.</summary>
    public enum BannerProvider
    {
        /// <summary>Nothing is shown: hidden by the game, no-ads, a fullscreen ad, or no banner in the scene.</summary>
        None = 0,

        /// <summary>The cross-promo creative (Unity UI).</summary>
        CrossPromo = 1,

        /// <summary>The native AppLovin MAX banner. It is drawn above all Unity UI.</summary>
        AppLovin = 2
    }
}
