using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public static class SdkSettingsManager
    {
        private static readonly string SettingsPath = 
            Path.Combine(Application.streamingAssetsPath, "amzn_god_sdk_settings.json");

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!File.Exists(SettingsPath))
            {
                var defaultSettings = new SdkSettingsData();
                SaveSettings(defaultSettings);
            }
        }

        public static SdkSettingsData LoadSettings()
        {
            if (!File.Exists(SettingsPath))
                return new SdkSettingsData();

            try
            {
                string json = File.ReadAllText(SettingsPath);
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
                if (!Directory.Exists(Application.streamingAssetsPath)) 
                    Directory.CreateDirectory(Application.streamingAssetsPath);

                string json = JsonUtility.ToJson(settings, true);
                File.WriteAllText(SettingsPath, json);
                
#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
                
                Debug.Log("AMZN GoD SDK settings saved successfully!");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save settings: {e.Message}");
            }
        }
    }
}