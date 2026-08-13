#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Вся аналитика IAP за одним фасадом. Каждый вызов — fire-and-forget под try/catch
    /// (ТЗ IAP-01): строка аналитики не может быть причиной несостоявшегося платежа.
    /// Раньше ReportPurchaseStarted стоял первой строкой BuyProduct БЕЗ защиты — упавший
    /// AndroidJavaClass в AppMetrica ронял покупку целиком. Образец —
    /// CrossPromoAnalytics.SafeReportAppMetrica.
    ///
    /// Воронка started/success/failed: дерево вложенных параметров, SKU всегда уровень 1.
    /// started = {"sku":""}; success = {"sku":{"new"|"already_owned":""}} (IAP-16: повторная
    /// покупка того, чем владеешь, отделима от продажи); failed =
    /// {"sku":{"reason":{"online|offline":""}}}. Восстановления — отдельным событием вне
    /// воронки (IAP-17): у них нет started.
    /// </summary>
    internal sealed class IapAnalytics
    {
        private readonly HashSet<string> _reportedCatalogReasons = new();

        private static string J(string s) => SdkJson.Escape(s ?? "");

        private static string Net() =>
            Application.internetReachability != NetworkReachability.NotReachable ? "online" : "offline";

        private static void SafeReport(string eventName, string json)
        {
            try
            {
                AmznGoDSDKCore.Instance?.ReportEventRawAppMetrica(eventName, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AMZNGoDSDK] IAP analytics report failed for '{eventName}': {e.Message}");
            }
        }

        public void PurchaseStarted(string sku) =>
            SafeReport("iap_purchase_started", $"{{\"{J(sku)}\":\"\"}}");

        public void PurchaseSuccess(string sku, bool alreadyOwned) =>
            SafeReport("iap_purchase_success",
                $"{{\"{J(sku)}\":{{\"{(alreadyOwned ? "already_owned" : "new")}\":\"\"}}}}");

        public void PurchaseFailed(string sku, string reason) =>
            SafeReport("iap_purchase_failed",
                $"{{\"{J(string.IsNullOrEmpty(sku) ? "unknown" : sku)}\":{{\"{J(reason)}\":{{\"{Net()}\":\"\"}}}}}}");

        /// <summary>Только расходуемые (IAP-17): подписочная ветка выполняется каждый запуск,
        /// и событие оттуда превратилось бы в счётчик запусков.</summary>
        public void PurchaseRestored(string sku) =>
            SafeReport("iap_purchase_restored", $"{{\"{J(sku)}\":\"\"}}");

        /// <summary>Снятие доступа (IAP-31; бывшее iap_entitlement_revoked — переименовано:
        /// шлётся и для подписок, а слово entitlement в конфиге значит тип товара). Причина
        /// вторым уровнем: expired (чек подписки с датой отмены), refunded (чек разовой
        /// покупки с датой отмены), grace_expired (сверка не проходит дольше грейса),
        /// receipt_gone (чек исчез из истории). Редкое по природе — сигнал тревоги: всплеск
        /// receipt_gone — это гонка класса IAP-26, а не отток.</summary>
        public void AccessRevoked(string sku, string reason) =>
            SafeReport("iap_access_revoked", $"{{\"{J(sku)}\":{{\"{J(reason)}\":\"\"}}}}");

        /// <summary>
        /// Отказы каталога (IAP-18): синхронный отказ запроса (catalog_request_failed) и отказ
        /// асинхронного ответа (catalog_response_failed, статус Amazon вторым уровнем) — разные
        /// причины. Дедуп по причине в пределах сессии: SKU шлются батчами по 100, одна
        /// поломка давала событие на каждый батч, ретраи умножали.
        /// </summary>
        public void CatalogFailed(string reason, string status = null)
        {
            // Статус входит в ключ дедупа: второй ОТЛИЧНЫЙ статус той же причины за сессию —
            // новая информация, а не дубль батча.
            if (!_reportedCatalogReasons.Add($"{reason}|{status}"))
                return;

            string json = string.IsNullOrEmpty(status)
                ? $"{{\"{J(reason)}\":\"\"}}"
                : $"{{\"{J(reason)}\":{{\"{J(status)}\":\"\"}}}}";
            SafeReport("iap_catalog_failed", json);
        }

        /// <summary>
        /// Amazon IAP v2 не отдаёт отдельного статуса отмены: закрытие окна покупки приходит
        /// как FAILED. Поэтому reason называется store_failed (IAP-22) — туда попадают в
        /// основном отмены пользователем, это не поломка.
        ///
        /// PENDING — Appstore SDK 3.x, «ребёнок попросил — родитель ещё не одобрил» (Amazon
        /// Kids): не отказ и не продажа. После одобрения чек приедет в getPurchaseUpdates и
        /// выдастся сверкой; в воронке пусть будет виден отдельной причиной, не store_failed.
        /// </summary>
        public static string MapAmazonStatus(string status) => status switch
        {
            "FAILED" => "store_failed",
            "INVALID_SKU" => "invalid_sku",
            "NOT_SUPPORTED" => "not_supported",
            "PENDING" => "pending",
            _ => "unknown",
        };
    }
}
#endif
