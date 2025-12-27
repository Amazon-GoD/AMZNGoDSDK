using System;
using System.Collections.Generic;
using UnityEngine.Purchasing;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
        public AppStoreTarget AppStoreTarget = AppStoreTarget.AmazonAppStore;
        public bool UseFakeStoreInEditor = true;
        public List<CatalogImportedProduct> CatalogImportedProducts = new();
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

    [Serializable]
    public class CatalogImportedProduct
    {
        public string ProductId;
        public ProductType Type;
    }

    public enum AppStoreTarget
    {
        AmazonAppStore,
        GooglePlay
    }
}
