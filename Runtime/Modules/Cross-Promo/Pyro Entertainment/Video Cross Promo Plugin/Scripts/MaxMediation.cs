using System;
using System.Collections;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class MaxMediation : MonoBehaviour
    {
        #pragma warning disable CS0618
        public static MaxMediation Instance;
        public bool IsReady { get; private set; }
        private string adUnitIdInterstitial;
        private string adUnitIdRewarded;

        private void Awake()
        {
            Instance = this;
            IsReady = false;
        }

        #region Initialize
        public void Initialize(string key, string interstitialAdID, string rewardedAdID, Action afterSdkInitialized = null)
        {
            adUnitIdInterstitial = interstitialAdID;
            adUnitIdRewarded = rewardedAdID;
            MaxSdkCallbacks.OnSdkInitializedEvent += (configuration) => { SdkInitted(afterSdkInitialized); };

            MaxSdk.SetSdkKey(key);
            MaxSdk.InitializeSdk();

            InitializeInterstitialAds();
            InitializeRewardedAds();

            StartCoroutine(Timer(10, () => { SdkInitted(afterSdkInitialized); }));
        }

        private IEnumerator Timer(float seconds, Action afterTimer)
        {
            yield return new WaitForSecondsRealtime(seconds);
            afterTimer?.Invoke();
        }

        private void SdkInitted(Action afterSdkInitialized = null)
        {
            if (!IsReady)
            {
                Debug.Log("MaxSDKInitialized");
                IsReady = true;
                afterSdkInitialized?.Invoke();
            }
        }
        #endregion

        private Action RewardedAction;

        public void ShowAd(Action rewardedAction = null)
        {
            Debug.Log("show max mediation");
            if (rewardedAction != null)
            {
                ShowRewarded();
                RewardedAction = rewardedAction;
            }
            else
            {
                ShowInterstitial();
            }
        }

        #region Interstitial
        private int retryAttempt;

        private void ShowInterstitial()
        {
            if (MaxSdk.IsInterstitialReady(adUnitIdInterstitial))
            {
                MaxSdk.ShowInterstitial(adUnitIdInterstitial);
            }
            else
            {
                Debug.Log("inter ad not ready");
            }
        }

        public void InitializeInterstitialAds()
        {
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClickedEvent;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;

            LoadInterstitial();
        }

        private void LoadInterstitial()
        {
            MaxSdk.LoadInterstitial(adUnitIdInterstitial);
        }

        private void OnInterstitialLoadedEvent(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            retryAttempt = 0;
        }

        private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            Debug.Log("Applovin interstitial ad load failed: " + errorInfo.Message);
            retryAttempt++;
            double retryDelay = Math.Pow(2, Math.Min(6, retryAttempt));

            Invoke("LoadInterstitial", (float)retryDelay);
        }

        private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }
        private void OnInterstitialAdFailedToDisplayEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
        {
            Debug.Log("Applovin interstitial ad show failed: " + errorInfo.Message);
            LoadInterstitial();
        }

        private void OnInterstitialClickedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }

        private void OnInterstitialHiddenEvent(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            LoadInterstitial();
        }
        #endregion

        #region Rewarded
        private int retryAttemptRewarded;

        private void ShowRewarded()
        {
            if (MaxSdk.IsRewardedAdReady(adUnitIdRewarded))
            {
                MaxSdk.ShowRewardedAd(adUnitIdRewarded);
            }
            else
            {
                Debug.Log("rewarded ad not ready");
            }
        }

        public void InitializeRewardedAds()
        {
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedAdClickedEvent;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;

            LoadRewardedAd();
        }

        private void LoadRewardedAd()
        {
            MaxSdk.LoadRewardedAd(adUnitIdRewarded);
        }

        private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            retryAttemptRewarded = 0;
        }

        private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo)
        {
            Debug.Log("Applovin rewarded ad load failed: " + errorInfo.Message);
            retryAttemptRewarded++;
            double retryDelay = Math.Pow(2, Math.Min(6, retryAttemptRewarded));

            Invoke("LoadRewardedAd", (float)retryDelay);
        }

        private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }

        private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
        {
            Debug.Log("Applovin rewarded ad show failed: " + errorInfo.Message);
            LoadRewardedAd();
        }

        private void OnRewardedAdClickedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdk.AdInfo adInfo)
        {
            LoadRewardedAd();
        }

        private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdk.AdInfo adInfo)
        {
            RewardedAction?.Invoke();
            RewardedAction = null;
        }

        private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }
        #endregion
    }
}
