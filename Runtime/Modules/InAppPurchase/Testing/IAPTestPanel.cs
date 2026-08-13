// Панель собирается в любой билд с включённым IAP — QA тестирует на релизных билдах
// (Development Build падает на Vulkan-баге эмулятора BlueStacks).
#if AMZN_IAP_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Тестовая среда для ручной проверки IAP (модель ТЗ ред. 3: источник истины — чек
    /// Amazon, SDK ничего не начисляет).
    ///
    /// Панель играет роль ИГРЫ: подписывается на события SDK (права выданы/сняты, начался
    /// оплаченный период, покупка завершена) и сама ведёт демо-баланс. Суммы начислений —
    /// выбор панели, как в реальной игре.
    ///
    /// Вешается на пустой объект сцены и целиком строит свой UI в рантайме — отдельным
    /// Canvas поверх игрового. Там, где настоящего Appstore нет (редактор, десктоп),
    /// ответы стора подаёт <see cref="SimulatedAmazonStore"/> через ту же точку входа, что
    /// и JNI-мост, — исполняется настоящий код модуля.
    /// </summary>
    [DisallowMultipleComponent]
    public class IAPTestPanel : MonoBehaviour
    {
        [Header("SKU (пусто — берётся первый включённый продукт из настроек SDK)")]
        [Tooltip("SKU подписки. Продукты настраиваются в AMZN GoD → SDK Settings → In-App Purchase.")]
        [SerializeField] private string _subscriptionSku;

        [Tooltip("SKU расходуемого продукта. Настраивается там же, в списке Consumable Products.")]
        [SerializeField] private string _consumableSku;

        [Tooltip("SKU разовой покупки (анлок/убрать рекламу). Список Non-Consumable Products.")]
        [SerializeField] private string _nonConsumableSku;

        [Header("Демо-начисления (роль игры)")]
        [Tooltip("Сколько монет панель начисляет за покупку расходуемого товара.")]
        [SerializeField] private int _coinsPerConsumable = 500;

        [Tooltip("Сколько монет панель начисляет за каждый оплаченный период подписки.")]
        [SerializeField] private int _coinsPerPeriod = 100;

        [Header("UI")]
        [Tooltip("Порядок сортировки Canvas. Должен быть выше игрового UI.")]
        [SerializeField] private int _sortingOrder = 500;

        [Tooltip("Сколько последних строк лога держать на экране. Высота панели считается от этого числа.")]
        [SerializeField] private int _maxLogLines = 9;

        [Tooltip("Секунд между обновлениями блока состояния.")]
        [SerializeField] private float _statusRefreshInterval = 0.25f;

        // Сколько ждём появления модуля на сцене, прежде чем показать 'модуль не найден'.
        private const float SdkWaitTimeout = 20f;

        // Демо-баланс панели. Ключ принадлежит ПАНЕЛИ (роль игры), SDK его не знает.
        private const string PanelMoneyKey = "IAPTestPanel_Money";

        // Счётчик полученных периодов подписки — наглядность «каждое продление выдаёт фичи».
        private const string PanelPeriodsKey = "IAPTestPanel_Periods";

        // Множитель межстрочного интервала uGUI Text: на нём считается высота панели лога.
        private const float LogLineHeightFactor = 1.2f;

        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.72f);
        private static readonly Color BuyColor = new(0.16f, 0.42f, 0.24f, 1f);
        private static readonly Color SimColor = new(0.15f, 0.3f, 0.5f, 1f);
        private static readonly Color DangerColor = new(0.55f, 0.15f, 0.15f, 1f);

        private Text _statusText;
        private Text _coinsText;
        private Text _logText;
        private Font _font;

        private readonly List<string> _logLines = new();
        private float _statusTimer;

        // SKU, по которым «Права выданы» уже логировалось в этой сессии. Событие Granted —
        // сигнал состояния и приходит с КАЖДОЙ сверкой полным снапшотом; без пометки
        // повторов лог читается как повторная выдача товара (особенно рядом с renewal).
        private readonly HashSet<string> _grantedLogged = new();

        private InAppPurchaseModule _module;
        private bool _listenersAttached;

        // Конфиг SDK, прочитанный тем же DataLoader, что и ядром: список продуктов модуль
        // наружу не отдаёт.
        private InAppPurchaseSettingData _settings;

        private InAppPurchaseModule Module
        {
            get
            {
                if (_module == null)
                    _module = FindObjectOfType<InAppPurchaseModule>(true);
                return _module;
            }
        }

        /// <summary>
        /// Автоспавн: в собранной игре панель никто не создаёт — в SDK нет ни сцены, ни
        /// префаба с ней, и dev-билд для QA оставался без кнопок. Создаёмся сами после
        /// загрузки первой сцены, если модуль IAP включён в настройках и панели ещё нет
        /// (добавленная в сцену руками имеет приоритет). DontDestroyOnLoad — QA видит
        /// панель в любой сцене. Сейчас попадает и в релизный билд (см. #if в шапке).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            try
            {
                var settings = DataLoader.LoadSettings();
                if (settings == null || !settings.Enabled)
                    return;
                if (settings.InAppPurchase == null || !settings.InAppPurchase.Enabled)
                    return;

#if UNITY_2023_1_OR_NEWER
                if (FindFirstObjectByType<IAPTestPanel>(FindObjectsInactive.Include) != null)
                    return;
#else
                if (FindObjectOfType<IAPTestPanel>(true) != null)
                    return;
#endif

                var go = new GameObject("IAP Test Panel (auto)");
                DontDestroyOnLoad(go);
                go.AddComponent<IAPTestPanel>();
                Debug.Log("[IAPTestPanel] Автоспавн: панель создана (тестовый инструмент — не для стора!)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IAPTestPanel] Автоспавн не удался: {e.Message}");
            }
        }

        private void Awake()
        {
            LoadSettings();
            _font = LoadBuiltinFont();
            EnsureEventSystem();
            BuildUI();
        }

        private void Start()
        {
            Log("Тестовая среда IAP запущена (модель ред. 3: правит чек Amazon)");
            StartCoroutine(AttachWhenReady());
        }

        private void Update()
        {
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer > 0f)
                return;

            _statusTimer = Mathf.Max(0.05f, _statusRefreshInterval);
            RefreshStatus();
        }

        private void OnDestroy()
        {
            if (!_listenersAttached || _module == null)
                return;

            _module.RemovePurchaseCompleteCallback(OnPurchaseComplete);
            _module.RemovePurchaseFailedCallback(OnPurchaseFailed);
            _module.RemoveEntitlementGrantedListener(OnEntitlementGranted);
            _module.RemoveEntitlementRevokedListener(OnEntitlementRevoked);
            _module.RemovePeriodStartedListener(OnPeriodStarted);
            _listenersAttached = false;
        }

        #region Actions

        public void BuySubscription()
        {
            var product = ResolveSubscription();
            Buy(product?.ProductId ?? Trimmed(_subscriptionSku),
                product is { Enabled: true },
                SimulatedAmazonStore.SubscriptionType,
                "subscription");
        }

        public void BuyConsumable()
        {
            var product = ResolveConsumable();
            Buy(product?.ProductId ?? Trimmed(_consumableSku),
                product is { Enabled: true },
                SimulatedAmazonStore.ConsumableType,
                "consumable");
        }

        public void BuyNonConsumable()
        {
            var product = ResolveNonConsumable();
            Buy(product?.ProductId ?? Trimmed(_nonConsumableSku),
                product is { Enabled: true },
                SimulatedAmazonStore.EntitledType,
                "non-consumable");
        }

        private void Buy(string sku, bool buyable, string productType, string kind)
        {
            var module = Module;
            if (module == null)
            {
                Log("Покупка невозможна: модуль IAP не найден на сцене");
                return;
            }

            if (sku == null)
            {
                Log($"Покупка невозможна: продукт ({kind}) не настроен в SDK Settings");
                return;
            }

            Log($"→ BuyProduct ({kind}) {sku}");
            module.BuyProduct(sku);

            if (SimulatedAmazonStore.HasRealStore)
                return;

            // Гейт симуляции обязан совпадать с гейтом модуля (CanBuy = настроен И включён):
            // иначе панель вбросила бы SUCCESSFUL для покупки, которую модуль отклонил, — в
            // логе рядом легли бы «покупка не прошла» и «права выданы», как будто модуль врёт.
            if (!buyable)
            {
                Log("   SKU нет в настройках SDK или продукт выключен — покупка отклонена модулем, симуляция пропущена");
                return;
            }

            // Долгоживущее право, уже принадлежащее аккаунту (активная подписка или
            // купленный анлок), — реальный стор ответил бы ALREADY_PURCHASED, иначе
            // повторный клик стакал бы периоды/выдавал право «заново».
            bool alreadyActive = productType != SimulatedAmazonStore.ConsumableType
                                 && module.HasReceipt(sku);

            string status = SimulatedAmazonStore.SimulatePurchase(sku, productType, alreadyActive);
            Log($"   (симуляция) стор: {status}");

            // Модуль после покупки сам запускает полную сверку — отвечаем за стор историей.
            SimulatedAmazonStore.SimulateRestore();
        }

        /// <summary>
        /// Симуляция продления подписки: продление у Amazon не наблюдается (поля чека не
        /// меняются) — SDK вычисляет периоды из PurchaseDate + N × Term против доверенного
        /// времени. Сдвигаем якорь чека симулятора на один Term назад (эквивалент реально
        /// прошедшего периода) и запускаем сверку: НАСТОЯЩИЙ путь модуля (FirePeriods +
        /// журнал) выдаёт следующий период ровно один раз, панель в роли игры начисляет
        /// монеты, в аналитику уходит iap_subscription_renewed.
        /// </summary>
        public void SimulateRenewal()
        {
            var module = Module;
            if (module == null)
            {
                Log("Продление невозможно: модуль IAP не найден на сцене");
                return;
            }

            if (SimulatedAmazonStore.HasRealStore)
            {
                Log("Симуляция продления недоступна с реальным стором: на устройстве период наступает по реальному времени");
                return;
            }

            var product = ResolveSubscription();
            if (product == null)
            {
                Log("Продление невозможно: подписка не настроена в SDK Settings");
                return;
            }

            if (product.TermDays <= 0)
            {
                Log($"Продление невозможно: у {product.ProductId} не задан Term (days)");
                return;
            }

            if (!SdkTrustedTime.HasFreshTime)
            {
                Log("Нет доверенного времени — периоды не начисляются и ждут сети (см. статус)");
                return;
            }

            // Эффективный срок — с учётом тестового TestTermMinutes («подписка на 10 минут»).
            double termDays = product.TestTermMinutes > 0 ? product.TestTermMinutes / 1440.0 : product.TermDays;

            if (!SimulatedAmazonStore.SimulateRenewal(product.ProductId, termDays))
            {
                Log($"Продление невозможно: у {product.ProductId} нет активного чека — сначала купи подписку");
                return;
            }

            Log($"↻ (симуляция) продление {product.ProductId}: +1 период ({TermText(product)}), запускаю сверку");

            // Периоды выдаёт настоящий путь модуля — сверка по обновлённой истории.
            module.RefreshEntitlements();
            SimulatedAmazonStore.SimulateRestore();
        }

        /// <summary>
        /// Ручной прогон сверки. Периоды выдаются не по таймеру, а при сверке (старт,
        /// покупка, форграунд) — с коротким тестовым сроком подписки (Term minutes (TEST))
        /// новый период иначе пришлось бы ждать до сворачивания/разворачивания приложения.
        /// Нажал после границы периода — период выдался.
        /// </summary>
        public void CheckPeriods()
        {
            var module = Module;
            if (module == null)
            {
                Log("Сверка невозможна: модуль IAP не найден на сцене");
                return;
            }

            Log("→ Сверка вручную (проверка периодов)");
            module.RefreshEntitlements();

            if (!SimulatedAmazonStore.HasRealStore)
                SimulatedAmazonStore.SimulateRestore();
        }

        /// <summary>
        /// Полный сброс к «чистой установке»: журналы и права модуля, чеки симулятора,
        /// демо-баланс панели, храповик доверенного времени. Модуль держит состояние в
        /// памяти — реальный эффект только после перезапуска Play Mode / приложения.
        /// </summary>
        public void WipeSdkState()
        {
            PlayerPrefs.DeleteKey(IapPrefsKeys.SchemaVersion);
            PlayerPrefs.DeleteKey(IapPrefsKeys.GrantedReceipts);
            PlayerPrefs.DeleteKey(IapPrefsKeys.PendingFulfillment);
            PlayerPrefs.DeleteKey(IapPrefsKeys.Entitlements);
            PlayerPrefs.DeleteKey(IapPrefsKeys.EntitlementReconciledAt);
            PlayerPrefs.DeleteKey(IapPrefsKeys.PeriodJournal);
            PlayerPrefs.DeleteKey(IapPrefsKeys.EverPurchasedSkus);
            PlayerPrefs.DeleteKey(IapPrefsKeys.LiveGrantProtection);
            PlayerPrefs.DeleteKey(IapPrefsKeys.LegacyFulfilledReceipts);
            PlayerPrefs.DeleteKey("AMZN_TrustedTimeUtc");   // приватный ключ SdkTrustedTime
            PlayerPrefs.DeleteKey(PanelMoneyKey);
            PlayerPrefs.DeleteKey(PanelPeriodsKey);
            PlayerPrefs.Save();

            SimulatedAmazonStore.ClearReceipts();

            Log("⚠ Состояние IAP стёрто (журналы, права, чеки симулятора, монеты).");
            Log("⚠ ПЕРЕЗАПУСТИ Play Mode / приложение — модуль держит старое состояние в памяти.");
        }

        #endregion

        #region SDK wiring

        private void LoadSettings()
        {
            try
            {
                _settings = DataLoader.LoadSettings()?.InAppPurchase;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IAPTestPanel] Не удалось прочитать настройки SDK: {e.Message}");
            }
        }

        private IEnumerator AttachWhenReady()
        {
            float waited = 0f;
            while (Module == null && waited < SdkWaitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var module = Module;
            if (module == null)
            {
                Log("Модуль IAP не найден на сцене — кнопки работать не будут");
                yield break;
            }

            if (!module.Enabled)
            {
                Log("Модуль IAP выключен в настройках SDK");
                yield break;
            }

            // Add/Remove, а не Set: панель не должна затирать колбэки игрового кода.
            module.AddPurchaseCompleteCallback(OnPurchaseComplete);
            module.AddPurchaseFailedCallback(OnPurchaseFailed);
            module.AddEntitlementGrantedListener(OnEntitlementGranted);
            module.AddEntitlementRevokedListener(OnEntitlementRevoked);
            module.AddPeriodStartedListener(OnPeriodStarted);
            _listenersAttached = true;

            Log("Модуль IAP подключён, слушаем события");

            if (SimulatedAmazonStore.HasRealStore)
                yield break;

            // Ждём инициализации: до неё события стора уходить некому. Ядро запускает
            // модули после проверки интернета — задержка реальна.
            while (!module.IsInitialized && waited < SdkWaitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!module.IsInitialized)
            {
                Log("Модуль IAP не инициализировался — симуляция не запущена");
                yield break;
            }

            StartSimulatedStore();
        }

        private void StartSimulatedStore()
        {
            if (!SimulatedAmazonStore.IsAvailable)
            {
                Log("СИМУЛЯЦИЯ НЕДОСТУПНА: переключи Build Target на Android");
                return;
            }

            Log($"СИМУЛЯЦИЯ СТОРА: покупки подтверждаются локально (чеков: {SimulatedAmazonStore.ReceiptCount})");

            SimulatedAmazonStore.PublishCatalog(CatalogForSimulation());

            // Модуль на старте запустил сверку и ждёт истории — отдаём её, как это делает
            // нативный слой. Это же завершает стартовый reconcile (IsRestored станет true).
            SimulatedAmazonStore.SimulateRestore();
        }

        private IEnumerable<SimulatedAmazonStore.SimulatedProduct> CatalogForSimulation()
        {
            var subscription = ResolveSubscription();
            if (subscription != null)
            {
                yield return new SimulatedAmazonStore.SimulatedProduct(
                    subscription.ProductId, SimulatedAmazonStore.SubscriptionType, subscription.DisplayName);
            }

            var consumable = ResolveConsumable();
            if (consumable != null)
            {
                yield return new SimulatedAmazonStore.SimulatedProduct(
                    consumable.ProductId, SimulatedAmazonStore.ConsumableType, consumable.DisplayName);
            }

            var nonConsumable = ResolveNonConsumable();
            if (nonConsumable != null)
            {
                yield return new SimulatedAmazonStore.SimulatedProduct(
                    nonConsumable.ProductId, SimulatedAmazonStore.EntitledType, nonConsumable.DisplayName);
            }
        }

        // --- Роль игры: начисления по событиям SDK ---

        private void OnPurchaseComplete(string productId)
        {
            Log($"✓ Покупка завершена: {productId}");

            // Расходуемый: SDK сообщил ровно один раз (журнал по ReceiptId) — начисляем.
            var consumable = ResolveConsumable();
            if (consumable != null && consumable.ProductId == productId)
            {
                AddMoney(_coinsPerConsumable);
                Log($"   [игра] +{_coinsPerConsumable} монет за расходуемый");
            }
        }

        private void OnPurchaseFailed(string productId) => Log($"✗ Покупка не прошла: {productId}");

        private void OnEntitlementGranted(IapEntitlement entitlement)
        {
            // Сигнал состояния, приходит при каждом запуске и каждой сверке — обработчик
            // обязан быть идемпотентным и НЕ начислять валюту (см. README). Панель только
            // логирует, отличая первую выдачу в сессии от переподтверждения снапшотом.
            Log(_grantedLogged.Add(entitlement.ProductId)
                ? $"● Права выданы: {entitlement.ProductId} ({entitlement.State})"
                : $"● Права подтверждены сверкой: {entitlement.ProductId}");
        }

        private void OnEntitlementRevoked(IapEntitlement entitlement)
        {
            // Сброс пометки: если право вернётся после реального отзыва, это снова «выданы».
            _grantedLogged.Remove(entitlement.ProductId);
            Log($"○ Права сняты: {entitlement.ProductId}");
        }

        private void OnPeriodStarted(IapPeriodStarted period)
        {
            // Ровно один раз на период (журнал SDK) — здесь начислять безопасно.
            AddMoney(_coinsPerPeriod);

            int totalPeriods = PlayerPrefs.GetInt(PanelPeriodsKey, 0) + 1;
            PlayerPrefs.SetInt(PanelPeriodsKey, totalPeriods);
            PlayerPrefs.Save();

            Log($"◆ Оплаченный период #{period.PeriodIndex}: {period.ProductId} → [игра] +{_coinsPerPeriod} монет (всего периодов: {totalPeriods})");
        }

        private void AddMoney(int amount)
        {
            PlayerPrefs.SetInt(PanelMoneyKey, PlayerPrefs.GetInt(PanelMoneyKey, 0) + amount);
            PlayerPrefs.Save();
        }

        private SubscriptionProduct ResolveSubscription()
        {
            var products = _settings?.SubscriptionProducts;
            if (products == null)
                return null;

            string sku = Trimmed(_subscriptionSku);
            return sku != null
                ? products.FirstOrDefault(p => p.ProductId == sku)
                : products.FirstOrDefault(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProductId));
        }

        private ConsumableProduct ResolveConsumable()
        {
            var products = _settings?.ConsumableProducts;
            if (products == null)
                return null;

            string sku = Trimmed(_consumableSku);
            return sku != null
                ? products.FirstOrDefault(p => p.ProductId == sku)
                : products.FirstOrDefault(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProductId));
        }

        private NonConsumableProduct ResolveNonConsumable()
        {
            var products = _settings?.NonConsumableProducts;
            if (products == null)
                return null;

            string sku = Trimmed(_nonConsumableSku);
            return sku != null
                ? products.FirstOrDefault(p => p.ProductId == sku)
                : products.FirstOrDefault(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProductId));
        }

        private static string Trimmed(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string TermText(SubscriptionProduct product) =>
            product.TestTermMinutes > 0 ? $"{product.TestTermMinutes}min (TEST)" : $"{product.TermDays}d";

        #endregion

        #region Status & log

        private void RefreshStatus()
        {
            if (_statusText == null)
                return;

            var module = Module;
            if (module == null)
            {
                _statusText.text = "Модуль IAP не найден на сцене.\nДобавь префаб AmznGoDSDK в сцену.";
                if (_coinsText != null) _coinsText.text = "монеты: модуль недоступен";
                return;
            }

            var core = AmznGoDSDKCore.Instance;
            string storeMode = SimulatedAmazonStore.HasRealStore
                ? "store: Amazon"
                : SimulatedAmazonStore.IsAvailable ? "store: СИМУЛЯЦИЯ" : "store: НЕТ (не тот Build Target)";

            var lines = new List<string>
            {
                $"SDK: {(core != null && core.IsInitialized ? "initialized" : "initializing…")}   " +
                $"IAP: {(module.Enabled ? "on" : "off")} / init={module.IsInitialized} / " +
                $"restored={module.IsRestored}   {storeMode}",
                $"Доверенное время: {(SdkTrustedTime.HasFreshTime ? SdkTrustedTime.UtcNow?.ToString("u") : "нет (периоды ждут сети)")}"
            };

            var subscription = ResolveSubscription();
            if (subscription == null)
            {
                lines.Add("Подписка: не настроена (AMZN GoD → SDK Settings → In-App Purchase)");
            }
            else
            {
                string sku = subscription.ProductId;
                lines.Add($"Подписка: {sku}   term={TermText(subscription)}   " +
                          $"state={module.GetEntitlementState(sku)}   IsSubscribed={module.IsSubscribed(sku)}   " +
                          $"{ProductText(module, sku)}");
            }

            var consumable = ResolveConsumable();
            if (consumable == null)
            {
                lines.Add("Расходуемый: не настроен (AMZN GoD → SDK Settings → In-App Purchase)");
            }
            else
            {
                string sku = consumable.ProductId;
                lines.Add($"Расходуемый: {sku}   покупался={module.HasEverPurchased(sku)}   {ProductText(module, sku)}");
            }

            var nonConsumable = ResolveNonConsumable();
            if (nonConsumable == null)
            {
                lines.Add("Разовая покупка: не настроена (AMZN GoD → SDK Settings → In-App Purchase)");
            }
            else
            {
                string sku = nonConsumable.ProductId;
                lines.Add($"Разовая покупка: {sku}   state={module.GetEntitlementState(sku)}   " +
                          $"HasReceipt={module.HasReceipt(sku)}   {ProductText(module, sku)}");
            }

            _statusText.text = string.Join("\n", lines);

            if (_coinsText != null)
                _coinsText.text = $"монеты [игра]: {PlayerPrefs.GetInt(PanelMoneyKey, 0)}   " +
                                  $"периодов получено: {PlayerPrefs.GetInt(PanelPeriodsKey, 0)}";
        }

        private static string ProductText(InAppPurchaseModule module, string sku)
        {
            var product = module.GetProduct(sku);
            return product == null
                ? "каталог: нет данных"
                : $"цена: {product.Price} тип: {product.ProductType}";
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Debug.Log($"[IAPTestPanel] {message}");

            _logLines.Add(line);
            int maxLines = Mathf.Max(1, _maxLogLines);
            if (_logLines.Count > maxLines)
                _logLines.RemoveRange(0, _logLines.Count - maxLines);

            if (_logText != null)
                _logText.text = string.Join("\n", _logLines);
        }

        #endregion

        #region UI construction

        private void BuildUI()
        {
            var canvasGo = new GameObject("IAP Test Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildStatusPanel(canvasGo.transform);
            BuildCoinsPanel(canvasGo.transform);
            BuildButtons(canvasGo.transform);
            BuildLogPanel(canvasGo.transform);
        }

        private void BuildStatusPanel(Transform parent)
        {
            var panel = CreatePanel("Status", parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(24f, -230f);
            rect.offsetMax = new Vector2(-24f, -24f);

            _statusText = CreateText("Text", panel.transform, "", 26, TextAnchor.UpperLeft);
            StretchWithPadding(_statusText.rectTransform, 16f);
            _statusText.text = "Ожидание модуля IAP…";
        }

        private void BuildCoinsPanel(Transform parent)
        {
            var panel = CreatePanel("Coins", parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(24f, -330f);
            rect.offsetMax = new Vector2(-24f, -240f);

            _coinsText = CreateText("Text", panel.transform, "монеты [игра]: —", 44, TextAnchor.MiddleCenter);
            StretchWithPadding(_coinsText.rectTransform, 12f);
        }

        private void BuildButtons(Transform parent)
        {
            const float buttonHeight = 72f;
            const float spacing = 12f;

            // Восстановление отдельной кнопкой не выводим: QA тестирует restore
            // переустановкой игры (WIPE SDK STATE + перезапуск = тот же сценарий).
            var actions = new List<(string label, Color color, UnityEngine.Events.UnityAction onClick)>
            {
                ("BUY SUBSCRIPTION", BuyColor, BuySubscription),
                ("BUY CONSUMABLE", BuyColor, BuyConsumable),
                ("BUY NON-CONSUMABLE", BuyColor, BuyNonConsumable),
            };

            var container = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            container.transform.SetParent(parent, false);
            container.layer = parent.gameObject.layer;

            var rect = container.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620f, actions.Count * buttonHeight + (actions.Count - 1) * spacing);
            rect.anchoredPosition = Vector2.zero;

            var layout = container.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            foreach (var (label, color, onClick) in actions)
                CreateButton(label, container.transform, color, onClick);
        }

        private void BuildLogPanel(Transform parent)
        {
            const float fontSize = 24f;
            const float padding = 16f;
            const float bottomMargin = 24f;

            // Высота считается от _maxLogLines: текст с verticalOverflow Overflow и якорем
            // снизу при нехватке места полез бы вверх, поверх кнопок.
            float lineHeight = fontSize * LogLineHeightFactor;
            float height = Mathf.Min(Mathf.Max(1, _maxLogLines) * lineHeight + padding * 2f, 420f);

            var panel = CreatePanel("Log", parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(24f, bottomMargin);
            rect.offsetMax = new Vector2(-24f, bottomMargin + height);

            _logText = CreateText("Text", panel.transform, "", (int)fontSize, TextAnchor.LowerLeft);
            StretchWithPadding(_logText.rectTransform, padding);
        }

        private GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            var image = go.GetComponent<Image>();
            image.color = PanelColor;
            image.raycastTarget = false;

            return go;
        }

        private void CreateButton(string label, Transform parent, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", go.transform, label, 34, TextAnchor.MiddleCenter);
            StretchWithPadding(text.rectTransform, 8f);
        }

        private Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = content;

            return text;
        }

        private static void StretchWithPadding(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        // В сцене может не быть EventSystem, а без него uGUI не доставляет клики.
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

#if UNITY_2023_1_OR_NEWER
            if (FindFirstObjectByType<EventSystem>() != null)
                return;
#else
            if (FindObjectOfType<EventSystem>() != null)
                return;
#endif

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // Имя встроенного шрифта поменялось в 2022.2 (Arial.ttf → LegacyRuntime.ttf).
        private static Font LoadBuiltinFont()
        {
#if UNITY_2022_2_OR_NEWER
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif

            if (font == null)
                Debug.LogWarning("[IAPTestPanel] Встроенный шрифт не найден, текст будет невидим");

            return font;
        }

        #endregion
    }
}
#endif
