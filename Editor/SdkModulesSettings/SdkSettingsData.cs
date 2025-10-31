using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class SdkSettingsData
    {
        public bool Enabled = true;
        
        public AdjustSettingData Adjust;
        public AppMetricaSettingData AppMetrica;
        public CrossPromoSettingData CrossPromo;
        public InfaticaSettingData Infatica;
    }
}