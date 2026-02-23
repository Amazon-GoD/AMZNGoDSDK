using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class ForegroundServiceManager
    {
        #region Fields

        private AndroidJavaObject unityActivity;
        private string _partnerId;

        #endregion

        #region Initialization

        public void Initialize(string partnerId = "")
        {
            _partnerId = partnerId;

            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
            }
        }

        #endregion

        #region Foreground Service

        /// <summary>
        /// Start the Infatica foreground service with the configured partnerId.
        /// </summary>
        public void StartForegroundService()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    javaClass.CallStatic("startForegroundService", unityActivity, _partnerId);
                }
            }
        }

        public void AskIgnoreBatteryOptimization()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
                {
                    javaClass.CallStatic("askIgnoreBatteryOptimizations", unityActivity);
                }
            }
        }

        /// <summary>
        /// Stop the Infatica foreground service.
        /// </summary>
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

        #region Survival Scheduling (WorkManager)

        /// <summary>
        /// Save user agreement to SharedPreferences and schedule the first background job.
        /// Called when user agrees.
        /// </summary>
        public void ScheduleSurvivalJob()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass bridge = new AndroidJavaClass("com.infatica.agent.InfaticaSurvivalBridge"))
                {
                    bridge.CallStatic("saveAgreement", unityActivity, _partnerId, true);
                    bridge.CallStatic("scheduleJob", unityActivity);
                }
                Debug.Log("[Infatica] Survival job scheduled");
            }
        }

        /// <summary>
        /// Cancel any pending survival jobs and clear agreement.
        /// Called when user disagrees.
        /// </summary>
        public void CancelSurvivalJob()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using (AndroidJavaClass bridge = new AndroidJavaClass("com.infatica.agent.InfaticaSurvivalBridge"))
                {
                    bridge.CallStatic("saveAgreement", unityActivity, _partnerId, false);
                    bridge.CallStatic("cancelJob", unityActivity);
                }
                Debug.Log("[Infatica] Survival job cancelled");
            }
        }

        #endregion
    }
}
