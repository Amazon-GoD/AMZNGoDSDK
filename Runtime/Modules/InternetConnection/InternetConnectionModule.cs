using System;
using System.Collections;
using UnityEngine;
namespace AMZNGoDSDK.Runtime
{
    [DisallowMultipleComponent]
    public sealed class InternetConnectionModule : ModuleBase
    {
        private const float MinCheckInterval = 1f;

        private InternetConnectionSettingData _settings = new();
        private Coroutine _monitorCoroutine;
        private bool _hasInternet = true;
        private bool _pausedByModule;
        private float _savedTimeScale = 1f;
        [SerializeField] private GameObject _connectionBanner;

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
                HideBanner();
                RestoreTimeScale();
                OnInternetAvailable?.Invoke();
            }
            else
            {
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
            if (!_settings.ShowBanner || _connectionBanner == null)
                return;

            _connectionBanner.SetActive(true);
        }

        private void HideBanner()
        {
            if (_connectionBanner != null)
                _connectionBanner.SetActive(false);
        }

        public override void Cleenup()
        {
            if (_monitorCoroutine != null)
            {
                StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }

            HideBanner();
        }
    }
}

