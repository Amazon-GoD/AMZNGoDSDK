using UnityEngine;
using System.Collections;

public static class AppChecker
{
    public static bool CheckIfAppInstalled(string packageName)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        AndroidJavaObject packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager");

        bool isInstalled = false;
        try
        {
            // Проверяем, установлено ли приложение
            AndroidJavaObject appInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0);
            isInstalled = (appInfo != null);
            Debug.Log($"Приложение {packageName} установлено: {isInstalled}");
        }
        catch (System.Exception)
        {
            Debug.Log($"Приложение {packageName} НЕ установлено");
        }
        return isInstalled;
#endif
        return false;
    }
}