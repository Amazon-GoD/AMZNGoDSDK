using System.Linq;
using UnityEngine.Purchasing;

namespace AMZNGoDSDK.Editor
{
    public static class InAppPurchaseCatalogHelper
    {
        public static void RefreshCatalog(InAppPurchaseSettingData settings)
        {
            if (settings == null)
                return;

            var catalog = ProductCatalog.LoadDefaultCatalog();
            if (catalog == null || catalog.allProducts == null)
                return;

            settings.CatalogImportedProducts.Clear();

            var existingSubscriptionIds = settings.SubscriptionProducts
                .Select(p => p.ProductId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            var existingConsumableIds = settings.ConsumableProducts
                .Select(p => p.ProductId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            foreach (var product in catalog.allProducts)
            {
                if (product == null || string.IsNullOrWhiteSpace(product.id))
                    continue;

                if (!settings.CatalogImportedProducts.Any(p => p.ProductId == product.id))
                {
                    settings.CatalogImportedProducts.Add(new CatalogImportedProduct
                    {
                        ProductId = product.id,
                        Type = product.type
                    });
                }

                if (product.type == ProductType.Subscription && !existingSubscriptionIds.Contains(product.id))
                {
                    settings.SubscriptionProducts.Add(new SubscriptionProduct
                    {
                        ProductId = product.id,
                        DisplayName = product.id,
                        RewardAmount = 0,
                        Enabled = true
                    });
                    existingSubscriptionIds.Add(product.id);
                }
                else if (product.type == ProductType.Consumable && !existingConsumableIds.Contains(product.id))
                {
                    settings.ConsumableProducts.Add(new ConsumableProduct
                    {
                        ProductId = product.id,
                        DisplayName = product.id,
                        RewardAmount = 0,
                        RewardKey = product.id,
                        Enabled = true
                    });
                    existingConsumableIds.Add(product.id);
                }
            }
        }
    }
}

