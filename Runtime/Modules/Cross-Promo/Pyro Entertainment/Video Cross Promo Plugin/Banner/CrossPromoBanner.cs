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
        private CanvasGroup _bannerCanvasGroup;
        private float _originalAlpha;
        private bool _originalInteractable;
        private bool _originalBlocksRaycasts;
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

            // Не деактивируем bannerGO: он может содержать сам контроллер, которому
            // нужно продолжать следить за cap, no-ads и готовностью MAX.
            _bannerCanvasGroup = bannerGO.GetComponent<CanvasGroup>();
            if (_bannerCanvasGroup == null)
            {
                _bannerCanvasGroup = bannerGO.AddComponent<CanvasGroup>();
            }
            _originalAlpha = _bannerCanvasGroup.alpha;
            _originalInteractable = _bannerCanvasGroup.interactable;
            _originalBlocksRaycasts = _bannerCanvasGroup.blocksRaycasts;

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
            if (bannerDataList.Count == 0 || adImage == null)
                return;

            // Баннера не видно на экране — показ не шлём.
            if (!_crossPromoVisible || _explicitlyHidden || !isActiveAndEnabled
                || bannerGO == null || !bannerGO.activeInHierarchy)
                return;

            // Куплено отключение рекламы — показ не шлём.
            if (isNoAds?.Invoke() ?? false)
                return;

            var index = _currentBannerIndex % bannerDataList.Count;
            var data = bannerDataList[index];
            adImage.sprite = data.sprite;

            // Показ баннера НЕ шлём в аналитику вообще: баннер меняется каждые 8 секунд и это
            // засоряло бы аналитику (раньше — 68% всех событий). Ни AppMetrica, ни бэкенд, ни
            // Adjust. Привязку установки даёт только клик по баннеру — его шлём во все каналы
            // (см. OnBannerClick).
            _lastShownIndex = index;
            _currentBannerIndex = (index + 1) % bannerDataList.Count;
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
            if (!_crossPromoVisible || !isActiveAndEnabled || _bannerCanvasGroup == null
                || _bannerCanvasGroup.alpha <= 0f || !_bannerCanvasGroup.interactable)
            {
                return;
            }

            if (_lastShownIndex < 0 || _lastShownIndex >= bannerDataList.Count)
            {
                return;
            }

            var data = bannerDataList[_lastShownIndex];
            CrossPromoAnalytics.ReportBannerClick(data);
            Debug.Log($"[CrossPromoBanner] Banner clicked → sending cp_click (paidAppId={data.paidAppId}, title={data.title})");
            CrossPromoModule.Instance?.TrackClick(data.paidAppId);
            StartCoroutine(TrackAndOpenUrl(data));
        }

        private IEnumerator TrackAndOpenUrl(BannerData data)
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
                yield return request.SendWebRequest();
            }
            else if (!string.IsNullOrWhiteSpace(data.trackingUrl))
            {
                Debug.LogWarning($"[CrossPromoBanner] Skipping non-http(s) TrackingUrl: {data.trackingUrl}");
            }

            if (!string.IsNullOrWhiteSpace(data.redirectUrl))
            {
                Application.OpenURL(data.redirectUrl);
            }
            else
            {
                onClose?.Invoke();
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
            if (!isActiveAndEnabled || _explicitlyHidden || bannerGO == null
                || !bannerGO.activeInHierarchy || (isNoAds?.Invoke() ?? false))
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
            var appLovinModule = SdkModuleRegistry.Get<AppLovinModule>();
            if (_appLovinModule != appLovinModule)
            {
                HideAppLovinBanner();
                _appLovinModule = appLovinModule;
            }

            // Последний cap может сработать ещё во время видео. Нативный баннер
            // допускается только после закрытия CP/MAX fullscreen-рекламы.
            if (_shouldUseAppLovinBanner && _module != null && !_module.IsVideoPromoVisible
                && _appLovinModule != null && !_appLovinModule.IsShowingAd
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
            if (_bannerCanvasGroup != null)
            {
                _bannerCanvasGroup.alpha = show ? _originalAlpha : 0f;
                _bannerCanvasGroup.interactable = show && _originalInteractable;
                _bannerCanvasGroup.blocksRaycasts = show && _originalBlocksRaycasts;
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
