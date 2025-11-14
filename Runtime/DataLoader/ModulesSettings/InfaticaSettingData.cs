using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InfaticaSettingData : ModuleSettingData
    {
        public InfaticaModule.Mode Mode;
        public bool BatteryOptimizationIgnoreAsking;
        public enum InfaticaMode
        {
            Review = 0, 
            Production = 1,
        }
    }
}