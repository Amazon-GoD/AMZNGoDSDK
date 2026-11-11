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
