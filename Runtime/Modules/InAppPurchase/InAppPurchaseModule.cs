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
        // Amazon GetProductData accepts at most 100 SKUs per request.
        private const int ProductDataBatchSize = 100;

        // Persistent set of receipts we have already granted + fulfilled.
        // Guarantees idempotent grants and prevents re-notifying closed receipts.
        private const string FulfilledReceiptsKey = "AMZN_FulfilledReceipts";
        private const char FulfilledReceiptsSeparator = '\n';

        private InAppPurchaseSettingData settings;

        private IAmazonIapV2 iapService;
        private bool _serviceReady;
        private readonly HashSet<string> registeredProductIds = new();
        private readonly Dictionary<string, ProductData> productDataCache = new();
        private readonly HashSet<string> ownedSkus = new();
        private readonly HashSet<string> _fulfilledReceipts = new();

        // SKUs registered during initialization, kept so RetryInitialize can re-request the catalog.
        private readonly List<string> _pendingSkus = new();

        private readonly Dictionary<ConsumableRewardType, Action<string, int>> _rewardTypeHandlers =
            new();
        private Action<string, int> _defaultConsumableRewardSetter;

        public Action<string> OnPurchaseComplete;
        public Action<string> OnPurchaseFailedCallback;

        public bool IsInitialized { get; private set; } = false;

        private Dictionary<string, SubscriptionStatus> subscriptionStatuses = new();

        private Action<bool> _restoreCallback;
        private bool _isRestoring;

        // Cancellation resolution is deferred until the last page (HasMore == false) and evaluated
        // against SKUs seen active across ALL pages, so it can never depend on receipt/page order.
        private readonly HashSet<string> _restoreActiveSkus = new();
        private readonly List<PurchaseReceipt> _restoreCancelledReceipts = new();

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

            LoadFulfilledReceipts();
            InitializeAmazonIAP();
        }

        private void InitializeAmazonIAP()
        {
            if (IsInitialized)
                return;

            // Phase 1 (fatal, done once): acquire the service and register listeners. If it
            // throws, the store stays down but retryable. Listeners are registered exactly once
            // so a retry never double-subscribes.
            if (!_serviceReady)
            {
                try
                {
                    iapService = AmazonIapV2Impl.Instance;

                    iapService.AddGetProductDataResponseListener(OnGetProductDataResponse);
                    iapService.AddPurchaseResponseListener(OnPurchaseResponseHandler);
                    iapService.AddGetPurchaseUpdatesResponseListener(OnGetPurchaseUpdatesResponse);

                    _serviceReady = true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AMZNGoDSDK] Amazon IAP service init error: {e.Message}");
                    iapService = null;
                    _serviceReady = false;
                    IsInitialized = false;
                    return;
                }
            }

            // Phase 2 (non-fatal, retryable): catalog + restore. A failure here must never tear
            // down the live service or leave IsInitialized out of sync — it just stays false so
            // RetryInitialize() can re-run this phase (all steps below are idempotent).
            try
            {
                RegisterProducts();
                RequestProductData();
                RequestPurchaseUpdates();
                CheckExpiredSubscriptions();

                IsInitialized = true;
                Debug.Log("[AMZNGoDSDK] Amazon IAP initialized");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZNGoDSDK] Amazon IAP catalog init error (retryable): {e.Message}");
            }
        }

        /// <summary>
        /// Re-attempts initialization after a failure. If the service is already fully initialized
        /// but the catalog failed to load asynchronously, only the product-data request is retried.
        /// </summary>
        public void RetryInitialize()
        {
            if (!Enabled)
                return;

            if (!IsInitialized)
            {
                InitializeAmazonIAP();
                return;
            }

            RequestProductData();
        }

        private void RegisterProducts()
        {
            _pendingSkus.Clear();

            foreach (var subscription in settings.SubscriptionProducts.Where(s => s.Enabled))
            {
                if (string.IsNullOrWhiteSpace(subscription.ProductId))
                    continue;

                RegisterSubscriptionStatus(subscription);

                if (!registeredProductIds.Add(subscription.ProductId))
                    continue;

                _pendingSkus.Add(subscription.ProductId);
            }

            foreach (var consumable in settings.ConsumableProducts.Where(c => c.Enabled))
            {
                if (string.IsNullOrWhiteSpace(consumable.ProductId))
                    continue;

                if (string.IsNullOrWhiteSpace(consumable.RewardKey))
                    consumable.RewardKey = consumable.ProductId;

                if (!registeredProductIds.Add(consumable.ProductId))
                    continue;

                _pendingSkus.Add(consumable.ProductId);
            }
        }

        private void RequestProductData()
        {
            if (iapService == null || _pendingSkus.Count == 0)
                return;

            // Chunk into batches of 100 — a single over-limit request fails as a whole,
            // taking the entire catalog (prices/titles) down with it.
            for (int i = 0; i < _pendingSkus.Count; i += ProductDataBatchSize)
            {
                var batch = _pendingSkus.GetRange(i, Math.Min(ProductDataBatchSize, _pendingSkus.Count - i));

                try
                {
                    iapService.GetProductData(new SkusInput { Skus = batch });
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AMZNGoDSDK] GetProductData batch [{i}..{i + batch.Count}) failed: {e.Message}");
                }
            }
        }

        private void RequestPurchaseUpdates()
        {
            if (iapService == null)
                return;

            try
            {
                BeginRestoreAccumulators();
                iapService.GetPurchaseUpdates(new ResetInput { Reset = true });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZNGoDSDK] GetPurchaseUpdates failed: {e.Message}");
            }
        }

        private void BeginRestoreAccumulators()
        {
            _restoreActiveSkus.Clear();
            _restoreCancelledReceipts.Clear();
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

                Debug.Log($"[AMZNGoDSDK] Purchase {response.Status}: {productId}");

                // Grant (idempotent by ReceiptId), persist, THEN notify fulfillment.
                ProcessReceipt(receipt, isNewPurchase: response.Status == "SUCCESSFUL");

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

            if (response.Receipts != null && response.Receipts.Count > 0)
            {
                foreach (var receipt in response.Receipts)
                {
                    if (receipt == null)
                        continue;

                    if (receipt.CancelDate != 0)
                    {
                        // Defer: ownership loss is resolved after all pages, against active
                        // terms seen anywhere in the history — never per-page/per-order.
                        _restoreCancelledReceipts.Add(receipt);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(receipt.Sku))
                        _restoreActiveSkus.Add(receipt.Sku);

                    ownedSkus.Add(receipt.Sku);

                    // Recovery path: grant unfulfilled consumables and restore subscription
                    // entitlement. Idempotent by ReceiptId — never re-grants or re-notifies.
                    ProcessReceipt(receipt, isNewPurchase: false);
                }
            }

            if (response.HasMore)
            {
                iapService.GetPurchaseUpdates(new ResetInput { Reset = false });
                return;
            }

            // Last page: a cancelled SKU is dropped only if it has no active term anywhere.
            foreach (var cancelled in _restoreCancelledReceipts)
            {
                if (!string.IsNullOrEmpty(cancelled.Sku) && !_restoreActiveSkus.Contains(cancelled.Sku))
                    ownedSkus.Remove(cancelled.Sku);
            }
            _restoreCancelledReceipts.Clear();
            _restoreActiveSkus.Clear();

            CompleteRestore(true);

            foreach (var status in subscriptionStatuses.Values.Where(s => s.IsActive))
                Debug.Log($"[AMZNGoDSDK] Active subscription: {status.ProductId} (until {status.ExpiresAt.ToLocalTime():G})");
        }

        /// <summary>
        /// Single grant + fulfillment path shared by the live purchase and recovery flows.
        /// Idempotent: a receipt is granted and fulfilled at most once (tracked persistently),
        /// which prevents lost rewards, double grants, and re-fulfillment of closed receipts.
        ///
        /// Ordering is deliberately grant → persist marker → NotifyFulfillment. The two persisted
        /// writes (reward and fulfilled-marker) are separate PlayerPrefs saves and thus not atomic:
        /// an app-kill in the sub-millisecond window between them can re-grant on the next launch.
        /// This is an accepted trade-off — the priority is never losing a paid consumable, so on the
        /// rare crash we favor a possible extra grant over a lost one. Marker-before-notify still
        /// guarantees NotifyFulfillment is never spammed on an already-closed receipt.
        /// </summary>
        private void ProcessReceipt(PurchaseReceipt receipt, bool isNewPurchase)
        {
            if (receipt == null || string.IsNullOrEmpty(receipt.ReceiptId))
                return;

            if (_fulfilledReceipts.Contains(receipt.ReceiptId))
                return;

            string productId = receipt.Sku;
            bool granted = false;

            var subscriptionProduct = settings.SubscriptionProducts
                .FirstOrDefault(s => s.ProductId == productId);

            if (subscriptionProduct != null)
            {
                if (isNewPurchase)
                {
                    Debug.Log($"[AMZNGoDSDK] Subscription purchased: {productId}");
                    ExtendSubscription(subscriptionProduct);
                    GiveSubscriptionReward(subscriptionProduct);
                    GrantSubscriptionConsumables(subscriptionProduct);
                }
                else if (!HasSubscription(subscriptionProduct.ProductId))
                {
                    // Restore entitlement only — do NOT replay historical rewards, otherwise
                    // a reinstall (which returns the full receipt history) would stack terms
                    // and re-grant coins for every past renewal.
                    ExtendSubscription(subscriptionProduct);
                }

                granted = true;
            }
            else
            {
                var consumableProduct = settings.ConsumableProducts
                    .FirstOrDefault(c => c.ProductId == productId);

                if (consumableProduct != null)
                {
                    Debug.Log($"[AMZNGoDSDK] Consumable granted: {productId}");
                    GiveConsumableReward(consumableProduct);
                    granted = true;
                }
            }

            if (!granted)
                Debug.LogWarning($"[AMZNGoDSDK] Receipt for unregistered product '{productId}', fulfilling without grant");

            // Persist BEFORE notifying so a crash between the two can never double-grant;
            // the receipt simply re-appears and is skipped here next time.
            MarkReceiptFulfilled(receipt.ReceiptId);
            NotifyFulfillment(receipt.ReceiptId);
        }

        private void NotifyFulfillment(string receiptId)
        {
            if (iapService == null || string.IsNullOrEmpty(receiptId))
                return;

            iapService.NotifyFulfillment(new NotifyFulfillmentInput
            {
                ReceiptId = receiptId,
                FulfillmentResult = "FULFILLED"
            });
        }

        private void LoadFulfilledReceipts()
        {
            _fulfilledReceipts.Clear();

            var raw = PlayerPrefs.GetString(FulfilledReceiptsKey, "");
            if (string.IsNullOrEmpty(raw))
                return;

            foreach (var id in raw.Split(new[] { FulfilledReceiptsSeparator }, StringSplitOptions.RemoveEmptyEntries))
                _fulfilledReceipts.Add(id);
        }

        private void MarkReceiptFulfilled(string receiptId)
        {
            if (string.IsNullOrEmpty(receiptId))
                return;

            if (!_fulfilledReceipts.Add(receiptId))
                return;

            PlayerPrefs.SetString(FulfilledReceiptsKey, string.Join(FulfilledReceiptsSeparator.ToString(), _fulfilledReceipts));
            PlayerPrefs.Save();
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
            BeginRestoreAccumulators();
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
