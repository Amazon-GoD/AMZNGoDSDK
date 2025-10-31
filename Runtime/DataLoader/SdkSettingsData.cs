using AMZNGoDSDK.Editor;
using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class SdkSettingsData
    {
        public bool Enabled;
        
        public AdjustSettingData Adjust;
        public AppMetricaSettingData AppMetrica;
        public CrossPromoSettingData CrossPromo;
        public InfaticaSettingData Infatica;
    }
}