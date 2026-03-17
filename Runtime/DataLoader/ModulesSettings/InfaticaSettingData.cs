using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InfaticaSettingData : ModuleSettingData
    {
        public string PartnerId = "";
        public string NotificationTitle = "";
        public InfaticaMode Mode;
        public InfaticaSdkVersion SdkVersion;
        public bool BatteryOptimizationIgnoreAsking;
        
        public enum InfaticaMode
        {
            Review = 0, 
            Production = 1,
        }

        public enum InfaticaSdkVersion
        {
            WithJobs = 0,
            WithoutJobs = 1,
        }
    }
}