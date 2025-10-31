using System;
using AdjustSdk;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class AdjustSettingData : ModuleSettingData
    {
        public string Key;
        public AdjustEnvironment Environment;
    }
}

