using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static Pyro.CrossPromoConfigurationManager;

public class CrossPromoBanner : MonoBehaviour
{
    List<bannerData> bannerDataList = new List<bannerData>();
    [SerializeField] Image adImage;
    [SerializeField] GameObject bannerGO;

    int gifIteratorCur;
    int gifIteratorNext;

    Action OnClose;
    Func<bool> IsNoAds;

    public IEnumerator Initialize(PromosConfigurationInfo _crossPromoConfigAll)
    {
        List<string> bannersUrl = new List<string>();
        List<string> trackingList = new List<string>();
        List<string> redirectList = new List<string>();
        _crossPromoConfigAll.Videos.ForEach(v =>
        {
            bannersUrl.Add(v.BannerUrl);
            trackingList.Add(v.TrackingUrl);
            redirectList.Add(v.RedirectUrl);
        });

        yield return StartCoroutine(DownloadBanners(bannersUrl, trackingList, redirectList));

        UpdateBannerUI();
        StartCoroutine(GifCor());
    }

    IEnumerator GifCor()
    {
        while (true)
        {
            ShowBanner();
            yield return new WaitForSecondsRealtime(8f);
        }
    }

    private void ShowBanner()
    {
        adImage.sprite = bannerDataList[gifIteratorNext].sprite;

        gifIteratorCur++;
        if (gifIteratorCur > bannerDataList.Count - 1) gifIteratorCur = 0;

        gifIteratorNext = gifIteratorCur + 1;
        if (gifIteratorNext > bannerDataList.Count - 1) gifIteratorNext = 0;
    }

    #region DownloadBanners
    IEnumerator DownloadBanners(List<string> bannerUrlList, List<string> trackingList, List<string> redirectList)
    {
        Debug.Log("DownloadBanners");
        if (bannerUrlList.Count == 0)
        {
            Debug.LogError("banners urlList is empty");
            yield break;
        }
        for (int i = 0; i < bannerUrlList.Count; i++)
        {
            string gifName = $"bannerGif_{i}.mp4";
            yield return StartCoroutine(DownloadBanner_Sprite(gifName, bannerUrlList[i], redirectList[i], trackingList[i]));
        }
    }
    IEnumerator DownloadBanner_Sprite(string gifname, string url, string redirectUrl, string trackingUrl)
    {
        Debug.Log("DownloadBanner");
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            bannerDataList.Add(new bannerData(gifname, sprite, redirectUrl, trackingUrl));
        }
        else Debug.Log(request.error);
    }
    #endregion

    #region UIFuncs
    public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
    {
        OnClose = onClose;
        IsNoAds = isNoAds;
    }
    public void OnBannerClick()
    {
        string tracking = bannerDataList[gifIteratorCur].trackingUrl;
        string redirect = bannerDataList[gifIteratorCur].redirectUrl;
        string videoname = bannerDataList[gifIteratorCur].title;
        TrackingManager.UrlTracking(redirect, tracking, videoname, "banner");
    }
    public void CloseButton()
    {
        //IAPManager.Instance.PurchaseSubscription();
    }
    public void hide() => bannerGO.SetActive(false);
    public void UpdateBannerUI()
    {
        bannerGO.SetActive(!IsNoAds());
    }
    #endregion
}
class bannerData
{
    public string title;
    public Sprite sprite;
    public string redirectUrl;
    public string trackingUrl;

    public bannerData(string title, Sprite sprite, string redirectUrl, string trackingUrl)
    {
        this.title = title;
        this.sprite = sprite;
        this.redirectUrl = redirectUrl;
        this.trackingUrl = trackingUrl;
    }
}