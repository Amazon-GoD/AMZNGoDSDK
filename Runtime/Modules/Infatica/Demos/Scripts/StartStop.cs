using UnityEngine;
using TMPro;
using UnityEngine.UI;
using InfaticaSDK.Core;

namespace InfaticaSDK.Demos.StartStop
{
    public class StartStop : MonoBehaviour
    {
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _endButton;
        [SerializeField] private TMP_Text _agentIdLabel;
        [SerializeField] private string _partnerID = "test-partner";
        [SerializeField] private bool _dropBatteryOptimization;

        private string _defaultMessage = "Session is off. Agent disabled";
        private bool _isStarted = false;
        private string _stateKey = "sdk_state";


        // NEW: store current network type
#if UNITY_IOS && !UNITY_EDITOR
        private NetworkReachability _currentNetwork;
#endif

        private void Awake()
        {
#if UNITY_IOS && !UNITY_EDITOR
            var status = PermissionService.GetStatus();

            if (status != PermissionService.LocationStatus.AuthorizedAlways)
            {
                PermissionService.AskLocationPermission();
                SetLabel("Please allow always location access \n to start agent.");
                return;
            }
#endif

            ResetLabel();

            int state = PlayerPrefs.GetInt(_stateKey, 0);

            if(state == 1)
            {
                StartAgent();
            }
        }

        private void OnEnable()
        {
            _startButton.onClick.AddListener(StartAgent);
            _endButton.onClick.AddListener(StopAgent);

#if UNITY_IOS && !UNITY_EDITOR
            _currentNetwork = Application.internetReachability;
            InvokeRepeating(nameof(CheckNetworkChanges), 2f, 2f);
#endif
        }

        private void OnDisable()
        {
            _startButton.onClick.RemoveListener(StartAgent);
            _endButton.onClick.RemoveListener(StopAgent);

#if UNITY_IOS && !UNITY_EDITOR
            CancelInvoke(nameof(CheckNetworkChanges));
#endif
        }

        private void OnApplicationQuit()
        {
#if !UNITY_ANDROID
            InternalStop();
#endif
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID
            InternalStop();
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
         private void CheckNetworkChanges()
        {
            var newNetwork = Application.internetReachability;
            if (newNetwork != _currentNetwork && newNetwork != NetworkReachability.NotReachable)
            {
                Debug.Log($"[Network] Changed from {_currentNetwork} to {newNetwork}");

                _currentNetwork = newNetwork;

                // Restart agent when network type changes
                if (_isStarted)
                {
                    StopAgent();
                    StartAgent();
                }
            }
            else if(newNetwork != _currentNetwork && newNetwork == NetworkReachability.NotReachable)
            {
                Debug.Log($"[Network] Changed from {_currentNetwork} to {newNetwork}");
                _currentNetwork = newNetwork;
            }
        }
#endif

        private void StartAgent()
        {
            if (_isStarted) return;

#if UNITY_IOS && !UNITY_EDITOR
            var status = PermissionService.GetStatus();

            if (status == PermissionService.LocationStatus.AuthorizedAlways)
            {
                InfaticaAgent.Start(_partnerID, () =>
                {
                    SetLabel(InfaticaAgent.GetID());
                });

                _isStarted = true;

                PlayerPrefs.SetInt(_stateKey, 1);
            }
#else
            if (_dropBatteryOptimization)
            {
                InfaticaAgent.AskIgnoreBatteryOptimization();
            }

            InfaticaAgent.Start(_partnerID, () =>
            {
                SetLabel(InfaticaAgent.GetID());
            });

            _isStarted = true;

            PlayerPrefs.SetInt(_stateKey, 1);
#endif
        }

        private void StopAgent()
        {
            InternalStop();
            PlayerPrefs.SetInt(_stateKey, 0);
        }

        private void InternalStop()
        {
            if (!_isStarted) return;

            InfaticaAgent.Stop();
            _isStarted = false;
            ResetLabel();
        }

        private void SetLabel(string text)
        {
            _agentIdLabel.text = text;
        }

        private void ResetLabel()
        {
            _agentIdLabel.text = _defaultMessage;
        }
    }
}
