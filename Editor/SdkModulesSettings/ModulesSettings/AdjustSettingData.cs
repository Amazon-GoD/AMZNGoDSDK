using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class AdjustSettingData : ModuleSettingData
    {
        public string Key;
        public AdjustEnvironment Environment;
        
        public enum AdjustEnvironment 
        {
            Sandbox,
            Production
        }
    }
}

