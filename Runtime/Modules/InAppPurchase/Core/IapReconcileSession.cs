#if AMZN_IAP_ENABLED
using System.Collections.Generic;
using com.amazon.device.iap.cpt;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Накопитель одного полного прогона GetPurchaseUpdates (Reset=true + все страницы).
    /// Это буквальная реализация правила ТЗ IAP-02 «применять результат снапшотом целиком,
    /// а не по одному чеку»: только на полном ответе выразимо «чека нет — прав нет»,
    /// иначе отсутствие чека неотличимо от отсутствия ответа. Оборванный прогон просто
    /// выбрасывается (Complete не вызывается) — частичный ответ не применяется никогда.
    /// Без side-эффектов и Unity API.
    /// </summary>
    internal sealed class IapReconcileSession
    {
        private readonly List<PurchaseReceipt> _receipts = new();

        public int PageCount { get; private set; }

        public void AddPage(IEnumerable<PurchaseReceipt> receipts)
        {
            PageCount++;

            if (receipts == null)
                return;

            foreach (var receipt in receipts)
            {
                if (receipt == null || string.IsNullOrEmpty(receipt.ReceiptId) || string.IsNullOrEmpty(receipt.Sku))
                    continue;   // guard на null-поля (IAP-09)
                _receipts.Add(receipt);
            }
        }

        public IapReconcileResult Complete(ISet<string> longLivedSkus)
        {
            var active = new HashSet<string>();

            foreach (var receipt in _receipts)
            {
                // cancelDate пуст (в C#-обёртке — 0), пока подписка активна; для разовой
                // покупки непустой CancelDate означает возврат денег. Установленный факт
                // из ТЗ, раздел G: сравнение с текущей датой не требуется.
                if (receipt.CancelDate == 0 && longLivedSkus.Contains(receipt.Sku))
                    active.Add(receipt.Sku);
            }

            return new IapReconcileResult(active, _receipts);
        }
    }

    internal sealed class IapReconcileResult
    {
        /// <summary>Долгоживущие SKU с действующим чеком в полном ответе.</summary>
        public readonly HashSet<string> ActiveSkus;

        /// <summary>Все валидные чеки полного ответа — для выдачи расходуемых, периодов и подтверждений.</summary>
        public readonly IReadOnlyList<PurchaseReceipt> Receipts;

        public IapReconcileResult(HashSet<string> activeSkus, IReadOnlyList<PurchaseReceipt> receipts)
        {
            ActiveSkus = activeSkus;
            Receipts = receipts;
        }
    }
}
#endif
