#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using com.amazon.device.iap.cpt;
using com.amazon.device.iap.cpt.json;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Фейковый Amazon-стор для редактора и любых сборок, где настоящего стора нет: покупка
    /// подтверждается сразу, без ожидания ответа Appstore.
    ///
    /// Не трогает ни <see cref="InAppPurchaseModule"/>, ни плагин Amazon. Ответы подаются через
    /// <c>AmazonIapV2Impl.FireEvent</c> — ту же публичную точку входа, в которую их пишет нативный
    /// слой. Благодаря этому в редакторе исполняется НАСТОЯЩИЙ код модуля: разбор чека, начисление
    /// награды, идемпотентность по ReceiptId, NotifyFulfillment и события воронки в аналитику.
    /// Тестируется поведение модуля, а не заглушка рядом с ним.
    ///
    /// Выданные чеки складываются в PlayerPrefs, поэтому Restore возвращает историю покупок и
    /// после перезапуска сцены.
    /// </summary>
    public static class SimulatedAmazonStore
    {
        public const string SubscriptionType = "SUBSCRIPTION";
        public const string ConsumableType = "CONSUMABLE";

        private const string ReceiptsKey = "AMZN_SimulatedReceipts";
        private const char ReceiptSeparator = '\n';
        private const char FieldSeparator = '|';

        /// <summary>True, если под приложением настоящий Appstore, а не заглушка плагина.</summary>
        public static readonly bool HasRealStore =
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>
        /// MiniJSON из плагина Amazon собирается только под мобильные таргеты (см. SUPPORTED_PLATFORM
        /// в MiniJSON.cs): на остальных и Serialize, и Deserialize возвращают null, а вместе с ними
        /// перестаёт работать доставка событий через FireEvent. Условие повторяет тамошнее — держать
        /// их синхронно придётся вручную, из другого файла директиву не видно.
        /// </summary>
        public static readonly bool IsAvailable =
#if UNITY_IPHONE || UNITY_ANDROID || __IOS__ || __ANDROID__
            true;
#else
            false;
#endif

        public readonly struct SimulatedProduct
        {
            public readonly string Sku;
            public readonly string ProductType;
            public readonly string Title;

            public SimulatedProduct(string sku, string productType, string title)
            {
                Sku = sku;
                ProductType = productType;
                Title = title;
            }
        }

        /// <summary>
        /// Отдаёт модулю каталог: цены и типы продуктов, как это делает GetProductData на устройстве.
        /// Цена помечена как симулированная, чтобы её не спутали с реальной.
        /// </summary>
        public static void PublishCatalog(IEnumerable<SimulatedProduct> products)
        {
            if (products == null)
                return;

            var productDataMap = new Dictionary<string, object>();
            foreach (var product in products)
            {
                if (string.IsNullOrWhiteSpace(product.Sku) || productDataMap.ContainsKey(product.Sku))
                    continue;

                productDataMap[product.Sku] = new Dictionary<string, object>
                {
                    { "sku", product.Sku },
                    { "productType", product.ProductType },
                    { "price", "(sim) $0.99" },
                    { "title", string.IsNullOrWhiteSpace(product.Title) ? product.Sku : product.Title },
                    { "description", "Simulated product" },
                };
            }

            if (productDataMap.Count == 0)
                return;

            // unavailableSkus намеренно не кладём: непустой список внутри SUCCESSFUL — это отдельная
            // причина отказа каталога, модуль отправил бы iap_catalog_failed на ровном месте.
            Fire("getProductDataResponse", new Dictionary<string, object>
            {
                { "requestId", NewId() },
                { "status", "SUCCESSFUL" },
                { "productDataMap", productDataMap },
            });
        }

        /// <summary>
        /// Мгновенно подтверждает покупку и возвращает статус, который отдал «стор».
        /// Новый чек запоминается, чтобы попасть в последующий Restore.
        ///
        /// <paramref name="alreadyActive"/> — принадлежит ли SKU аккаунту прямо сейчас (для
        /// подписки это её активность). Реальный Appstore на такую покупку отвечает
        /// ALREADY_PURCHASED вместе со старым чеком, а не новым чеком со статусом SUCCESSFUL:
        /// модуль по этому статусу заходит в ProcessReceipt с isNewPurchase == false и ничего
        /// не продлевает и не начисляет повторно. Без этой ветки каждый повторный клик стакал бы
        /// термы подписки и награды.
        /// </summary>
        public static string SimulatePurchase(string sku, string productType, bool alreadyActive = false)
        {
            if (string.IsNullOrWhiteSpace(sku))
                return "INVALID_SKU";

            var receipts = LoadReceipts();

            if (alreadyActive && TryFindLatestReceipt(receipts, sku, out var owned))
            {
                Fire("purchaseResponse", new Dictionary<string, object>
                {
                    { "requestId", NewId() },
                    { "status", "ALREADY_PURCHASED" },
                    { "purchaseReceipt", ToDictionary(owned) },
                });

                return "ALREADY_PURCHASED";
            }

            var receipt = new Receipt(NewId(), sku, productType, UnixNowMillis());
            receipts.Add(receipt);
            SaveReceipts(receipts);

            Fire("purchaseResponse", new Dictionary<string, object>
            {
                { "requestId", NewId() },
                { "status", "SUCCESSFUL" },
                { "purchaseReceipt", ToDictionary(receipt) },
            });

            return "SUCCESSFUL";
        }

        // Берём последний выданный чек по SKU: у подписки это текущий терм, его же вернул бы стор.
        private static bool TryFindLatestReceipt(List<Receipt> receipts, string sku, out Receipt found)
        {
            for (int i = receipts.Count - 1; i >= 0; i--)
            {
                if (receipts[i].Sku == sku)
                {
                    found = receipts[i];
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>
        /// Отдаёт модулю всю выданную историю чеков — вход в путь сверки/восстановления.
        /// ReceiptId стабилен между запусками, как у реального стора: журнал периодов и
        /// снапшот прав ключуются по нему, и ротация (бывший editor-костыль под старую
        /// модель локальных дат) выдавала бы период на каждый вход в плеймод.
        /// </summary>
        public static void SimulateRestore()
        {
            var stored = LoadReceipts();
            var payload = new List<object>(stored.Count);

            foreach (var receipt in stored)
                payload.Add(ToDictionary(receipt));

            Fire("getPurchaseUpdatesResponse", new Dictionary<string, object>
            {
                { "requestId", NewId() },
                { "status", "SUCCESSFUL" },
                { "hasMore", false },
                { "receipts", payload },
            });
        }

        /// <summary>
        /// Помечает последний чек SKU отменённым (возврат денег / отмена подписки) — для
        /// проверки снятия прав снапшотом: на следующей сверке права по SKU не будет.
        /// </summary>
        public static bool SimulateCancel(string sku)
        {
            var receipts = LoadReceipts();
            for (int i = receipts.Count - 1; i >= 0; i--)
            {
                if (receipts[i].Sku != sku)
                    continue;

                receipts[i] = new Receipt(receipts[i].ReceiptId, receipts[i].Sku, receipts[i].ProductType,
                    receipts[i].PurchaseDate, UnixNowMillis());
                SaveReceipts(receipts);
                return true;
            }
            return false;
        }

        /// <summary>Забывает выданные чеки — фейковый аккаунт становится «без покупок».</summary>
        public static void ClearReceipts()
        {
            PlayerPrefs.DeleteKey(ReceiptsKey);
            PlayerPrefs.Save();
        }

        /// <summary>Сколько чеков сейчас числится за фейковым аккаунтом.</summary>
        public static int ReceiptCount => LoadReceipts().Count;

        private static void Fire(string eventId, Dictionary<string, object> response)
        {
            if (!IsAvailable)
            {
                Debug.LogWarning($"[SimulatedAmazonStore] MiniJSON плагина недоступен на текущем " +
                                 $"Build Target — событие {eventId} не отправлено. " +
                                 $"Переключи платформу на Android.");
                return;
            }

            var message = new Dictionary<string, object>
            {
                { "eventId", eventId },
                { "response", response },
            };

            string json = Json.Serialize(message);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[SimulatedAmazonStore] Не удалось сериализовать событие {eventId}");
                return;
            }

            // Обращение к Instance обязательно до FireEvent: статический логгер плагина ставится
            // в конструкторе реализации, и без этого FireEvent упадёт на logger.Debug.
            var _ = AmazonIapV2Impl.Instance;

            AmazonIapV2Impl.FireEvent(json);
        }

        private readonly struct Receipt
        {
            public readonly string ReceiptId;
            public readonly string Sku;
            public readonly string ProductType;
            public readonly long PurchaseDate;
            public readonly long CancelDate;   // 0 = активен; непустой = возврат/отмена

            public Receipt(string receiptId, string sku, string productType, long purchaseDate, long cancelDate = 0)
            {
                ReceiptId = receiptId;
                Sku = sku;
                ProductType = productType;
                PurchaseDate = purchaseDate;
                CancelDate = cancelDate;
            }
        }

        private static Dictionary<string, object> ToDictionary(Receipt receipt) => new()
        {
            { "receiptId", receipt.ReceiptId },
            { "sku", receipt.Sku },
            { "productType", receipt.ProductType },
            { "purchaseDate", receipt.PurchaseDate },
            { "cancelDate", receipt.CancelDate },
        };

        private static List<Receipt> LoadReceipts()
        {
            var result = new List<Receipt>();

            var raw = PlayerPrefs.GetString(ReceiptsKey, "");
            if (string.IsNullOrEmpty(raw))
                return result;

            foreach (var line in raw.Split(new[] { ReceiptSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(FieldSeparator);
                if (parts.Length < 4 || !long.TryParse(parts[3], out var purchaseDate))
                    continue;

                long cancelDate = 0;
                if (parts.Length >= 5)
                    long.TryParse(parts[4], out cancelDate);   // старый формат без поля — активен

                result.Add(new Receipt(parts[0], parts[1], parts[2], purchaseDate, cancelDate));
            }

            return result;
        }

        private static void SaveReceipts(List<Receipt> receipts)
        {
            var lines = new List<string>(receipts.Count);
            foreach (var receipt in receipts)
            {
                lines.Add(string.Join(FieldSeparator.ToString(),
                    receipt.ReceiptId, receipt.Sku, receipt.ProductType, receipt.PurchaseDate, receipt.CancelDate));
            }

            PlayerPrefs.SetString(ReceiptsKey, string.Join(ReceiptSeparator.ToString(), lines));
            PlayerPrefs.Save();
        }

        private static string NewId() => Guid.NewGuid().ToString("N");

        private static long UnixNowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
#endif
