using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
        public AppStoreTarget AppStoreTarget = AppStoreTarget.AmazonAppStore;
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
        public string RewardKey;
        public bool Enabled = true;
    }

    public enum AppStoreTarget
    {
        AmazonAppStore,
        GooglePlay
    }
}
