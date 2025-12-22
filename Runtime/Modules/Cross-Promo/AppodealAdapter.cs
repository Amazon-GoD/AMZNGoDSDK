using System;
using UnityEngine;
using UnityEngine.Events;
//using AppodealStack.Monetization.Api;
//using AppodealStack.Monetization.Common;

public class AppodealAdapter : MonoBehaviour
{
    UnityAction onClose;

    public void Initialize(string key)
    {
        //int adTypes = AppodealAdType.Interstitial | AppodealAdType.Banner | AppodealAdType.RewardedVideo | AppodealAdType.Mrec;
        //string appKey = Key;
        //AppodealCallbacks.Sdk.OnInitialized += OnInitializationFinished;
        //Appodeal.Initialize(appKey, adTypes);

        //AppodealCallbacks.RewardedVideo.OnFinished += OnRewardedVideoClosed;
    }

    //private void OnRewardedVideoClosed(object sender, RewardedVideoFinishedEventArgs e)
    //{
    //    onClose.Invoke();
    //    onClose = null;
    //}

    //public void OnInitializationFinished(object sender, SdkInitializedEventArgs e) { }

    public void Show_Interstitial()
    {
        //if (Appodeal.IsLoaded(AppodealAdType.Interstitial))
        //{
        //    Appodeal.Show(AppodealShowStyle.Interstitial);
        //}
    }

    public void Show_Rewarded(Action getReward)
    {
        //if (Appodeal.IsLoaded(AppodealAdType.RewardedVideo))
        //{
        //    onClose = getReward;
        //    Appodeal.Show(AppodealShowStyle.RewardedVideo);
        //}
    }

    public bool IsReady()
    {
        return false;//Appodeal.IsLoaded(AppodealAdType.Interstitial) && Appodeal.IsLoaded(AppodealAdType.RewardedVideo);
    }
}
