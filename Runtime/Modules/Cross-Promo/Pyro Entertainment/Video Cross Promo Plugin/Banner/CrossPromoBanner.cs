#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoBanner : MonoBehaviour
    {
        private const string BannerPlacement = "banner";
        private const float ClickTrackingTimeoutSeconds = 1.5f;

        // Повторный тап по этому же баннеру игнорируется, пока первый ещё ждёт перехода в стор
        // (до ClickTrackingTimeoutSeconds — на медленной сети всё это время ничего не видно) и ещё
        // секунду после: иначе второй cp_click с новым event_id и второе открытие стора.
        private const float ClickDebounceSeconds = ClickTrackingTimeoutSeconds + 1f;
        private float _lastHandledClickTime = float.NegativeInfinity;

        private readonly List<BannerData> bannerDataList = new();
        [SerializeField] private Image adImage;
        [SerializeField] private GameObject bannerGO;

        private Action onClose;
        private Func<bool> isNoAds;

        private Coroutine _rotationCoroutine;
        private Coroutine _initializationCoroutine;
        private PromosConfigurationInfo _currentConfig;
        private CrossPromoModule _module;
        private int _currentBannerIndex;
        private int _lastShownIndex = -1;
        private PromosConfigurationInfo _downloadConfig;
        private List<PromoConfiguration> _bannerConfigurations = new();
        private int _nextDownloadIndex;
        private bool _explicitlyHidden;
        private bool _crossPromoVisible;
        private CanvasGroup _presentationCanvasGroup;
        private readonly List<CanvasGroup> _visibilityCanvasGroups = new();
        private readonly List<Canvas> _visibilityCanvases = new();
        private bool _shouldUseAppLovinBanner;
        private float _nextFillCheckTime;
        private const float FillCheckIntervalSeconds = 0.25f;

#if AMZN_APPLOVIN_ENABLED
        private static CrossPromoBanner _appLovinBannerOwner;
        private AppLovinModule _appLovinModule;
#endif

        private void Awake()
        {
            if (bannerGO == null)
            {
                bannerGO = gameObject;
            }

            // Первый CanvasGroup остаётся интерфейсом игры (в том числе GetComponent).
            // Служебное переключение CP/MAX использует отдельную группу: не перетираем
            // alpha/interactable игры и не меняем иерархию пользовательского префаба.
            if (bannerGO.GetComponent<CanvasGroup>() == null)
            {
                bannerGO.AddComponent<CanvasGroup>();
            }
            _presentationCanvasGroup = bannerGO.AddComponent<CanvasGroup>();
            SetCrossPromoVisible(false);

            if (adImage == null)
            {
                var adTransform = transform.Find("ad");
                if (adTransform != null)
                {
                    adImage = adTransform.GetComponent<Image>();
                }

                if (adImage == null)
                {
                    adImage = GetComponentInChildren<Image>();
                }
            }

            CrossPromoModule.OnConfigLoaded += OnModuleConfigLoaded;
            CrossPromoModule.OnBannerFuncsUpdated += OnModuleBannerFuncsUpdated;
        }

        private void OnEnable()
        {
            BindToModule();
            StartInitializationIfNeeded();
            RefreshBannerUI();
        }

        private void Update()
        {
            BindToModule();
            RefreshBannerUI(forceFillCheck: false);
        }

        private void OnDisable()
        {
            HideAppLovinBanner();
            SetCrossPromoVisible(false);
            StopInitialization();
        }

        private void OnDestroy()
        {
            CrossPromoModule.OnConfigLoaded -= OnModuleConfigLoaded;
            CrossPromoModule.OnBannerFuncsUpdated -= OnModuleBannerFuncsUpdated;
            HideAppLovinBanner();
            SetCrossPromoVisible(false);
            StopInitialization();
            if (_presentationCanvasGroup != null)
                Destroy(_presentationCanvasGroup);
        }

        private void BindToModule()
        {
            if (_module != null)
            {
                return;
            }

            _module = CrossPromoModule.Instance;
            if (_module == null)
            {
                return;
            }

            ApplyBannerFunctions(_module.CurrentBannerOnClose, _module.CurrentIsNoAds);
            ApplyConfig(_module.LoadedConfig);
        }

        private void OnModuleConfigLoaded(PromosConfigurationInfo config)
        {
            ApplyConfig(config);
        }

        private void OnModuleBannerFuncsUpdated(Action onClose, Func<bool> isNoAds)
        {
            ApplyBannerFunctions(onClose, isNoAds);
        }

        private void ApplyConfig(PromosConfigurationInfo config)
        {
            if (ReferenceEquals(_currentConfig, config))
            {
                return;
            }

            _currentConfig = config;
            StopInitialization();
            StartInitializationIfNeeded();
            RefreshBannerUI();
        }

        private void StartInitializationIfNeeded()
        {
            if (!isActiveAndEnabled || _initializationCoroutine != null)
                return;

            if (ReferenceEquals(_downloadConfig, _currentConfig)
                && _nextDownloadIndex >= _bannerConfigurations.Count)
                return;

            _initializationCoroutine = StartCoroutine(Initialize(_currentConfig));
        }

        private void StopInitialization()
        {
            if (_initializationCoroutine != null)
            {
                StopCoroutine(_initializationCoroutine);
                _initializationCoroutine = null;
            }
        }

        private void ApplyBannerFunctions(Action onClose, Func<bool> isNoAds)
        {
            this.onClose = onClose;
            this.isNoAds = isNoAds;
            RefreshBannerUI();
        }

        public IEnumerator Initialize(PromosConfigurationInfo config)
        {
            _currentConfig = config;
            if (!ReferenceEquals(_downloadConfig, config))
            {
                _downloadConfig = config;
                StopRotation();
                bannerDataList.Clear();
                _currentBannerIndex = 0;
                _lastShownIndex = -1;
                _nextDownloadIndex = 0;
                // Лимиты могут изменить Videos во время скачивания. Snapshot также
                // позволяет продолжить прерванную загрузку после OnEnable.
                _bannerConfigurations = config?.Videos != null
                    ? new List<PromoConfiguration>(config.Videos)
                    : new List<PromoConfiguration>();
            }

            while (_nextDownloadIndex < _bannerConfigurations.Count)
            {
                if (!isActiveAndEnabled)
                    yield break;

                // Вложенный IEnumerator останавливается вместе с основной корутиной.
                yield return DownloadBannerSprite(_bannerConfigurations[_nextDownloadIndex]);
                _nextDownloadIndex++;
            }

            _initializationCoroutine = null;
            StopRotation();
            RefreshBannerUI();
        }

        private void StartRotationIfNeeded()
        {
            if (_rotationCoroutine != null) return;
            if (bannerDataList.Count == 0) return;
            if (!isActiveAndEnabled) return;
            if (!_crossPromoVisible) return;
            _rotationCoroutine = StartCoroutine(GifCor());
        }

        private void StopRotation()
        {
            if (_rotationCoroutine == null) return;
            StopCoroutine(_rotationCoroutine);
            _rotationCoroutine = null;
        }

        private IEnumerator GifCor()
        {
            while (bannerDataList.Count > 0)
            {
                ShowBanner();
                yield return new WaitForSecondsRealtime(8f);
            }
        }

        private void ShowBanner()
        {
            if (bannerDataList.Count == 0 || adImage == null || !adImage.isActiveAndEnabled)
                return;

            // Проверяем непосредственно перед событием: fullscreen/UI могли измениться
            // между Update контроллера и очередной итерацией ротации.
            if (!_crossPromoVisible || !CanDisplayBanner())
                return;

            var index = _currentBannerIndex % bannerDataList.Count;
            var data = bannerDataList[index];
            if (data.sprite == null)
                return;

            adImage.sprite = data.sprite;

            _lastShownIndex = index;
            _currentBannerIndex = (index + 1) % bannerDataList.Count;

            // Каждая итерация видимого баннера — показ на нашем бэкенде. С 1.0.9 Analytics не шлёт
            // его сразу: показы копятся и уходят одним cp_impression с "n" (не чаще раза в 5 минут
            // на paid_app_id).
            // В AppMetrica и Adjust показы баннера по-прежнему не отправляются.
            CrossPromoModule.Instance?.TrackImpression(data.paidAppId, BannerPlacement);
        }

        private IEnumerator DownloadBannerSprite(PromoConfiguration video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.BannerUrl))
                yield break;

            string title = string.IsNullOrWhiteSpace(video.Title) ? $"banner_{bannerDataList.Count}" : video.Title;
            string paidAppId = video.AppPackageName?.Count > 0 ? video.AppPackageName[0] : null;

            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(video.BannerUrl);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var texture = DownloadHandlerTexture.GetContent(request);
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                // Adjust-ссылки НЕ собираем здесь: device id ещё не готов на момент скачивания.
                // Ссылку клика соберём в TrackAndOpenUrl, в момент клика (см. задачу про баннер).
                bannerDataList.Add(new BannerData(
                    title,
                    sprite,
                    video.RedirectUrl,
                    video.TrackingUrl,
                    paidAppId)
                {
                    config = video
                });
            }
            else
            {
                Debug.LogWarning($"[CrossPromoBanner] Failed to download banner: {request.error}");
            }
        }

        #region UIFuncs

        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            this.onClose = onClose;
            this.isNoAds = isNoAds;
            RefreshBannerUI();
        }

        public void OnBannerClick()
        {
            RefreshBannerUI();
            if (!_crossPromoVisible || !CanDisplayBanner() || !IsBannerUiVisible(requireInteraction: true))
            {
                return;
            }

            if (_lastShownIndex < 0 || _lastShownIndex >= bannerDataList.Count)
            {
                return;
            }

            // Отсчёт — от последнего ОБРАБОТАННОГО тапа: тап по невидимому баннеру выше не считается.
            // realtimeSinceStartup не зависит от timeScale.
            float now = Time.realtimeSinceStartup;
            if (now - _lastHandledClickTime < ClickDebounceSeconds)
            {
                Debug.Log($"[CrossPromoBanner] Repeated tap within {ClickDebounceSeconds}s ignored");
                return;
            }
            _lastHandledClickTime = now;

            var data = bannerDataList[_lastShownIndex];
            CrossPromoAnalytics.ReportBannerClick(data);
            Debug.Log($"[CrossPromoBanner] Banner clicked → sending cp_click (paidAppId={data.paidAppId}, placement={BannerPlacement}, title={data.title})");

            // Модуль переживает скрытие/уничтожение баннера. Данные креатива и callback
            // фиксируем при клике, чтобы ротация не изменила событие или переход.
            var module = CrossPromoModule.Instance;
            MonoBehaviour host = module != null ? module : this;
            host.StartCoroutine(TrackAndOpenUrl(host, module, data, onClose));
        }

        private static IEnumerator TrackAndOpenUrl(
            MonoBehaviour host, CrossPromoModule module, BannerData data, Action onClose)
        {
            bool backendDone = module == null;
            bool externalDone = false;
            float deadline = Time.realtimeSinceStartup + ClickTrackingTimeoutSeconds;

            // Бэкенд стартует первым, независимо от скорости Adjust/TrackingUrl.
            // Это единственный cp_click: отдельного fire-and-forget вызова нет.
            if (module != null)
                host.StartCoroutine(TrackAndNotify(
                    module.TrackClickRoutine(data.paidAppId, BannerPlacement), () => backendDone = true));
            else
                Debug.LogWarning("[CrossPromoBanner] CrossPromoModule is unavailable — backend click cannot be sent.");

            host.StartCoroutine(TrackAndNotify(SendExternalClickTracking(data), () => externalDone = true));

            while ((!backendDone || !externalDone) && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!backendDone || !externalDone)
                Debug.LogWarning($"[CrossPromoBanner] Click tracking still in flight after {ClickTrackingTimeoutSeconds}s — continuing redirect, tracking continues in background.");

            if (!string.IsNullOrWhiteSpace(data.redirectUrl))
                Application.OpenURL(data.redirectUrl);
            else
                onClose?.Invoke();
        }

        private static IEnumerator TrackAndNotify(IEnumerator tracking, Action onDone)
        {
            yield return tracking;
            onDone();
        }

        private static IEnumerator SendExternalClickTracking(BannerData data)
        {
            // Собираем ссылку Adjust В МОМЕНТ КЛИКА — когда device id уже готов. Раньше она
            // собиралась заранее (при скачивании картинки) и уходила без device id.
            string adjustClickUrl = data.config != null
                ? CrossPromoAdjustTracking.BuildClickUrl(data.config)
                : data.adjustClickUrl;

            yield return CrossPromoAdjustTracking.SendGet(adjustClickUrl);

            if (CrossPromoAdjustTracking.IsHttpUrl(data.trackingUrl))
            {
                using UnityWebRequest request = UnityWebRequest.Get(data.trackingUrl);
                request.timeout = 15;
                yield return request.SendWebRequest();
            }
            else if (!string.IsNullOrWhiteSpace(data.trackingUrl))
            {
                Debug.LogWarning($"[CrossPromoBanner] Skipping non-http(s) TrackingUrl: {data.trackingUrl}");
            }
        }

        public void hide()
        {
            _explicitlyHidden = true;
            RefreshBannerUI();
        }

        public void UpdateBannerUI()
        {
            _explicitlyHidden = false;
            if (bannerGO != null && !bannerGO.activeSelf)
                bannerGO.SetActive(true);

            RefreshBannerUI();
        }

        private void RefreshBannerUI(bool forceFillCheck = true)
        {
#if AMZN_APPLOVIN_ENABLED
            var appLovinModule = SdkModuleRegistry.Get<AppLovinModule>();
            if (_appLovinModule != appLovinModule)
            {
                HideAppLovinBanner();
                _appLovinModule = appLovinModule;
            }
#endif

            // Одна eligibility для обоих провайдеров: скрытие Unity UI и fullscreen
            // должны прерывать также CP-ротацию, а не только запрещать нативный MAX.
            if (!CanDisplayBanner())
            {
                HideAppLovinBanner();
                SetCrossPromoVisible(false);
                return;
            }

            if (forceFillCheck || Time.realtimeSinceStartup >= _nextFillCheckTime)
            {
                _shouldUseAppLovinBanner = _module != null && _module.ShouldUseAppLovinBanner;
                _nextFillCheckTime = Time.realtimeSinceStartup + FillCheckIntervalSeconds;
            }

#if AMZN_APPLOVIN_ENABLED
            if (_shouldUseAppLovinBanner && _appLovinModule != null
                && _appLovinModule.ShowBanner())
            {
                _appLovinBannerOwner = this;
                SetCrossPromoVisible(false);
                return;
            }
#endif

            HideAppLovinBanner();
            SetCrossPromoVisible(true);
        }

        private bool CanDisplayBanner()
        {
            if (!isActiveAndEnabled || _explicitlyHidden || bannerGO == null
                || !bannerGO.activeInHierarchy || (isNoAds?.Invoke() ?? false)
                || (_module != null && _module.IsVideoPromoVisible))
                return false;

#if AMZN_APPLOVIN_ENABLED
            var appLovinModule = SdkModuleRegistry.Get<AppLovinModule>();
            if (appLovinModule != null && appLovinModule.IsShowingAd)
                return false;
#endif

            return IsBannerUiVisible(requireInteraction: false);
        }

        private bool IsBannerUiVisible(bool requireInteraction)
        {
            if (adImage == null || !adImage.isActiveAndEnabled || adImage.color.a <= 0f
                || adImage.canvas == null || adImage.canvasRenderer.GetAlpha() <= 0f)
                return false;

            bool ignoreParentGroups = false;
            for (var current = adImage.transform; current != null; current = current.parent)
            {
                current.GetComponents(_visibilityCanvases);
                foreach (var canvas in _visibilityCanvases)
                {
                    if (!canvas.isActiveAndEnabled)
                        return false;
                }

                if (ignoreParentGroups)
                    continue;

                current.GetComponents(_visibilityCanvasGroups);
                foreach (var group in _visibilityCanvasGroups)
                {
                    // Наша alpha=0 означает замену картинки нативным MAX, а не скрытие
                    // экрана игрой. Не создаём зависимость eligibility от своего output.
                    if (group == _presentationCanvasGroup || !group.isActiveAndEnabled)
                        continue;

                    if (group.alpha <= 0f
                        || (requireInteraction && (!group.interactable || !group.blocksRaycasts)))
                        return false;

                    if (group.ignoreParentGroups)
                        ignoreParentGroups = true;
                }
            }

            return true;
        }

        private void HideAppLovinBanner()
        {
#if AMZN_APPLOVIN_ENABLED
            // CP-only экземпляр и старый баннер сцены не должны скрывать MAX,
            // запрос на который уже принадлежит другому контроллеру.
            if (_appLovinBannerOwner != this)
                return;

            if (_appLovinModule != null)
                _appLovinModule.HideBanner();
            _appLovinBannerOwner = null;
#endif
        }

        private void SetCrossPromoVisible(bool show)
        {
            _crossPromoVisible = show;
            if (_presentationCanvasGroup != null)
            {
                _presentationCanvasGroup.alpha = show ? 1f : 0f;
                _presentationCanvasGroup.interactable = show;
                _presentationCanvasGroup.blocksRaycasts = show;
            }

            if (show)
                StartRotationIfNeeded();
            else
                StopRotation();
        }

        #endregion
    }

    internal class BannerData
    {
        public string title;
        public Sprite sprite;
        public string redirectUrl;
        public string trackingUrl;
        public string paidAppId;
        public string adjustImpressionUrl;
        public string adjustClickUrl;

        // Конфиг креатива (для баннера) — чтобы собрать Adjust-ссылку клика в момент клика,
        // когда device id уже готов, а не заранее при скачивании картинки.
        public PromoConfiguration config;

        public BannerData(
            string title,
            Sprite sprite,
            string redirectUrl,
            string trackingUrl,
            string paidAppId,
            string adjustImpressionUrl = null,
            string adjustClickUrl = null)
        {
            this.title = title;
            this.sprite = sprite;
            this.redirectUrl = redirectUrl;
            this.trackingUrl = trackingUrl;
            this.paidAppId = paidAppId;
            this.adjustImpressionUrl = adjustImpressionUrl;
            this.adjustClickUrl = adjustClickUrl;
        }
    }
}
#endif
