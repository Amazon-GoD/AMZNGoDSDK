using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class SdkSettingsData
    {
        public bool Enabled;
        
        public AdjustSettingData Adjust = new();
        public AppMetricaSettingData AppMetrica = new();
        public CrossPromoSettingData CrossPromo = new();
        public InfaticaSettingData Infatica = new();
    }
}