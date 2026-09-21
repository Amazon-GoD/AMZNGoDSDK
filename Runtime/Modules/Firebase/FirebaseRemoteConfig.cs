#if AMZN_FIREBASE_ENABLED
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.RemoteConfig;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public partial class FirebaseModule
    {
        private int _remoteConfigFetchTimeoutSeconds = FirebaseSettingData.DefaultFetchTimeoutSeconds;
        private int _remoteConfigMinimumFetchIntervalSeconds = FirebaseSettingData.DefaultMinimumFetchIntervalSeconds;
        private readonly Dictionary<string, string> _remoteGroups = new Dictionary<string, string>();

        /// <summary>Initial resolution finished; cached values or local defaults may be in use.</summary>
        public bool IsRemoteConfigReady { get; private set; }
        public bool LastRemoteConfigFetchSucceeded { get; private set; }
        public event Action OnRemoteConfigReady;

        private async Task InitializeRemoteConfigAsync(int version)
        {
            FirebaseRemoteConfig remoteConfig = null;
            bool cacheReady = false;
            LastRemoteConfigFetchSucceeded = false;
            try
            {
                remoteConfig = FirebaseRemoteConfig.DefaultInstance;
                await remoteConfig.EnsureInitializedAsync();
                if (!IsCurrentInitialization(version)) return;
                cacheReady = true;

                await remoteConfig.SetConfigSettingsAsync(new ConfigSettings
                {
                    FetchTimeoutInMilliseconds = (ulong)_remoteConfigFetchTimeoutSeconds * 1000UL,
                    MinimumFetchIntervalInMilliseconds = (ulong)_remoteConfigMinimumFetchIntervalSeconds * 1000UL
                });
                if (!IsCurrentInitialization(version)) return;

                await remoteConfig.FetchAndActivateAsync();
                if (!IsCurrentInitialization(version)) return;
                // A false result means nothing new was activated, not a failed fetch.
                LastRemoteConfigFetchSucceeded = remoteConfig.Info.LastFetchStatus == LastFetchStatus.Success;
            }
            catch (Exception exception)
            {
                if (IsCurrentInitialization(version))
                    Debug.LogWarning($"[FirebaseModule] Remote Config unavailable: {exception.Message}. Using cached/local groups.");
            }
            finally
            {
                if (IsCurrentInitialization(version) && cacheReady)
                {
                    try
                    {
                        // Freeze activated remote values for this session. Local A/B defaults
                        // belong to the registry, so late registration needs no async SetDefaults.
                        foreach (var pair in remoteConfig.AllValues)
                        {
                            if (pair.Value.Source == ValueSource.RemoteValue)
                                _remoteGroups[pair.Key] = pair.Value.StringValue;
                        }
                    }
                    catch (Exception exception)
                    {
                        _remoteGroups.Clear();
                        Debug.LogWarning($"[FirebaseModule] Cannot read Remote Config: {exception.Message}. Using local groups.");
                    }
                }
            }
        }

        private void CompleteRemoteConfig()
        {
            IsRemoteConfigReady = true;
            int version = _initializationVersion;
            // Snapshot permits feature callbacks to unregister other queued tests safely.
            var pending = new List<string>(_pendingTests);
            foreach (string testId in pending)
            {
                if (!IsCurrentInitialization(version)) return;
                if (_pendingTests.Remove(testId)) Run(testId);
            }
            if (IsCurrentInitialization(version)) InvokeSafely(OnRemoteConfigReady);
        }
    }
}
#endif
