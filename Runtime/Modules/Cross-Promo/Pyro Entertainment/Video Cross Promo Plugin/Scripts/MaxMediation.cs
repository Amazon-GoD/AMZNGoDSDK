using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class MaxMediation : MonoBehaviour
{
#pragma warning disable CS0618 // Тип или член устарел
    public static MaxMediation Instance;
    public bool IsReady = true;
    string adUnitIdInterstitial;
    string adUnitIdRewarded;

    private void Awake()
    {
        Instance = this;
    }

    #region Initialize
    public void Initialize(string key, string InterstitialAdID, string RewardedAdID, Action AfterSDKInitialized = null)
    {
        adUnitIdInterstitial = InterstitialAdID;
        adUnitIdRewarded = RewardedAdID;
        MaxSdkCallbacks.OnSdkInitializedEvent += (configuration) => { SDKInitted(AfterSDKInitialized);};
        
        MaxSdk.SetSdkKey(key);
        MaxSdk.InitializeSdk();

        InitializeInterstitialAds();
        InitializeRewardedAds();

        // we will need wait 10 sec or init finish
        StartCoroutine(Timer(10, () => { SDKInitted(AfterSDKInitialized); }));
    }
    private IEnumerator Timer(float seconds, Action afterTimer)
    {
        yield return new WaitForSecondsRealtime(seconds);
        afterTimer.Invoke();
    }
    private void SDKInitted(Action AfterSDKInitialized = null)
    {
        if (IsReady == false)
        {
            Debug.Log("MaxSDKInitialized");
            AfterSDKInitialized?.Invoke();
            IsReady = true;
        }
    }
    #endregion


    Action RewardedAction;
    public void ShowAd(Action rewardedAction = null)
    {
        Debug.Log("show max mediation");
        if (rewardedAction != null)
        {
            ShowRewarded();
            RewardedAction = rewardedAction;
        }
        else ShowInterstitial();
    }
    #region Interstitial
    int retryAttempt;
    private void ShowInterstitial()
    {
        if (MaxSdk.IsInterstitialReady(adUnitIdInterstitial))
        {
            MaxSdk.ShowInterstitial(adUnitIdInterstitial);
        }
        else Debug.Log("inter ad not ready");
    }
    public void InitializeInterstitialAds()
    {
        // Attach callback
        MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
        MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
        MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
        MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClickedEvent;
        MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
        MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;

        // Load the first interstitial
        LoadInterstitial();
    }

    private void LoadInterstitial()
    {
        MaxSdk.LoadInterstitial(adUnitIdInterstitial);
    }

    private void OnInterstitialLoadedEvent(string adUnitId, MaxSdk.AdInfo adInfo)
    {
        // Interstitial ad is ready for you to show. MaxSdk.IsInterstitialReady(adUnitId) now returns 'true'

        // Reset retry attempt
        retryAttempt = 0;
    }
    private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo)
    {
        // Interstitial ad failed to load
        // AppLovin recommends that you retry with exponentially higher delays, up to a maximum delay (in this case 64 seconds)
        Debug.Log("Applovin interstitial ad load failed: " + errorInfo.Message);
        retryAttempt++;
        double retryDelay = Math.Pow(2, Math.Min(6, retryAttempt));

        Invoke("LoadInterstitial", (float)retryDelay);
    }
    private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }
    private void OnInterstitialAdFailedToDisplayEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
    {
        // Interstitial ad failed to display. AppLovin recommends that you load the next ad.
        Debug.Log("Applovin interstitial ad show failed: " + errorInfo.Message);
        LoadInterstitial();
    }
    private void OnInterstitialClickedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }
    private void OnInterstitialHiddenEvent(string adUnitId, MaxSdk.AdInfo adInfo)
    {
        // Interstitial ad is hidden. Pre-load the next ad.
        LoadInterstitial();
    }
    #endregion
    #region Rewarded
    int retryAttemptRewarded;
    private void ShowRewarded()
    {
        if (MaxSdk.IsRewardedAdReady(adUnitIdRewarded))
        {
            MaxSdk.ShowRewardedAd(adUnitIdRewarded);
        }
        else Debug.Log("rewarded ad not ready");
    }
    public void InitializeRewardedAds()
    {
        // Attach callback
        MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
        MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
        MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;
        MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedAdClickedEvent;
        MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
        MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
        MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
        MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;

        // Load the first rewarded ad
        LoadRewardedAd();
    }

    private void LoadRewardedAd()
    {
        MaxSdk.LoadRewardedAd(adUnitIdRewarded);
    }

    private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdk.AdInfo adInfo)
    {
        // Rewarded ad is ready for you to show. MaxSdk.IsRewardedAdReady(adUnitId) now returns 'true'.

        // Reset retry attempt
        retryAttemptRewarded = 0;
    }

    private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo)
    {
        // Rewarded ad failed to load
        // AppLovin recommends that you retry with exponentially higher delays, up to a maximum delay (in this case 64 seconds).
        Debug.Log("Applovin rewarded ad load failed: " + errorInfo.Message);
        retryAttemptRewarded++;
        double retryDelay = Math.Pow(2, Math.Min(6, retryAttemptRewarded));

        Invoke("LoadRewardedAd", (float)retryDelay);
    }

    private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }

    private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdk.ErrorInfo errorInfo, MaxSdk.AdInfo adInfo)
    {
        // Rewarded ad failed to display. AppLovin recommends that you load the next ad.
        Debug.Log("Applovin rewarded ad show failed: " + errorInfo.Message);
        LoadRewardedAd();
    }

    private void OnRewardedAdClickedEvent(string adUnitId, MaxSdk.AdInfo adInfo) { }

    private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdk.AdInfo adInfo)
    {
        // Rewarded ad is hidden. Pre-load the next ad
        LoadRewardedAd();
    }

    private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdk.AdInfo adInfo)
    {
        // The rewarded ad displayed and the user should receive the reward.
        RewardedAction?.Invoke();
        RewardedAction = null;
    }

    private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdk.AdInfo adInfo)
    {
        // Ad revenue paid. Use this callback to track user revenue.
    }
    #endregion
}
