#if AMZN_FIREBASE_ENABLED
using System;
using System.Collections;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public partial class FirebaseModule
    {
        private const string AdjustStartupCacheKey = "amzn_sdk.adjust_enable";
        private bool _adjustStartupRequested;
        private bool _adjustStartupDecisionApplied;

        /// <summary>
        /// Core calls this before initializing Adjust. Uses the same A/B registry and Run callbacks
        /// as game experiments; the startup decision is applied only once for this module lifetime.
        /// </summary>
        public IEnumerator ResolveAdjustStartup(Action<bool> applyDecision)
        {
            if (applyDecision == null) throw new ArgumentNullException(nameof(applyDecision));
            if (!IsRemoteConfigConfigured)
            {
                applyDecision(true);
                yield break;
            }
            if (_adjustStartupRequested || _isInitializing || IsRemoteConfigReady)
                throw new InvalidOperationException("Adjust startup must be resolved once, before Firebase initialization.");

            _adjustStartupRequested = true;
            _forceRemoteConfigFetchOnStartup = true;
            // This key belongs to the SDK. Replace any configured entry with the fixed contract.
            RemoveTest(FirebaseSettingData.AdjustEnableTestId);
            RegisterTest(FirebaseSettingData.AdjustEnableTestId, FirebaseSettingData.AdjustEnabledGroup);
            RegisterFeature(FirebaseSettingData.AdjustEnableTestId, FirebaseSettingData.AdjustEnabledGroup,
                () => ApplyAdjustStartupDecision(true, applyDecision));
            RegisterFeature(FirebaseSettingData.AdjustEnableTestId, FirebaseSettingData.AdjustDisabledGroup,
                () => ApplyAdjustStartupDecision(false, applyDecision));
            Run(FirebaseSettingData.AdjustEnableTestId);

            // Fetch has its own native timeout. This also bounds dependency/setup/activation waits.
            float deadline = Time.realtimeSinceStartup + _remoteConfigFetchTimeoutSeconds + 5f;
            Initialize();
            int version = _initializationVersion;
            while (IsCurrentInitialization(version) && !IsRemoteConfigReady && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!IsCurrentInitialization(version)) yield break;

            if (!IsRemoteConfigReady)
            {
                Debug.LogWarning("[FirebaseModule] Startup config deadline exceeded; using cached/local groups for this session.");
                // Freezes this session. A late native response must not re-enable Adjust after startup.
                CompleteRemoteConfig();
            }
        }

        private void ApplyAdjustStartupFallback()
        {
            if (!_adjustStartupRequested || LastRemoteConfigFetchSucceeded) return;
            if (_remoteGroups.TryGetValue(FirebaseSettingData.AdjustEnableTestId, out string group)
                && (group == FirebaseSettingData.AdjustEnabledGroup || group == FirebaseSettingData.AdjustDisabledGroup)) return;

            _remoteGroups[FirebaseSettingData.AdjustEnableTestId] = PlayerPrefs.GetInt(AdjustStartupCacheKey, 1) == 0
                ? FirebaseSettingData.AdjustDisabledGroup : FirebaseSettingData.AdjustEnabledGroup;
        }

        private void ApplyAdjustStartupDecision(bool allow, Action<bool> applyDecision)
        {
            if (_adjustStartupDecisionApplied) return;
            applyDecision(allow);
            _adjustStartupDecisionApplied = true;
            PlayerPrefs.SetInt(AdjustStartupCacheKey, allow ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[FirebaseModule] {FirebaseSettingData.AdjustEnableTestId} = {(allow ? "true" : "false")} for this session.");
        }
    }
}
#endif
