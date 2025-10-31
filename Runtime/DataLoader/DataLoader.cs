using System.IO;
using AMZNGoDSDK.Editor;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class DataLoader
    {
        private static readonly string SettingsPath = 
            Path.Combine(Application.streamingAssetsPath, "amzn_god_sdk_settings.json");
        
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
    }
}