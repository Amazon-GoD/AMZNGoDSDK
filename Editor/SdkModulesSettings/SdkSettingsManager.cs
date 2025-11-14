using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public static class SdkSettingsManager
    {
        private const string ConfigFileName = "amzn_god_sdk.json";
        private const string ResourcesPath = "Assets/Resources/";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (ConfigFileIsAlreadyExist())
            {
                var defaultSettings = new SdkSettingsData();
                SaveSettings(defaultSettings);
            }
        }

        private static bool ConfigFileIsAlreadyExist()
        {
            try
            {
                string fullPath = Path.Combine(ResourcesPath, ConfigFileName);
                
                return File.ReadAllText(fullPath).Length > 0;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public static SdkSettingsData LoadSettings()
        {
            TextAsset jsonFile = Resources.Load<TextAsset>(ConfigFileName.Split('.')[0]);
            
            if (jsonFile == null)
                return new SdkSettingsData();
            
            return JsonUtility.FromJson<SdkSettingsData>(jsonFile.text);
        }

        public static void SaveSettings(SdkSettingsData settings)
        {
            string json = JsonUtility.ToJson(settings, true);
            
            string fullPath = Path.Combine(ResourcesPath, ConfigFileName);
            
            if (!Directory.Exists(ResourcesPath))
                Directory.CreateDirectory(ResourcesPath);
            
            File.WriteAllText(fullPath, json);
            
            AssetDatabase.Refresh();
        }
    }
}