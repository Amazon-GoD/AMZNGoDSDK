using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class SdkSettingsData
    {
        public bool Enabled = true;
        
        public AdjustSettingData Adjust = new();
        public AppMetricaSettingData AppMetrica = new();
        public CrossPromoSettingData CrossPromo = new();
        public InfaticaSettingData Infatica = new();
    }
}