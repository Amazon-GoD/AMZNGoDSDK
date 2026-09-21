#if AMZN_FIREBASE_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public partial class FirebaseModule : ModuleBase
    {
        private bool _analyticsEnabled;
        private bool _crashlyticsEnabled;
        private bool _isInitialized;
        private bool _remoteConfigEnabled;
        private bool _isInitializing;
        private int _initializationVersion;

        public bool IsInitialized => _isInitialized;
        public bool AnalyticsEnabled => _isInitialized && _analyticsEnabled;
        public bool CrashlyticsEnabled => _isInitialized && _crashlyticsEnabled;
        public bool RemoteConfigEnabled => _isInitialized && _remoteConfigEnabled;

        public event Action OnInitialized;

        public void Construct(FirebaseSettingData settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Construct(settings.Enabled, settings.EnableAnalytics, settings.EnableCrashlytics, settings.EnableRemoteConfig);
            _remoteConfigFetchTimeoutSeconds = settings.RemoteConfigFetchTimeoutSeconds > 0
                ? settings.RemoteConfigFetchTimeoutSeconds : FirebaseSettingData.DefaultFetchTimeoutSeconds;
            _remoteConfigMinimumFetchIntervalSeconds = settings.RemoteConfigMinimumFetchIntervalSeconds > 0
                ? settings.RemoteConfigMinimumFetchIntervalSeconds : FirebaseSettingData.DefaultMinimumFetchIntervalSeconds;
        }

        public void Construct(bool enable, bool enableAnalytics, bool enableCrashlytics) =>
            Construct(enable, enableAnalytics, enableCrashlytics, false);

        public void Construct(bool enable, bool enableAnalytics,
            bool enableCrashlytics, bool remoteConfigEnabled)
        {
            Enabled = enable;
            _analyticsEnabled = enableAnalytics;
            _crashlyticsEnabled = enableCrashlytics;
            _remoteConfigEnabled = remoteConfigEnabled;
        }

        public override void Initialize()
        {
            if (!Enabled || _isInitializing || _isInitialized)
                return;

            _isInitializing = true;
            IsRemoteConfigReady = false;
            _ = InitializeAsync(++_initializationVersion);
        }

        // Started on Unity's main thread; awaits retain its SynchronizationContext.
        private async Task InitializeAsync(int version)
        {
            try
            {
                DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (!IsCurrentInitialization(version)) return;

                if (dependencyStatus != DependencyStatus.Available)
                {
                    Debug.LogError($"[FirebaseModule] Firebase dependencies are not available: {dependencyStatus}");
                    return;
                }

                if (FirebaseApp.DefaultInstance == null)
                {
                    Debug.LogWarning("[FirebaseModule] FirebaseApp.DefaultInstance is null after dependency resolution.");
                    return;
                }

                FirebaseAnalytics.SetAnalyticsCollectionEnabled(_analyticsEnabled);
                Crashlytics.IsCrashlyticsCollectionEnabled = _crashlyticsEnabled;

                // Analytics and Crashlytics are usable independently of the network fetch.
                _isInitialized = true;
                InvokeSafely(OnInitialized);
                if (IsCurrentInitialization(version) && _remoteConfigEnabled)
                    await InitializeRemoteConfigAsync(version);
            }
            catch (Exception exception)
            {
                if (IsCurrentInitialization(version))
                    Debug.LogError($"[FirebaseModule] Initialization failed: {exception.Message}. A/B tests will use local defaults.");
            }
            finally
            {
                if (IsCurrentInitialization(version))
                {
                    _isInitializing = false;
                    CompleteRemoteConfig();
                }
            }
        }

        private bool IsCurrentInitialization(int version) => this != null && version == _initializationVersion;

        private static void InvokeSafely(Action listeners)
        {
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private const int MaxFirebaseParams = 25;
        private const int MaxFirebaseValueLength = 100;

        public void LogEvent(string eventName, Dictionary<string, string> parameters = null)
        {
            if (!_analyticsEnabled || !_isInitialized || string.IsNullOrWhiteSpace(eventName))
                return;

            if (parameters == null || parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
                return;
            }

            Parameter[] parameterArray = parameters
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .Take(MaxFirebaseParams)
                .Select(kv =>
                {
                    var value = kv.Value ?? string.Empty;
                    if (value.Length > MaxFirebaseValueLength)
                        value = value.Substring(0, MaxFirebaseValueLength);
                    return new Parameter(kv.Key, value);
                })
                .ToArray();

            if (parameterArray.Length == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
                return;
            }

            FirebaseAnalytics.LogEvent(eventName, parameterArray);
        }

        public void RecordException(Exception exception)
        {
            if (!_crashlyticsEnabled || !_isInitialized || exception == null)
                return;

            Crashlytics.LogException(exception);
        }

        public void LogCrash(string message)
        {
            if (!_crashlyticsEnabled || !_isInitialized || string.IsNullOrWhiteSpace(message))
                return;

            Crashlytics.Log(message);
        }

        public void SetUserId(string userId)
        {
            if (!_analyticsEnabled || !_isInitialized)
                return;

            FirebaseAnalytics.SetUserId(userId);
        }

        public void SetUserProperty(string propertyName, string propertyValue)
        {
            if (!_analyticsEnabled || !_isInitialized)
                return;

            FirebaseAnalytics.SetUserProperty(propertyName, propertyValue);
        }

        public override void Cleanup()
        {
            ++_initializationVersion;
            _isInitialized = false;
            _isInitializing = false;
            IsRemoteConfigReady = false;
            LastRemoteConfigFetchSucceeded = false;
            _remoteGroups.Clear();
            ClearAll();
            OnInitialized = null;
            OnRemoteConfigReady = null;
        }

        private void OnDestroy() => Cleanup();
    }
}
#endif
