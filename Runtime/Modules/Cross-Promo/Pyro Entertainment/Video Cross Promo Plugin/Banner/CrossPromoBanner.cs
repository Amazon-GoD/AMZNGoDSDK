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

        private void Awake()
        {
            if (bannerGO == null)
            {
                bannerGO = gameObject;
            }

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

        private void Start()
        {
            BindToModule();
            if (_module == null)
            {
                StartCoroutine(DelayedBind());
            }
        }

        private IEnumerator DelayedBind()
        {
            while (_module == null)
            {
                yield return null;
                BindToModule();
            }
        }

        private void OnDestroy()
        {
            CrossPromoModule.OnConfigLoaded -= OnModuleConfigLoaded;
            CrossPromoModule.OnBannerFuncsUpdated -= OnModuleBannerFuncsUpdated;
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
            if (_initializationCoroutine != null)
            {
                StopCoroutine(_initializationCoroutine);
            }

            _initializationCoroutine = StartCoroutine(Initialize(config));
        }

        private void ApplyBannerFunctions(Action onClose, Func<bool> isNoAds)
        {
            this.onClose = onClose;
            this.isNoAds = isNoAds;
            UpdateBannerUI();
        }

        public IEnumerator Initialize(PromosConfigurationInfo config)
        {
            bannerDataList.Clear();
            _currentBannerIndex = 0;
            _lastShownIndex = -1;

            if (config?.Videos == null || config.Videos.Count == 0)
            {
                UpdateBannerUI();
                yield break;
            }

            foreach (var video in config.Videos)
                yield return StartCoroutine(DownloadBannerSprite(video));

            if (_rotationCoroutine != null)
            {
                StopCoroutine(_rotationCoroutine);
            }

            _rotationCoroutine = StartCoroutine(GifCor());
            UpdateBannerUI();
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
            {
                return;
            }

            var index = _currentBannerIndex % bannerDataList.Count;
            var data = bannerDataList[index];
            adImage.sprite = data.sprite;
            CrossPromoAnalytics.ReportBannerShow(data);
            Debug.Log($"[CrossPromoBanner] Banner shown → sending cp_impression (paidAppId={data.paidAppId}, title={data.title})");
            CrossPromoModule.Instance?.TrackImpression(data.paidAppId);
            if (!string.IsNullOrWhiteSpace(data.adjustImpressionUrl))
                StartCoroutine(CrossPromoAdjustTracking.SendGet(data.adjustImpressionUrl));
            _lastShownIndex = index;
            _currentBannerIndex = (index + 1) % bannerDataList.Count;
        }

        private IEnumerator DownloadBannerSprite(PromoConfiguration video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.BannerUrl))
                yield break;

            string title = string.IsNullOrWhiteSpace(video.Title) ? $"banner_{bannerDataList.Count}" : video.Title;
            string paidAppId = video.AppPackageName?.Count > 0 ? video.AppPackageName[0] : null;
            string adjustImpression = CrossPromoAdjustTracking.BuildImpressionUrl(video);
            string adjustClick = CrossPromoAdjustTracking.BuildClickUrl(video);

            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(video.BannerUrl);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var texture = DownloadHandlerTexture.GetContent(request);
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                bannerDataList.Add(new BannerData(
                    title,
                    sprite,
                    video.RedirectUrl,
                    video.TrackingUrl,
                    paidAppId,
                    adjustImpression,
                    adjustClick));
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
            UpdateBannerUI();
        }

        public void OnBannerClick()
        {
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
            yield return CrossPromoAdjustTracking.SendGet(data.adjustClickUrl);

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

        public void hide() => bannerGO.SetActive(false);

        public void UpdateBannerUI()
        {
            if (bannerGO == null)
            {
                return;
            }

            if (isNoAds == null)
            {
                bannerGO.SetActive(true);
                return;
            }

            bannerGO.SetActive(!isNoAds());
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