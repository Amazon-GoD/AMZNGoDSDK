using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using static Pyro.CrossPromoConfigurationManager;

public class CrossPromoBanner : MonoBehaviour
{
    List<bannerData> bannerDataList = new List<bannerData>();
    [SerializeField] Image adImage;
    [SerializeField] GameObject bannerGO;
    [SerializeField] VideoPlayer videoPlayer;
    int gifIterator = 0;
    private string curVideoName;

    public Action OnClose;
    public Func<bool> IsNoAds;

    public IEnumerator Initialize(PromosConfigurationInfo _crossPromoConfigFirst)
    {
        List<string> bannersUrl = new List<string>();
        List<string> trackingList = new List<string>();
        List<string> redirectList = new List<string>();
        _crossPromoConfigFirst.Videos.ForEach(v =>
        {
            bannersUrl.Add(v.BannerUrl);
            trackingList.Add(v.TrackingUrl);
            redirectList.Add(v.RedirectUrl);
        });

        videoPlayer.prepareCompleted += OnPrepareCompleted;
        ResizeRenderTexture();
        yield return DownloadBanners(bannersUrl, trackingList, redirectList);

        UpdateBannerUI();

        StartCoroutine(GifCor());
    }
    private IEnumerator GifCor()
    {
        while (true)
        {
            ShowBanner();
            yield return videoPlayer.isPrepared;
            yield return new WaitForSecondsRealtime(12.95f);
        }
    }
    private void ShowBanner()
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = bannerDataList[gifIterator].path;

        gifIterator++;
        if (gifIterator > bannerDataList.Count - 1) gifIterator = 0;

        videoPlayer.Prepare();
    }

    #region DownloadBanners
    private IEnumerator DownloadBanners(List<string> bannerUrlList, List<string> trackingList, List<string> redirectList)
    {
        Debug.Log("DownloadBanners");
        if (bannerUrlList.Count == 0)
        {
            Debug.LogError("banners urlList is empty");
            yield break;
        }
        for (int i = 0; i < bannerUrlList.Count; i++)
        {
            //yield return StartCoroutine(DownloadBanner(bannerUrlList[i], redirectList[i], trackingList[i]));
            string gifName = $"bannerGif_{i}.mp4";
            yield return DownloadBanner_Video(gifName, bannerUrlList[i], redirectList[0], trackingList[0]);
        }
    }
    private IEnumerator DownloadBanner_Video(string videoName, string url, string redirectUrl, string trackingUrl)
    {
        string localPath = Path.Combine(Application.persistentDataPath, videoName);

        UnityWebRequest request = UnityWebRequest.Get(url);
        var operation = request.SendWebRequest();
        Debug.Log($"Downloading {videoName} started");
        while (!operation.isDone) yield return null;

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"banner downloading process completed successfully and {videoName} saved to {localPath}, received: " + request.downloadHandler.text);
            byte[] videoData = request.downloadHandler.data;//��������������� ������� �����
            yield return File.WriteAllBytesAsync(localPath, videoData);
        }
        else Debug.LogError($"||{url}||/n Error in banner downloading process, can't download {videoName} due to {request.error}");

        bannerDataList.Add(new bannerData(videoName, localPath, redirectUrl, trackingUrl));
    }
    #endregion

    public void OnBannerClick()
    {
        string tracking = bannerDataList[gifIterator].trackingUrl;
        string redirect = bannerDataList[gifIterator].redirectUrl;
        string videoname = bannerDataList[gifIterator].title;
        TrackingManager.UrlTracking(redirect, tracking, videoname, "banner");
    }
    public void CloseButton()
    {
        //show offer/noAds buy window
        OnClose?.Invoke();
    }

    public void UpdateBannerUI()
    {
        bool noads = false;
        if (IsNoAds != null) noads = IsNoAds();
        bannerGO.SetActive(!noads);
    }
    private void ResizeRenderTexture()
    {
        RectTransform rectTransform = videoPlayer.GetComponent<RectTransform>();
        
        //int width = (int)rectTransform.rect.width;
        //int height = (int)rectTransform.rect.height;
        RenderTexture currentTexture = videoPlayer.targetTexture;

        if (currentTexture != null)
            currentTexture.Release();

        RenderTexture newTexture = new RenderTexture(640, 100, 16);
        newTexture.Create();

        videoPlayer.targetTexture = newTexture;
        videoPlayer.GetComponent<RawImage>().texture = newTexture;
    }
    private void OnPrepareCompleted(VideoPlayer source)
    {
        videoPlayer.Play();
    }
}
class bannerData
{
    public string title;
    public string path;
    public string redirectUrl;
    public string trackingUrl;

    public bannerData(string title, string path, string redirectUrl, string trackingUrl)
    {
        this.path = path;
        this.redirectUrl = redirectUrl;
        this.trackingUrl = trackingUrl;
        this.title = title;
    }
}
