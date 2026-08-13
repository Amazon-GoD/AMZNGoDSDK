#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using com.amazon.device.iap.cpt;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Amazon Appstore IAP (ТЗ «исправление IAP», ред. 3).
    ///
    /// Источник истины о правах — чеки Amazon: подписка/разовая покупка активна, если в
    /// ПОЛНОМ ответе GetPurchaseUpdates есть её чек с пустым CancelDate. Результат сверки
    /// применяется снапшотом целиком; любой сбой оставляет состояние Unknown, а не «прав
    /// нет». SDK ничего не начисляет (IAP-13): он сообщает состояние
    /// (Unknown/Entitled/NotEntitled), события выдачи и снятия прав и начало новых
    /// оплаченных периодов подписки — начисляет игра.
    ///
    /// Оркестрация здесь; механика — в Core/: каталог продуктов, журналы чеков, стор прав,
    /// сессия сверки, калькулятор периодов, повторы с бэкоффом, шов к плагину Amazon и
    /// аналитика. Плагин Amazon (Plugins/Amazon/AmazonIapV2) не тронут.
    /// </summary>
    public class InAppPurchaseModule : ModuleBase
    {
        // Amazon GetProductData принимает не больше 100 SKU за запрос.
        private const int ProductDataBatchSize = 100;

        // IAP-11: пересверка на возврате из фона, если состояние старше этого.
        private const float ReconcileStaleSeconds = 5f * 60f;

        // IAP-21: окно блокировки повторной покупки. Ответ может не прийти вовсе (игрок
        // свернул приложение на диалоге, мост умер) — бессрочный лок заблокировал бы
        // магазин до перезапуска.
        private const float PendingPurchaseWindowSeconds = 90f;

        private InAppPurchaseSettingData settings;

        private IIapServiceGateway _gateway;
        private bool _serviceReady;

        private readonly IapProductCatalog _catalog = new();
        private readonly IapReceiptJournal _journal = new();
        private readonly IapEntitlementStore _entitlements = new();
        private readonly IapAnalytics _analytics = new();

        private IapRetryScheduler _reconcileRetry;
        private IapRetryScheduler _catalogRetry;

        // Текущий прогон сверки; null — сверка не идёт. Обрыв = выбросить сессию целиком,
        // частичный ответ не применяется никогда.
        private IapReconcileSession _reconcileSession;
        private readonly List<Action<bool>> _restoreCallbacks = new();
        private float _lastReconcileRealtime = float.NegativeInfinity;

        // Чеки последней успешной сверки: доначисление периодов, когда доверенное время
        // приходит ПОЗЖЕ ответа Amazon (старт без сети).
        private IReadOnlyList<PurchaseReceipt> _lastReceipts;

        // IAP-21: SKU текущей покупки + отметка времени + защёлка «терминальное событие
        // воронки уже отправлено» (ветка catch шлёт failed, но нативка после этого всё ещё
        // может прислать ответ — иначе на один started пришлись бы и failed, и success).
        private string _pendingPurchaseSku;
        private string _pendingPurchaseRequestId;
        private float _pendingPurchaseStartedRealtime;
        private bool _pendingTerminalReported;

        public Action<string> OnPurchaseComplete;
        public Action<string> OnPurchaseFailedCallback;

        private readonly List<Action<IapEntitlement>> _grantedListeners = new();
        private readonly List<Action<IapEntitlement>> _revokedListeners = new();
        private readonly List<Action<IapPeriodStarted>> _periodListeners = new();

        /// <summary>Сервис поднят и каталог запрошен: BuyProduct легален. Состоянию прав
        /// верить рано — см. <see cref="IsRestored"/> (IAP-11).</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Была успешная ПОЛНАЯ сверка с Amazon в этой сессии — состоянию можно верить.</summary>
        public bool IsRestored => _entitlements.ReconciledThisSession;

        public DateTime LastReconciliationUtc => _entitlements.ReconciledAtUtc;

        public void Construct(InAppPurchaseSettingData iapSettings)
        {
            settings = iapSettings;
            Enabled = iapSettings.Enabled;
        }

        #region Initialization

        public override void Initialize()
        {
            if (!Enabled)
                return;

            _reconcileRetry = new IapRetryScheduler(this, RetryReconcile, OnReconcileExhausted);
            _catalogRetry = new IapRetryScheduler(this, RetryCatalog, null);

            _catalog.Configure(settings);

            // Миграция строго до первого обращения к Amazon: засев GrantedReceipts из
            // старого журнала — иначе вся история расходуемых выдастся повторно (IAP-12).
            IapReceiptJournal.MigrateIfNeeded(_catalog.LongLivedSkus);
            _journal.Load();
            _entitlements.Load();

            // Грейс на старте НЕ проверяется (IAP-32): до первой сверки отметка старая по
            // определению, и вернувшийся через неделю игрок с сетью получал бы мигание
            // «прав нет» → «права есть» плюс ложный отзыв в аналитике. Грейс оценивается
            // после реального провала сверки (OnReconcileExhausted) и на форграунде.
            SdkTrustedTime.OnFirstFreshTime += OnTrustedTimeAvailable;
            StartCoroutine(SdkTrustedTime.FetchOnce());

            InitializeAmazonIAP();
        }

        private void InitializeAmazonIAP()
        {
            if (!_serviceReady)
            {
                _gateway ??= new AmazonIapV2Gateway();

                if (!_gateway.TryAcquire(OnGetProductDataResponse, OnPurchaseResponseHandler,
                        OnGetPurchaseUpdatesResponse, out var error))
                {
                    Debug.LogError($"[AMZNGoDSDK] Amazon IAP service init error: {error}");
                    _analytics.CatalogFailed("init_error");
                    return;
                }

                _serviceReady = true;
            }

            // Идемпотентно: список запроса каталога наполняется заново при каждом вызове,
            // поэтому RetryInitialize реально перезапрашивает цены (IAP-04).
            _catalog.Configure(settings);
            RequestProductData();
            StartReconcile(null);
            RetryPendingFulfillments();

            if (!IsInitialized)
            {
                IsInitialized = true;
                Debug.Log("[AMZNGoDSDK] Amazon IAP initialized");
            }
        }

        /// <summary>
        /// Повтор инициализации/каталога после сбоя. Дёргается автоматически повторами с
        /// бэкоффом и доступен игре через AmznGoDSDKCore (IAP-04: раньше не вызывался
        /// нигде и не работал бы из-за пустого списка SKU при повторном вызове).
        /// </summary>
        public void RetryInitialize()
        {
            if (!Enabled)
                return;

            InitializeAmazonIAP();
        }

        /// <summary>Автоповтор ТОЛЬКО каталога: полный InitializeAmazonIAP при каждой
        /// неудачной волне заодно гонял бы сверку и NotifyFulfillment в сторону Amazon.</summary>
        private void RetryCatalog()
        {
            if (!_serviceReady)
            {
                InitializeAmazonIAP();
                return;
            }

            _catalog.Configure(settings);
            RequestProductData();
        }

        private void RequestProductData()
        {
            var skus = _catalog.CatalogRequestSkus;
            if (skus.Count == 0)
                return;

            // Батчи по 100: один сверхлимитный запрос падает целиком вместе со всем каталогом.
            for (int i = 0; i < skus.Count; i += ProductDataBatchSize)
            {
                var batch = new List<string>();
                int end = Math.Min(i + ProductDataBatchSize, skus.Count);
                for (int j = i; j < end; j++)
                    batch.Add(skus[j]);

                if (!_gateway.TryGetProductData(batch, out var error))
                {
                    Debug.LogError($"[AMZNGoDSDK] GetProductData batch [{i}..{end}) failed: {error}");
                    _analytics.CatalogFailed("catalog_request_failed");
                    _catalogRetry.OnFailure();
                    return;
                }
            }
        }

        private void OnGetProductDataResponse(GetProductDataResponse response)
        {
            if (response.Status != "SUCCESSFUL")
            {
                Debug.LogWarning($"[AMZNGoDSDK] GetProductData failed: {response.Status}");
                _analytics.CatalogFailed("catalog_response_failed", response.Status);
                _catalogRetry.OnFailure();
                return;
            }

            _catalogRetry.OnSuccess();

            if (response.ProductDataMap != null)
                _catalog.StoreProductData(response.ProductDataMap);

            if (response.UnavailableSkus != null && response.UnavailableSkus.Count > 0)
            {
                // Приходит ВНУТРИ SUCCESSFUL — частичная недоступность части SKU, а не
                // отказ запроса каталога. Отдельная причина уровня 1.
                Debug.LogWarning($"[AMZNGoDSDK] Unavailable SKUs: {string.Join(", ", response.UnavailableSkus)}");
                _analytics.CatalogFailed("sku_unavailable");
            }

            Debug.Log($"[AMZNGoDSDK] Product data loaded: {_catalog.CachedProductCount} products");
        }

        #endregion

        #region Purchase

        public void BuyProduct(string productId)
        {
            productId ??= "unknown";                     // единая нормализация ключа SKU в воронке

            _analytics.PurchaseRequested(productId);     // ПЕРВОЙ строкой — каждое нажатие (IAP-29)

            // IAP-29: линия раздела — ушёл вызов в Amazon или нет. Локальные отказы — в
            // blocked, started шлётся только после успешного TryPurchase: инварианты
            // requested = started + blocked и started = success + failed.
            if (!_serviceReady)
            {
                Debug.LogError("[AMZNGoDSDK] Store not initialized. Purchase impossible.");
                _analytics.PurchaseBlocked(productId, "not_initialized");
                return;
            }

            if (!_catalog.CanBuy(productId))
            {
                Debug.LogError($"[AMZNGoDSDK] Product not registered or disabled: {productId}");
                _analytics.PurchaseBlocked(productId, "product_not_registered");
                return;
            }

            // IAP-21: вторая покупка до ответа перетёрла бы SKU текущей, и отказ по первой
            // приписался бы второму товару. Лок только внутри окна — см. константу.
            if (_pendingPurchaseSku != null
                && Time.realtimeSinceStartup - _pendingPurchaseStartedRealtime < PendingPurchaseWindowSeconds)
            {
                Debug.LogWarning($"[AMZNGoDSDK] Purchase already in progress ({_pendingPurchaseSku}), rejecting {productId}");
                _analytics.PurchaseBlocked(productId, "purchase_in_progress");
                return;
            }

            _pendingPurchaseSku = productId;
            _pendingPurchaseStartedRealtime = Time.realtimeSinceStartup;
            _pendingTerminalReported = false;

            if (!_gateway.TryPurchase(productId, out var requestId, out var error))
            {
                // Текст ошибки в событие не кладём: там урлы/id аккаунта — рост
                // кардинальности параметра и утечка PII. Вызов в Amazon не ушёл → blocked;
                // started не отправлен, поэтому защёлка глушит терминал позднего ответа
                // нативки целиком (терминал без started сломал бы инвариант).
                Debug.LogError($"[AMZNGoDSDK] Purchase threw for {productId}: {error}");
                _analytics.PurchaseBlocked(productId, "exception");
                _pendingTerminalReported = true;
                _pendingPurchaseSku = null;
                _pendingPurchaseRequestId = null;
                return;
            }

            // Ответ приходит асинхронно (UnitySendMessage, не раньше следующего кадра),
            // поэтому записать RequestId и отправить started после вызова — безопасно.
            _pendingPurchaseRequestId = requestId;
            _analytics.PurchaseStarted(productId);       // вызов реально ушёл в Amazon (IAP-29)
        }

        private void OnPurchaseResponseHandler(PurchaseResponse response)
        {
            // Ответ ЧУЖОГО запроса (RequestId есть с обеих сторон и не совпал) — например,
            // покупка, брошенная на диалоге дольше 90-секундного окна, чей ответ пришёл уже
            // после старта следующей. Чек из него обрабатываем честно (деньги могли
            // списаться), но лок и воронку ТЕКУЩЕЙ покупки не трогаем: раньше такой ответ
            // закрывал текущую покупку и приписывал ей чужой отказ. Когда сравнивать нечего
            // (editor-стаб/симулятор без RequestId) — считаем ответ своим, как раньше.
            bool foreign = _pendingPurchaseSku != null
                && !string.IsNullOrEmpty(response.RequestId)
                && !string.IsNullOrEmpty(_pendingPurchaseRequestId)
                && response.RequestId != _pendingPurchaseRequestId;

            string pendingSku;
            bool terminalAlreadySent;

            if (foreign)
            {
                Debug.LogWarning($"[AMZNGoDSDK] Purchase response for a stale request {response.RequestId} " +
                                 $"(current: {_pendingPurchaseRequestId}) — processing receipt without touching the current purchase");
                pendingSku = null;   // атрибуция только по чеку, не по текущей покупке

                // Терминал воронки ЧУЖОЙ покупки шлём: её started уже отправлен, и закрыть
                // его, кроме как отсюда, нечем — глушение ломало бы started ≈ success +
                // failed ровно на мультитапах. SKU берётся из чека — отдельный ключ воронки,
                // с текущей покупкой не конфликтует. Ответ без чека (типичный FAILED) закрыть
                // нечем: SKU неизвестен, терминал под "unknown" загрязнил бы воронку —
                // подавляем. Состояние ТЕКУЩЕЙ покупки не трогаем в любом случае.
                terminalAlreadySent = string.IsNullOrEmpty(response.PurchaseReceipt?.Sku);
            }
            else
            {
                pendingSku = _pendingPurchaseSku;
                terminalAlreadySent = _pendingTerminalReported;
                _pendingPurchaseSku = null;      // обнуляем ДО внешних колбэков (ре-энтрантный BuyProduct)
                _pendingPurchaseRequestId = null;
                _pendingTerminalReported = false;
            }

            bool successStatus = response.Status == "SUCCESSFUL" || response.Status == "ALREADY_PURCHASED";

            if (successStatus)
            {
                var receipt = response.PurchaseReceipt;
                if (receipt == null)
                {
                    string noReceiptSku = pendingSku ?? "unknown";

                    if (response.Status == "ALREADY_PURCHASED")
                    {
                        // Повторное нажатие «купить»: ALREADY_PURCHASED часто приходит БЕЗ
                        // чека. Это не отказ и не продажа — в воронке already_owned (IAP-16),
                        // право подтвердит сверка. Игре — failed: ничего не выдано.
                        Debug.Log($"[AMZNGoDSDK] Purchase ALREADY_PURCHASED without receipt: {noReceiptSku}");
                        if (!terminalAlreadySent)
                            _analytics.PurchaseSuccess(noReceiptSku, alreadyOwned: true);
                        StartReconcile(null);
                        if (!foreign)
                            SafeInvokePurchaseFailed(noReceiptSku);
                        return;
                    }

                    // SUCCESSFUL, а чека нет — деньги могли списаться. Чек почти наверняка
                    // приедет в истории: перезапрашиваем её прямо здесь (IAP-20).
                    Debug.LogWarning($"[AMZNGoDSDK] Purchase {response.Status} without receipt: {noReceiptSku}");
                    if (!terminalAlreadySent)
                        _analytics.PurchaseFailed(noReceiptSku, "no_receipt");
                    StartReconcile(null);
                    if (!foreign)
                        SafeInvokePurchaseFailed(noReceiptSku);
                    return;
                }

                string sku = string.IsNullOrEmpty(receipt.Sku) ? (pendingSku ?? "unknown") : receipt.Sku;
                bool alreadyOwned = response.Status == "ALREADY_PURCHASED";

                Debug.Log($"[AMZNGoDSDK] Purchase {response.Status}: {sku}");

                // Исключение из обработки (журнал, PlayerPrefs) не должно всплыть в FireEvent
                // плагина и оставить игру без терминального колбэка — класс проблемы IAP-01.
                LiveReceiptOutcome outcome;
                try
                {
                    outcome = ProcessLiveReceipt(receipt, newPurchase: !alreadyOwned);
                    _journal.SaveIfDirty();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AMZNGoDSDK] Live receipt processing threw for {sku}: {e}");
                    if (!terminalAlreadySent)
                        _analytics.PurchaseFailed(sku, "exception");
                    StartReconcile(null);   // чек не подтверждён — вернётся в истории
                    if (!foreign)
                        SafeInvokePurchaseFailed(sku);
                    return;
                }

                // Фолбэк-матчинг мог нормализовать receipt.Sku (term → родительский):
                // колбэки и воронка должны нести SKU, который игра знает по настройкам.
                if (!string.IsNullOrEmpty(receipt.Sku))
                    sku = receipt.Sku;

                if (outcome == LiveReceiptOutcome.NotProcessed)
                {
                    // Стор ответил SUCCESSFUL, но выдать нечего (SKU не настроен, тип
                    // разошёлся): игре — честный failed, а не вечное молчание с подвисшим
                    // UI покупки. Чек не закрыт и вернётся сверкой, когда конфиг починят.
                    if (!terminalAlreadySent)
                        _analytics.PurchaseFailed(sku, "receipt_not_processed");
                    StartReconcile(null);
                    if (!foreign)
                        SafeInvokePurchaseFailed(sku);
                    return;
                }

                // Аналитика ПОСЛЕ выдачи (IAP-01) и с разрезом new/already_owned (IAP-16):
                // повторная покупка того, чем игрок уже владеет, — не продажа.
                if (!terminalAlreadySent)
                    _analytics.PurchaseSuccess(sku, alreadyOwned);

                // Duplicate — повторный чек уже выданного расходуемого (дублированный ответ
                // нативки): терминальный колбэк по нему уже был, второй OnPurchaseComplete
                // начислил бы товар дважды («ровно один раз на чек», README §3).
                if (outcome == LiveReceiptOutcome.Granted)
                    SafeInvokePurchaseComplete(sku);

                // Живая покупка применила только-добавляющее обновление; полная сверка
                // следом подтверждает его штатным снапшотом.
                StartReconcile(null);
            }
            else
            {
                string sku = response.PurchaseReceipt?.Sku ?? pendingSku ?? "unknown";
                Debug.LogWarning($"[AMZNGoDSDK] Purchase failed: {sku} - {response.Status}");
                if (!terminalAlreadySent)
                    _analytics.PurchaseFailed(sku, IapAnalytics.MapAmazonStatus(response.Status));
                if (!foreign)
                    SafeInvokePurchaseFailed(sku);
            }
        }

        private enum LiveReceiptOutcome
        {
            /// <summary>Чек дал новое право/товар — игре положен OnPurchaseComplete.</summary>
            Granted,

            /// <summary>Повторный чек уже выданного (дублированный ответ нативки):
            /// терминал по нему уже был, колбэки не зовутся.</summary>
            Duplicate,

            /// <summary>Чек не обработан (пустые поля, ненастроенный SKU, расхождение
            /// типа) — игре положен failed, чек не закрыт и вернётся сверкой.</summary>
            NotProcessed,
        }

        /// <summary>
        /// Чек живой покупки. Только добавление: свежий SUCCESSFUL-чек может дать право или
        /// товар, но никогда ничего не снимает — снятие исключительно снапшотом сверки.
        /// Матчинг к настройкам — с нормализацией term SKU (раздел H ТЗ).
        /// newPurchase = статус SUCCESSFUL (не ALREADY_PURCHASED): только такой чек — новая
        /// транзакция, которой положен якорь журнала периодов.
        /// </summary>
        private LiveReceiptOutcome ProcessLiveReceipt(PurchaseReceipt receipt, bool newPurchase)
        {
            if (string.IsNullOrEmpty(receipt.ReceiptId) || string.IsNullOrEmpty(receipt.Sku))
                return LiveReceiptOutcome.NotProcessed;

            if (!TryResolveAndNormalize(receipt, out var product))
            {
                LogUnknownReceipt(receipt);
                return LiveReceiptOutcome.NotProcessed;
            }

            if (!ValidateProductType(receipt, product))
                return LiveReceiptOutcome.NotProcessed;

            _entitlements.MarkEverPurchased(receipt.Sku);
            var outcome = LiveReceiptOutcome.Granted;

            switch (product.Kind)
            {
                case IapProductKind.Consumable:
                    // Идемпотентность по ReceiptId — ранний return старого кода, который
                    // гасил переоценку подписок (IAP-02), переехал ровно сюда. Повторный
                    // чек (дублированный ответ нативки) не даёт второго начисления.
                    if (_journal.IsGranted(receipt.ReceiptId))
                        outcome = LiveReceiptOutcome.Duplicate;
                    _journal.MarkGranted(receipt.ReceiptId);
                    break;

                case IapProductKind.Subscription:
                    _entitlements.ApplyLivePurchaseGrant(receipt.Sku);
                    RaiseGranted(receipt.Sku);

                    // Якорь журнала для СВЕЖЕЙ покупки: без него FirePeriods, вышедший по
                    // отсутствию доверенного времени, оставил бы чек неизвестным журналу — и
                    // первое время спустя N периодов выдало бы только текущий (правило
                    // засева), молча съев промежуточные. -1 = «чек знаем, не выдано ни
                    // одного» → позже выдастся диапазон с нуля. Восстановленным чекам якорь
                    // НЕ ставится: переустановка + недоступный бэкенд получили бы всю
                    // историю подписки (фарм, от которого правило засева и защищает).
                    if (newPurchase && !_journal.TryGetLastFiredPeriod(receipt.ReceiptId, out _))
                        _journal.SetLastFiredPeriod(receipt.ReceiptId, -1);

                    FirePeriods(receipt, product);   // журнал пишут ОБЕ ветки через один хелпер (IAP-14)
                    break;

                case IapProductKind.NonConsumable:
                    _entitlements.ApplyLivePurchaseGrant(receipt.Sku);
                    RaiseGranted(receipt.Sku);
                    break;
            }

            NotifyFulfillmentSafe(receipt.ReceiptId);
            return outcome;
        }

        #endregion

        #region Reconcile (GetPurchaseUpdates)

        public void RestorePurchases(Action<bool> onComplete = null)
        {
            if (!_serviceReady)
            {
                Debug.LogWarning("[AMZNGoDSDK] Cannot restore purchases — not initialized");
                onComplete?.Invoke(false);
                return;
            }

            StartReconcile(onComplete);
        }

        /// <summary>Ручная пересверка прав; ею же пользуются форграунд и повторы.</summary>
        public void RefreshEntitlements()
        {
            if (_serviceReady)
                StartReconcile(null);
        }

        private void StartReconcile(Action<bool> onComplete)
        {
            if (onComplete != null)
                _restoreCallbacks.Add(onComplete);

            // Single-flight: слушатель Amazon один, RequestId не читается — параллельные
            // прогоны перемешали бы страницы. Колбэк дождётся текущего прогона.
            if (!_reconcileRetry.TryBegin())
                return;

            BeginReconcileRun();
        }

        private void RetryReconcile()
        {
            if (_serviceReady && _reconcileRetry.TryBegin())
                BeginReconcileRun();
        }

        private void BeginReconcileRun()
        {
            // Новый прогон всегда с Reset=true и чистых аккумуляторов — дожимать оборванную
            // цепочку нельзя (IAP-03): Reset=false отдаёт только новое, а для подписок лишь
            // неподтверждённое.
            _reconcileSession = new IapReconcileSession();

            if (!_gateway.TryGetPurchaseUpdates(true, out var requestId, out var error))
            {
                Debug.LogError($"[AMZNGoDSDK] GetPurchaseUpdates failed: {error}");
                OnReconcileRunFailed();
                return;
            }

            _reconcileSession.RegisterRequest(requestId);
        }

        private void OnReconcileRunFailed()
        {
            _reconcileSession = null;    // частичный ответ выбрасывается целиком
            _reconcileRetry.OnFailure(); // бэкофф 2/8/30 с; состояние остаётся Unknown

            // Ручной Restore не должен держать экран «Восстановление…» весь цикл бэкоффа
            // (~40 с): колбэк отпускаем по первому сбою, ретраи продолжаются фоном — если
            // поздний прогон успеет, игра узнает штатными событиями прав.
            CompleteRestoreCallbacks(false);
        }

        private void OnReconcileExhausted()
        {
            // Попытки кончились: по одной неудаче права НЕ трогаем (Unknown — не «прав
            // нет»), но именно здесь честное место грейса (IAP-32): «пытались проверить и
            // не смогли» — если сверки нет дольше порога, доступ снимается один раз.
            foreach (var sku in _entitlements.EvaluateGrace(DateTime.UtcNow, TermDaysFor))
            {
                _analytics.AccessRevoked(sku, "grace_expired");
                RaiseRevoked(sku);
            }

            CompleteRestoreCallbacks(false);
        }

        private void OnGetPurchaseUpdatesResponse(GetPurchaseUpdatesResponse response)
        {
            if (_reconcileSession == null)
            {
                Debug.LogWarning("[AMZNGoDSDK] GetPurchaseUpdates response without an active run — ignored");
                return;
            }

            // Ответ мёртвого прогона (ватчдог объявил его умершим, начался новый): страница
            // ЧУЖОЙ цепочки в текущей сессии — это неполный снапшот и массовое снятие прав.
            if (!_reconcileSession.OwnsResponse(response.RequestId))
            {
                Debug.LogWarning($"[AMZNGoDSDK] GetPurchaseUpdates response for a stale run ({response.RequestId}) — ignored");
                return;
            }

            if (response.Status != "SUCCESSFUL")
            {
                Debug.LogWarning($"[AMZNGoDSDK] GetPurchaseUpdates failed: {response.Status}");
                OnReconcileRunFailed();
                return;
            }

            // Нормализация ДО аккумуляции: снапшот прав считается по receipt.Sku, и term SKU
            // подписки должен быть приведён к настроенному родительскому уже здесь.
            if (response.Receipts != null)
                foreach (var receipt in response.Receipts)
                    TryResolveAndNormalize(receipt, out _);

            _reconcileSession.AddPage(response.Receipts);

            if (response.HasMore)
            {
                // Продолжение страниц внутри одного ответа — единственное место для Reset=false.
                if (!_gateway.TryGetPurchaseUpdates(false, out var requestId, out var error))
                {
                    Debug.LogError($"[AMZNGoDSDK] GetPurchaseUpdates continuation failed: {error}");
                    OnReconcileRunFailed();
                    return;
                }

                _reconcileSession.RegisterRequest(requestId);
                return;
            }

            var result = _reconcileSession.Complete(_catalog.LongLivedSkus);
            _reconcileSession = null;
            ApplyReconcileResult(result);
        }

        private void ApplyReconcileResult(IapReconcileResult result)
        {
            // 1. Чеки: выдача расходуемых (однократно), периоды подписок, подтверждения.
            //    Упавший NotifyFulfillment кладёт чек в очередь и НЕ рвёт цикл (IAP-03).
            //    try/catch на каждый чек: исключение (из игрового колбэка в том числе) не
            //    должно оставить снапшот неприменённым, а single-flight — залипшим.
            foreach (var receipt in result.Receipts)
            {
                try { ProcessRestoredReceipt(receipt); }
                catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] Restored receipt {receipt.ReceiptId} processing threw: {e}"); }
            }

            // 2. Права — снапшотом целиком: единственное место, где право может стать NotEntitled.
            var diff = _entitlements.ApplySnapshot(result, _catalog.LongLivedSkus, DateTime.UtcNow);

            _lastReceipts = result.Receipts;
            _lastReconcileRealtime = Time.realtimeSinceStartup;
            _journal.SaveIfDirty();

            // Событие выдачи — сигнал состояния «игрок этим владеет», приходит при каждом
            // запуске (история приезжает целиком каждый раз). Обработчик игры обязан быть
            // идемпотентным и не начислять валюту — см. README.
            foreach (var sku in diff.Entitled)
                RaiseGranted(sku);

            foreach (var (sku, cause) in diff.Revoked)
            {
                // Причина для iap_access_revoked (IAP-31): чек с датой отмены у подписки —
                // истечение, у разовой покупки — возврат денег; исчезнувший чек — отдельный
                // сигнал (гонка класса IAP-26 или сбой истории), не отток.
                string reason = cause == IapSnapshotRevokeCause.ReceiptCancelled
                    ? (_catalog.TryResolve(sku, out var product) && product.Kind == IapProductKind.Subscription
                        ? "expired"
                        : "refunded")
                    : "receipt_gone";

                Debug.LogWarning($"[AMZNGoDSDK] Entitlement revoked by reconciliation: {sku} ({reason})");
                _analytics.AccessRevoked(sku, reason);
                RaiseRevoked(sku);
            }

            _reconcileRetry.OnSuccess();
            CompleteRestoreCallbacks(true);

            Debug.Log($"[AMZNGoDSDK] Reconciled with Amazon: receipts={result.Receipts.Count}, active=[{string.Join(", ", result.ActiveSkus)}]");
        }

        private void ProcessRestoredReceipt(PurchaseReceipt receipt)
        {
            // Чеки страницы уже нормализованы при приёме; повторный вызов идемпотентен.
            if (!TryResolveAndNormalize(receipt, out var product))
            {
                LogUnknownReceipt(receipt);
                return;
            }

            if (!ValidateProductType(receipt, product))
                return;

            _entitlements.MarkEverPurchased(receipt.Sku);

            switch (product.Kind)
            {
                case IapProductKind.Consumable:
                    // Невыданный расходуемый: сообщаем игре ровно один раз (журнал).
                    // Отменённый Amazon'ом (возврат) — не выдаём, но чек закрываем, иначе
                    // он будет возвращаться в истории вечно (IAP-06).
                    if (receipt.CancelDate == 0 && !_journal.IsGranted(receipt.ReceiptId))
                    {
                        _journal.MarkGranted(receipt.ReceiptId);
                        _analytics.PurchaseRestored(receipt.Sku);
                        SafeInvokePurchaseComplete(receipt.Sku);   // начисляет игра (IAP-13)
                    }
                    break;

                case IapProductKind.Subscription:
                    // Проверка раздела H ТЗ: по документации getSku возвращает родительский
                    // SKU подписки (то, что заведено в настройках), но мост нестандартный —
                    // если на устройстве Sku окажется term-ом, матчинг придётся вести по TermSku.
                    Debug.Log($"[AMZNGoDSDK] Subscription receipt: sku={receipt.Sku}, termSku={receipt.TermSku}, " +
                              $"type={receipt.ProductType}, purchase={receipt.PurchaseDate}, cancel={receipt.CancelDate}");

                    // Ветка схлопнута (IAP-02): ничего не выдаёт и не продлевает — право
                    // выражается снапшотом, периоды считает калькулятор (потолок CancelDate
                    // внутри него: оплаченные ДО отмены периоды выдаются).
                    FirePeriods(receipt, product);
                    break;

                case IapProductKind.NonConsumable:
                    break;   // право выражается снапшотом
            }

            NotifyFulfillmentSafe(receipt.ReceiptId);
        }

        private void CompleteRestoreCallbacks(bool success)
        {
            if (_restoreCallbacks.Count == 0)
                return;

            var callbacks = new List<Action<bool>>(_restoreCallbacks);
            _restoreCallbacks.Clear();

            foreach (var callback in callbacks)
            {
                try { callback?.Invoke(success); }
                catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] Restore callback threw: {e}"); }
            }
        }

        #endregion

        #region Receipt helpers

        /// <summary>
        /// Матчинг чека к настройкам с нормализацией SKU: если фолбэк каталога (term SKU
        /// подписки, раздел H ТЗ) сматчил чек не по точному Sku, receipt.Sku приводится к
        /// настроенному ProductId — снапшот прав, журналы и события игры живут в одном
        /// пространстве SKU. Идемпотентно; null-чек безопасен.
        /// </summary>
        private bool TryResolveAndNormalize(PurchaseReceipt receipt, out IapConfiguredProduct product)
        {
            if (!_catalog.TryResolveReceipt(receipt, out product))
                return false;

            if (!string.Equals(receipt.Sku, product.ProductId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[AMZNGoDSDK] Receipt sku '{receipt.Sku}' (termSku '{receipt.TermSku}') " +
                                 $"matched to configured product '{product.ProductId}' — sku normalized");
                receipt.Sku = product.ProductId;
            }

            return true;
        }

        /// <summary>
        /// Неизвестный чек НЕ закрываем (IAP-05): раньше он получал NotifyFulfillment и
        /// сгорал без выдачи навсегда. Незакрытый вернётся при следующем запуске — и будет
        /// обработан, когда продукт появится в настройках.
        /// </summary>
        private void LogUnknownReceipt(PurchaseReceipt receipt)
        {
            Debug.LogError($"[AMZNGoDSDK] Receipt for unconfigured product '{receipt.Sku}' " +
                           $"(type {receipt.ProductType}). NOT fulfilling — add the product to SDK Settings.");
        }

        /// <summary>Сверка типа из настроек с тем, что говорит Amazon (IAP-05): при
        /// расхождении — внятная ошибка, чек не обрабатывается и не закрывается.</summary>
        private bool ValidateProductType(PurchaseReceipt receipt, IapConfiguredProduct product)
        {
            if (string.IsNullOrEmpty(receipt.ProductType))
                return true;   // старые стабы/тесты тип не заполняют — не повод жечь чек

            string expected = product.Kind switch
            {
                IapProductKind.Subscription => "SUBSCRIPTION",
                IapProductKind.Consumable => "CONSUMABLE",
                _ => "ENTITLED",
            };

            if (receipt.ProductType == expected)
                return true;

            Debug.LogError($"[AMZNGoDSDK] Product type mismatch for '{receipt.Sku}': configured as {expected}, " +
                           $"Amazon says {receipt.ProductType}. Receipt NOT processed — fix the product type in SDK Settings.");
            return false;
        }

        private void NotifyFulfillmentSafe(string receiptId)
        {
            if (_gateway.TryNotifyFulfillment(receiptId, out var error))
            {
                _journal.ClearPendingFulfillment(receiptId);
                return;
            }

            // Не рвём обработку остальных чеков (IAP-03). Выдача уже случилась и записана,
            // поэтому повтор подтверждения при старте ничего не выдаст заново (IAP-12).
            Debug.LogWarning($"[AMZNGoDSDK] NotifyFulfillment failed for {receiptId}: {error} — queued for retry");
            _journal.MarkPendingFulfillment(receiptId);
        }

        private void RetryPendingFulfillments()
        {
            var pending = _journal.PendingFulfillment;
            if (pending.Count == 0)
                return;

            Debug.Log($"[AMZNGoDSDK] Retrying {pending.Count} pending fulfillment(s)");
            foreach (var receiptId in new List<string>(pending))
                NotifyFulfillmentSafe(receiptId);
            _journal.SaveIfDirty();
        }

        /// <summary>
        /// Оплаченные периоды подписки (IAP-14). Единственная точка выдачи для обеих веток —
        /// живой покупки и восстановления: если бы живая покупка выдавала мимо журнала,
        /// следующий запуск выдал бы второй раз.
        /// </summary>
        private void FirePeriods(PurchaseReceipt receipt, IapConfiguredProduct product)
        {
            if (product.Kind != IapProductKind.Subscription)
                return;

            if (product.TermDays <= 0)
            {
                // Барьеры IAP-15 (окно настроек + билд-гард) сюда не пускают; если срок всё
                // же приехал нулевым — не начисляем и говорим об этом, а не подставляем 1.
                Debug.LogError($"[AMZNGoDSDK] Subscription '{product.ProductId}' has no TermDays — periods are NOT accrued. " +
                               "Set Term (days) in AMZN GoD → SDK Settings.");
                return;
            }

            // Без доверенного времени не выдаём ничего — ждём сети (часы устройства игрок
            // может перевести). Живая покупка к этому моменту уже поставила якорь журнала,
            // поэтому OnTrustedTimeAvailable доначислит с нуля; восстановленные чеки без
            // записи в журнале сознательно идут по правилу засева (только текущий период).
            var now = SdkTrustedTime.UtcNow;
            if (now == null)
                return;

            bool hasEntry = _journal.TryGetLastFiredPeriod(receipt.ReceiptId, out int lastFired);
            var toFire = IapSubscriptionPeriods.PeriodsToFire(
                receipt.PurchaseDate, product.TermDays, receipt.CancelDate, receipt.DeferredDate,
                now.Value, hasEntry, lastFired);

            foreach (var index in toFire)
            {
                // Журнал до события: при краше между ними период выдастся повторно после
                // рестарта (журнал не успел на диск) — теряем в пользу игрока, не наоборот.
                _journal.SetLastFiredPeriod(receipt.ReceiptId, index);

                var period = new IapPeriodStarted(
                    receipt.Sku, receipt.ReceiptId, index,
                    IapSubscriptionPeriods.PeriodStartUtc(receipt.PurchaseDate, product.TermDays, index));

                Debug.Log($"[AMZNGoDSDK] Paid period started: {receipt.Sku} #{index}");

                // IAP-30: продление — в аналитику (журнал выше гарантирует «ровно один раз
                // на период», перезапуски не дублируют). Нулевой период — сама покупка, у
                // неё уже есть iap_purchase_success.
                if (index >= 1)
                    _analytics.SubscriptionRenewed(receipt.Sku, index);

                RaisePeriodStarted(period);
            }
        }

        private void OnTrustedTimeAvailable()
        {
            if (_lastReceipts == null)
                return;

            // Тот же фильтр, что и в основном пути: расхождение типа продукта не должно
            // обходиться через отложенное доначисление (старт без сети).
            foreach (var receipt in _lastReceipts)
                if (TryResolveAndNormalize(receipt, out var product) && ValidateProductType(receipt, product))
                    FirePeriods(receipt, product);

            _journal.SaveIfDirty();
        }

        #endregion

        #region Lifecycle

        private void OnApplicationPause(bool paused)
        {
            if (!Enabled)
                return;

            if (paused)
            {
                _journal.SaveIfDirty();
                _entitlements.SaveIfDirty();
                return;
            }

            OnForeground();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (Enabled && focused)
                OnForeground();
        }

        private void OnForeground()
        {
            if (!_serviceReady)
                return;

            // Брошенный диалог покупки не должен блокировать магазин (IAP-21), но сам
            // диалог Amazon — оверлей: возврат из него тоже даёт pause/focus. Сбрасываем
            // только протухший лок — безусловный сброс терял бы SKU для атрибуции отказа
            // (почти каждая реальная отмена уходила бы как sku=unknown) и выключал лок.
            if (_pendingPurchaseSku != null
                && Time.realtimeSinceStartup - _pendingPurchaseStartedRealtime >= PendingPurchaseWindowSeconds)
            {
                _pendingPurchaseSku = null;
                _pendingPurchaseRequestId = null;
            }

            // IAP-32: пока прогон сверки в полёте, грейс не оценивается — решение примет её
            // исход (успех обновит отметку, исчерпание ретраев снимет доступ). Критично на
            // старте: OnApplicationFocus(true) приходит и при запуске, и без этой проверки
            // грейс сработал бы по старой отметке до завершения первой сверки — то самое
            // мигание, ради которого вызов убран из Initialize.
            if (_reconcileSession == null)
                foreach (var sku in _entitlements.EvaluateGrace(DateTime.UtcNow, TermDaysFor))
                {
                    _analytics.AccessRevoked(sku, "grace_expired");
                    RaiseRevoked(sku);
                }

            _reconcileRetry.OnForeground();
            _catalogRetry.OnForeground();

            if (!SdkTrustedTime.HasFreshTime)
                StartCoroutine(SdkTrustedTime.FetchOnce());

            // Продление, случившееся пока приложение было в фоне, иначе не заметить до
            // перезапуска процесса (IAP-11).
            if (Time.realtimeSinceStartup - _lastReconcileRealtime > ReconcileStaleSeconds)
                StartReconcile(null);
        }

        public override void Cleanup()
        {
            SdkTrustedTime.OnFirstFreshTime -= OnTrustedTimeAvailable;
            _gateway?.ReleaseListeners();
        }

        #endregion

        #region State API

        /// <summary>Порог грейса складывается из базы и срока периода подписки: у разовых
        /// покупок и SKU вне конфига добавка нулевая (см. IapEntitlementStore.EvaluateGrace).</summary>
        private int TermDaysFor(string sku) =>
            _catalog.TryResolve(sku, out var product) ? product.TermDays : 0;

        public IapEntitlementState GetEntitlementState(string productId) =>
            _entitlements.GetState(productId);

        public IapEntitlement GetEntitlement(string productId) =>
            _entitlements.GetEntitlement(productId);

        /// <summary>
        /// Legacy-bool: пока состояние Unknown, отдаёт последнее сохранённое значение, а НЕ
        /// false — иначе каждый запуск начинался бы с того, что подписчик на пару секунд
        /// теряет премиум (IAP-02/IAP-11).
        /// </summary>
        public bool IsSubscribed(string productId) =>
            _entitlements.GetEffectiveAccess(productId);

        public bool HasSubscription(string productId) =>
            IsSubscribed(productId);

        /// <summary>
        /// Отвечает ИДЕНТИЧНО IsSubscribed (IAP-07: раньше методы врали в разные стороны).
        /// Только долгоживущие права: чеки расходуемых Amazon возвращает вечно, и по ним
        /// HasReceipt был бы навсегда true при давно потраченном балансе. «Покупал ли
        /// когда-либо» — <see cref="HasEverPurchased"/>.
        /// </summary>
        public bool HasReceipt(string productId) =>
            _entitlements.GetEffectiveAccess(productId);

        public bool HasEverPurchased(string productId) =>
            _entitlements.HasEverPurchased(productId);

        public ProductData GetProduct(string productId) => _catalog.GetProduct(productId);

        public IEnumerable<ProductData> GetAllProducts() => _catalog.GetAllProducts();

        #endregion

        #region Listeners

        // Пары Add/Remove вместо event: доигрывание последнего снимка поздним подписчикам
        // требует тела метода (IAP-11 — иначе поздний подписчик теряет событие навсегда).

        public void AddEntitlementGrantedListener(Action<IapEntitlement> listener)
        {
            if (listener == null)
                return;
            _grantedListeners.Add(listener);
            foreach (var sku in _entitlements.EntitledSkus)
                SafeInvoke(listener, _entitlements.GetEntitlement(sku));
        }

        public void RemoveEntitlementGrantedListener(Action<IapEntitlement> listener)
        {
            if (listener != null)
                _grantedListeners.Remove(listener);
        }

        public void AddEntitlementRevokedListener(Action<IapEntitlement> listener)
        {
            if (listener == null)
                return;
            _revokedListeners.Add(listener);
            foreach (var sku in _entitlements.RevokedOwnedSkus)
                SafeInvoke(listener, _entitlements.GetEntitlement(sku));
        }

        public void RemoveEntitlementRevokedListener(Action<IapEntitlement> listener)
        {
            if (listener != null)
                _revokedListeners.Remove(listener);
        }

        // Без доигрывания: защита от потери — персистентный журнал периодов, а не replay.
        public void AddPeriodStartedListener(Action<IapPeriodStarted> listener)
        {
            if (listener != null)
                _periodListeners.Add(listener);
        }

        public void RemovePeriodStartedListener(Action<IapPeriodStarted> listener)
        {
            if (listener != null)
                _periodListeners.Remove(listener);
        }

        private void RaiseGranted(string sku)
        {
            var entitlement = _entitlements.GetEntitlement(sku);
            foreach (var listener in _grantedListeners.ToArray())
                SafeInvoke(listener, entitlement);
        }

        private void RaiseRevoked(string sku)
        {
            var entitlement = _entitlements.GetEntitlement(sku);
            foreach (var listener in _revokedListeners.ToArray())
                SafeInvoke(listener, entitlement);
        }

        private void RaisePeriodStarted(IapPeriodStarted period)
        {
            foreach (var listener in _periodListeners.ToArray())
            {
                try { listener(period); }
                catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] Period listener threw: {e}"); }
            }
        }

        private static void SafeInvoke(Action<IapEntitlement> listener, IapEntitlement entitlement)
        {
            try { listener(entitlement); }
            catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] Entitlement listener threw: {e}"); }
        }

        // Игровой обработчик — чужой код: его исключение не должно рвать обработку чеков,
        // применение снапшота и single-flight сверки (тот же принцип, что IAP-01 для аналитики).

        private void SafeInvokePurchaseComplete(string sku)
        {
            try { OnPurchaseComplete?.Invoke(sku); }
            catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] OnPurchaseComplete handler threw: {e}"); }
        }

        private void SafeInvokePurchaseFailed(string sku)
        {
            try { OnPurchaseFailedCallback?.Invoke(sku); }
            catch (Exception e) { Debug.LogError($"[AMZNGoDSDK] OnPurchaseFailed handler threw: {e}"); }
        }

        // --- Legacy-колбэки покупок (контракт сохранён) ---

        public void SetPurchaseCompleteCallback(Action<string> callback) => OnPurchaseComplete = callback;

        public void SetPurchaseFailedCallback(Action<string> callback) => OnPurchaseFailedCallback = callback;

        public void AddPurchaseCompleteCallback(Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseComplete += callback;
        }

        public void RemovePurchaseCompleteCallback(Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseComplete -= callback;
        }

        public void AddPurchaseFailedCallback(Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseFailedCallback += callback;
        }

        public void RemovePurchaseFailedCallback(Action<string> callback)
        {
            if (callback == null) return;
            OnPurchaseFailedCallback -= callback;
        }

        #endregion
    }
}
#endif
