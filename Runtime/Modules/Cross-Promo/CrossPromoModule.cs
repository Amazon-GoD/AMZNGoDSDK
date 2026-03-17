#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoModule : ModuleBase
    {
        public static CrossPromoModule Instance { get; private set; }

        public static event Action<PromosConfigurationInfo> OnConfigLoaded;
        public static event Action<Action, Func<bool>> OnBannerFuncsUpdated;

        [SerializeField] private CrossPromoConfigurationManager _configurationManager;
        [SerializeField] private AppodealAdapter _appodealAdapter;
        [SerializeField] private CrossPromoVideoOverlay _videoOverlay;

        private CrossPromoTrackingService _trackingService;
        private string _configUrl;
        private string _appodealSDKKey;
        private bool _appodealInitialized;
        private PromosConfigurationInfo _crossPromoConfig;
        private Action _currentBannerOnClose;
        private Func<bool> _currentIsNoAds;

        public bool IsAdsReady => _appodealAdapter?.IsReady() ?? false;

        public PromosConfigurationInfo LoadedConfig => _crossPromoConfig;

        public Action CurrentBannerOnClose => _currentBannerOnClose;
        public Func<bool> CurrentIsNoAds => _currentIsNoAds;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            UnsubscribeFromAppodealEvents();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Construct(CrossPromoSettingData settings)
        {
            Enabled = settings.Enabled;
            _configUrl = settings.ConfigUrl;
            _appodealSDKKey = settings.AppodealSdkKey;

            if (Enabled && !string.IsNullOrEmpty(settings.TrackingBaseUrl))
            {
                _trackingService = gameObject.AddComponent<CrossPromoTrackingService>();
                _trackingService.Construct(
                    settings.TrackingBaseUrl,
                    settings.TrackingApiKey,
                    settings.AppType,
                    settings.DefaultPromotedAppId);
            }

            if (!string.IsNullOrWhiteSpace(_appodealSDKKey))
            {
                _appodealAdapter?.Initialize(_appodealSDKKey);
                _appodealInitialized = true;
            }
            else
            {
                _appodealInitialized = false;
            }
        }

        public override void Initialize()
        {
            _trackingService?.Initialize();
            SubscribeToAppodealEvents();
            StartCoroutine(InitCrossPromoModulesCorAsync());
        }

        private IEnumerator InitCrossPromoModulesCorAsync()
        {
            yield return LoadJson();
            OnConfigLoaded?.Invoke(_crossPromoConfig);
        }

        private IEnumerator LoadJson()
        {
            if (string.IsNullOrWhiteSpace(_configUrl))
            {
                yield break;
            }

            var operation = _configurationManager.FetchRemoteConfigAsync(_configUrl);

            while (!operation.IsCompleted)
            {
                yield return null;
            }

            if (operation.IsCompletedSuccessfully)
            {
                _crossPromoConfig = operation.Result;
            }
            else
            {
                Debug.LogWarning($"[CrossPromoModule] Remote config load failed: {operation.Exception?.Message}");
            }
        }

        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            _currentBannerOnClose = onClose;
            _currentIsNoAds = isNoAds;
            OnBannerFuncsUpdated?.Invoke(onClose, isNoAds);
        }

        public void ShowInterstitial()
        {
            if (!Enabled)
            {
                return;
            }

            if (!_appodealInitialized)
            {
                Debug.LogWarning("[CrossPromoModule] Appodeal SDK is not initialized");
                return;
            }

            _appodealAdapter?.Show_Interstitial();
            if (_appodealAdapter?.IsReady() == true)
                return;
            Debug.LogWarning("[CrossPromoModule] Appodeal interstitial not loaded yet, caching request");
        }

        public void ShowRewarded(Action callback)
        {
            if (!Enabled)
            {
                return;
            }

            if (!_appodealInitialized)
            {
                Debug.LogWarning("[CrossPromoModule] Appodeal SDK is not initialized");
                return;
            }

            _appodealAdapter?.Show_Rewarded(callback);
            if (_appodealAdapter?.IsReady() == true)
                return;
            Debug.LogWarning("[CrossPromoModule] Appodeal rewarded not loaded yet, caching request");
        }

        public void TrackImpression(string paidAppId)
        {
            Debug.Log($"[CrossPromoModule] TrackImpression called, paidAppId={paidAppId ?? "null (using config default)"}");
            _trackingService?.TrackImpression(paidAppId);
        }

        public void TrackClick(string paidAppId)
        {
            Debug.Log($"[CrossPromoModule] TrackClick called, paidAppId={paidAppId ?? "null (using config default)"}");
            _trackingService?.TrackClick(paidAppId);
        }

        private void SubscribeToAppodealEvents()
        {
            if (_trackingService == null)
                return;

            AppodealAdapter.OnInterstitialAdShown += OnAppodealAdShown;
            AppodealAdapter.OnInterstitialAdClicked += OnAppodealAdClicked;
            AppodealAdapter.OnRewardedAdShown += OnAppodealAdShown;
            AppodealAdapter.OnRewardedAdClicked += OnAppodealAdClicked;
        }

        private void UnsubscribeFromAppodealEvents()
        {
            AppodealAdapter.OnInterstitialAdShown -= OnAppodealAdShown;
            AppodealAdapter.OnInterstitialAdClicked -= OnAppodealAdClicked;
            AppodealAdapter.OnRewardedAdShown -= OnAppodealAdShown;
            AppodealAdapter.OnRewardedAdClicked -= OnAppodealAdClicked;
        }

        private void OnAppodealAdShown()
        {
            Debug.Log("[CrossPromoModule] Appodeal ad shown → sending cp_impression (paidAppId from config)");
            _trackingService?.TrackImpression(null);
        }

        private void OnAppodealAdClicked()
        {
            Debug.Log("[CrossPromoModule] Appodeal ad clicked → sending cp_click (paidAppId from config)");
            _trackingService?.TrackClick(null);
        }

        #region Video Promo

        /// <summary>
        /// Shows a weighted-random video promo from the loaded configuration.
        /// Falls back to invoking <paramref name="onClose"/> immediately if no videos are available.
        /// </summary>
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null)
        {
            if (!Enabled)
            {
                onClose?.Invoke();
                return;
            }

            _crossPromoConfig?.CheckVideosShowLimit();

            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
            {
                Debug.LogWarning("[CrossPromoModule] No video promos available in config.");
                onClose?.Invoke();
                return;
            }

            var config = SelectWeightedRandom(_crossPromoConfig.Videos);
            if (config == null || string.IsNullOrWhiteSpace(config.VideoUrl) && string.IsNullOrWhiteSpace(config.FileName))
            {
                Debug.LogWarning("[CrossPromoModule] Selected video promo has no URL or file name.");
                onClose?.Invoke();
                return;
            }

            if (_videoOverlay == null)
            {
                Debug.LogError("[CrossPromoModule] VideoOverlay is not assigned. Cannot show video promo.");
                onClose?.Invoke();
                return;
            }

            _videoOverlay.Show(config, onClose, onCTAClick);
        }

        /// <summary>Shows a specific video promo by <see cref="PromoConfiguration"/>.</summary>
        public void ShowVideoPromo(PromoConfiguration config, Action onClose = null, Action onCTAClick = null)
        {
            if (_videoOverlay == null)
            {
                Debug.LogError("[CrossPromoModule] VideoOverlay is not assigned.");
                onClose?.Invoke();
                return;
            }

            _videoOverlay.Show(config, onClose, onCTAClick);
        }

        /// <summary>Returns true if a video overlay is currently being shown.</summary>
        public bool IsVideoPromoVisible => _videoOverlay != null && _videoOverlay.IsVisible;

        private static PromoConfiguration SelectWeightedRandom(List<PromoConfiguration> videos)
        {
            if (videos == null || videos.Count == 0)
                return null;

            float total = videos.Sum(v => v.Weight);
            if (total <= 0)
                return videos[0];

            float random = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var video in videos)
            {
                cumulative += video.Weight;
                if (random <= cumulative)
                    return video;
            }

            return videos[videos.Count - 1];
        }

        #endregion

        public override void Cleenup()
        {
            UnsubscribeFromAppodealEvents();
        }
    }
}
#endif
