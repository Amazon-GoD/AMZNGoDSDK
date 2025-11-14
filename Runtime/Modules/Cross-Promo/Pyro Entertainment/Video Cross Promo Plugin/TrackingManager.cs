using System;
using System.Collections.Generic;
using UnityEngine;

public static class TrackingManager
{
    public static bool UseAdjustModule;
    public static bool UseAppMetricaModule;

    static Action trackAdjust;
    static Action trackAppmetrica;

    public static void UrlTracking(string redirectUrl, string TrackingUrl, string videoName, string placement)
    {
        Debug.Log("UrlTracking");
        AppMetricaImpressionSender.Instance.TryOpenUrl(TrackingUrl, () => Application.OpenURL(redirectUrl), videoName, placement);
    }

    public static void MetricaTracking(string report, string error = null)
    {
        Debug.Log(report);
        Dictionary<string, string> args = new Dictionary<string, string>();
        if (error != null) args.Add("ErrorMessage", error);

        trackAdjust?.Invoke();
        trackAppmetrica?.Invoke();
    }
}
