using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public static class SdkSettingsManager
    {
        private static readonly string AMZNGoDSDKKey = nameof(AMZNGoDSDKKey);

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!PlayerPrefs.HasKey(AMZNGoDSDKKey))
            {
                var defaultSettings = new SdkSettingsData();
                SaveSettings(defaultSettings);
            }
        }

        public static SdkSettingsData LoadSettings()
        {
            if (!PlayerPrefs.HasKey(AMZNGoDSDKKey))
                return new SdkSettingsData();

            try
            {
                string json = PlayerPrefs.GetString(AMZNGoDSDKKey);
                return JsonUtility.FromJson<SdkSettingsData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load settings: {e.Message}");
                return new SdkSettingsData();
            }
        }

        public static void SaveSettings(SdkSettingsData settings)
        {
            try
            {
                string json = JsonUtility.ToJson(settings, true);
                PlayerPrefs.SetString(AMZNGoDSDKKey, json);
                PlayerPrefs.Save();
                
                Debug.Log("AMZN GoD SDK settings saved successfully!");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save settings: {e.Message}");
            }
        }
    }
}