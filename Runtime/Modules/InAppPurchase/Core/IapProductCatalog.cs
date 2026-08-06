#if AMZN_IAP_ENABLED
using System.Collections.Generic;
using com.amazon.device.iap.cpt;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    internal enum IapProductKind
    {
        Subscription,
        Consumable,
        NonConsumable,
    }

    internal sealed class IapConfiguredProduct
    {
        public string ProductId;
        public IapProductKind Kind;
        public bool Enabled;
        public int TermDays;   // только для подписок; 0 = не задан (IAP-15)
    }

    /// <summary>
    /// Каталог настроенных продуктов. Держит два множества с РАЗНЫМИ ролями — их слипание
    /// в одном registeredProductIds и было сутью IAP-04:
    ///  • известные SKU (_bySku) — для матчинга чеков, независимо от Enabled (IAP-08:
    ///    выключение товара не должно отбирать доступ у заплативших);
    ///  • список к запросу каталога (_catalogRequestSkus) — наполняется заново при КАЖДОМ
    ///    Configure, поэтому RetryInitialize реально перезапрашивает цены.
    /// Enabled читается ровно в одном месте — CanBuy.
    /// </summary>
    internal sealed class IapProductCatalog
    {
        private readonly Dictionary<string, IapConfiguredProduct> _bySku = new();
        private readonly List<string> _catalogRequestSkus = new();
        private readonly HashSet<string> _longLivedSkus = new();
        private readonly Dictionary<string, ProductData> _productDataCache = new();

        /// <summary>Подписки и разовые покупки — права, выражаемые снапшотом сверки.</summary>
        public ISet<string> LongLivedSkus => _longLivedSkus;

        public IReadOnlyList<string> CatalogRequestSkus => _catalogRequestSkus;

        public void Configure(InAppPurchaseSettingData settings)
        {
            _bySku.Clear();
            _catalogRequestSkus.Clear();
            _longLivedSkus.Clear();

            if (settings == null)
                return;

            if (settings.SubscriptionProducts != null)
                foreach (var p in settings.SubscriptionProducts)
                    if (p != null)
                        Register(p.ProductId, IapProductKind.Subscription, p.Enabled, p.TermDays);

            if (settings.ConsumableProducts != null)
                foreach (var p in settings.ConsumableProducts)
                    if (p != null)
                        Register(p.ProductId, IapProductKind.Consumable, p.Enabled, 0);

            if (settings.NonConsumableProducts != null)
                foreach (var p in settings.NonConsumableProducts)
                    if (p != null)
                        Register(p.ProductId, IapProductKind.NonConsumable, p.Enabled, 0);
        }

        private void Register(string sku, IapProductKind kind, bool enabled, int termDays)
        {
            if (string.IsNullOrWhiteSpace(sku))
                return;

            sku = sku.Trim();

            if (_bySku.ContainsKey(sku))
            {
                // Дубликаты режутся ещё валидацией окна настроек; здесь только страховка.
                Debug.LogWarning($"[AMZNGoDSDK] Duplicate product id in settings: {sku}");
                return;
            }

            _bySku[sku] = new IapConfiguredProduct
            {
                ProductId = sku,
                Kind = kind,
                Enabled = enabled,
                TermDays = termDays,
            };

            if (kind != IapProductKind.Consumable)
                _longLivedSkus.Add(sku);

            // Цены нужны только тому, что продаётся; выключенный товар не запрашиваем.
            if (enabled)
                _catalogRequestSkus.Add(sku);
        }

        public bool TryResolve(string sku, out IapConfiguredProduct product)
        {
            product = null;
            return !string.IsNullOrEmpty(sku) && _bySku.TryGetValue(sku, out product);
        }

        /// <summary>Единственное место, где Enabled влияет на поведение (IAP-08).</summary>
        public bool CanBuy(string sku) => TryResolve(sku, out var product) && product.Enabled;

        public void StoreProductData(Dictionary<string, ProductData> map)
        {
            foreach (var kvp in map)
                _productDataCache[kvp.Key] = kvp.Value;
        }

        public ProductData GetProduct(string sku)
        {
            if (string.IsNullOrEmpty(sku))
                return null;
            _productDataCache.TryGetValue(sku, out var data);
            return data;
        }

        public IEnumerable<ProductData> GetAllProducts() => _productDataCache.Values;

        public int CachedProductCount => _productDataCache.Count;
    }
}
#endif
