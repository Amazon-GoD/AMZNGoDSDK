#if AMZN_CROSSPROMO_ENABLED
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Folds the state of every banner instance (several scenes can each hold one) into a
    /// single answer for the game: what occupies the banner slot right now.
    /// </summary>
    internal sealed class CrossPromoBannerSlot
    {
        private readonly Dictionary<int, BannerProvider> _providers = new Dictionary<int, BannerProvider>();

        internal BannerProvider Current { get; private set; } = BannerProvider.None;

        /// <summary>
        /// Whether the banner should ask AppLovin MAX for an ad. With the cross-promo creative
        /// allowed, MAX takes over only after a loaded config has run out of fill (caps). With
        /// the creative switched off by the game, MAX is the only provider left and is used
        /// right away, without waiting for the config or the caps.
        /// </summary>
        internal static bool ShouldRouteToAppLovin(bool moduleEnabled, bool appLovinAllowed,
            bool creativeAllowed, bool configReturnedVideos, bool hasFill)
        {
            if (!moduleEnabled || !appLovinAllowed)
                return false;

            return !creativeAllowed || (configReturnedVideos && !hasFill);
        }

        /// <summary>
        /// What one banner instance shows. <paramref name="appLovinRequested"/>: MAX accepted
        /// the show request; <paramref name="appLovinHasAd"/>: it has an ad to draw. Until then
        /// the creative keeps the slot, unless the game switched the creative off.
        /// </summary>
        internal static BannerProvider Resolve(bool creativeAllowed, bool appLovinRequested, bool appLovinHasAd)
        {
            if (appLovinRequested && appLovinHasAd)
                return BannerProvider.AppLovin;

            return creativeAllowed ? BannerProvider.CrossPromo : BannerProvider.None;
        }

        /// <summary>Records one banner's state. Returns true when the aggregated provider changed.</summary>
        internal bool Report(int bannerId, BannerProvider provider)
        {
            if (provider == BannerProvider.None)
                _providers.Remove(bannerId);
            else
                _providers[bannerId] = provider;

            // The native MAX banner is global, so it wins over any cross-promo instance.
            var aggregated = BannerProvider.None;
            foreach (var pair in _providers)
            {
                if (pair.Value == BannerProvider.AppLovin)
                {
                    aggregated = BannerProvider.AppLovin;
                    break;
                }

                aggregated = BannerProvider.CrossPromo;
            }

            if (aggregated == Current)
                return false;

            Current = aggregated;
            return true;
        }
    }
}
#endif
