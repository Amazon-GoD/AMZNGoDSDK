using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class InfaticaSettingData : ModuleSettingData
    {
        public string PartnerId = "";
        public InfaticaMode Mode;
        public bool BatteryOptimizationIgnoreAsking;
        public enum InfaticaMode
        {
            Review = 0, 
            Production = 1,
        }
    }
}