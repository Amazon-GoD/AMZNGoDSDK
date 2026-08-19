#if AMZN_INTERNETCONNECTION_ENABLED
using System;
using System.Collections;
using UnityEngine;
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
            CheckConnectivity(initial: true);

            while (Enabled)
            {
                float interval = Mathf.Max(MinCheckInterval, _settings.CheckIntervalSeconds);
                yield return new WaitForSecondsRealtime(interval);
                CheckConnectivity();
            }
        }

        private void CheckConnectivity(bool initial = false)
        {
            bool connected = Application.internetReachability != NetworkReachability.NotReachable;
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
            CheckConnectivity();
        }

        public override void Cleanup()
        {
            if (_monitorCoroutine != null)
            {
                StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }

            if (_bannerView != null)
                _bannerView.OnRetry -= HandleRetryRequested;

            HideBanner();

            // Never leave the game frozen if the SDK is torn down while offline.
            RestoreTimeScale();
        }
    }
}
#endif
