using Pyro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using static Pyro.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoModule : ModuleBase
    {
#pragma warning disable CS4014 //for InitCrossPromoModulesCorAsync

        //Other classes
        [SerializeField] private CrossPromoVideoManager _videoManager;
        [FormerlySerializedAs("_pyroBanner")] [SerializeField] private CrossPromoBanner _crossPromoBanner;
        [SerializeField] private CrossPromoConfigurationManager _configurationManager;
        [SerializeField] private AppodealAdapter _appodealAdapter;
        
        private string _configUrl;
        private string _appodealSDKKey;
        private string _maxSDKKey;
        private string _interstitialAdID;
        private string _rewardedAdID;
        private PromosConfigurationInfo _crossPromoConfigAll;
        private PromosConfigurationInfo _crossPromoConfigNotWatchedYet;

        private CrossPromoProviderType _selectedProviderType = CrossPromoProviderType.Appodeal;
        private readonly List<ICrossPromoProvider> _providers = new();
        private ICrossPromoProvider _activeProvider;

        //general
        public bool IsAdsReady => (CurrentProvider?.IsReady ?? false) || MaxMediation.Instance.IsReady;

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
            SetupProviders();
        }

        public override void Initialize() => 
            StartCoroutine(InitCrossPromoModulesCorAsync());

        #region ModuleInitializer

        public IEnumerator InitCrossPromoModulesCorAsync()
        {
            yield return LoadJson();

            if (CurrentProvider != null)
            {
                yield return CurrentProvider.Initialize(_crossPromoConfigAll, _crossPromoConfigNotWatchedYet);
            }

            yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);
        }

        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            _crossPromoBanner.SetBannerFuncs(onClose, isNoAds);
        }

        private IEnumerator LoadJson()
        {
            var operation = _configurationManager.FetchRemoteConfigAsync(_configUrl);
            
            while (!operation.IsCompleted) yield return null;

            if (operation.IsCompletedSuccessfully)
            {
                _crossPromoConfigAll = operation.Result;
                _crossPromoConfigNotWatchedYet = _crossPromoConfigAll.Copy();
            }
            else Debug.Log(operation.Exception.Message);
        }

        #endregion

        #region ShowAd

        public void ShowInterstitial()
        {
            CurrentProvider?.ShowInterstitial();
        }

        public void ShowRewarded(Action getReward)
        {
            CurrentProvider?.ShowRewarded(getReward);
        }

        #endregion

        public override void Cleenup() { }
        
        #region Internal helpers

        private ICrossPromoProvider CurrentProvider
        {
            get
            {
                if (_activeProvider != null)
                    return _activeProvider;

                _activeProvider = GetProvider(_selectedProviderType) ?? (_providers.Count > 0 ? _providers[0] : null);
                return _activeProvider;
            }
        }

        private void SetupProviders()
        {
            _providers.Clear();
            _providers.Add(new PyroCrossPromoProvider(_videoManager));
            _providers.Add(new AppodealCrossPromoProvider(_appodealAdapter, _appodealSDKKey));
            _activeProvider = null;
        }

        private ICrossPromoProvider GetProvider(CrossPromoProviderType providerType)
        {
            foreach (var provider in _providers)
            {
                if (provider.ProviderType == providerType)
                    return provider;
            }

            return null;
        }

        private interface ICrossPromoProvider
        {
            CrossPromoProviderType ProviderType { get; }
            IEnumerator Initialize(PromosConfigurationInfo configAll, PromosConfigurationInfo configNotWatched);
            bool IsReady { get; }
            void ShowInterstitial();
            void ShowRewarded(Action getReward);
        }

        private class PyroCrossPromoProvider : ICrossPromoProvider
        {
            private readonly CrossPromoVideoManager _videoManager;
            private PromosConfigurationInfo _configAll;
            private PromosConfigurationInfo _configNotWatched;

            public CrossPromoProviderType ProviderType => CrossPromoProviderType.Pyro;
            public bool IsReady => CrossPromoManager.Instance?.IsReady ?? false;

            public PyroCrossPromoProvider(CrossPromoVideoManager videoManager)
            {
                _videoManager = videoManager;
            }

            public IEnumerator Initialize(PromosConfigurationInfo configAll, PromosConfigurationInfo configNotWatched)
            {
                _configAll = configAll;
                _configNotWatched = configNotWatched;

                if (_videoManager == null || _configAll == null)
                    yield break;

                yield return _videoManager.Initialize(_configAll);
            }

            public void ShowInterstitial()
            {
                if (_configAll == null || _configNotWatched == null)
                    return;

                CrossPromoManager.Instance?.Show(_configAll, _configNotWatched);
            }

            public void ShowRewarded(Action getReward)
            {
                if (_configAll == null || _configNotWatched == null)
                    return;

                CrossPromoManager.Instance?.Show(_configAll, _configNotWatched, getReward);
            }
        }

        private class AppodealCrossPromoProvider : ICrossPromoProvider
        {
            private readonly AppodealAdapter _adapter;
            private readonly string _sdkKey;

            public CrossPromoProviderType ProviderType => CrossPromoProviderType.Appodeal;
            public bool IsReady => _adapter?.IsReady() ?? false;

            public AppodealCrossPromoProvider(AppodealAdapter adapter, string sdkKey)
            {
                _adapter = adapter;
                _sdkKey = sdkKey;
            }

            public IEnumerator Initialize(PromosConfigurationInfo configAll, PromosConfigurationInfo configNotWatched)
            {
                _adapter?.Initialize(_sdkKey);
                yield break;
            }

            public void ShowInterstitial()
            {
                _adapter?.Show_Interstitial();
            }

            public void ShowRewarded(Action getReward)
            {
                _adapter?.Show_Rewarded(getReward);
            }
        }

        #endregion
        
        #region Debug

#if UNITY_EDITOR
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Q)) ShowInterstitial();
        }
#endif

        #endregion
    }
}

