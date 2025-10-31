using UnityEngine;
using System.Collections;
using System;

public class AppMetricaImpressionSender : MonoBehaviour
{
    public static AppMetricaImpressionSender Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void TryOpenUrl(string url, Action OpenRedirecUrl, string videoName, string placement) => OpenUrl(url, OpenRedirecUrl, videoName, placement);
    private void OpenUrl(string url, Action OpenRedirecUrl, string videoName, string placement)
    {
        var webViewGameObject = new GameObject("UniWebView");
        var webView = webViewGameObject.AddComponent<UniWebView>();

        Action loaded = () =>
        {
            if (webView == null) return;
            Debug.Log("loaded invoke");
            Destroy(webView.gameObject);
            webView = null;
            OpenRedirecUrl.Invoke();
        };

        webView.OnPageStarted += (view, url) => StartCoroutine(urlLoadingTimer(loaded, 2f));
        webView.OnPageFinished += (view, statusCode, url) => loaded.Invoke();
        webView.OnLoadingErrorReceived += (view, errorCode, errorMessage, payload) => loaded.Invoke();

        webView.Frame = new Rect(0, 0, 1, 1);
        webView.Alpha = 0f;

        string newUrl = AddUTM(url, videoName, placement);
        Debug.Log("newUrl: " + newUrl);
        webView.Load(url);
        webView.Show();
    }
    private IEnumerator urlLoadingTimer(Action loaded, float time)
    {
        Debug.Log("urlLoadingTimer");
        yield return new WaitForSecondsRealtime(time);
        loaded.Invoke();
    }
    private string AddUTM(string url, string videoName, string placement)
    {
        var source = $"&utm_source={Application.identifier}";
        var video = $"&utm_medium={videoName}";
        var pl = $"&utm_campaign={placement}";
        string utm = source + video + pl;
        Debug.Log("UTM generated: " + utm);
        return url + utm;
    }
}
