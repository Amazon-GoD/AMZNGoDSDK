using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
        public bool UseAmazonAppStore = true;
        public bool UseFakeStoreInEditor = true;
    }

    [Serializable]
    public class SubscriptionProduct
    {
        public string ProductId;
        public string DisplayName;
        public int RewardAmount;
        public bool Enabled = true;
    }

    [Serializable]
    public class ConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public int RewardAmount;
        public bool Enabled = true;
    }
}
