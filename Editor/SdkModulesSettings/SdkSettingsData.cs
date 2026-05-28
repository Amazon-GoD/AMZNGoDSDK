using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class SdkSettingsData
    {
        public bool Enabled = false;

        public AdjustSettingData Adjust = new();
        public AppMetricaSettingData AppMetrica = new();
        public CrossPromoSettingData CrossPromo = new();
        public InfaticaSettingData Infatica = new();
        public InAppPurchaseSettingData InAppPurchase = new();
        public FirebaseSettingData Firebase = new();
        public InternetConnectionSettingData InternetConnection = new();
        public DebugConsoleSettingData DebugConsole = new();
        public AnalyticsSettingData Analytics = new();
    }
}
