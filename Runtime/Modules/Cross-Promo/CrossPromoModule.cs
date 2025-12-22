using Pyro;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using static Pyro.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime {
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

        //general
        public bool IsAdsReady => CrossPromoManager.Instance.IsReady || MaxMediation.Instance.IsReady;

        enum VideoAdType { CrossPromo, Appodeal };
        VideoAdType videoAdType = VideoAdType.Appodeal;

        public void Construct(
            bool enable, 
            string configUrl, 
            string appodealSDKKey,
            string maxSDKKey, 
            string interstitialAdID, 
            string rewardedAdID)
        {
            Enabled = enable;
            _configUrl = configUrl;
            _appodealSDKKey = appodealSDKKey;
            _maxSDKKey = maxSDKKey;
            _interstitialAdID = interstitialAdID;
            _rewardedAdID = rewardedAdID;
        }

        public override void Initialize() => 
            StartCoroutine(InitCrossPromoModulesCorAsync());

        #region ModuleInitializer

        public IEnumerator InitCrossPromoModulesCorAsync()
        {
            yield return LoadJson();

            switch (videoAdType)
            {
                case VideoAdType.CrossPromo:
                    yield return _videoManager.Initialize(_crossPromoConfigAll);
                    break;
                case VideoAdType.Appodeal:
                    _appodealAdapter.Initialize(_appodealSDKKey);
                    break;
            }

            yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);

            //if (IsAllVideosWatched())
            //{
            //    IEnumerator InitVideoBanner()
            //    {
            //        if (videoAdType == VideoAdType.CrossPromo) yield return _videoManager.Initialize(_crossPromoConfigAll);
            //        yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);
            //    }
            //    MaxMediation.Instance.Initialize(_maxSDKKey, _interstitialAdID, _rewardedAdID, () => StartCoroutine(InitVideoBanner()));
            //}
            //else
            //{
            //    if (videoAdType == VideoAdType.CrossPromo) yield return _videoManager.Initialize(_crossPromoConfigAll);
            //    yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);
            //    MaxMediation.Instance.Initialize(_maxSDKKey, _interstitialAdID, _rewardedAdID);
            //}
        }

        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            _crossPromoBanner.SetBannerFuncs(onClose, isNoAds);
        }

        private bool IsAllVideosWatched()
        {
            foreach (var videoInfo in _crossPromoConfigNotWatchedYet.Videos)
            {
                int videoShowCount = PlayerPrefs.GetInt(videoInfo.Title, 0);
                if (videoShowCount < videoInfo.MaxShowCount) return false;
            }
            return true;
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
            switch (videoAdType)
            {
                case VideoAdType.CrossPromo:
                    CrossPromoManager.Instance.Show(_crossPromoConfigAll, _crossPromoConfigNotWatchedYet);
                    break;
                case VideoAdType.Appodeal:
                    _appodealAdapter.Show_Interstitial();
                    break;
            }
        }

        public void ShowRewarded(Action getReward)
        {
            switch (videoAdType)
            {
                case VideoAdType.CrossPromo:
                    CrossPromoManager.Instance.Show(_crossPromoConfigAll, _crossPromoConfigNotWatchedYet, getReward);
                    break;
                case VideoAdType.Appodeal:
                    _appodealAdapter.Show_Rewarded(getReward);
                    break;
            }
        }

        #endregion

        public override void Cleenup() { }
        
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

