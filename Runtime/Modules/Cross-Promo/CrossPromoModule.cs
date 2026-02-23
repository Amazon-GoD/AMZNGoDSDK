#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
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
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Construct(
            bool enable,
            string configUrl,
            string appodealSDKKey)
        {
            Enabled = enable;
            _configUrl = configUrl;
            _appodealSDKKey = appodealSDKKey;

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

        public override void Initialize() =>
            StartCoroutine(InitCrossPromoModulesCorAsync());

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

        public override void Cleenup() { }
    }
}
#endif
