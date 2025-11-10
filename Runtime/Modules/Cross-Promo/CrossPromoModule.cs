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
        
        private string _configUrl = "https://pub-bbc57aaaa559422daa4079987645f56e.r2.dev/test.json";
        private string _maxSDKKey;
        private string _interstitialAdID;
        private string _rewardedAdID;
        private PromosConfigurationInfo _crossPromoConfigAll;
        private PromosConfigurationInfo _crossPromoConfigNotWatchedYet;

        //banner submodule
        public Action OnClose;
        public Func<bool> IsNoAds;

        //general
        public bool IsAdsReady => CrossPromoManager.Instance.IsReady || MaxMediation.Instance.IsReady;

        public void Construct(
            bool enable, 
            string configUrl, 
            string maxSDKKey, 
            string interstitialAdID, 
            string rewardedAdID)
        {
            Enabled = enable;
            _configUrl = configUrl;
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

            if (IsAllVideosWatched())
            {
                IEnumerator InitVideoBanner()
                {
                    yield return _videoManager.Initialize(_crossPromoConfigAll);
                    yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);
                }
                MaxMediation.Instance.Initialize(_maxSDKKey, _interstitialAdID, _rewardedAdID, () => StartCoroutine(InitVideoBanner()));
            }
            else
            {
                yield return _videoManager.Initialize(_crossPromoConfigAll);
                yield return _crossPromoBanner.Initialize(_crossPromoConfigAll);
                MaxMediation.Instance.Initialize(_maxSDKKey, _interstitialAdID, _rewardedAdID);
            }

            _crossPromoBanner.OnClose = OnClose;
            _crossPromoBanner.IsNoAds = IsNoAds;
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

        public void ShowInterstitial() => 
            ShowAd();

        public void ShowRewarded(Action getReward) => 
            ShowAd(getReward);

        private void ShowAd(Action getReward = null)
        {
            CrossPromoManager.Instance.Show(_crossPromoConfigAll, _crossPromoConfigNotWatchedYet, getReward);

            //if (IsAdsReady())
            //{
            //    Debug.Log("Pyro show ad");
            //}
            //else
            //{
            //    //CrossPromoNotReadyCallback();
            //}
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

