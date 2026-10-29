#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Баннер подписки (<c>AmznGoDSDKCore.ShowSubBanner</c>): выбор плейсмента, спавн префаба
    /// под ориентацию экрана, покупка и закрытие.
    ///
    ///  * Можно ли показывать, баннер решает сам по IsSubscribed — тому же методу модуля, в
    ///    который делегирует AmznGoDSDKCore.IsSubscribed (фасад модулю не виден, ответ один):
    ///    подписка активна — баннер не показывается, а открытый закрывается сам, каким бы
    ///    путём подписка ни появилась (покупка из баннера, restore, сверка).
    ///  * Пока неизвестно, есть ли подписка, запрос ждёт (до <see cref="StateWaitTimeoutSeconds"/>):
    ///    SDK читает сохранённые права только после инициализации модуля, а на устройстве честный
    ///    ответ даёт лишь сверка с Amazon. Без ожидания баннер мелькал бы у подписчика.
    ///  * Плейсмент не указан — плейсменты подписки идут по кругу; если он один, показывается он.
    ///  * Ориентация — по экрану устройства (ширина ≥ высоты — горизонтальный префаб). Повернули
    ///    экран при открытом баннере — баннер пересобирается под новую ориентацию.
    ///  * «Купить» покупает SKU этого баннера. Отказ и отмена покупки баннер не закрывают —
    ///    остаётся крестик.
    ///  * onClosed вызывается ровно один раз на принятый запрос; аргумент — активна ли подписка
    ///    по итогу (куплена или уже была), false — закрыли крестиком или показ не состоялся.
    /// </summary>
    internal sealed class SubscriptionBannerPresenter : MonoBehaviour
    {
        private const string Tag = "[SubscriptionBanner]";

        // Префабы копирует в generated Resources ModuleResourceSynchronizer, пока модуль IAP включён.
        private const string HorizontalPrefabPath = "AMZNGoDSDK/SubscriptionBannerHorizontal";
        private const string VerticalPrefabPath = "AMZNGoDSDK/SubscriptionBannerVertical";

        // Поверх игрового UI, но ниже баннера «нет сети» (InternetConnection, по умолчанию 10000):
        // без сети покупка всё равно не пройдёт, и игрок должен видеть именно это.
        private const int SortingOrder = 9000;

        // Под эти разрешения свёрстаны префабы.
        private static readonly Vector2 HorizontalReferenceResolution = new(1920f, 1080f);
        private static readonly Vector2 VerticalReferenceResolution = new(1080f, 1920f);

        private const float StateWaitTimeoutSeconds = 10f;

        /// <summary>
        /// Тестовая среда (Testing/IAPTestPanel) подставляет сюда симуляцию покупки там, где нет
        /// настоящего Appstore: в редакторе BuyProduct уходит в заглушку плагина и не отвечает.
        /// В релизном пакете Testing вырезан — поле всегда null, «купить» зовёт BuyProduct.
        /// </summary>
        internal static Action<string> PurchaseOverride;

        private sealed class Request
        {
            public string Sku;
            public string RequestedPlacement;
            public SubscriptionBannerPlacement Placement;
            public Action<bool> OnClosed;
        }

        private InAppPurchaseModule _module;

        private SubscriptionBannerSettings _settings;
        private bool _settingsLoaded;

        // Следующий плейсмент по кругу для каждой подписки. Живёт до перезапуска игры.
        private readonly Dictionary<string, int> _rotation = new(StringComparer.Ordinal);

        private Request _pending;
        private float _pendingDeadline;

        private Request _current;
        private GameObject _root;
        private bool _shownHorizontal;

        public bool IsVisible => _root != null;

        internal void Bind(InAppPurchaseModule module) => _module = module;

        private void OnDestroy() => DestroyView();

        /// <summary>
        /// Запрос показа. true — баннер показан или будет показан, как только станет известно
        /// состояние подписки; тогда onClosed обязательно придёт. false — показа не будет и
        /// onClosed не вызывается (подписка уже активна, SKU не настроен, уже открыт баннер).
        /// </summary>
        public bool Show(string sku, string placement, Action<bool> onClosed)
        {
            sku = sku?.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                Debug.LogWarning($"{Tag} SKU не задан.");
                return false;
            }

            if (!_module.IsSubscriptionProduct(sku))
            {
                Debug.LogWarning($"{Tag} '{sku}' не настроен как включённая подписка (SDK Settings → In-App Purchase).");
                return false;
            }

            // Подписка активна — баннер не нужен. Тот же ответ, что AmznGoDSDKCore.IsSubscribed.
            if (_module.IsSubscribed(sku))
                return false;

            if (IsVisible)
            {
                Debug.LogWarning($"{Tag} Уже открыт баннер '{_current.Sku}' — запрос '{sku}' пропущен.");
                return false;
            }

            SubscriptionBannerProduct config = LoadSettings()?.Find(sku);
            if (config == null || config.Placements == null || config.Placements.Count == 0)
            {
                Debug.LogWarning($"{Tag} Для '{sku}' не настроены картинки баннера (SDK Settings → In-App Purchase → Subscription Banner).");
                return false;
            }

            var request = new Request
            {
                Sku = sku,
                RequestedPlacement = string.IsNullOrWhiteSpace(placement) ? null : placement.Trim(),
                OnClosed = onClosed,
            };

            if (!IsSubscriptionStateKnown())
            {
                // Ждёт один запрос — последний: показывать подряд баннеры, накопленные за
                // время ожидания, игроку незачем.
                if (_pending != null)
                    Finish(_pending, false);

                _pending = request;
                _pendingDeadline = Time.unscaledTime + StateWaitTimeoutSeconds;
                Debug.Log($"{Tag} '{sku}': ждём, пока станет известно состояние подписки.");
                return true;
            }

            return Present(request, config);
        }

        /// <summary>Закрывает открытый баннер и отменяет ожидающий показа запрос.</summary>
        public void Hide()
        {
            if (_pending != null)
            {
                Request pending = _pending;
                _pending = null;
                Finish(pending, false);
            }

            Close(false);
        }

        private void Update()
        {
            if (_pending != null)
                TickPending();

            if (!IsVisible)
                return;

            // Баннер сам следит, что его можно держать на экране: подписка стала активной —
            // закрываемся. Дёшево (словарь прав) и не зависит от того, каким путём пришло право.
            if (_module.IsSubscribed(_current.Sku))
            {
                Close(true);
                return;
            }

            if (IsHorizontalScreen() != _shownHorizontal)
                Rebuild();
        }

        private void TickPending()
        {
            Request request = _pending;

            if (_module.IsSubscribed(request.Sku))
            {
                _pending = null;
                Finish(request, true);
                return;
            }

            if (IsSubscriptionStateKnown())
            {
                _pending = null;
                SubscriptionBannerProduct config = LoadSettings()?.Find(request.Sku);
                if (config == null || IsVisible || !Present(request, config))
                    Finish(request, false);
                return;
            }

            if (Time.unscaledTime >= _pendingDeadline)
            {
                _pending = null;
                Debug.LogWarning($"{Tag} '{request.Sku}': состояние подписки так и не стало известно " +
                                 $"за {StateWaitTimeoutSeconds:0} с (нет сети или стор не ответил) — баннер не показан.");
                Finish(request, false);
            }
        }

        /// <summary>
        /// Можно ли верить ответу «подписки нет». С настоящим Appstore — только после сверки с
        /// Amazon в этой сессии. Без него (редактор, десктоп) сверки не будет никогда: там
        /// достаточно загруженных сохранённых прав.
        /// </summary>
        private bool IsSubscriptionStateKnown()
        {
            if (!_module.IsInitialized)
                return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            return _module.IsRestored;
#else
            return true;
#endif
        }

        private bool Present(Request request, SubscriptionBannerProduct config)
        {
            request.Placement = ResolvePlacement(config, request.RequestedPlacement);
            if (request.Placement == null)
            {
                Debug.LogWarning($"{Tag} У '{request.Sku}' нет ни одного плейсмента.");
                return false;
            }

            if (!Spawn(request, config))
                return false;

            _current = request;
            Debug.Log($"{Tag} Показан баннер '{request.Sku}', плейсмент '{request.Placement.Name}'.");
            return true;
        }

        private SubscriptionBannerPlacement ResolvePlacement(SubscriptionBannerProduct config, string requested)
        {
            if (requested != null)
            {
                SubscriptionBannerPlacement named = config.FindPlacement(requested);
                if (named != null)
                    return named;

                Debug.LogWarning($"{Tag} У '{config.ProductId}' нет плейсмента '{requested}' — показан плейсмент по умолчанию.");
                return config.Placements[0];
            }

            // По кругу: счётчик двигается только при реальном показе.
            _rotation.TryGetValue(config.ProductId.Trim(), out int index);
            index %= config.Placements.Count;
            _rotation[config.ProductId.Trim()] = index + 1;
            return config.Placements[index];
        }

        private bool Spawn(Request request, SubscriptionBannerProduct config)
        {
            bool horizontal = IsHorizontalScreen();

            // Нет картинки у плейсмента — берём картинку плейсмента по умолчанию.
            Sprite background = request.Placement.GetBackground(horizontal);
            if (background == null && config != null && config.Placements.Count > 0)
                background = config.Placements[0].GetBackground(horizontal);

            if (background == null)
            {
                Debug.LogError($"{Tag} У '{request.Sku}' не задано ни одной картинки баннера — показ отменён.");
                return false;
            }

            string prefabPath = horizontal ? HorizontalPrefabPath : VerticalPrefabPath;
            GameObject prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"{Tag} Префаб не найден в Resources/{prefabPath}. Он создаётся автоматически, " +
                               "пока модуль In-App Purchase включён в SDK Settings, — дождись окончания компиляции.");
                return false;
            }

            // Свой Canvas: префаб рассчитан на родительский Canvas, а баннер должен быть поверх
            // любого игрового UI и переживать смену сцен.
            var root = new GameObject("AMZNGoDSDK_SubscriptionBanner");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = horizontal ? HorizontalReferenceResolution : VerticalReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            GameObject instance = Instantiate(prefab, root.transform, false);
            var view = instance.GetComponent<SubscriptionBanner>();
            if (view == null)
            {
                Debug.LogError($"{Tag} На префабе {prefabPath} нет компонента {nameof(SubscriptionBanner)}.");
                Destroy(root);
                return false;
            }

            EnsureEventSystem();

            view.ContinueClicked += HandleContinue;
            view.CloseClicked += HandleClose;
            view.Show(background);

            _root = root;
            _shownHorizontal = horizontal;
            return true;
        }

        private void Rebuild()
        {
            Request request = _current;
            DestroyView();

            SubscriptionBannerProduct config = LoadSettings()?.Find(request.Sku);
            if (!Spawn(request, config))
            {
                _current = null;
                Finish(request, false);
            }
        }

        private void HandleContinue()
        {
            if (_current == null)
                return;

            string sku = _current.Sku;
            Debug.Log($"{Tag} «Купить»: {sku}");

            // Баннер остаётся открытым, пока IsSubscribed не станет true (см. Update): окно
            // Amazon рисуется поверх, а при отказе игрок вернётся к тому же предложению.
            if (PurchaseOverride != null)
                PurchaseOverride(sku);
            else
                _module.BuyProduct(sku);
        }

        private void HandleClose() => Close(false);

        private void Close(bool subscribed)
        {
            Request request = _current;
            _current = null;
            DestroyView();

            if (request == null)
                return;

            Debug.Log($"{Tag} Баннер '{request.Sku}' закрыт ({(subscribed ? "подписка оформлена" : "без покупки")}).");
            Finish(request, subscribed);
        }

        private void DestroyView()
        {
            if (_root == null)
                return;

            Destroy(_root);
            _root = null;
        }

        // onClosed — игровой код: его исключение не должно ломать состояние баннера.
        private static void Finish(Request request, bool subscribed)
        {
            try { request.OnClosed?.Invoke(subscribed); }
            catch (Exception e) { Debug.LogError($"{Tag} onClosed handler threw: {e}"); }
        }

        private SubscriptionBannerSettings LoadSettings()
        {
            if (!_settingsLoaded)
            {
                _settingsLoaded = true;
                _settings = Resources.Load<SubscriptionBannerSettings>(SubscriptionBannerSettings.ResourcesPath);
            }

            return _settings;
        }

        private static bool IsHorizontalScreen() => Screen.width >= Screen.height;

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
    }
}
#endif
