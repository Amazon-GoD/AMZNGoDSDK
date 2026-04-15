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
        [SerializeField] private CrossPromoVideoOverlay _videoOverlay;

        private CrossPromoTrackingService _trackingService;
        private string _configUrl;
        private PromosConfigurationInfo _crossPromoConfig;
        private Action _currentBannerOnClose;
        private Func<bool> _currentIsNoAds;
        private VideoPlayerBackend _videoBackend;
        private PromoConfiguration _preloadedConfig;
        private CrossPromoVideoPlayer _preloadPlayer;
        private PromoConfiguration _lastShownConfig;
        private bool _firstPreloadDone;
        private Coroutine _initCoroutine;
        private Coroutine _showCoroutine;

        public PromosConfigurationInfo LoadedConfig => _crossPromoConfig;

        /// <summary>True when a video is preloaded and ready to play instantly.</summary>
        public bool IsVideoReady => _preloadPlayer != null && _preloadPlayer.IsPreloaded;

        public Action CurrentBannerOnClose => _currentBannerOnClose;
        public Func<bool> CurrentIsNoAds => _currentIsNoAds;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_initCoroutine != null) { StopCoroutine(_initCoroutine); _initCoroutine = null; }
            if (_showCoroutine != null) { StopCoroutine(_showCoroutine); _showCoroutine = null; }
            DisposePreloadPlayer();
            if (Instance == this)
            {
                Instance = null;
                OnConfigLoaded = null;
                OnBannerFuncsUpdated = null;
            }
        }

        public void Construct(CrossPromoSettingData settings)
        {
            Debug.Log($"[CrossPromoModule] Construct() called. Enabled={settings.Enabled}, ConfigUrl='{settings.ConfigUrl}', VideoBackend={settings.VideoBackend}");
            Enabled = settings.Enabled;
            _configUrl = settings.ConfigUrl;
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
        }

        private bool _initializeRequested;
        private bool _initRunning;

        public override void Initialize()
        {
            Debug.Log($"[CrossPromoModule] Initialize() called. Enabled={Enabled}, configUrl='{_configUrl}', videoBackend={_videoBackend}");
            DisposePreloadPlayer();
            _preloadedConfig = null;
            _firstPreloadDone = false;
            _initializeRequested = true;
            _trackingService?.Initialize();
            StartInitCoroutine();
        }

        private void StartInitCoroutine()
        {
            if (_initRunning) return;
            if (!isActiveAndEnabled) return;   // OnEnable will retry
            _initRunning = true;
            _initCoroutine = StartCoroutine(InitCrossPromoModulesCorAsync());
        }

        private void OnEnable()
        {
            // If a prior deactivation killed the init coroutine mid-flight (config fetch
            // or preload), pick up where we left off on re-activation.
            if (!_initializeRequested) return;
            if (_crossPromoConfig == null)
            {
                StartInitCoroutine();
            }
            else if (_preloadedConfig == null && !_initRunning)
            {
                PreloadNextVideo();
            }
        }

        private void OnDisable()
        {
            // StartCoroutine stops silently when the host GO is deactivated; the finally
            // block in InitCrossPromoModulesCorAsync may not run in that path. Reset the
            // guard here so OnEnable can legitimately restart the init.
            _initRunning = false;
        }

        private void DisposePreloadPlayer()
        {
            if (_preloadPlayer == null) return;

            _preloadPlayer.OnError -= HandlePreloadError;
            if (_preloadPlayer.gameObject != null)
                Destroy(_preloadPlayer.gameObject);
            _preloadPlayer = null;
        }

        private void HandlePreloadError(string error)
        {
            Debug.LogWarning($"[CrossPromoModule] Preload failed: {error}. Clearing preloaded config.");
            _preloadedConfig = null;
        }

        private IEnumerator InitCrossPromoModulesCorAsync()
        {
            Debug.Log("[CrossPromoModule] InitCrossPromoModulesCorAsync started");
            try
            {
                yield return LoadJson();
                Debug.Log($"[CrossPromoModule] LoadJson finished. _crossPromoConfig is {(_crossPromoConfig == null ? "NULL" : "set")}, Videos count = {_crossPromoConfig?.Videos?.Count ?? -1}");
                OnConfigLoaded?.Invoke(_crossPromoConfig);

                PreloadNextVideo();
            }
            finally
            {
                _initRunning = false;
            }
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

        #region Interstitial

        /// <summary>
        /// Shows a cross-promo video as an interstitial ad.
        /// Identical to <see cref="ShowVideoPromo"/> but sends inter_requested / inter_displayed analytics.
        /// </summary>
        public void ShowInterstitial(Action onClose = null, Action onCTAClick = null)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowInterstitial: module is DISABLED");
                onClose?.Invoke();
                return;
            }

            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, "interstitial", onRewarded: null));
        }

        #endregion

        #region Rewarded

        /// <summary>
        /// Shows a cross-promo video as a rewarded ad.
        /// <paramref name="onRewarded"/> is called when the video completes (the user earns the reward).
        /// </summary>
        public void ShowRewarded(Action onClose = null, Action onCTAClick = null, Action onRewarded = null)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowRewarded: module is DISABLED");
                onClose?.Invoke();
                return;
            }

            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, "rewarded", onRewarded));
        }

        #endregion

        #region Video Promo

        /// <summary>
        /// Shows a weighted-random video promo from the loaded configuration.
        /// Falls back to invoking <paramref name="onClose"/> immediately if no videos are available.
        /// </summary>
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null)
        {
            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, placement: null, onRewarded: null));
        }

        /// <summary>Shows a specific video promo by <see cref="PromoConfiguration"/>.
        /// Routed through the same preload-aware path as the parameterless overload, so if the
        /// preloaded player happens to hold this config's URL, playback starts instantly.</summary>
        public void ShowVideoPromo(PromoConfiguration config, Action onClose = null, Action onCTAClick = null)
        {
            if (config == null)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoPromo: config is null.");
                onClose?.Invoke();
                return;
            }

            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, placement: null, onRewarded: null, forcedConfig: config));
        }

        /// <summary>Returns true if a video overlay is currently being shown.</summary>
        public bool IsVideoPromoVisible => _videoOverlay != null && _videoOverlay.IsVisible;

        private IEnumerator ShowVideoInternalCoroutine(Action onClose, Action onCTAClick, string placement, Action onRewarded, PromoConfiguration forcedConfig = null)
        {
            float tShow = Time.realtimeSinceStartup;
            Debug.Log($"[CrossPromoModule][T={tShow:F2}] >>> ShowVideoInternal() TAP. Enabled={Enabled}, placement={placement ?? "video"}, preloadPlayer={(_preloadPlayer == null ? "null" : $"state={_preloadPlayer.State},isPreloaded={_preloadPlayer.IsPreloaded},isLoading={_preloadPlayer.IsLoading},url={_preloadPlayer.PreloadedUrl}")}");

            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoInternal: module is DISABLED");
                onClose?.Invoke();
                yield break;
            }

            if (IsVideoPromoVisible)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoInternal: another video promo is already visible, ignoring.");
                onClose?.Invoke();
                yield break;
            }

            _crossPromoConfig?.CheckVideosShowLimit();

            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
            {
                Debug.LogWarning("[CrossPromoModule] No video promos available in config.");
                onClose?.Invoke();
                yield break;
            }

            Debug.Log($"[CrossPromoModule] Show: forcedConfig='{forcedConfig?.Title}', _preloadedConfig='{_preloadedConfig?.Title}', _lastShownConfig='{_lastShownConfig?.Title}'");
            var config = forcedConfig ?? _preloadedConfig ?? SelectWeightedRandom(_crossPromoConfig.Videos, _lastShownConfig);
            Debug.Log($"[CrossPromoModule] Show: selected config='{config?.Title}'");

            if (config == null || (string.IsNullOrWhiteSpace(config.VideoUrl) && string.IsNullOrWhiteSpace(config.FileName)))
            {
                Debug.LogWarning("[CrossPromoModule] Selected video promo has no URL or file name.");
                onClose?.Invoke();
                yield break;
            }

            if (_videoOverlay == null)
            {
                Debug.LogError("[CrossPromoModule] VideoOverlay is not assigned. Cannot show video promo.");
                onClose?.Invoke();
                yield break;
            }

            // Swap in the preload player whether it's Ready OR still Loading (as long as the URL
            // matches). For the Loading case, CrossPromoVideoPlayer.PlayPreloaded sets autoPlay=true
            // so playback kicks in the moment Prepare completes — starting a second Load on the
            // overlay's own player would waste the in-flight Prepare and double the wait.
            bool usedPreload = false;
            if (_preloadPlayer != null && (_preloadPlayer.IsPreloaded || _preloadPlayer.IsLoading))
            {
                string preloadUrl = ResolvePromoUrl(config);
                bool urlMatch = _preloadPlayer.PreloadedUrl == preloadUrl;
                Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup - tShow:F2}] Preload-swap check: urlMatch={urlMatch}, preloadedUrl='{_preloadPlayer.PreloadedUrl}', requestedUrl='{preloadUrl}', preloadState={_preloadPlayer.State}");
                if (urlMatch)
                {
                    Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup - tShow:F2}] SWAPPING preload player into overlay (was {(_preloadPlayer.IsPreloaded ? "READY" : "LOADING")})");
                    _preloadPlayer.OnError -= HandlePreloadError;
                    _videoOverlay.SwapVideoPlayer(_preloadPlayer);
                    _preloadPlayer = null;
                    _preloadedConfig = null;
                    usedPreload = true;
                }
            }
            else
            {
                Debug.LogWarning($"[CrossPromoModule][T={Time.realtimeSinceStartup - tShow:F2}] NO preload available. preloadPlayer={(_preloadPlayer == null ? "null" : $"state={_preloadPlayer.State}")} — overlay will Load from scratch (expect long black screen)");
            }

            if (!usedPreload && _videoOverlay.VideoPlayer != null)
                _videoOverlay.VideoPlayer.SetBackend(_videoBackend);

            _lastShownConfig = config;

            if (placement == "interstitial")
            {
                var data = BuildBannerData(config);
                CrossPromoAnalytics.ReportInterDisplayed(data, placement);
            }
            else if (placement == "rewarded")
            {
                var data = BuildBannerData(config);
                CrossPromoAnalytics.ReportRewardDisplayed(data, placement);
            }

            if (onRewarded != null)
            {
                Action rewardHandler = null;
                rewardHandler = () =>
                {
                    _videoOverlay.OnVideoCompleted -= rewardHandler;
                    onRewarded.Invoke();
                };
                _videoOverlay.OnVideoCompleted += rewardHandler;
            }

            Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup - tShow:F2}] Calling overlay.Show() usedPreload={usedPreload}");
            _videoOverlay.Show(config, () =>
            {
                onClose?.Invoke();
            }, onCTAClick);

            // Defer the next preload until the current video has actually started rendering.
            // On Android, starting a second VideoPlayer.Prepare while the first is still
            // spinning up MediaCodec competes for hardware decoders — the current video
            // stalls on a black RT for ~1s while the next preload grabs resources.
            Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup - tShow:F2}] overlay.Show returned; deferring next preload until current video is Playing");
            StartCoroutine(DeferredPreloadNextVideo());
        }

        private IEnumerator DeferredPreloadNextVideo()
        {
            var currentPlayer = _videoOverlay != null ? _videoOverlay.VideoPlayer : null;
            if (currentPlayer != null)
            {
                float waitStart = Time.realtimeSinceStartup;
                const float maxWait = 3f;
                while (currentPlayer.State != CrossPromoVideoPlayer.PlaybackState.Playing
                       && Time.realtimeSinceStartup - waitStart < maxWait)
                {
                    yield return null;
                }

                // Small grace period after the first frame lands so Android MediaCodec
                // finishes its init work before we spin up a second decoder.
                yield return new WaitForSecondsRealtime(0.15f);

                Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup:F2}] Deferred preload trigger (waited {Time.realtimeSinceStartup - waitStart:F2}s, currentState={currentPlayer.State})");
            }

            PreloadNextVideo();
        }

        private static BannerData BuildBannerData(PromoConfiguration config)
        {
            return new BannerData(
                config.Title, null, config.RedirectUrl, config.TrackingUrl,
                config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null);
        }

        private CrossPromoVideoPlayer EnsurePreloadPlayer()
        {
            if (_preloadPlayer != null) return _preloadPlayer;

            // Preloader lives on a detached DontDestroyOnLoad GameObject so that:
            //  * deactivation of the CrossPromo module's host GO doesn't silently cancel
            //    Unity VideoPlayer.Prepare() (which happens on inactive hierarchy);
            //  * scene changes don't wipe the buffered video.
            // It has no RawImage / MeshRenderer target, so its RenderTexture is never
            // displayed and audio stays silent (we only Prepare, not Play, until swap).
            //
            // Create inactive first so Awake() is deferred — lets us set the backend
            // BEFORE Awake() spins up a default Unity VideoPlayer component that
            // would be useless for the Media3 backend.
            var go = new GameObject("CrossPromoPreloader");
            go.SetActive(false);
            _preloadPlayer = go.AddComponent<CrossPromoVideoPlayer>();
            _preloadPlayer.SetBackend(_videoBackend);
            _preloadPlayer.OnError += HandlePreloadError;
            go.SetActive(true);
            DontDestroyOnLoad(go);
            return _preloadPlayer;
        }

        private void PreloadNextVideo()
        {
            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
                return;

            _crossPromoConfig.CheckVideosShowLimit();
            if (_crossPromoConfig.Videos.Count == 0)
                return;

            Debug.Log($"[CrossPromoModule] PreloadNextVideo: _lastShownConfig='{_lastShownConfig?.Title}', videos.Count={_crossPromoConfig.Videos.Count}");
            var next = SelectWeightedRandom(_crossPromoConfig.Videos, _lastShownConfig);
            if (next == null || (string.IsNullOrWhiteSpace(next.VideoUrl) && string.IsNullOrWhiteSpace(next.FileName)))
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

            // Skip analytics for the very first preload after app launch (there's no
            // corresponding Show event for it yet). Every subsequent preload follows a
            // real Show, so 'requested' count stays 1:1 with actual displays.
            if (_firstPreloadDone)
            {
                CrossPromoAnalytics.ReportInterRequested("interstitial");
            }
            else
            {
                _firstPreloadDone = true;
            }
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

        private static PromoConfiguration SelectWeightedRandom(List<PromoConfiguration> videos, PromoConfiguration exclude = null)
        {
            if (videos == null || videos.Count == 0)
                return null;

            // Title-based exclude comparison: reference equality breaks if the Videos
            // list was rebuilt (e.g. config reparse), producing a silently biased pool.
            var pool = (exclude != null && videos.Count > 1)
                ? videos.Where(v => v.Title != exclude.Title).ToList()
                : videos;

            if (pool.Count == 0)
                pool = videos;

            float total = pool.Sum(v => v.Weight);
            Debug.Log($"[CrossPromoModule] SelectWeightedRandom: pool={pool.Count}/{videos.Count}, excludeTitle='{exclude?.Title}', total={total:F4}, weights=[{string.Join(",", pool.Select(v => $"{v.Title}:{v.Weight:F3}"))}]");

            if (total <= 0)
            {
                Debug.LogWarning($"[CrossPromoModule] SelectWeightedRandom: total weight <= 0, falling back to pool[0]='{pool[0].Title}'");
                return pool[0];
            }

            float random = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var video in pool)
            {
                cumulative += video.Weight;
                if (random < cumulative)
                {
                    Debug.Log($"[CrossPromoModule] SelectWeightedRandom: random={random:F4} → picked '{video.Title}'");
                    return video;
                }
            }

            Debug.Log($"[CrossPromoModule] SelectWeightedRandom: random={random:F4} → fallthrough, picked '{pool[pool.Count - 1].Title}'");
            return pool[pool.Count - 1];
        }

        #endregion

        public override void Cleanup()
        {
        }
    }
}
#endif
