#if AMZN_INTERNETCONNECTION_ENABLED
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
namespace AMZNGoDSDK.Runtime
{
    [DisallowMultipleComponent]
    public sealed class InternetConnectionModule : ModuleBase
    {
        private const string Tag = "[InternetConnection]";
        private const float MinCheckInterval = 1f;

        /// <summary>Path (inside the module's own Resources folder) of the built-in offline banner prefab.</summary>
        private const string BannerResourcePath = "AMZNGoDSDK/OfflineBanner";

        private InternetConnectionSettingData _settings = new();
        private Coroutine _monitorCoroutine;
        private Coroutine _immediateCheckCoroutine;
        private bool _hasInternet = true;
        private bool _pausedByModule;
        private float _savedTimeScale = 1f;

        [Header("Banner")]
        [Tooltip("Optional. Leave empty to spawn the built-in banner prefab from the module's Resources folder.")]
        [SerializeField] private OfflineBannerView _bannerView;

        [Tooltip("Optional override for the built-in banner prefab.")]
        [SerializeField] private GameObject _bannerPrefab;

        private bool _bannerResolved;

        public bool IsConnected => _hasInternet;
        public bool IsInitialized { get; private set; }

        public event Action OnInternetAvailable;
        public event Action OnInternetLost;

        public void Construct(bool enabled, InternetConnectionSettingData settings)
        {
            Enabled = enabled;
            _settings = settings ?? new InternetConnectionSettingData();
        }

        public override void Initialize()
        {
            if (!Enabled)
                return;

            if (_monitorCoroutine != null)
                StopCoroutine(_monitorCoroutine);

            _monitorCoroutine = StartCoroutine(MonitorRoutine());
        }

        /// <summary>
        /// Runtime toggle. Disabling stops connectivity monitoring, hides the offline banner,
        /// restores the time scale paused by this module and makes <see cref="IsConnected"/>
        /// report true (a disabled checker must not gate gameplay on a stale offline state).
        /// Enabling (re)starts monitoring with the settings supplied via Construct.
        /// The toggle itself raises no connectivity events; after enabling, an initial
        /// connectivity check runs immediately and reports the current state (same
        /// behaviour as Initialize), so subscribers get one event right away.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled)
                return;

            Enabled = enabled;

            if (enabled)
            {
                Initialize();
                return;
            }

            StopMonitoring();

            HideBanner();
            RestoreTimeScale();

            // Also unblocks WaitUntilConnected: its loop exits as soon as the module
            // is disabled or the connection state reads as online.
            _hasInternet = true;
        }

        private void StopMonitoring()
        {
            if (_monitorCoroutine != null)
            {
                StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }

            if (_immediateCheckCoroutine != null)
            {
                StopCoroutine(_immediateCheckCoroutine);
                _immediateCheckCoroutine = null;
            }
        }

        public IEnumerator WaitUntilConnected()
        {
            if (!Enabled)
                yield break;

            while (Enabled && !_hasInternet)
                yield return null;
        }

        private IEnumerator MonitorRoutine()
        {
            IsInitialized = true;
            yield return CheckConnectivityRoutine(initial: true);

            while (Enabled)
            {
                float interval = Mathf.Max(MinCheckInterval, _settings.CheckIntervalSeconds);
                yield return new WaitForSecondsRealtime(interval);
                yield return CheckConnectivityRoutine(initial: false);
            }
        }

        /// <summary>
        /// Determines connectivity and applies the result. Reachability alone only proves a
        /// network interface is up; when it looks online and the HTTP probe is enabled, a real
        /// request confirms the link, so dead DNS or a missing upstream still read as offline.
        /// NotReachable short-circuits before the first yield, so a hard offline state applies
        /// synchronously (Initialize relies on that for the startup connectivity gate).
        /// </summary>
        private IEnumerator CheckConnectivityRoutine(bool initial)
        {
            bool connected = Application.internetReachability != NetworkReachability.NotReachable;

            if (connected && _settings.UseHttpProbe && !string.IsNullOrEmpty(_settings.ProbeUrl))
            {
                using (UnityWebRequest request = UnityWebRequest.Head(_settings.ProbeUrl))
                {
                    request.timeout = Mathf.Max(1, Mathf.RoundToInt(_settings.ProbeTimeoutSeconds));
                    yield return request.SendWebRequest();

                    // The module may have been disabled while the probe was in flight.
                    if (!Enabled)
                        yield break;

                    connected = request.result == UnityWebRequest.Result.Success;
                }
            }

            ApplyConnectivityState(connected, initial);
        }

        private void ApplyConnectivityState(bool connected, bool initial)
        {
            if (!initial && connected == _hasInternet)
                return;

            _hasInternet = connected;

            if (_hasInternet)
            {
                Debug.Log($"{Tag} Connection available.");
                HideBanner();
                RestoreTimeScale();
                OnInternetAvailable?.Invoke();
            }
            else
            {
                Debug.Log($"{Tag} Connection lost.");
                PauseGame();
                ShowBanner();
                OnInternetLost?.Invoke();
            }
        }

        private void PauseGame()
        {
            if (!_settings.PauseGameWhenOffline || _pausedByModule)
                return;

            _savedTimeScale = Time.timeScale;
            if (_savedTimeScale <= 0f)
                _savedTimeScale = 1f;

            Time.timeScale = 0f;
            _pausedByModule = true;
        }

        private void RestoreTimeScale()
        {
            if (!_pausedByModule)
                return;

            Time.timeScale = Mathf.Max(0.01f, _savedTimeScale);
            _pausedByModule = false;
        }

        private void ShowBanner()
        {
            if (!_settings.ShowBanner)
                return;

            OfflineBannerView banner = EnsureBanner();
            if (banner == null)
                return;

            banner.Show();
        }

        private void HideBanner()
        {
            if (_bannerView != null)
                _bannerView.Hide();
        }

        /// <summary>
        /// Resolves the banner once: uses the serialized reference if the integrator wired their own,
        /// otherwise spawns the module's built-in prefab from Resources.
        /// </summary>
        private OfflineBannerView EnsureBanner()
        {
            if (_bannerResolved)
                return _bannerView;

            // Resolve only once — a failed lookup must not be retried on every connectivity check.
            _bannerResolved = true;

            if (_bannerView == null)
            {
                GameObject prefab = _bannerPrefab != null
                    ? _bannerPrefab
                    : Resources.Load<GameObject>(BannerResourcePath);

                if (prefab == null)
                {
                    Debug.LogError($"{Tag} Offline banner prefab not found at Resources/{BannerResourcePath}. Banner will not be shown.");
                    return null;
                }

                GameObject instance = Instantiate(prefab);
                instance.name = "AMZNGoDSDK_OfflineBanner";
                DontDestroyOnLoad(instance);

                _bannerView = instance.GetComponent<OfflineBannerView>();
                if (_bannerView == null)
                {
                    Debug.LogError($"{Tag} Offline banner prefab has no {nameof(OfflineBannerView)} component.");
                    Destroy(instance);
                    return null;
                }
            }

            _bannerView.Configure(_settings);
            _bannerView.OnRetry += HandleRetryRequested;
            _bannerView.Hide(immediate: true);

            return _bannerView;
        }

        private void HandleRetryRequested()
        {
            // Re-poll right away. If we are still offline nothing changes, so no event is raised.
            // A single in-flight immediate check is enough — repeated taps must not stack probes.
            if (!Enabled || _immediateCheckCoroutine != null)
                return;

            _immediateCheckCoroutine = StartCoroutine(ImmediateCheckRoutine());
        }

        private IEnumerator ImmediateCheckRoutine()
        {
            yield return CheckConnectivityRoutine(initial: false);
            _immediateCheckCoroutine = null;
        }

        public override void Cleanup()
        {
            StopMonitoring();

            if (_bannerView != null)
                _bannerView.OnRetry -= HandleRetryRequested;

            // EnsureBanner subscribes OnRetry only on resolve. After tearing the subscription
            // down the banner must be re-resolved, or a later Initialize would show a banner
            // whose retry button no longer triggers a connectivity check.
            _bannerResolved = false;

            HideBanner();

            // Never leave the game frozen if the SDK is torn down while offline.
            RestoreTimeScale();
        }
    }
}
#endif
