#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Unity.Services.Core;

namespace AMZNGoDSDK.Runtime
{
    public class InAppPurchaseModule : ModuleBase, IStoreListener
    {
        private InAppPurchaseSettingData settings;

        private IStoreController storeController;
        private IExtensionProvider extensionProvider;
        private readonly HashSet<string> addedProductIds = new();

        private readonly Dictionary<ConsumableRewardType, Action<string, int>> _rewardTypeHandlers =
            new();
        private Action<string, int> _defaultConsumableRewardSetter;

        public Action<string> OnPurchaseComplete;
        public Action<string> OnPurchaseFailedCallback;

        public bool IsInitialized { get; private set; } = false;

        // Словарь для отслеживания статуса подписок
        private Dictionary<string, SubscriptionStatus> subscriptionStatuses = new();

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

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await UnityServices.InitializeAsync();
                Debug.Log("[AMZNGoDSDK] 🟢 Unity Gaming Services инициализированы");

                var module = StandardPurchasingModule.Instance(
                    settings.UseAmazonAppStore ? AppStore.AmazonAppStore : AppStore.GooglePlay
                );

                if (settings.UseFakeStoreInEditor)
                {
                    module.useFakeStoreUIMode = FakeStoreUIMode.StandardUser;
                }

                var builder = ConfigurationBuilder.Instance(module);

                BuildProducts(builder);

                UnityPurchasing.Initialize(this, builder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZNGoDSDK] ❌ Ошибка инициализации Unity Gaming Services: {e.Message}");
                IsInitialized = true;
            }

            CheckExpiredSubscriptions();
        }

        private void BuildProducts(ConfigurationBuilder builder)
        {
            addedProductIds.Clear();

            // Добавляем товары из настроек
            foreach (var subscription in settings.SubscriptionProducts.Where(s => s.Enabled))
            {
                AddSubscriptionProduct(builder, subscription);
            }

            foreach (var consumable in settings.ConsumableProducts.Where(c => c.Enabled))
            {
                AddConsumableProduct(builder, consumable);
            }

            // Импорт из Unity IAP каталога
            ImportProductsFromUnityCatalog(builder);
        }

        private void AddSubscriptionProduct(ConfigurationBuilder builder, SubscriptionProduct subscription)
        {
            if (string.IsNullOrWhiteSpace(subscription.ProductId))
                return;

            if (!addedProductIds.Add(subscription.ProductId))
                return;

            builder.AddProduct(subscription.ProductId, ProductType.Subscription);

            RegisterSubscriptionStatus(subscription);
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

        private void AddConsumableProduct(ConfigurationBuilder builder, ConsumableProduct consumable)
        {
            if (string.IsNullOrWhiteSpace(consumable.ProductId))
                return;

            if (!addedProductIds.Add(consumable.ProductId))
                return;

            // подставляем ключ награды по умолчанию
            if (string.IsNullOrWhiteSpace(consumable.RewardKey))
                consumable.RewardKey = consumable.ProductId;

            builder.AddProduct(consumable.ProductId, ProductType.Consumable);
        }

        private void ImportProductsFromUnityCatalog(ConfigurationBuilder builder)
        {
            var catalog = ProductCatalog.LoadDefaultCatalog();

            if (catalog == null || catalog.allProducts == null || catalog.allProducts.Count == 0)
                return;

            foreach (var item in catalog.allProducts)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;

                if (!addedProductIds.Add(item.id))
                    continue;

                builder.AddProduct(item.id, item.type);

                if (item.type == ProductType.Subscription && !subscriptionStatuses.ContainsKey(item.id))
                {
                    RegisterSubscriptionStatus(new SubscriptionProduct
                    {
                        ProductId = item.id,
                        DisplayName = item.id,
                        RewardAmount = 0,
                        Enabled = true
                    });
                }

                if (item.type == ProductType.Subscription && settings.SubscriptionProducts.All(s => s.ProductId != item.id))
                {
                    settings.SubscriptionProducts.Add(new SubscriptionProduct
                    {
                        ProductId = item.id,
                        DisplayName = item.id,
                        RewardAmount = 0,
                        Enabled = true
                    });
                }

                if (item.type == ProductType.Consumable && settings.ConsumableProducts.All(c => c.ProductId != item.id))
                {
                    settings.ConsumableProducts.Add(new ConsumableProduct
                    {
                        ProductId = item.id,
                        DisplayName = item.id,
                        RewardAmount = 0,
                        RewardKey = item.id,
                        Enabled = true
                    });
                }
            }
        }

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            storeController = controller;
            extensionProvider = extensions;

            Debug.Log("[AMZNGoDSDK] 🟢 Unity IAP инициализирован");

            foreach (var status in subscriptionStatuses.Values.Where(s => s.IsActive))
            {
                Debug.Log($"[AMZNGoDSDK] ✅ Подписка активна: {status.ProductId} (до {status.ExpiresAt.ToLocalTime():G})");
            }

            IsInitialized = true;
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            Debug.LogError($"[AMZNGoDSDK] ❌ IAP инициализация не удалась: {error}");
            IsInitialized = true;
        }

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            Debug.LogError($"[AMZNGoDSDK] ❌ IAP инициализация не удалась: {error} - {message}");
            IsInitialized = true;
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string productId = args.purchasedProduct.definition.id;

            // Проверяем, является ли это подпиской
            var subscriptionProduct = settings.SubscriptionProducts.FirstOrDefault(s => s.ProductId == productId);
            if (subscriptionProduct != null)
            {
                Debug.Log($"[AMZNGoDSDK] 🎉 Подписка успешно куплена: {productId}");
                ExtendSubscription(subscriptionProduct);
                GiveSubscriptionReward(subscriptionProduct);
                GrantSubscriptionConsumables(subscriptionProduct);
            }
            else
            {
                // Это единичный товар
                var consumableProduct = settings.ConsumableProducts.FirstOrDefault(c => c.ProductId == productId);
                if (consumableProduct != null)
                {
                    Debug.Log($"[AMZNGoDSDK] 🎉 Товар успешно куплен: {productId}");
                    GiveConsumableReward(consumableProduct);
                }
            }

            OnPurchaseComplete?.Invoke(productId);
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            Debug.LogWarning($"[AMZNGoDSDK] ❌ Покупка не удалась: {product.definition.id} - {failureReason}");
            OnPurchaseFailedCallback?.Invoke(product.definition.id);
        }

        public void RestorePurchases(Action<bool> onComplete = null)
        {
            if (extensionProvider == null)
            {
                Debug.LogWarning("[AMZNGoDSDK] ❌ Нельзя восстановить покупки — магазин не инициализирован");
                onComplete?.Invoke(false);
                return;
            }

            try
            {
                if (settings.UseAmazonAppStore)
                {
                    Debug.LogWarning("[AMZNGoDSDK] ℹ️ Amazon Appstore не поддерживает явный restore — покупки подтягиваются при запросе товаров");
                    onComplete?.Invoke(false);
                    return;
                }

                var google = extensionProvider.GetExtension<IGooglePlayStoreExtensions>();
                if (google != null)
                {
#pragma warning disable CS0618
                    google.RestoreTransactions(success => onComplete?.Invoke(success));
#pragma warning restore CS0618
                    return;
                }

                var apple = extensionProvider.GetExtension<IAppleExtensions>();
                if (apple != null)
                {
                    apple.RestoreTransactions(onComplete);
                    return;
                }

                onComplete?.Invoke(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZNGoDSDK] ❌ Ошибка восстановления покупок: {e.Message}");
                onComplete?.Invoke(false);
            }
        }

        public void BuyProduct(string productId)
        {
            if (storeController == null)
            {
                Debug.LogError("[AMZNGoDSDK] ❌ Магазин не инициализирован. Покупка невозможна.");
                return;
            }

            var product = storeController.products.WithID(productId);
            if (product == null)
            {
                Debug.LogError($"[AMZNGoDSDK] ❌ Продукт не найден: {productId}");
                return;
            }

            storeController.InitiatePurchase(productId);
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
            var product = storeController?.products.WithID(productId);
            return product != null && product.hasReceipt;
        }

        public Product GetProduct(string productId)
        {
            return storeController?.products.WithID(productId);
        }

        public IEnumerable<Product> GetAllProducts()
        {
            return storeController?.products.all ?? Enumerable.Empty<Product>();
        }

        private void GiveSubscriptionReward(SubscriptionProduct subscription)
        {
            int currentCoins = PlayerPrefs.GetInt("TotalCoins", 0);
            PlayerPrefs.SetInt("TotalCoins", currentCoins + subscription.RewardAmount);
            PlayerPrefs.Save();

            Debug.Log($"[AMZNGoDSDK] 💎 Выдана награда за подписку {subscription.ProductId}: {subscription.RewardAmount} монет");
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

                Debug.LogWarning($"[AMZNGoDSDK] ⚠️ Подписка истекла: {status.ProductId} (до {status.ExpiresAt:G})");
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
            Debug.Log($"[AMZNGoDSDK] 💎 Выдана награда ({rewardType}) {rewardKey}: {rewardAmount}");
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
            OnPurchaseComplete += callback;
        }

        public void SetPurchaseFailedCallback(System.Action<string> callback)
        {
            OnPurchaseFailedCallback += callback;
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

        public override void Cleenup()
        {
            // Очистка ресурсов если необходимо
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
