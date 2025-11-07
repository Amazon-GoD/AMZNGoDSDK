using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;
using TMPro;
using Button = UnityEngine.UI.Button;
using UnityEngine.EventSystems;
using System;
using static Pyro.CrossPromoConfigurationManager;
using System.Linq;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace Pyro
{
    [RequireComponent(typeof(VideoPlayer))]
    public class CrossPromoVideoManager : MonoBehaviour
    {
        [SerializeField] Button _openButton;
        [SerializeField] Button _closeButton;
        [SerializeField] Button _muteButton;

        [SerializeField] Sprite _unmuteSprite;
        [SerializeField] Sprite _muteSprite;

        [SerializeField] RawImage _imageRenderer;
        [SerializeField] TMP_Text _openButtonLabel;

        [SerializeField] Transform _crossAdPanel;
        [SerializeField] Transform _overlay;

        public bool IsReady { get { return _isReady; } }

        private Dictionary<string, string> _localPaths = new Dictionary<string, string>();

        List<string> downloadedVideos = new List<string>();
        public bool isVideoDownloaded(string title) => downloadedVideos.Contains(title);

        private VideoPlayer _videoPlayer;

        private Coroutine _closeButtonCoroutine;
        private Coroutine _overlayButtonCoroutine;

        private bool _isReady;
        private bool _isMute;
        private float _gameTimeScale;

        private void Awake()
        {
            _closeButtonCoroutine = null;
            _overlayButtonCoroutine = null;
            _videoPlayer = GetComponent<VideoPlayer>();

            _gameTimeScale = Time.timeScale;
            _isReady = false;
            _isMute = false;
            _videoPlayer.started += (vp) => TrackingManager.MetricaTracking("crosspromo_show_success");
        }
        private void Start()
        {
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.prepareCompleted += OnPrepareCompleted;

            ResizeRenderTexture(Screen.width, Screen.height);

            _muteButton.onClick.AddListener(() =>
            {
                if (_isMute)
                {
                    _muteButton.image.sprite = _muteSprite;
                    _videoPlayer.SetDirectAudioMute(0, false);
                    _isMute = false;
                }
                else
                {
                    _muteButton.image.sprite = _unmuteSprite;
                    _videoPlayer.SetDirectAudioMute(0, true);
                    _isMute = true;
                }
            });
        }

        #region Init
        public IEnumerator Initialize(PromosConfigurationInfo _crossPromoConfigFirst)
        {
            List<string> completeVideoFileNames = new List<string>();
            List<string> videoUrls = new List<string>();
            List<string> titles = new List<string>();

            if (_crossPromoConfigFirst.Videos.Count > 0)
            {
                _crossPromoConfigFirst.Videos.ForEach(v =>
                {
                    string[] nameComponents = v.FileName.Split('.');
                    if (nameComponents.Length > 0)
                    {
                        string completeVideoFileName = $"{nameComponents[0]}.{v.FileExtension.ToString()}";
                        completeVideoFileNames.Add(completeVideoFileName);
                        videoUrls.Add(v.VideoUrl);
                        titles.Add(v.Title);
                    }
                });
            }
            yield return DownloadVideos(completeVideoFileNames, videoUrls, titles);
        }
        private IEnumerator DownloadVideos(List<string> completeVideoFileNames, List<string> videoUrls, List<string> titles)
        {
            if (videoUrls.Count == 0) yield break;
            for (int i = 0; i < titles.Count; i++)
            {
                yield return DownloadVideo(completeVideoFileNames[i], videoUrls[i], titles[i]);
                _isReady = true;
            }

            DeleteVideos(completeVideoFileNames);

            CrossPromoManager.Instance.OnAdsLoadedCallback?.Invoke();
        }
        private IEnumerator DownloadVideo(string videoName, string url, string videoTitle)
        {
            string localPath = Path.Combine(Application.persistentDataPath, videoName);

            UnityWebRequest request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();
            Debug.Log($"Downloading {videoName} started");
            while (!operation.isDone) yield return null;

            if (request.result == UnityWebRequest.Result.Success) Debug.Log("video downloading process completed successfully, received: " + request.downloadHandler.text);
            else Debug.LogError("Error in video downloading process: " + request.error);

            bool videoExists = false;
            if (File.Exists(localPath))
            {
                videoExists = FileHashUtility.CompareExistingFileWithDownloadedData(localPath, request.downloadHandler.data);
            }

            if (videoExists == false)
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    yield return File.WriteAllBytesAsync(localPath, request.downloadHandler.data);
                    Debug.Log($"Downloaded and saved {videoName} to {localPath}");
                }
                else
                {
                    Debug.LogError($"Failed to download {videoName}: {request.error}");
                }
            }
            else Debug.Log($"Video {videoName}: is already exist");

            downloadedVideos.Add(videoTitle);
            _localPaths.Add(videoName, localPath); // Add to local paths list
        }
        #endregion

        #region ShowAd
        public void PlayVideo(string id, PromoConfiguration videoInfo, Action onClose)
        {
            _gameTimeScale = Time.timeScale;
            Time.timeScale = 0;

            InitializeAdUI(videoInfo.Title, videoInfo.ButtonText, videoInfo.RedirectUrl, videoInfo.TrackingUrl, onClose);

            _videoPlayer.url = _localPaths[id]; // Set the local file path to the VideoPlayer
            ClearRenderTexture(_videoPlayer.targetTexture);
            _videoPlayer.Prepare();

            _overlayButtonCoroutine = StartCoroutine(ShowOverlay(videoInfo.OverlayShowDelayInSeconds));
            _closeButtonCoroutine = StartCoroutine(ShowCloseButton(videoInfo.CloseShowDelayInSeconds));
        }
        private void InitializeAdUI(string title, string buttonText, string redirectUrl, string TrackingUrl, Action onClose)
        {
            AudioListener.volume = 0f;
            _openButton.onClick.RemoveAllListeners();
            _closeButton.onClick.RemoveAllListeners();

            string placement = (onClose == null) ? "interstitial" : "rewarded";
            AddOpenButtonEvents(redirectUrl, TrackingUrl, title, placement);

            _closeButton.onClick.AddListener(() =>
            {
                _videoPlayer.Stop();

                int showCount = PlayerPrefs.GetInt(title, 0);
                PlayerPrefs.SetInt(title, showCount + 1);
                Debug.Log(title + " show saved");

                Time.timeScale = _gameTimeScale;
                AudioListener.volume = 1f;
                onClose?.Invoke();
                CrossPromoManager.Instance.AdCloseCallback?.Invoke();
                _overlay.gameObject.SetActive(false);
                _crossAdPanel.gameObject.SetActive(false);

                ClearCoroutines();
            });

            _openButtonLabel.color = _openButton.colors.normalColor;

            _openButtonLabel.SetText(buttonText);

            //_secondsLabel.gameObject.SetActive(true);
            _closeButton.gameObject.SetActive(false);
            _overlay.gameObject.SetActive(false);
            _crossAdPanel.gameObject.SetActive(true);
        }
        #endregion

        #region smallFuncs
        private void ClearRenderTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null) return;

            // Set the RenderTexture as active
            RenderTexture activeRT = RenderTexture.active;
            RenderTexture.active = renderTexture;

            // Clear the texture with a solid color
            GL.Clear(true, true, Color.black);

            // Restore the previously active RenderTexture
            RenderTexture.active = activeRT;
        }

        private void ResizeRenderTexture(int width, int height)
        {
            RenderTexture currentTexture = _videoPlayer.targetTexture;

            if (currentTexture != null)
                currentTexture.Release();

            RenderTexture newTexture = new RenderTexture(width, height, 16);
            newTexture.Create();

            _videoPlayer.targetTexture = newTexture;
            _imageRenderer.texture = newTexture;
        }
        private void DeleteVideos(List<string> videos)
        {
            string[] videoExtensions = { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg" };
            var videoFiles = Directory.GetFiles(Application.persistentDataPath)
            .Where(file => videoExtensions.Contains(Path.GetExtension(file).ToLower())).Select(Path.GetFileName);

            foreach (string video in videoFiles)
            {
                if (videos.Contains(video) || !video.Contains("mp4")) continue;
                File.Delete(Path.Combine(Application.persistentDataPath, video));
            }
        }
        private void OnPrepareCompleted(VideoPlayer source)
        {
            Debug.Log("Video is prepared and ready to play.");
            _videoPlayer.Play();
        }
        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError($"VideoPlayer error: {message}");

            _overlay.gameObject.SetActive(false);
            _crossAdPanel.gameObject.SetActive(false);

            CrossPromoManager.Instance.OnPlayErrorCallback?.Invoke(message);
            TrackingManager.MetricaTracking("crosspromo_show_fail", message);
        }
        private void ClearCoroutines()
        {
            if (_closeButtonCoroutine != null)
                StopCoroutine(_closeButtonCoroutine);

            if (_overlayButtonCoroutine != null)
                StopCoroutine(_overlayButtonCoroutine);

            _closeButtonCoroutine = null;
            _overlayButtonCoroutine = null;
        }
        private void AddOpenButtonEvents(string redirectUrl, string TrackingUrl, string videoName, string placement)
        {
            // Add the EventTrigger component if not already present
            EventTrigger eventTrigger = _openButton.gameObject.GetComponent<EventTrigger>();
            if (eventTrigger == null)
            {
                eventTrigger = _openButton.gameObject.AddComponent<EventTrigger>();
            }

            // Create PointerUp event entry
            EventTrigger.Entry pointerEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            EventTrigger.Entry pointerExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };

            // Add a callback to the PointerUp event
            pointerEnter.callback.AddListener((data) => { _openButtonLabel.color = _openButton.colors.highlightedColor; });
            pointerExit.callback.AddListener((data) => { _openButtonLabel.color = _openButton.colors.normalColor; });

            // Add the entry to the EventTrigger
            eventTrigger.triggers.Add(pointerEnter);
            eventTrigger.triggers.Add(pointerExit);

            _openButton.onClick.AddListener(() =>
            {
                TrackingManager.UrlTracking(redirectUrl, TrackingUrl, videoName, placement);
                CrossPromoManager.Instance.AdClickCallback?.Invoke();
            });
        }
        IEnumerator ShowOverlay(int seconds)
        {
            float elapsedTime = 0f;

            while (elapsedTime < (float)seconds)
            {
                elapsedTime += Time.unscaledDeltaTime;
                yield return null; // Wait for the next frame
            }

            _overlay.gameObject.SetActive(true);
        }
        IEnumerator ShowCloseButton(int seconds)
        {
            //_secondsLabel.SetText(seconds.ToString());

            float elapsedTime = 0f;
            while (elapsedTime < (float)seconds)
            {
                elapsedTime += Time.unscaledDeltaTime;
                int elapsedSeconds = Convert.ToInt32(Mathf.Round(elapsedTime));
                int displayTime = seconds - elapsedSeconds;
                //_secondsLabel.SetText(displayTime.ToString());
                yield return null; // Wait for the next frame
            }

            //_secondsLabel.gameObject.SetActive(false);
            _closeButton.gameObject.SetActive(true);

            ClearCoroutines();
        }
        #endregion
    }
}
