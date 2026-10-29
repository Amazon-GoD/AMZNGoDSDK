using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class AdjustSettingData : ModuleSettingData
    {
        public string Key;
        public AdjustEnvironment Environment;
        // Зеркало Runtime.AdjustSettingData.AllowSandboxInRelease.
        public bool AllowSandboxInRelease;
        
        public enum AdjustEnvironment 
        {
            Sandbox,
            Production
        }
    }
}

