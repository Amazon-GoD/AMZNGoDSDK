using System;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Трёхзначное состояние права на продукт (ТЗ IAP-02). Unknown — «не проверяли» и это
    /// НЕ то же самое, что «прав нет»: любой сбой сверки с Amazon (сеть, не-SUCCESSFUL,
    /// обрыв пагинации) оставляет Unknown, а NotEntitled ставится только по успешной
    /// полной сверке истории чеков.
    ///
    /// Типы лежат в Utlts, а не в папке модуля, намеренно: при выключении IAP тулинг
    /// переименовывает папку модуля в InAppPurchase~, и на эти типы перестала бы
    /// компилироваться #else-ветка фасада AmznGoDSDKCore.
    /// </summary>
    public enum IapEntitlementState
    {
        Unknown = 0,
        Entitled = 1,
        NotEntitled = 2,
    }

    /// <summary>
    /// Снимок права на один продукт: знание (State) отдельно от политики (EffectiveAccess).
    /// EffectiveAccess — это то, что отвечают legacy-методы IsSubscribed/HasReceipt: пока
    /// состояние Unknown, они отдают последнее сохранённое значение, а не false.
    /// </summary>
    public readonly struct IapEntitlement
    {
        public readonly string ProductId;
        public readonly IapEntitlementState State;
        public readonly bool EffectiveAccess;

        /// <summary>Момент последней успешной сверки с Amazon (UTC). MinValue — сверки не было никогда.</summary>
        public readonly DateTime ReconciledAtUtc;

        public IapEntitlement(string productId, IapEntitlementState state, bool effectiveAccess, DateTime reconciledAtUtc)
        {
            ProductId = productId;
            State = state;
            EffectiveAccess = effectiveAccess;
            ReconciledAtUtc = reconciledAtUtc;
        }
    }

    /// <summary>
    /// Начался новый оплаченный период подписки (ТЗ IAP-14). Продления у Amazon не
    /// наблюдаются (при продлении не меняется ни одно поле чека), поэтому периоды
    /// вычисляются от PurchaseDate чека и настроенного срока. Событие приходит ровно один
    /// раз на период (журнал по ReceiptId); что начислять за период — решает игра (IAP-13).
    /// </summary>
    public readonly struct IapPeriodStarted
    {
        public readonly string ProductId;
        public readonly string ReceiptId;

        /// <summary>Номер периода, 0 — первый оплаченный период (сама покупка).</summary>
        public readonly int PeriodIndex;

        /// <summary>Начало периода (UTC): PurchaseDate чека + PeriodIndex × срок.</summary>
        public readonly DateTime PeriodStartUtc;

        public IapPeriodStarted(string productId, string receiptId, int periodIndex, DateTime periodStartUtc)
        {
            ProductId = productId;
            ReceiptId = receiptId;
            PeriodIndex = periodIndex;
            PeriodStartUtc = periodStartUtc;
        }
    }
}
