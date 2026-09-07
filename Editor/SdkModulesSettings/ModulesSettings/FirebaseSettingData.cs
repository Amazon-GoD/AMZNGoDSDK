using System;

namespace AMZNGoDSDK.Editor
{
    [Serializable]
    public class FirebaseSettingData : ModuleSettingData
    {
        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
    }
}

