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
        public int TermDays;          // только для подписок; 0 = не задан (IAP-15)
        public int TestTermMinutes;   // ТЕСТ: > 0 заменяет TermDays в расчёте периодов

        /// <summary>Срок периода в днях для калькулятора: тестовый срок в минутах (если
        /// задан) выражается дробными днями — математика периодов и так на double.</summary>
        public double EffectiveTermDays =>
            TestTermMinutes > 0 ? TestTermMinutes / 1440.0 : TermDays;
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
                        Register(p.ProductId, IapProductKind.Subscription, p.Enabled, p.TermDays, p.TestTermMinutes);

            if (settings.ConsumableProducts != null)
                foreach (var p in settings.ConsumableProducts)
                    if (p != null)
                        Register(p.ProductId, IapProductKind.Consumable, p.Enabled, 0, 0);

            if (settings.NonConsumableProducts != null)
                foreach (var p in settings.NonConsumableProducts)
                    if (p != null)
                        Register(p.ProductId, IapProductKind.NonConsumable, p.Enabled, 0, 0);
        }

        private void Register(string sku, IapProductKind kind, bool enabled, int termDays, int testTermMinutes)
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

            // Громко и при каждом Configure: тестовый срок в прод-конфиге означает периоды
            // каждые несколько минут у реальных игроков.
            if (testTermMinutes > 0)
                Debug.LogWarning($"[AMZNGoDSDK] TEST term active for '{sku}': {testTermMinutes} min instead of " +
                                 $"{termDays} d. Must be 0 in a production config (SDK Settings → Term minutes (TEST)).");

            _bySku[sku] = new IapConfiguredProduct
            {
                ProductId = sku,
                Kind = kind,
                Enabled = enabled,
                TermDays = termDays,
                TestTermMinutes = testTermMinutes,
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

        /// <summary>
        /// Матчинг ЧЕКА к настроенному продукту (раздел H ТЗ). Основной путь — точный
        /// receipt.Sku; фолбэки закрывают подписочный квирк Amazon, когда чек приходит с
        /// term SKU вместо родительского:
        ///  1) receipt.TermSku совпал с настроенным SKU (в настройках завели term);
        ///  2) receipt.Sku — term вида «родительский + суффикс», и родительский-префикс
        ///     ровно один среди настроенных подписок (неоднозначность = не угадываем).
        /// Нормализацию Sku к настроенному ProductId делает вызывающий.
        /// </summary>
        public bool TryResolveReceipt(PurchaseReceipt receipt, out IapConfiguredProduct product)
        {
            product = null;
            if (receipt == null)
                return false;

            if (TryResolve(receipt.Sku, out product))
                return true;

            if (TryResolve(receipt.TermSku, out product))
                return true;

            if (receipt.ProductType == "SUBSCRIPTION" && !string.IsNullOrEmpty(receipt.Sku))
            {
                IapConfiguredProduct match = null;
                foreach (var candidate in _bySku.Values)
                {
                    if (candidate.Kind != IapProductKind.Subscription)
                        continue;
                    if (receipt.Sku.Length <= candidate.ProductId.Length
                        || !receipt.Sku.StartsWith(candidate.ProductId, System.StringComparison.Ordinal))
                        continue;

                    if (match != null)
                        return false;   // два префикса-кандидата — неоднозначно
                    match = candidate;
                }

                if (match != null)
                {
                    product = match;
                    return true;
                }
            }

            return false;
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
