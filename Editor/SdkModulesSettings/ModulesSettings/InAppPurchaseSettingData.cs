using System;
using System.Collections.Generic;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
    }

    [Serializable]
    public class SubscriptionProduct
    {
        public string ProductId;
        public string DisplayName;
        public int RewardAmount;
        public int DurationDays = 30;
        public List<SubscriptionConsumableReward> ConsumableRewards = new();
        public bool Enabled = true;
    }

    [Serializable]
    public class ConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public int RewardAmount;
        public string RewardKey;
        public ConsumableRewardType RewardType = ConsumableRewardType.Default;
        public bool Enabled = true;
    }

    [Serializable]
    public class SubscriptionConsumableReward
    {
        public string ProductId;
        public int RewardAmount;
        public ConsumableRewardType RewardType = ConsumableRewardType.Default;
        public string RewardKey;
    }
}
