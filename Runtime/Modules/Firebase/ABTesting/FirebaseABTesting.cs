#if AMZN_FIREBASE_ENABLED
using System;
using System.Collections.Generic;
using AMZNGoDSDK.Runtime.ABTesting;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public partial class FirebaseModule
    {
        public const string ABTestExposureEvent = "ab_test_exposure";

        private readonly FeaturesTesting _featuresTesting = new FeaturesTesting();
        private readonly Dictionary<string, string> _defaultGroups = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _selectedGroups = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _reportedGroups = new Dictionary<string, string>();
        private readonly Dictionary<string, HashSet<string>> _configuredGroups = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _pendingTests = new HashSet<string>();
        private readonly HashSet<string> _runningTests = new HashSet<string>();

        private void RegisterConfiguredTests(List<ABTestEntry> tests)
        {
            if (tests == null) return;
            foreach (var test in tests)
            {
                // A malformed entry in a hand-edited/old config must not block Firebase startup.
                try
                {
                    if (test == null) throw new ArgumentException("Null test entry.");
                    ValidateABIdentifier(test.TestId, nameof(test.TestId));
                    string control = test.GetDefaultGroup();
                    ValidateABIdentifier(control, nameof(test.DefaultGroup));
                    if (test.GroupNames == null || !test.GroupNames.Contains(control))
                        throw new ArgumentException("Default group must belong to GroupNames.");
                    var groups = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string group in test.GroupNames)
                    {
                        ValidateABIdentifier(group, nameof(test.GroupNames));
                        if (!groups.Add(group)) throw new ArgumentException("Duplicate group.");
                    }
                    if (_defaultGroups.ContainsKey(test.TestId))
                        throw new ArgumentException("Duplicate test ID.");
                    RegisterTest(test.TestId, control);
                    _configuredGroups.Add(test.TestId, groups);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogWarning($"[FirebaseABTesting] Ignoring invalid configured test '{test?.TestId}': {exception.Message}");
                }
            }
        }

        private bool HasGroup(string testId, string groupName) => groupName != null
            && ((_configuredGroups.TryGetValue(testId, out var groups) && groups.Contains(groupName))
                || _featuresTesting.HasFeature(testId, groupName));

        /// <summary>Explicit control group, independent of feature registration order.</summary>
        public void RegisterTest(string testId, string defaultGroupName)
        {
            ValidateABIdentifier(testId, nameof(testId));
            ValidateABIdentifier(defaultGroupName, nameof(defaultGroupName));
            _defaultGroups[testId] = defaultGroupName;
        }

        public void RegisterFeature(string testId, string groupName, ITestableFeature feature)
        {
            ValidateABIdentifier(testId, nameof(testId));
            ValidateABIdentifier(groupName, nameof(groupName));
            _featuresTesting.AddOrUpdateFeature(testId, groupName, feature);
        }

        public void RegisterFeature(string testId, string groupName, Action feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            RegisterFeature(testId, groupName, new CallbackFeature(feature));
        }

        /// <summary>
        /// Queues one execution per test while loading. Later calls reapply the selected
        /// feature (e.g. for a new scene), without repeating the exposure event.
        /// </summary>
        public void Run(string testId)
        {
            ValidateABIdentifier(testId, nameof(testId));
            if (!Enabled) return;
            if (!_defaultGroups.TryGetValue(testId, out var control) || !_featuresTesting.HasFeature(testId, control))
            {
                Debug.LogWarning($"[FirebaseABTesting] Register test '{testId}' and its control feature before Run.");
                return;
            }

            if (!IsRemoteConfigReady)
            {
                _pendingTests.Add(testId);
                return;
            }

            if (!TryGetTestGroup(testId, out var groupName) || !_runningTests.Add(testId)) return;
            int version = _initializationVersion;
            try
            {
                if (!_featuresTesting.TryRunFeature(testId, groupName))
                    Debug.LogWarning($"[FirebaseABTesting] Register a handler for '{testId}/{groupName}' before Run.");
                else if (IsCurrentInitialization(version) && _defaultGroups.ContainsKey(testId))
                    SendABTestExposure(testId, groupName);
            }
            catch (Exception exception)
            {
                // A broken game feature must not interrupt other tests or Firebase readiness.
                Debug.LogError($"[FirebaseABTesting] Feature '{testId}/{groupName}' failed: {exception}");
            }
            finally { _runningTests.Remove(testId); }
        }

        /// <summary>Configured tests can be queried without handlers; code-only tests need a control handler.</summary>
        public bool TryGetTestGroup(string testId, out string groupName)
        {
            groupName = null;
            if (!Enabled || !IsRemoteConfigReady || string.IsNullOrWhiteSpace(testId)
                || !_defaultGroups.TryGetValue(testId, out var control)
                || !HasGroup(testId, control)) return false;

            if (_selectedGroups.TryGetValue(testId, out var selected) && HasGroup(testId, selected))
            {
                groupName = selected;
                return true;
            }

            _remoteGroups.TryGetValue(testId, out var remote);
            groupName = HasGroup(testId, remote) ? remote : control;
            if (!string.IsNullOrEmpty(remote) && groupName != remote)
                Debug.LogWarning($"[FirebaseABTesting] Unknown group '{remote}' for '{testId}'; using '{control}'.");
            _selectedGroups[testId] = groupName;
            return true;
        }

        public void UnregisterFeature(string testId, string groupName) => _featuresTesting.RemoveFeature(testId, groupName);

        public void RemoveTest(string testId)
        {
            if (string.IsNullOrWhiteSpace(testId)) return;
            _featuresTesting.RemoveRemoteId(testId);
            _configuredGroups.Remove(testId);
            _defaultGroups.Remove(testId);
            _selectedGroups.Remove(testId);
            _reportedGroups.Remove(testId);
            _pendingTests.Remove(testId);
        }

        public void ClearAll()
        {
            _featuresTesting.Clear();
            _configuredGroups.Clear();
            _defaultGroups.Clear();
            _selectedGroups.Clear();
            _reportedGroups.Clear();
            _pendingTests.Clear();
        }

        private void SendABTestExposure(string testId, string groupName)
        {
            if (_reportedGroups.TryGetValue(testId, out var reported) && reported == groupName) return;
            _reportedGroups[testId] = groupName;
            var parameters = new Dictionary<string, string> { { "test_id", testId }, { "group_name", groupName } };
            try { LogEvent(ABTestExposureEvent, parameters); }
            catch (Exception exception) { Debug.LogWarning($"[FirebaseABTesting] Firebase event failed: {exception.Message}"); }
#if AMZN_APPMETRICA_ENABLED
            try
            {
                var appMetrica = SdkModuleRegistry.Get<AppMetricaModule>();
                if (appMetrica != null && appMetrica.Enabled && appMetrica.Initialized)
                    appMetrica.ReportEvent(ABTestExposureEvent, parameters);
            }
            catch (Exception exception) { Debug.LogWarning($"[FirebaseABTesting] AppMetrica event failed: {exception.Message}"); }
#endif
        }

        private static void ValidateABIdentifier(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxFirebaseValueLength)
                throw new ArgumentException($"A/B identifiers must contain 1–{MaxFirebaseValueLength} characters.", name);
        }

        private sealed class CallbackFeature : ITestableFeature
        {
            private readonly Action _callback;
            internal CallbackFeature(Action callback) { _callback = callback; }
            public void Run() => _callback();
        }
    }
}
#endif
