using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class ForegroundServiceManager
    {
        #region Fields

        private AndroidJavaObject unityActivity;

        #endregion

        #region MonoBehaviour

        public void Initialize()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
            }
        }

        #endregion

        #region Methods

        // Start foreground service and request battery optimization permission
        public void StartForegroundService()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    // Start the foreground service
                    javaClass.CallStatic("startForegroundService", unityActivity);
                }
            }
        }

        public void AskIgnoreBatteryOptimization()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    // Request battery optimizations
                    javaClass.CallStatic("askIgnoreBatteryOptimizations", unityActivity);
                }
            }
        }

        // Stop the service
        public void StopService()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    javaClass.CallStatic("stopService", unityActivity);
                }
            }
        }

        #endregion
    }
}
