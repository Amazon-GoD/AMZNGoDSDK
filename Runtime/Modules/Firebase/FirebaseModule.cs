#if AMZN_FIREBASE_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class FirebaseModule : ModuleBase
    {
        private bool _analyticsEnabled;
        private bool _crashlyticsEnabled;
        private bool _isInitialized;

        public bool IsInitialized => _isInitialized;
        public bool AnalyticsEnabled => _isInitialized && _analyticsEnabled;
        public bool CrashlyticsEnabled => _isInitialized && _crashlyticsEnabled;

        public event Action OnInitialized;

        public void Construct(bool enable, bool enableAnalytics, bool enableCrashlytics)
        {
            Enabled = enable;
            _analyticsEnabled = enableAnalytics;
            _crashlyticsEnabled = enableCrashlytics;
        }

        public override void Initialize()
        {
            if (!Enabled)
                return;

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(checkTask =>
            {
                if (checkTask.IsFaulted)
                {
                    Debug.LogError($"[FirebaseModule] Dependency check failed: {checkTask.Exception?.Message}");
                    return;
                }

                DependencyStatus dependencyStatus = checkTask.Result;
                if (dependencyStatus != DependencyStatus.Available)
                {
                    Debug.LogError($"[FirebaseModule] Firebase dependencies are not available: {dependencyStatus}");
                    return;
                }

                if (FirebaseApp.DefaultInstance == null)
                {
                    Debug.LogWarning("[FirebaseModule] FirebaseApp.DefaultInstance is null after dependency resolution.");
                }

                FirebaseAnalytics.SetAnalyticsCollectionEnabled(_analyticsEnabled);
                Crashlytics.IsCrashlyticsCollectionEnabled = _crashlyticsEnabled;

                _isInitialized = true;
                OnInitialized?.Invoke();
            });
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
            _isInitialized = false;
        }
    }
}
#endif
