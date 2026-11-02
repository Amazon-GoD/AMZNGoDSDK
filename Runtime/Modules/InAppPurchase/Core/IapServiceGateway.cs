#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using com.amazon.device.iap.cpt;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Шов между модулем и плагином Amazon. Каждый нативный вызов превращает исключение в
    /// false + текст причины: нативка бросает Exception на любой error-ответ
    /// (AmazonIapV2Base → Jsonable.CheckForErrors → throw), причём не только AmazonException —
    /// RequestOutput.CreateFromJson бросает так же, а на не-мобильном Build Target
    /// Json.Deserialize возвращает null и CheckForErrors даёт NRE. Поэтому ловим Exception.
    ///
    /// Интерфейс — единственный способ проверить IAP-03/IAP-12 в редакторе: стаб Amazon
    /// отвечает "{}" и никогда не падает, так что очередь повторов NotifyFulfillment и
    /// правило «не рвать страницу» иначе уехали бы в прод непроверенными.
    /// </summary>
    internal interface IIapServiceGateway
    {
        bool TryAcquire(
            GetProductDataResponseDelegate onProductData,
            PurchaseResponseDelegate onPurchase,
            GetPurchaseUpdatesResponseDelegate onPurchaseUpdates,
            out string error);

        void ReleaseListeners();

        bool TryGetProductData(List<string> skus, out string error);

        // RequestId нужен вызывающему для матчинга асинхронного ответа с запросом: слушатель
        // у плагина один на все запросы, и без RequestId поздний ответ мёртвого прогона
        // неотличим от ответа текущего. Editor-стаб отдаёт "{}" — тогда requestId == null,
        // и матчинг на стороне вызывающего обязан быть мягким.
        bool TryPurchase(string sku, out string requestId, out string error);
        bool TryGetPurchaseUpdates(bool reset, out string requestId, out string error);
        bool TryNotifyFulfillment(string receiptId, out string error);
    }

    internal sealed class AmazonIapV2Gateway : IIapServiceGateway
    {
        private IAmazonIapV2 _service;
        private GetProductDataResponseDelegate _onProductData;
        private PurchaseResponseDelegate _onPurchase;
        private GetPurchaseUpdatesResponseDelegate _onPurchaseUpdates;

        public bool TryAcquire(
            GetProductDataResponseDelegate onProductData,
            PurchaseResponseDelegate onPurchase,
            GetPurchaseUpdatesResponseDelegate onPurchaseUpdates,
            out string error)
        {
            try
            {
                _service = AmazonIapV2Impl.Instance;
                _service.AddGetProductDataResponseListener(onProductData);
                _service.AddPurchaseResponseListener(onPurchase);
                _service.AddGetPurchaseUpdatesResponseListener(onPurchaseUpdates);

                _onProductData = onProductData;
                _onPurchase = onPurchase;
                _onPurchaseUpdates = onPurchaseUpdates;

                error = null;
                return true;
            }
            catch (Exception e)
            {
                _service = null;
                error = e.Message;
                return false;
            }
        }

        public void ReleaseListeners()
        {
            if (_service == null)
                return;

            try
            {
                if (_onProductData != null) _service.RemoveGetProductDataResponseListener(_onProductData);
                if (_onPurchase != null) _service.RemovePurchaseResponseListener(_onPurchase);
                if (_onPurchaseUpdates != null) _service.RemoveGetPurchaseUpdatesResponseListener(_onPurchaseUpdates);
            }
            catch (Exception)
            {
                // Cleanup при выгрузке — падать здесь уже не на что.
            }
        }

        public bool TryGetProductData(List<string> skus, out string error) =>
            Call(() => _service.GetProductData(new SkusInput { Skus = skus }), out error);

        public bool TryPurchase(string sku, out string requestId, out string error) =>
            CallWithRequestId(() => _service.Purchase(new SkuInput { Sku = sku }), out requestId, out error);

        public bool TryGetPurchaseUpdates(bool reset, out string requestId, out string error) =>
            CallWithRequestId(() => _service.GetPurchaseUpdates(new ResetInput { Reset = reset }), out requestId, out error);

        public bool TryNotifyFulfillment(string receiptId, out string error)
        {
            if (string.IsNullOrEmpty(receiptId))
            {
                error = "empty receiptId";
                return false;
            }

            return Call(() => _service.NotifyFulfillment(new NotifyFulfillmentInput
            {
                ReceiptId = receiptId,
                FulfillmentResult = "FULFILLED",
            }), out error);
        }

        private bool Call(Action action, out string error)
        {
            if (_service == null)
            {
                error = "service not acquired";
                return false;
            }

            try
            {
                action();
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private bool Call(Func<object> action, out string error) =>
            Call(() => { action(); }, out error);

        private bool CallWithRequestId(Func<RequestOutput> action, out string requestId, out string error)
        {
            requestId = null;

            if (_service == null)
            {
                error = "service not acquired";
                return false;
            }

            try
            {
                requestId = action()?.RequestId;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
#endif
