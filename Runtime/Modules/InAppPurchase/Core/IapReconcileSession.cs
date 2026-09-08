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
        private readonly HashSet<string> _requestIds = new();

        public int PageCount { get; private set; }

        /// <summary>RequestId запроса, отправленного в рамках ЭТОГО прогона (Reset=true и
        /// каждое продолжение страниц). Editor-стаб RequestId не отдаёт — null игнорируется.</summary>
        public void RegisterRequest(string requestId)
        {
            if (!string.IsNullOrEmpty(requestId))
                _requestIds.Add(requestId);
        }

        /// <summary>
        /// Принадлежит ли ответ этому прогону. Защита от гонки ватчдога: прогон без ответа
        /// объявляется мёртвым через таймаут, стартует новый — если после этого хвостовая
        /// страница СТАРОГО прогона с HasMore=false попала бы в новую сессию, снапшот
        /// применился бы с одной страницей и снял права у всех, кого на ней нет.
        /// Матчинг мягкий: когда сравнивать нечего (editor-стаб, симулятор, ответ без
        /// RequestId), ответ принимается — как до этой проверки.
        /// </summary>
        public bool OwnsResponse(string responseRequestId)
        {
            if (_requestIds.Count == 0 || string.IsNullOrEmpty(responseRequestId))
                return true;
            return _requestIds.Contains(responseRequestId);
        }

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
            var activeAny = new HashSet<string>();

            foreach (var receipt in _receipts)
            {
                // cancelDate пуст (в C#-обёртке — 0), пока подписка активна; для разовой
                // покупки непустой CancelDate означает возврат денег. Установленный факт
                // из ТЗ, раздел G: сравнение с текущей датой не требуется.
                if (receipt.CancelDate != 0)
                    continue;

                activeAny.Add(receipt.Sku);
                if (longLivedSkus.Contains(receipt.Sku))
                    active.Add(receipt.Sku);
            }

            return new IapReconcileResult(active, activeAny, _receipts);
        }
    }

    internal sealed class IapReconcileResult
    {
        /// <summary>Долгоживущие SKU с действующим чеком в полном ответе.</summary>
        public readonly HashSet<string> ActiveSkus;

        /// <summary>Все SKU с действующим чеком, без фильтра по конфигу — для переоценки
        /// сохранённых прав по SKU, которые из настроек уже удалили (рефанд должен снимать
        /// доступ и у них).</summary>
        public readonly HashSet<string> ActiveAnySkus;

        /// <summary>Все валидные чеки полного ответа — для выдачи расходуемых, периодов и подтверждений.</summary>
        public readonly IReadOnlyList<PurchaseReceipt> Receipts;

        public IapReconcileResult(HashSet<string> activeSkus, HashSet<string> activeAnySkus, IReadOnlyList<PurchaseReceipt> receipts)
        {
            ActiveSkus = activeSkus;
            ActiveAnySkus = activeAnySkus;
            Receipts = receipts;
        }
    }
}
#endif
