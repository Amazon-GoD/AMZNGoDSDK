using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class AdjustSettingData : ModuleSettingData
    {
        public string Key;
        public AdjustEnvironment Environment;
        
        public enum AdjustEnvironment
        {
            Sandbox = 0,
            Production = 1
        }
    }
}

