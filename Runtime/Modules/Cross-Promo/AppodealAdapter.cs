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

        Debug.Log("[AppodealAdapter] Initialized with key: " + key);
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
    public void onInterstitialLoaded(bool isPrecache) { }
    public void onInterstitialFailedToLoad() { }
    public void onInterstitialShowFailed() { }
    public void onInterstitialShown() { }
    public void onInterstitialClosed() { }
    public void onInterstitialClicked() { }
    public void onInterstitialExpired() { }

    // IRewardedVideoAdListener implementation
    public void onRewardedVideoLoaded(bool isPrecache) { }
    public void onRewardedVideoFailedToLoad() { }
    public void onRewardedVideoShowFailed() { }
    public void onRewardedVideoShown() { }
    public void onRewardedVideoFinished(double amount, string currency)
    {
        Debug.Log("[AppodealAdapter] Rewarded video finished, calling callback");
        _onRewardedCallback?.Invoke();
        _onRewardedCallback = null;
    }
    public void onRewardedVideoClosed(bool finished) { }
    public void onRewardedVideoExpired() { }
    public void onRewardedVideoClicked() { }
    public void OnInterstitialLoaded(bool isPrecache)
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialFailedToLoad()
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialShowFailed()
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialShown()
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialClosed()
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialClicked()
    {
        throw new NotImplementedException();
    }

    public void OnInterstitialExpired()
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoLoaded(bool isPrecache)
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoFailedToLoad()
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoShowFailed()
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoShown()
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoFinished(double amount, string currency)
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoClosed(bool finished)
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoExpired()
    {
        throw new NotImplementedException();
    }

    public void OnRewardedVideoClicked()
    {
        throw new NotImplementedException();
    }
}
