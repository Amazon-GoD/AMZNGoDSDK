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
        public InAppPurchaseSettingData InAppPurchase = new();
        public FirebaseSettingData Firebase = new();
        public InternetConnectionSettingData InternetConnection = new();
        public DebugConsoleSettingData DebugConsole = new();
        public AnalyticsSettingData Analytics = new();

        // Deprecated: значения мигрируются в Analytics через DataLoader.MigrateLegacyAnalytics
        // при первой загрузке после апгрейда с хотфикс-версии. После первого save из SDK
        // Settings Window поле исчезает из JSON. Не использовать в новом коде.
        public FirstOpenTrackingSettingData FirstOpenTracking = new();
    }
}
