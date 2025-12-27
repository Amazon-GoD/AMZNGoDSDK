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
        [SerializeField] private MaxMediation _maxMediation;

        private string _configUrl;
        private string _appodealSDKKey;
        private string _maxSDKKey;
        private string _interstitialAdID;
        private string _rewardedAdID;
        private CrossPromoProviderType _selectedProviderType;

        private bool _appLovinEnabled;
        private bool _appLovinConfigured;
        private bool _appodealInitialized;
        private PromosConfigurationInfo _crossPromoConfig;
        private Action _currentBannerOnClose;
        private Func<bool> _currentIsNoAds;

        public bool IsAdsReady =>
            _selectedProviderType == CrossPromoProviderType.Appodeal
                ? (_appodealAdapter?.IsReady() ?? false)
                : (_appLovinEnabled && (_maxMediation?.IsReady ?? false));

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
            string appodealSDKKey,
            string maxSDKKey,
            string interstitialAdID,
            string rewardedAdID,
            CrossPromoProviderType providerType)
        {
            Enabled = enable;
            _configUrl = configUrl;
            _appodealSDKKey = appodealSDKKey;
            _maxSDKKey = maxSDKKey;
            _interstitialAdID = interstitialAdID;
            _rewardedAdID = rewardedAdID;
            _selectedProviderType = providerType;

            if (!string.IsNullOrWhiteSpace(_appodealSDKKey))
            {
                _appodealAdapter?.Initialize(_appodealSDKKey);
                _appodealInitialized = true;
            }
            else
            {
                _appodealInitialized = false;
            }

            if (providerType == CrossPromoProviderType.AppLovin)
            {
                EnableAppLovin();
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

        public void EnableAppLovin(string sdkKey = null, string interstitialId = null, string rewardedId = null)
        {
            _appLovinEnabled = true;

            if (!string.IsNullOrWhiteSpace(sdkKey) && _maxSDKKey != sdkKey)
            {
                _maxSDKKey = sdkKey;
                _appLovinConfigured = false;
            }

            if (!string.IsNullOrWhiteSpace(interstitialId))
            {
                _interstitialAdID = interstitialId;
            }

            if (!string.IsNullOrWhiteSpace(rewardedId))
            {
                _rewardedAdID = rewardedId;
            }

            InitializeAppLovin();
        }

        public void ShowInterstitial()
        {
            if (!Enabled)
            {
                return;
            }

            if (_selectedProviderType == CrossPromoProviderType.Appodeal)
            {
                if (!_appodealInitialized)
                {
                    Debug.LogWarning("[CrossPromoModule] Appodeal SDK is not initialized");
                    return;
                }

                _appodealAdapter?.Show_Interstitial();
                if (_appodealAdapter?.IsReady() == true)
                    return;
                Debug.LogWarning("[CrossPromoModule] Appodeal interstitial not loaded yet, caching request");
                return;
            }

            if (!_appLovinEnabled)
            {
                EnableAppLovin();
            }

            if (_appLovinEnabled && _maxMediation?.IsReady == true)
            {
                _maxMediation.ShowAd();
                return;
            }

            Debug.LogWarning("[CrossPromoModule] AppLovin interstitial is not ready");
        }

        public void ShowRewarded(Action callback)
        {
            if (!Enabled)
            {
                return;
            }

            if (_selectedProviderType == CrossPromoProviderType.Appodeal)
            {
                if (!_appodealInitialized)
                {
                    Debug.LogWarning("[CrossPromoModule] Appodeal SDK is not initialized");
                    return;
                }

                _appodealAdapter?.Show_Rewarded(callback);
                if (_appodealAdapter?.IsReady() == true)
                    return;
                Debug.LogWarning("[CrossPromoModule] Appodeal rewarded not loaded yet, caching request");
                return;
            }

            if (!_appLovinEnabled)
            {
                EnableAppLovin();
            }

            if (_appLovinEnabled && _maxMediation?.IsReady == true)
            {
                _maxMediation.ShowAd(callback);
                return;
            }

            Debug.LogWarning("[CrossPromoModule] AppLovin rewarded is not ready");
        }

        private void InitializeAppLovin()
        {
            if (_appLovinConfigured)
            {
                return;
            }

            if (_maxMediation == null)
            {
                Debug.LogWarning("[CrossPromoModule] MaxMediation component is missing");
                return;
            }

            if (string.IsNullOrWhiteSpace(_maxSDKKey))
            {
                Debug.LogWarning("[CrossPromoModule] Max SDK key is not provided");
                return;
            }

            _maxMediation.Initialize(_maxSDKKey, _interstitialAdID, _rewardedAdID);
            _appLovinConfigured = true;
        }

        public override void Cleenup() { }
    }
}

