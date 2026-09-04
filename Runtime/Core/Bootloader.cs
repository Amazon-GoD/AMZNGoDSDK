using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Initial scene: waits for SDK init + first cross-promo video to be preloaded,
    /// then loads the main scene. Prevents the 8-second black screen on first Show().
    /// </summary>
    public class Bootloader : MonoBehaviour
    {
        [SerializeField] private string _nextScene = "SampleScene";
        [Tooltip("Max time to wait for preload before giving up and loading the main scene anyway.")]
        [SerializeField] private float _maxWaitSeconds = 15f;

        [Header("UI (optional)")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Slider _progressBar;

        private void Start()
        {
            StartCoroutine(BootCoroutine());
        }

        private IEnumerator BootCoroutine()
        {
            SetStatus("Initializing SDK...");

            var sdk = AmznGoDSDKCore.Instance;

            float elapsed = 0f;
            while (sdk == null && elapsed < _maxWaitSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
                sdk = AmznGoDSDKCore.Instance;
            }

            if (sdk == null)
            {
                Debug.LogWarning("[Bootloader] SDK instance not found, continuing without preload.");
                LoadNext();
                yield break;
            }

            while (!sdk.IsInitialized && elapsed < _maxWaitSeconds)
            {
                UpdateProgress(elapsed);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetStatus("Preloading ad...");

            while (!sdk.IsVideoReady && elapsed < _maxWaitSeconds)
            {
                UpdateProgress(elapsed);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (sdk.IsVideoReady)
                Debug.Log($"[Bootloader] Preload ready in {elapsed:F2}s, loading {_nextScene}.");
            else
                Debug.LogWarning($"[Bootloader] Preload not ready after {_maxWaitSeconds}s, loading {_nextScene} anyway.");

            LoadNext();
        }

        private void LoadNext()
        {
            SceneManager.LoadScene(_nextScene);
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        private void UpdateProgress(float elapsed)
        {
            if (_progressBar != null)
                _progressBar.value = Mathf.Clamp01(elapsed / _maxWaitSeconds);
        }
    }
}
