using System.IO;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class DataLoader
    {
        private const string ConfigFileName = "amzn_god_sdk";
        
        public static SdkSettingsData LoadSettings()
        {
            TextAsset jsonFile = Resources.Load<TextAsset>(ConfigFileName);
            
            if (jsonFile == null)
                throw new FileNotFoundException("Could not find config file: " + ConfigFileName);
            
            return JsonUtility.FromJson<SdkSettingsData>(jsonFile.text);
        }
    }
}