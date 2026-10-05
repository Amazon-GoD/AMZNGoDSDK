using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public static class InAppPurchaseCatalogHelper
    {
        internal sealed class ImportResult
        {
            internal int Consumables;
            internal int NonConsumables;
            internal int Subscriptions;
            internal int Duplicates;
            internal int InvalidProducts;
            internal int UnknownTypes;

            internal int AddedCount => Consumables + NonConsumables + Subscriptions;

            internal string GetSummary()
            {
                string summary = $"Добавлено: {Consumables} расходуемых, {NonConsumables} разовых покупок, {Subscriptions} подписок.\n" +
                    $"Пропущено: {Duplicates} повторов SKU, {InvalidProducts} товаров без SKU, " +
                    $"{UnknownTypes} товаров с неизвестным или отсутствующим типом.";
                if (Subscriptions > 0)
                    summary += "\n\nУкажите Term (days) для каждой новой подписки по данным консоли Amazon: " +
                        "Unity IAP Catalog не содержит этого срока. Без него настройки не сохранятся.";
                return summary + (AddedCount > 0
                    ? "\n\nТовары добавлены в текущие настройки окна. Проверьте их и нажмите Save Settings."
                    : "\n\nНастройки не изменены.");
            }
        }

        // Собственные DTO позволяют импортировать каталог и без установленного Unity Purchasing.
        // Nullable сохраняет различие между Consumable (0) и отсутствующим полем type.
        [DataContract]
        private sealed class Catalog
        {
            [DataMember(Name = "products", IsRequired = true)] public List<CatalogProduct> Products;
        }

        [DataContract]
        private sealed class CatalogProduct
        {
            [DataMember(Name = "id")] public string Id;
            [DataMember(Name = "type")] public int? Type;
            [DataMember(Name = "storeIDs")] public List<StoreId> StoreIds;
            [DataMember(Name = "defaultDescription")] public ProductDescription Description;
        }

        [DataContract]
        private sealed class StoreId
        {
            [DataMember(Name = "store")] public string Store;
            [DataMember(Name = "id")] public string Id;
        }

        [DataContract]
        private sealed class ProductDescription
        {
            [DataMember(Name = "title")] public string Title;
        }

        internal static string FindCatalogPath()
        {
            string path = Path.Combine(Application.dataPath, "Resources", "IAPProductCatalog.json");
            if (File.Exists(path)) return path;
            path = Path.Combine(Application.dataPath, "Plugins", "UnityPurchasing", "Resources", "IAPProductCatalog.json");
            return File.Exists(path) ? path : null;
        }

        internal static ImportResult ImportFile(InAppPurchaseSettingData settings, string path, Action beforeApply)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var serializer = new DataContractJsonSerializer(typeof(Catalog));
            Catalog catalog;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                catalog = (Catalog)serializer.ReadObject(stream);
            if (catalog?.Products == null)
                throw new InvalidDataException("В каталоге должен быть массив products.");

            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.ConsumableProducts != null)
                foreach (var product in settings.ConsumableProducts) AddKnownId(knownIds, product?.ProductId);
            if (settings.NonConsumableProducts != null)
                foreach (var product in settings.NonConsumableProducts) AddKnownId(knownIds, product?.ProductId);
            if (settings.SubscriptionProducts != null)
                foreach (var product in settings.SubscriptionProducts) AddKnownId(knownIds, product?.ProductId);

            var additions = new InAppPurchaseSettingData();
            var result = new ImportResult();
            foreach (var product in catalog.Products)
            {
                string id = GetAmazonId(product);
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.InvalidProducts++;
                    continue;
                }
                if (!product.Type.HasValue || product.Type.Value < 0 || product.Type.Value > 2)
                {
                    result.UnknownTypes++;
                    continue;
                }
                if (!knownIds.Add(id))
                {
                    result.Duplicates++;
                    continue;
                }

                string title = DecodeTitle(product.Description?.Title);
                string displayName = string.IsNullOrWhiteSpace(title) ? id : title.Trim();
                switch (product.Type.Value)
                {
                    case 0:
                        additions.ConsumableProducts.Add(new ConsumableProduct
                        {
                            ProductId = id, DisplayName = displayName, Enabled = true
                        });
                        result.Consumables++;
                        break;
                    case 1:
                        additions.NonConsumableProducts.Add(new NonConsumableProduct
                        {
                            ProductId = id, DisplayName = displayName, Enabled = true
                        });
                        result.NonConsumables++;
                        break;
                    case 2:
                        additions.SubscriptionProducts.Add(new SubscriptionProduct
                        {
                            ProductId = id, DisplayName = displayName, Enabled = true,
                            TermDays = 0, TestTermMinutes = 0
                        });
                        result.Subscriptions++;
                        break;
                }
            }

            // До этого места только чтение и проверка: ошибка каталога не меняет черновик окна.
            if (result.AddedCount > 0)
            {
                beforeApply?.Invoke();
                settings.ConsumableProducts ??= new List<ConsumableProduct>();
                settings.NonConsumableProducts ??= new List<NonConsumableProduct>();
                settings.SubscriptionProducts ??= new List<SubscriptionProduct>();
                settings.ConsumableProducts.AddRange(additions.ConsumableProducts);
                settings.NonConsumableProducts.AddRange(additions.NonConsumableProducts);
                settings.SubscriptionProducts.AddRange(additions.SubscriptionProducts);
            }
            return result;
        }

        private static void AddKnownId(HashSet<string> knownIds, string id)
        {
            if (!string.IsNullOrWhiteSpace(id)) knownIds.Add(id.Trim());
        }

        private static string GetAmazonId(CatalogProduct product)
        {
            if (product?.StoreIds != null)
                foreach (var storeId in product.StoreIds)
                    if (string.Equals(storeId?.Store?.Trim(), "AmazonApps", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(storeId.Id))
                        return storeId.Id.Trim();
            return product?.Id?.Trim();
        }

        private static string DecodeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            // Unity IAP дополнительно кодирует Unicode в title перед сериализацией JSON.
            return Regex.Replace(title, @"\\u([0-9a-fA-F]{4})", match =>
                ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        }
    }
}
