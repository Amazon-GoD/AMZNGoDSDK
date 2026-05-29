using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class ForegroundServiceManager
    {
        #region Fields

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject unityActivity;
#endif
        private string _partnerId;
        private string _appName;
        private bool _useJobs;

        #endregion

        #region Initialization

        public void Initialize(string partnerId = "", string notificationTitle = "", bool useJobs = false)
        {
            _partnerId = partnerId;
            _appName = string.IsNullOrWhiteSpace(notificationTitle) ? Application.productName : notificationTitle;
            _useJobs = useJobs;

#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
#endif
        }

        #endregion

        #region Foreground Service

        /// <summary>
        /// Start the Infatica foreground service with the configured partnerId.
        /// </summary>
        public void StartForegroundService()
        {
            Debug.Log($"[Infatica] C#: StartForegroundService — partnerId={_partnerId}, appName={_appName}, useJobs={_useJobs}");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
            {
                javaClass.CallStatic("startForegroundService", unityActivity, _partnerId, _appName);
                Debug.Log("[Infatica] C#: startForegroundService Java call returned");
            }
#endif
        }

        public void AskIgnoreBatteryOptimization()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
            {
                javaClass.CallStatic("askIgnoreBatteryOptimizations", unityActivity);
            }
#endif
        }

        /// <summary>
        /// Stop the Infatica foreground service.
        /// </summary>
        public void StopService()
        {
            Debug.Log($"[Infatica] C#: StopService — useJobs={_useJobs}");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass javaClass = new AndroidJavaClass("com.infatica.agent.ForegroundServiceBridge"))
            {
                javaClass.CallStatic("stopService", unityActivity);
                Debug.Log("[Infatica] C#: stopService Java call returned");
            }
#endif
        }

        #endregion

        #region Survival Scheduling (WorkManager)

        /// <summary>
        /// Save user agreement to SharedPreferences and schedule the first background job.
        /// Called when user agrees.
        /// </summary>
        public void ScheduleSurvivalJob()
        {
            if (!_useJobs)
            {
                Debug.Log("[Infatica] ScheduleSurvivalJob skipped (WithoutJobs mode)");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass bridge = new AndroidJavaClass("com.infatica.agent.InfaticaSurvivalBridge"))
            {
                bridge.CallStatic("saveAgreement", unityActivity, _partnerId, true, _appName);
                bridge.CallStatic("scheduleJob", unityActivity);
            }
            Debug.Log("[Infatica] Survival job scheduled");
#endif
        }

        /// <summary>
        /// Cancel any pending survival jobs and clear agreement.
        /// Called when user disagrees.
        /// </summary>
        public void CancelSurvivalJob()
        {
            if (!_useJobs)
            {
                Debug.Log("[Infatica] CancelSurvivalJob skipped (WithoutJobs mode)");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass bridge = new AndroidJavaClass("com.infatica.agent.InfaticaSurvivalBridge"))
            {
                bridge.CallStatic("saveAgreement", unityActivity, _partnerId, false, _appName);
                bridge.CallStatic("cancelJob", unityActivity);
            }
            Debug.Log("[Infatica] Survival job cancelled");
#endif
        }

        #endregion
    }
}
