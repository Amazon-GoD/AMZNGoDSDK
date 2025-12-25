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

        public Action<string> OnPurchaseComplete;
        public Action<string> OnPurchaseFailedCallback;
        private Action<string, int> _consumableRewardSetter;

        public bool IsInitialized { get; private set; } = false;

        // Словарь для отслеживания статуса подписок
        private Dictionary<string, SubscriptionStatus> subscriptionStatuses = new();

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

            if (!subscriptionStatuses.ContainsKey(subscription.ProductId))
            {
                subscriptionStatuses[subscription.ProductId] = new SubscriptionStatus
                {
                    ProductId = subscription.ProductId,
                    IsSubscribed = LoadSubscriptionStatus(subscription.ProductId),
                    RewardAmount = subscription.RewardAmount
                };
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
                    subscriptionStatuses[item.id] = new SubscriptionStatus
                    {
                        ProductId = item.id,
                        IsSubscribed = LoadSubscriptionStatus(item.id),
                        RewardAmount = 0
                    };
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

            // Проверяем активные подписки
            foreach (var subscription in settings.SubscriptionProducts.Where(s => s.Enabled))
            {
                var product = storeController.products.WithID(subscription.ProductId);
                if (product != null && product.hasReceipt)
                {
                    Debug.Log($"[AMZNGoDSDK] ✅ Подписка активна: {subscription.ProductId}");
                    SetSubscriptionStatus(subscription.ProductId, true);
                }
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
                SetSubscriptionStatus(productId, true);
                GiveSubscriptionReward(subscriptionProduct);
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

        public bool IsSubscribed(string productId)
        {
            return subscriptionStatuses.ContainsKey(productId) && subscriptionStatuses[productId].IsSubscribed;
        }

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
            string rewardKey = string.IsNullOrWhiteSpace(consumable.RewardKey)
                ? consumable.ProductId
                : consumable.RewardKey;

            var rewardSetter = _consumableRewardSetter ?? DefaultConsumableRewardSetter;
            rewardSetter.Invoke(rewardKey, consumable.RewardAmount);

            Debug.Log($"[AMZNGoDSDK] 💎 Выдана награда за товар {consumable.ProductId}: {consumable.RewardAmount} (key: {rewardKey})");
        }

        private void SetSubscriptionStatus(string productId, bool status)
        {
            if (subscriptionStatuses.ContainsKey(productId))
            {
                subscriptionStatuses[productId].IsSubscribed = status;
                subscriptionStatuses[productId].LastCheck = DateTime.UtcNow;

                PlayerPrefs.SetInt($"SubscriptionStatus_{productId}", status ? 1 : 0);
                PlayerPrefs.SetString($"SubscriptionLastCheck_{productId}", DateTime.UtcNow.ToString("o"));
                PlayerPrefs.Save();
            }
        }

        private bool LoadSubscriptionStatus(string productId)
        {
#if UNITY_EDITOR
            return false;
#else
            return PlayerPrefs.GetInt($"SubscriptionStatus_{productId}", 0) == 1;
#endif
        }

        private void CheckExpiredSubscriptions()
        {
            foreach (var subscription in subscriptionStatuses)
            {
                string lastCheckKey = $"SubscriptionLastCheck_{subscription.Key}";
                string lastCheckStr = PlayerPrefs.GetString(lastCheckKey, "");

                if (!DateTime.TryParse(lastCheckStr, out DateTime lastCheck))
                {
                    continue;
                }

                if ((DateTime.UtcNow - lastCheck).TotalDays >= 7)
                {
                    Debug.LogWarning($"[AMZNGoDSDK] ⚠️ Более 7 дней без обновлений — подписка считается отменённой: {subscription.Key}");
                    SetSubscriptionStatus(subscription.Key, false);
                }
            }
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
            _consumableRewardSetter = rewardSetter;
        }

        public override void Cleenup()
        {
            // Очистка ресурсов если необходимо
        }

        [Serializable]
        private class SubscriptionStatus
        {
            public string ProductId;
            public bool IsSubscribed;
            public int RewardAmount;
            public DateTime LastCheck;
        }

        private void DefaultConsumableRewardSetter(string rewardKey, int rewardAmount)
        {
            int current = PlayerPrefs.GetInt(rewardKey, 0);
            PlayerPrefs.SetInt(rewardKey, current + rewardAmount);
            PlayerPrefs.Save();
        }
    }
}
