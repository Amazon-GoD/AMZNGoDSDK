namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Canonical analytics formats, independent of configurable placement names.
    /// Unknown formats are omitted from event payloads rather than guessed.
    /// </summary>
    public static class AdAnalyticsFormat
    {
        public const string Interstitial = "interstitial";
        public const string Rewarded = "rewarded";
        public const string Banner = "banner";

        public static string KnownOrNull(string value)
        {
            switch (value)
            {
                case Interstitial:
                case Rewarded:
                case Banner:
                    return value;
                default:
                    return null;
            }
        }
    }
}
