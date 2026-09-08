using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class FirebaseSettingData : ModuleSettingData
    {
        public bool EnableAnalytics = true;
        public bool EnableCrashlytics = true;
    }
}

