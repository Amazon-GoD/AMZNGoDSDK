#if AMZN_CROSSPROMO_ENABLED
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class AppChecker
    {
        public static bool CheckIfAppInstalled(string packageName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            var packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager");

            try
            {
                var appInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0);
                var isInstalled = appInfo != null;
                Debug.Log($"App {packageName} installed: {isInstalled}");
                return isInstalled;
            }
            catch (System.Exception)
            {
                Debug.Log($"App {packageName} is not installed");
            }
#endif
            return false;
        }
    }
}
#endif