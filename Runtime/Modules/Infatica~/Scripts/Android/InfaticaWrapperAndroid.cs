using UnityEngine;

namespace InfaticaSDK.Core
{
    public static class InfaticaWrapperAndroid
    {
        #region Fields

        private static AndroidJavaObject _unityActivity;

        #endregion

        #region Methods

        // Start foreground service and request battery optimization permission
        public static void StartAgent(string parnerId)
        {
            if(_unityActivity == null)
            {
                Initialize();
            }

            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    // Start the foreground service
                    javaClass.CallStatic("startForegroundService", _unityActivity, parnerId);
                }
            }
        }

        public static bool IsIgnoringBatteryOptimizations()
        {
            if (_unityActivity == null)
                Initialize();

            using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
            {
                return javaClass.CallStatic<bool>("isIgnoringBatteryOptimizations", _unityActivity);
            }
        }

        public static void AskIgnoreBatteryOptimization()
        {
            if (_unityActivity == null)
            {
                Initialize();
            }

            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    // Request battery optimizations
                    javaClass.CallStatic("askIgnoreBatteryOptimizations", _unityActivity);
                }
            }
        }

        // Stop the service
        public static void StopAgent()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    javaClass.CallStatic("stopService", _unityActivity);
                }
            }
        }

        private static void Initialize()
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
        #endregion
    }
}
