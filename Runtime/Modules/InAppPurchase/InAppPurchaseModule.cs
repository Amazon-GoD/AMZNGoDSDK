#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using com.amazon.device.iap.cpt;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class InAppPurchaseModule : ModuleBase
    {
        private InAppPurchaseSettingData settings;

        private IAmazonIapV2 iapService;
        private readonly HashSet<string> registeredProductIds = new();
        private readonly Dictionary<string, ProductData> productDataCache = new();
        private readonly HashSet<string> ownedSkus = new();

        private readonly Dictionary<ConsumableRewardType, Action<string, int>> _rewardTypeHandlers =
            new();
        private Action<string, int> _defaultConsumableRewardSetter;

        public Action<string> OnPurchaseComplete;
        public Action<string> OnPurchaseFailedCallback;

        public bool IsInitialized { get; private set; } = false;

        private Dictionary<string, SubscriptionStatus> subscriptionStatuses = new();

        private Action<bool> _restoreCallback;
        private bool _isRestoring;

        private void Awake()
        {
            _defaultConsumableRewardSetter = DefaultConsumableRewardSetter;
            _rewardTypeHandlers[ConsumableRewardType.Default] = _defaultConsumableRewardSetter;
        }

        public void Construct(InAppPurchaseSettingData iapSettings)
        {
            settings = iapSettings;
            Enabled = iapSettings.Enabled;
        }

        public override void Initialize()
        {
            if (!Enabled)
                return;

            InitializeAmazonIAP();
        }

        private void InitializeAmazonIAP()
        {
            try
            {
                iapService = AmazonIapV2Impl.Instance;

                iapService.AddGetProductDataResponseListener(OnGetProductDataResponse);
                iapService.AddPurchaseResponseListener(OnPurchaseResponseHandler);
                iapService.AddGetPurchaseUpdatesResponseListener(OnGetPurchaseUpdatesResponse);

                var allSkus = new List<string>();

                foreach (var subscription in settings.SubscriptionProducts.Where(s => s.Enabled))
                {
                    if (string.IsNullOrWhiteSpace(subscription.ProductId))
                        continue;

                    if (!registeredProductIds.Add(subscription.ProductId))
                        continue;

                    allSkus.Add(subscription.ProductId);
                    RegisterSubscriptionStatus(subscription);
                }

                foreach (var consumable in settings.ConsumableProducts.Where(c => c.Enabled))
                {
                    if (string.IsNullOrWhiteSpace(consumable.ProductId))
                        continue;

                    if (!registeredProductIds.Add(consumable.ProductId))
                        continue;

                    if (string.IsNullOrWhiteSpace(consumable.RewardKey))
                        consumable.RewardKey = consumable.ProductId;

                    allSkus.Add(consumable.ProductId);
                }

                if (allSkus.Count > 0)
                {
                    iapService.GetProductData(new SkusInput { Skus = allSkus });
                }

                iapService.GetPurchaseUpdates(new ResetInput { Reset = true });

                Debug.Log("[AMZNGoDSDK] Amazon IAP initialized");
                IsInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZNGoDSDK] Amazon IAP initialization error: {e.Message}");
                IsInitialized = false;
                iapService = null;
                return;
            }

            CheckExpiredSubscriptions();
        }

        private void OnGetProductDataResponse(GetProductDataResponse response)
        {
            if (response.Status != "SUCCESSFUL")
            {
                Debug.LogWarning($"[AMZNGoDSDK] GetProductData failed: {response.Status}");
                return;
            }

            if (response.ProductDataMap != null)
            {
                foreach (var kvp in response.ProductDataMap)
                    productDataCache[kvp.Key] = kvp.Value;
            }

            if (response.UnavailableSkus != null && response.UnavailableSkus.Count > 0)
                Debug.LogWarning($"[AMZNGoDSDK] Unavailable SKUs: {string.Join(", ", response.UnavailableSkus)}");

            Debug.Log($"[AMZNGoDSDK] Product data loaded: {productDataCache.Count} products");
        }

        private void OnPurchaseResponseHandler(PurchaseResponse response)
        {
            if (response.Status == "SUCCESSFUL" || response.Status == "ALREADY_PURCHASED")
            {
                var receipt = response.PurchaseReceipt;
                if (receipt == null)
                    return;

                string productId = receipt.Sku;
                ownedSkus.Add(productId);

                if (response.Status == "SUCCESSFUL")
                {
                    var subscriptionProduct = settings.SubscriptionProducts
                        .FirstOrDefault(s => s.ProductId == productId);

                    if (subscriptionProduct != null)
                    {
                        Debug.Log($"[AMZNGoDSDK] Subscription purchased: {productId}");
                        ExtendSubscription(subscriptionProduct);
                        GiveSubscriptionReward(subscriptionProduct);
                        GrantSubscriptionConsumables(subscriptionProduct);
                    }
                    else
                    {
                        var consumableProduct = settings.ConsumableProducts
                            .FirstOrDefault(c => c.ProductId == productId);

                        if (consumableProduct != null)
                        {
                            Debug.Log($"[AMZNGoDSDK] Product purchased: {productId}");
                            GiveConsumableReward(consumableProduct);
                        }
                    }
                }

                iapService.NotifyFulfillment(new NotifyFulfillmentInput
                {
                    ReceiptId = receipt.ReceiptId,
                    FulfillmentResult = "FULFILLED"
                });

                OnPurchaseComplete?.Invoke(productId);
            }
            else
            {
                string productId = response.PurchaseReceipt?.Sku ?? "unknown";
                Debug.LogWarning($"[AMZNGoDSDK] Purchase failed: {productId} - {response.Status}");
                OnPurchaseFailedCallback?.Invoke(productId);
            }
        }

        private void OnGetPurchaseUpdatesResponse(GetPurchaseUpdatesResponse response)
        {
            if (response.Status != "SUCCESSFUL")
            {
                Debug.LogWarning($"[AMZNGoDSDK] GetPurchaseUpdates failed: {response.Status}");
                CompleteRestore(false);
                return;
            }

            if (response.Receipts != null)
            {
                foreach (var receipt in response.Receipts)
                {
                    if (receipt.CancelDate != 0)
                    {
                        ownedSkus.Remove(receipt.Sku);
                        continue;
                    }

                    ownedSkus.Add(receipt.Sku);

                    if (receipt.ProductType == "SUBSCRIPTION")
                    {
                        var sub = settings.SubscriptionProducts
                            .FirstOrDefault(s => s.ProductId == receipt.Sku);

                        if (sub != null && !HasSubscription(sub.ProductId))
                            ExtendSubscription(sub);
                    }

                    iapService.NotifyFulfillment(new NotifyFulfillmentInput
                    {
                        ReceiptId = receipt.ReceiptId,
                        FulfillmentResult = "FULFILLED"
                    });
                }
            }

            if (response.HasMore)
            {
                iapService.GetPurchaseUpdates(new ResetInput { Reset = false });
            }
            else
            {
                CompleteRestore(true);

                foreach (var status in subscriptionStatuses.Values.Where(s => s.IsActive))
                    Debug.Log($"[AMZNGoDSDK] Active subscription: {status.ProductId} (until {status.ExpiresAt.ToLocalTime():G})");
            }
        }

        private void CompleteRestore(bool success)
        {
            if (!_isRestoring)
                return;

            _restoreCallback?.Invoke(success);
            _restoreCallback = null;
            _isRestoring = false;
        }

        public void RestorePurchases(Action<bool> onComplete = null)
        {
            if (iapService == null)
            {
                Debug.LogWarning("[AMZNGoDSDK] Cannot restore purchases — not initialized");
                onComplete?.Invoke(false);
                return;
            }

            _restoreCallback = onComplete;
            _isRestoring = true;
            iapService.GetPurchaseUpdates(new ResetInput { Reset = true });
        }

        public void BuyProduct(string productId)
        {
            if (iapService == null)
            {
                Debug.LogError("[AMZNGoDSDK] Store not initialized. Purchase impossible.");
                return;
            }

            if (!registeredProductIds.Contains(productId))
            {
                Debug.LogError($"[AMZNGoDSDK] Product not registered: {productId}");
                return;
            }

            iapService.Purchase(new SkuInput { Sku = productId });
        }

        public bool HasSubscription(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
                return false;

            return subscriptionStatuses.TryGetValue(productId, out var status) && status.IsActive;
        }

        public bool IsSubscribed(string productId) =>
            HasSubscription(productId);

        public bool HasReceipt(string productId)
        {
            return ownedSkus.Contains(productId);
        }

        public ProductData GetProduct(string productId)
        {
            productDataCache.TryGetValue(productId, out var data);
            return data;
        }

        public IEnumerable<ProductData> GetAllProducts()
        {
            return productDataCache.Values;
        }

        private void RegisterSubscriptionStatus(SubscriptionProduct subscription)
        {
            if (subscription == null || string.IsNullOrWhiteSpace(subscription.ProductId))
                return;

            if (!subscriptionStatuses.TryGetValue(subscription.ProductId, out var status))
            {
                status = new SubscriptionStatus
                {
                    ProductId = subscription.ProductId,
                    RewardAmount = subscription.RewardAmount,
                    ExpiresAt = LoadSubscriptionExpiration(subscription.ProductId)
                };

                subscriptionStatuses[subscription.ProductId] = status;
            }
            else
            {
                status.RewardAmount = subscription.RewardAmount;

                if (status.ExpiresAt == DateTime.MinValue)
                    status.ExpiresAt = LoadSubscriptionExpiration(subscription.ProductId);
            }
        }

        private void GiveSubscriptionReward(SubscriptionProduct subscription)
        {
            int currentCoins = PlayerPrefs.GetInt("TotalCoins", 0);
            PlayerPrefs.SetInt("TotalCoins", currentCoins + subscription.RewardAmount);
            PlayerPrefs.Save();

            Debug.Log($"[AMZNGoDSDK] Subscription reward {subscription.ProductId}: {subscription.RewardAmount} coins");
        }

        private void GiveConsumableReward(ConsumableProduct consumable)
        {
            if (consumable == null)
                return;

            string rewardKey = string.IsNullOrWhiteSpace(consumable.RewardKey)
                ? consumable.ProductId
                : consumable.RewardKey;

            int amount = Math.Max(1, consumable.RewardAmount);
            ConsumableRewardType rewardType = consumable.RewardType;

            ApplyConsumableReward(rewardKey, amount, rewardType);
        }

        private void CheckExpiredSubscriptions()
        {
            foreach (var status in subscriptionStatuses.Values)
            {
                if (status.IsActive || status.ExpiresAt == DateTime.MinValue)
                    continue;

                Debug.LogWarning($"[AMZNGoDSDK] Subscription expired: {status.ProductId} (until {status.ExpiresAt:G})");
                SaveSubscriptionStatus(status);
            }
        }

        private void ExtendSubscription(SubscriptionProduct subscription)
        {
            if (subscription == null || !subscriptionStatuses.TryGetValue(subscription.ProductId, out var status))
                return;

            DateTime now = DateTime.UtcNow;
            DateTime basePoint = status.IsActive ? status.ExpiresAt : now;
            int durationDays = Math.Max(1, subscription.DurationDays);
            status.ExpiresAt = basePoint.AddDays(durationDays);

            SaveSubscriptionStatus(status);
        }

        private void GrantSubscriptionConsumables(SubscriptionProduct subscription)
        {
            if (subscription?.ConsumableRewards == null || subscription.ConsumableRewards.Count == 0)
                return;

            foreach (var reward in subscription.ConsumableRewards)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.ProductId))
                    continue;

                int amount = Math.Max(1, reward.RewardAmount);
                string rewardKey = string.IsNullOrWhiteSpace(reward.RewardKey) ? reward.ProductId : reward.RewardKey;
                ConsumableRewardType rewardType = reward.RewardType;

                if (rewardType == default)
                    rewardType = ResolveConsumableRewardType(reward.ProductId);

                ApplyConsumableReward(rewardKey, amount, rewardType);
            }
        }

        private void ApplyConsumableReward(string rewardKey, int rewardAmount, ConsumableRewardType rewardType)
        {
            if (string.IsNullOrWhiteSpace(rewardKey) || rewardAmount <= 0)
                return;

            if (!_rewardTypeHandlers.TryGetValue(rewardType, out var handler))
                handler = _defaultConsumableRewardSetter ?? DefaultConsumableRewardSetter;

            handler.Invoke(rewardKey, rewardAmount);
            Debug.Log($"[AMZNGoDSDK] Reward ({rewardType}) {rewardKey}: {rewardAmount}");
        }

        private ConsumableRewardType ResolveConsumableRewardType(string productId)
        {
            var consumable = settings.ConsumableProducts.FirstOrDefault(c => c.ProductId == productId);

            if (consumable == null)
                return ConsumableRewardType.Default;

            return consumable.RewardType;
        }

        private DateTime LoadSubscriptionExpiration(string productId)
        {
#if UNITY_EDITOR
            return DateTime.MinValue;
#else
            var stored = PlayerPrefs.GetString($"SubscriptionExpires_{productId}", "");
            return DateTime.TryParse(stored, out var expiration) ? expiration : DateTime.MinValue;
#endif
        }

        private void SaveSubscriptionStatus(SubscriptionStatus status)
        {
            if (status == null)
                return;

            PlayerPrefs.SetString($"SubscriptionExpires_{status.ProductId}", status.ExpiresAt.ToString("o"));
            PlayerPrefs.SetInt($"SubscriptionStatus_{status.ProductId}", status.IsActive ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetPurchaseCompleteCallback(System.Action<string> callback)
        {
            OnPurchaseComplete = callback;
        }

        public void SetPurchaseFailedCallback(System.Action<string> callback)
        {
            OnPurchaseFailedCallback = callback;
        }

        public void AddPurchaseCompleteCallback(System.Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseComplete += callback;
        }

        public void RemovePurchaseCompleteCallback(System.Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseComplete -= callback;
        }

        public void AddPurchaseFailedCallback(System.Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseFailedCallback += callback;
        }

        public void RemovePurchaseFailedCallback(System.Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseFailedCallback -= callback;
        }

        public void SetConsumableRewardSetter(Action<string, int> rewardSetter)
        {
            _defaultConsumableRewardSetter = rewardSetter ?? DefaultConsumableRewardSetter;
            _rewardTypeHandlers[ConsumableRewardType.Default] = _defaultConsumableRewardSetter;
        }

        public void RegisterConsumableRewardType(ConsumableRewardType rewardType, Action<string, int> handler)
        {
            if (handler == null)
                return;

            _rewardTypeHandlers[rewardType] = handler;
        }

        public override void Cleanup()
        {
            if (iapService != null)
            {
                iapService.RemoveGetProductDataResponseListener(OnGetProductDataResponse);
                iapService.RemovePurchaseResponseListener(OnPurchaseResponseHandler);
                iapService.RemoveGetPurchaseUpdatesResponseListener(OnGetPurchaseUpdatesResponse);
            }
        }

        [Serializable]
        private class SubscriptionStatus
        {
            public string ProductId;
            public int RewardAmount;
            public DateTime ExpiresAt;

            public bool IsActive => ExpiresAt > DateTime.UtcNow;
        }

        private void DefaultConsumableRewardSetter(string rewardKey, int rewardAmount)
        {
            int current = PlayerPrefs.GetInt(rewardKey, 0);
            PlayerPrefs.SetInt(rewardKey, current + rewardAmount);
            PlayerPrefs.Save();
        }
    }
}
#endif
