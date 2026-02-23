#if AMZN_IAP_ENABLED
using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public enum ConsumableRewardType
    {
        Default = 0,
        BonusCoins = 1,
        PremiumEnergy = 2
    }
}
#endif
