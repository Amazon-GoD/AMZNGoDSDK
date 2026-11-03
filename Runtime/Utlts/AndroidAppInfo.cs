using System;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Чтение статики своего APK через JNI для профиля игры в first_open (с 1.0.11, см.
    /// <see cref="GameProfile"/>): строковых ресурсов (их генерирует Firebase из google-services.json)
    /// и meta-data манифеста (AdMob App ID, который пишет MAX). Нет ресурса или ключа — null, а не
    /// исключение. Любой сбой JNI — null и предупреждение только с типом исключения: значения в лог
    /// не выводим. В редакторе и на других платформах всегда null.
    /// </summary>
    public static class AndroidAppInfo
    {
        private const string Tag = "[AndroidAppInfo]";

        // android.content.pm.PackageManager.GET_META_DATA
        private const int GetMetaData = 128;

        /// <summary>Строковый ресурс приложения по имени (<c>Resources.getIdentifier</c>) или null.</summary>
        public static string GetStringResource(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(name))
                return null;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                {
                    string packageName = activity.Call<string>("getPackageName");
                    int id = resources.Call<int>("getIdentifier", name, "string", packageName);
                    return id == 0 ? null : resources.Call<string>("getString", id);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} String resource '{name}' is not readable ({e.GetType().Name})");
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>Строковое meta-data из манифеста приложения (<c>ApplicationInfo.metaData</c>) или null.</summary>
        public static string GetMetaDataString(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(key))
                return null;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    string packageName = activity.Call<string>("getPackageName");
                    using (var appInfo = packageManager.Call<AndroidJavaObject>("getApplicationInfo", packageName, GetMetaData))
                    using (var metaData = appInfo?.Get<AndroidJavaObject>("metaData"))
                    {
                        // metaData == null, когда в манифесте нет ни одного meta-data.
                        return metaData?.Call<string>("getString", key);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Manifest meta-data '{key}' is not readable ({e.GetType().Name})");
                return null;
            }
#else
            return null;
#endif
        }
    }
}
