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
        private VideoPlayerBackend _videoBackend;
        private PromoConfiguration _preloadedConfig;
        private CrossPromoVideoPlayer _preloadPlayer;

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
            Debug.Log($"[CrossPromoModule] Construct() called. Enabled={settings.Enabled}, ConfigUrl='{settings.ConfigUrl}', VideoBackend={settings.VideoBackend}");
            Enabled = settings.Enabled;
            _configUrl = settings.ConfigUrl;
            _appodealSDKKey = settings.AppodealSdkKey;
            _videoBackend = settings.VideoBackend;

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
            Debug.Log($"[CrossPromoModule] Initialize() called. Enabled={Enabled}, configUrl='{_configUrl}', videoBackend={_videoBackend}");
            _trackingService?.Initialize();
            SubscribeToAppodealEvents();
            StartCoroutine(InitCrossPromoModulesCorAsync());
        }

        private IEnumerator InitCrossPromoModulesCorAsync()
        {
            Debug.Log("[CrossPromoModule] InitCrossPromoModulesCorAsync started");
            yield return LoadJson();
            Debug.Log($"[CrossPromoModule] LoadJson finished. _crossPromoConfig is {(_crossPromoConfig == null ? "NULL" : "set")}, Videos count = {_crossPromoConfig?.Videos?.Count ?? -1}");
            OnConfigLoaded?.Invoke(_crossPromoConfig);

            PreloadNextVideo();
        }

        private IEnumerator LoadJson()
        {
            Debug.Log($"[CrossPromoModule] LoadJson() called. _configUrl='{_configUrl}'");

            if (string.IsNullOrWhiteSpace(_configUrl))
            {
                Debug.LogWarning("[CrossPromoModule] LoadJson: _configUrl is EMPTY — skipping config fetch!");
                yield break;
            }

            Debug.Log($"[CrossPromoModule] Starting FetchRemoteConfigAsync for URL: {_configUrl}");
            var operation = _configurationManager.FetchRemoteConfigAsync(_configUrl);

            float waitStart = Time.realtimeSinceStartup;
            while (!operation.IsCompleted)
            {
                yield return null;
            }
            float elapsed = Time.realtimeSinceStartup - waitStart;

            Debug.Log($"[CrossPromoModule] FetchRemoteConfigAsync completed in {elapsed:F2}s. Status={operation.Status}, IsCompletedSuccessfully={operation.IsCompletedSuccessfully}, IsFaulted={operation.IsFaulted}, IsCanceled={operation.IsCanceled}");

            if (operation.IsCompletedSuccessfully)
            {
                _crossPromoConfig = operation.Result;
                Debug.Log($"[CrossPromoModule] Config loaded OK. Videos={_crossPromoConfig?.Videos?.Count ?? 0}");

                if (_crossPromoConfig?.Videos != null)
                {
                    for (int i = 0; i < _crossPromoConfig.Videos.Count; i++)
                    {
                        var v = _crossPromoConfig.Videos[i];
                        Debug.Log($"[CrossPromoModule]   Video[{i}]: Title='{v.Title}', URL='{v.VideoUrl}', Weight={v.Weight}, MaxShow={v.MaxShowCount}, Packages=[{string.Join(",", v.AppPackageName ?? new())}]");
                    }
                }
            }
            else
            {
                Debug.LogError($"[CrossPromoModule] Remote config load FAILED. Exception: {operation.Exception}");
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
            Debug.Log($"[CrossPromoModule] ShowVideoPromo() called. Enabled={Enabled}, _crossPromoConfig is {(_crossPromoConfig == null ? "NULL" : "set")}, Videos={_crossPromoConfig?.Videos?.Count ?? -1}");

            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoPromo: module is DISABLED");
                onClose?.Invoke();
                return;
            }

            Debug.Log($"[CrossPromoModule] Before CheckVideosShowLimit: Videos count = {_crossPromoConfig?.Videos?.Count ?? -1}");
            _crossPromoConfig?.CheckVideosShowLimit();
            Debug.Log($"[CrossPromoModule] After CheckVideosShowLimit: Videos count = {_crossPromoConfig?.Videos?.Count ?? -1}");

            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
            {
                Debug.LogWarning($"[CrossPromoModule] No video promos available in config. _crossPromoConfig={(_crossPromoConfig == null ? "NULL" : "exists")}, Videos={(_crossPromoConfig?.Videos == null ? "NULL" : $"count={_crossPromoConfig.Videos.Count}")}");
                onClose?.Invoke();
                return;
            }

            var config = _preloadedConfig ?? SelectWeightedRandom(_crossPromoConfig.Videos);
            _preloadedConfig = null;

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

            bool usedPreload = false;
            if (_preloadPlayer != null && _preloadPlayer.IsPreloaded)
            {
                string preloadUrl = ResolvePromoUrl(config);
                if (_preloadPlayer.PreloadedUrl == preloadUrl)
                {
                    _videoOverlay.SwapVideoPlayer(_preloadPlayer);
                    _preloadPlayer = null;
                    usedPreload = true;
                }
            }

            if (!usedPreload && _videoOverlay.VideoPlayer != null)
                _videoOverlay.VideoPlayer.SetBackend(_videoBackend);

            _videoOverlay.Show(config, () =>
            {
                onClose?.Invoke();
            }, onCTAClick);

            PreloadNextVideo();
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

            if (_videoOverlay.VideoPlayer != null)
                _videoOverlay.VideoPlayer.SetBackend(_videoBackend);

            _videoOverlay.Show(config, onClose, onCTAClick);
        }

        /// <summary>Returns true if a video overlay is currently being shown.</summary>
        public bool IsVideoPromoVisible => _videoOverlay != null && _videoOverlay.IsVisible;

        private CrossPromoVideoPlayer EnsurePreloadPlayer()
        {
            if (_preloadPlayer != null) return _preloadPlayer;

            var go = new GameObject("CrossPromoPreloader");
            go.transform.SetParent(transform);
            _preloadPlayer = go.AddComponent<CrossPromoVideoPlayer>();
            _preloadPlayer.SetBackend(_videoBackend);
            return _preloadPlayer;
        }

        private void PreloadNextVideo()
        {
            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
                return;

            _crossPromoConfig.CheckVideosShowLimit();
            if (_crossPromoConfig.Videos.Count == 0)
                return;

            var next = SelectWeightedRandom(_crossPromoConfig.Videos);
            if (next == null || string.IsNullOrWhiteSpace(next.VideoUrl) && string.IsNullOrWhiteSpace(next.FileName))
                return;

            _preloadedConfig = next;

            string url = ResolvePromoUrl(next);
            if (string.IsNullOrWhiteSpace(url))
                return;

            var player = EnsurePreloadPlayer();
            player.SetBackend(_videoBackend);
            player.Loop = false;
            player.Preload(url);
            Debug.Log($"[CrossPromoModule] Preloading next video on dedicated player: {next.Title}");
        }

        private static string ResolvePromoUrl(PromoConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.VideoUrl))
                return config.VideoUrl;

            if (!string.IsNullOrWhiteSpace(config.FileName))
            {
                string ext = config.FileExtension.ToString();
                string fileName = config.FileName.EndsWith($".{ext}", System.StringComparison.OrdinalIgnoreCase)
                    ? config.FileName
                    : $"{config.FileName}.{ext}";
                return System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            }

            return null;
        }

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
