using System;
using UnityEngine;
using AppodealStack.Monetization.Api;
using AppodealStack.Monetization.Common;

public class AppodealAdapter : MonoBehaviour, IInterstitialAdListener, IRewardedVideoAdListener
{
    private Action _onRewardedCallback;

    public void Initialize(string key)
    {
        // Настройка типов рекламы
        int adTypes = AppodealAdType.Interstitial | AppodealAdType.RewardedVideo;

        // Установка callback'ов
        Appodeal.SetInterstitialCallbacks(this);
        Appodeal.SetRewardedVideoCallbacks(this);

        // Инициализация Appodeal
        Appodeal.Initialize(key, adTypes);

        // Включение автокэширования
        Appodeal.SetAutoCache(AppodealAdType.Interstitial, true);
        Appodeal.SetAutoCache(AppodealAdType.RewardedVideo, true);

        AppodealCallbacks.RewardedVideo.OnFinished += OnRewardedVideoFinishedEvent;
        AppodealCallbacks.RewardedVideo.OnClosed += OnRewardedVideoClosedEvent;

        Debug.Log("[AppodealAdapter] Initialized with key: " + key);
    }

    private void OnDestroy()
    {
        AppodealCallbacks.RewardedVideo.OnFinished -= OnRewardedVideoFinishedEvent;
        AppodealCallbacks.RewardedVideo.OnClosed -= OnRewardedVideoClosedEvent;
    }

    public void Show_Interstitial()
    {
        if (Appodeal.IsLoaded(AppodealAdType.Interstitial) && Appodeal.CanShow(AppodealAdType.Interstitial))
        {
            Appodeal.Show(AppodealShowStyle.Interstitial);
            Debug.Log("[AppodealAdapter] Showing interstitial");
        }
        else
        {
            Debug.LogWarning("[AppodealAdapter] Interstitial not ready, caching...");
            Appodeal.Cache(AppodealAdType.Interstitial);
        }
    }

    public void Show_Rewarded(Action getReward)
    {
        _onRewardedCallback = getReward;

        if (Appodeal.IsLoaded(AppodealAdType.RewardedVideo) && Appodeal.CanShow(AppodealAdType.RewardedVideo))
        {
            Appodeal.Show(AppodealShowStyle.RewardedVideo);
            Debug.Log("[AppodealAdapter] Showing rewarded video");
        }
        else
        {
            Debug.LogWarning("[AppodealAdapter] Rewarded video not ready, caching...");
            Appodeal.Cache(AppodealAdType.RewardedVideo);
        }
    }

    public bool IsReady()
    {
        return Appodeal.IsLoaded(AppodealAdType.Interstitial) || Appodeal.IsLoaded(AppodealAdType.RewardedVideo);
    }

    // IInterstitialAdListener implementation
    public void onInterstitialLoaded(bool isPrecache) { Debug.Log("[AppodealAdapter] Interstitial loaded"); }
    public void onInterstitialFailedToLoad() { Debug.LogWarning("[AppodealAdapter] Interstitial failed to load"); }
    public void onInterstitialShowFailed() { Debug.LogWarning("[AppodealAdapter] Interstitial show failed"); }
    public void onInterstitialShown() { Debug.Log("[AppodealAdapter] Interstitial shown"); }
    public void onInterstitialClosed() { Debug.Log("[AppodealAdapter] Interstitial closed"); }
    public void onInterstitialClicked() { Debug.Log("[AppodealAdapter] Interstitial clicked"); }
    public void onInterstitialExpired() { Debug.Log("[AppodealAdapter] Interstitial expired"); }

    // IRewardedVideoAdListener implementation
    public void onRewardedVideoLoaded(bool isPrecache) { Debug.Log("[AppodealAdapter] Rewarded video loaded"); }
    public void onRewardedVideoFailedToLoad() { Debug.LogWarning("[AppodealAdapter] Rewarded video failed to load"); }
    public void onRewardedVideoShowFailed() { Debug.LogWarning("[AppodealAdapter] Rewarded video show failed"); }
    public void onRewardedVideoShown() { Debug.Log("[AppodealAdapter] Rewarded video shown"); }
    public void onRewardedVideoFinished(double amount, string currency)
    {
        Debug.Log("[AppodealAdapter] Rewarded video finished, calling callback");
        InvokeRewardedCallback();
    }
    public void onRewardedVideoClosed(bool finished)
    {
        Debug.Log("[AppodealAdapter] Rewarded video closed");
        if (finished)
        {
            InvokeRewardedCallback();
        }
    }
    public void onRewardedVideoExpired() { Debug.Log("[AppodealAdapter] Rewarded video expired"); }
    public void onRewardedVideoClicked() { Debug.Log("[AppodealAdapter] Rewarded video clicked"); }
    public void OnInterstitialLoaded(bool isPrecache) { Debug.Log("[AppodealAdapter] Interstitial loaded (new callback)"); }
    public void OnInterstitialFailedToLoad() { Debug.LogWarning("[AppodealAdapter] Interstitial failed to load (new callback)"); }
    public void OnInterstitialShowFailed() { Debug.LogWarning("[AppodealAdapter] Interstitial show failed (new callback)"); }
    public void OnInterstitialShown() { Debug.Log("[AppodealAdapter] Interstitial shown (new callback)"); }
    public void OnInterstitialClosed() { Debug.Log("[AppodealAdapter] Interstitial closed (new callback)"); }
    public void OnInterstitialClicked() { Debug.Log("[AppodealAdapter] Interstitial clicked (new callback)"); }
    public void OnInterstitialExpired() { Debug.Log("[AppodealAdapter] Interstitial expired (new callback)"); }

    public void OnRewardedVideoLoaded(bool isPrecache) { Debug.Log("[AppodealAdapter] Rewarded loaded (new callback)"); }
    public void OnRewardedVideoFailedToLoad() { Debug.LogWarning("[AppodealAdapter] Rewarded failed to load (new callback)"); }
    public void OnRewardedVideoShowFailed() { Debug.LogWarning("[AppodealAdapter] Rewarded show failed (new callback)"); }
    public void OnRewardedVideoShown() { Debug.Log("[AppodealAdapter] Rewarded shown (new callback)"); }
    public void OnRewardedVideoFinished(double amount, string currency)
    {
        Debug.Log("[AppodealAdapter] Rewarded finished (new callback)");
        InvokeRewardedCallback();
    }
    public void OnRewardedVideoClosed(bool finished)
    {
        Debug.Log("[AppodealAdapter] Rewarded closed (new callback)");
        if (finished)
        {
            InvokeRewardedCallback();
        }
    }
    public void OnRewardedVideoExpired() { Debug.Log("[AppodealAdapter] Rewarded expired (new callback)"); }
    public void OnRewardedVideoClicked() { Debug.Log("[AppodealAdapter] Rewarded clicked (new callback)"); }

    private void OnRewardedVideoFinishedEvent(object sender, RewardedVideoFinishedEventArgs args)
    {
        Debug.Log("[AppodealAdapter] Rewarded finished event");
        InvokeRewardedCallback();
    }

    private void OnRewardedVideoClosedEvent(object sender, RewardedVideoClosedEventArgs args)
    {
        Debug.Log("[AppodealAdapter] Rewarded closed event finished=" + args.Finished);
        if (args.Finished)
        {
            InvokeRewardedCallback();
        }
    }

    private void InvokeRewardedCallback()
    {
        _onRewardedCallback?.Invoke();
        _onRewardedCallback = null;
    }
}
