using System;

namespace AMZNGoDSDK.Runtime
{
    public enum FirstOpenAppType
    {
        Free = 0,
        Paid = 1
    }

    [Serializable]
    public class FirstOpenTrackingSettingData
    {
        public string BaseUrl;
        public string ApiKey;
        public FirstOpenAppType AppType;
    }
}
