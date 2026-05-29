using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace InfaticaSDK.Core
{
    public static class InfaticaAgent
    {
        #region Methods

        public static void Start(string partnerID, Action OnStarted)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            InfaticaWrapperWindows.StartAgent(partnerID);
            OnStarted?.Invoke();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            InfaticaWrapperMac.StartAgent(partnerID);
            OnStarted?.Invoke();
#elif UNITY_ANDROID && !UNITY_EDITOR
            InfaticaWrapperAndroid.StartAgent(partnerID);
            OnStarted?.Invoke();
#elif UNITY_IOS && !UNITY_EDITOR
            InfaticaWrapperIOS.StartPermissionLocation();
            InfaticaWrapperIOS.BeginBackgroundTaskIfNeeded();
            InfaticaWrapperIOS.StartAgentFull(partnerID);
            OnStarted?.Invoke();
#endif
        }

        public static void Stop()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            InfaticaWrapperWindows.StopAgent();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            InfaticaWrapperMac.StopAgent();
#elif UNITY_ANDROID && !UNITY_EDITOR
            InfaticaWrapperAndroid.StopAgent();
#elif UNITY_IOS && !UNITY_EDITOR
            InfaticaWrapperIOS.EndBackgroundTaskIfNeeded();
            InfaticaWrapperIOS.StopAgentFull(1);
#endif
        }

        public static string GetID()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return InfaticaWrapperWindows.GetAgentId();
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return InfaticaWrapperMac.GetAgentId();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return "Agent running...";
#elif UNITY_IOS && !UNITY_EDITOR
            return InfaticaWrapperIOS.GetAgentIdFull();
#endif
        }

        public static void AskIgnoreBatteryOptimization()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!InfaticaWrapperAndroid.IsIgnoringBatteryOptimizations())
            {
                InfaticaWrapperAndroid.AskIgnoreBatteryOptimization();
            }
#else
            Debug.LogWarning("Not supported on this platform. Only Android");
#endif
        }
        #endregion
    }
}

