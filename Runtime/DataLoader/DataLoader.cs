using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class DataLoader
    {
        private static readonly string AMZNGoDSDKKey = nameof(AMZNGoDSDKKey);
        
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
    }
}