#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Cross-platform video player with two backends:
    /// <list type="bullet">
    /// <item><see cref="VideoPlayerBackend.UnityVideoPlayer"/> — works on all platforms.</item>
    /// <item><see cref="VideoPlayerBackend.Media3"/> — Android-only via AndroidX Media3 ExoPlayer.
    /// On non-Android platforms fires <see cref="OnError"/> immediately.</item>
    /// </list>
    /// </summary>
    public class CrossPromoVideoPlayer : MonoBehaviour
    {
        public enum PlaybackState
        {
            Idle,
            Loading,
            Ready,
            Playing,
            Paused,
            Completed,
            Error
        }

        #region Events

        public event Action<PlaybackState, PlaybackState> OnStateChanged;
        public event Action OnReady;
        public event Action OnStarted;
        public event Action OnCompleted;
        public event Action OnLoopPointReached;
        public event Action<string> OnError;

        #endregion

        #region Serialized Settings

        [Header("Playback")]
        [SerializeField] private bool _autoPlay = true;
        [SerializeField] private bool _loop;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private string _videoUrl;

        [Header("Rendering")]
        [Tooltip("Optional. If not set, a RenderTexture matching the video resolution is created automatically.")]
        [SerializeField] private RenderTexture _targetTexture;
        [SerializeField] private RawImage _targetRawImage;
        [SerializeField] private List<MeshRenderer> _targetMeshRenderers = new();

        [Header("Error Handling")]
        [SerializeField, Range(0, 10)] private int _maxRetries = 3;
        [SerializeField] private float _retryDelay = 2f;

        #endregion

        #region Private State — Shared

        private VideoPlayerBackend _backend = VideoPlayerBackend.UnityVideoPlayer;
        private PlaybackState _state = PlaybackState.Idle;
        private int _retryCount;
        private bool _wasPausedByApp;
        private Coroutine _retryCoroutine;
        private string _preloadedUrl;
        private bool _isPreloaded;

        #endregion

        #region Private State — Unity VideoPlayer

        private VideoPlayer _player;
        private bool _renderTextureOwned;

        #endregion

        #region Private State — Media3 (Android)

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _media3Plugin;
        private Texture2D _media3Texture;
        private bool _media3Ready;
        private float _media3SavedVolume = 1f;
        private bool _media3Muted;
#endif

        #endregion

        #region Properties

        public PlaybackState State => _state;
        public bool IsPlaying => _state == PlaybackState.Playing;
        public bool IsPaused => _state == PlaybackState.Paused;
        public bool IsLoading => _state == PlaybackState.Loading;

        public double Duration
        {
            get
            {
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (_media3Plugin != null && _media3Ready)
                        return _media3Plugin.Call<long>("getDuration") / 1000.0;
#endif
                    return 0;
                }

                return _player != null && _player.isPrepared ? _player.length : 0;
            }
        }

        public double CurrentTime
        {
            get
            {
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (_media3Plugin != null && _media3Ready)
                        return _media3Plugin.Call<long>("getCurrentPosition") / 1000.0;
#endif
                    return 0;
                }

                return _player != null ? _player.time : 0;
            }
        }

        public float Progress => Duration > 0 ? (float)(CurrentTime / Duration) : 0f;

        public ulong FrameCount => _player != null ? _player.frameCount : 0;

        public long CurrentFrame => _player != null ? _player.frame : 0;

        public int Width
        {
            get
            {
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    return _media3Texture != null ? _media3Texture.width : 0;
#else
                    return 0;
#endif
                }
                return _player != null && _player.isPrepared ? (int)_player.width : 0;
            }
        }

        public int Height
        {
            get
            {
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    return _media3Texture != null ? _media3Texture.height : 0;
#else
                    return 0;
#endif
                }
                return _player != null && _player.isPrepared ? (int)_player.height : 0;
            }
        }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (_media3Plugin != null)
                        _media3Plugin.Call("setVolume", _volume);
#endif
                }
                else if (_player != null)
                {
                    _player.SetDirectAudioVolume(0, _volume);
                }
            }
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (_media3Plugin != null)
                        _media3Plugin.Call("setLooping", _loop);
#endif
                }
                else if (_player != null)
                {
                    _player.isLooping = value;
                }
            }
        }

        public bool AutoPlay
        {
            get => _autoPlay;
            set => _autoPlay = value;
        }

        public RenderTexture TargetTexture => _targetTexture;

        public Texture CurrentTexture
        {
            get
            {
                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    return _media3Texture;
#else
                    return null;
#endif
                }
                return _player?.texture;
            }
        }

        public VideoPlayer UnityPlayer => _player;
        public VideoPlayerBackend CurrentBackend => _backend;

        #endregion

        #region Public API — Backend Selection

        public void SetBackend(VideoPlayerBackend backend)
        {
            _backend = backend;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_backend == VideoPlayerBackend.UnityVideoPlayer)
                EnsureUnityVideoPlayer();
        }

        private void Start()
        {
            if (_autoPlay && !string.IsNullOrWhiteSpace(_videoUrl))
                Load(_videoUrl);
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_backend == VideoPlayerBackend.Media3 && _media3Ready && _media3Plugin != null)
            {
                _media3Plugin.Call("updateTexture");
            }
#endif
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (_state == PlaybackState.Idle || _state == PlaybackState.Error)
                return;

            if (pauseStatus)
            {
                if (_state == PlaybackState.Playing)
                {
                    _wasPausedByApp = true;
                    PauseInternal();
                }
            }
            else if (_wasPausedByApp && _state == PlaybackState.Paused)
            {
                _wasPausedByApp = false;
                ResumeInternal();
            }
        }

        private void OnDestroy()
        {
            CancelRetry();

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Cleanup();
#endif
            }
            else
            {
                UnsubscribePlayerEvents();
                if (_player != null)
                    _player.Stop();
                ReleaseOwnedTexture();
            }
        }

        #endregion

        #region Public API — Loading

        public void Load(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[CrossPromoVideoPlayer] Cannot load: URL is empty.");
                return;
            }

            CancelRetry();
            _videoUrl = url;
            _retryCount = 0;

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Load(url);
#else
                Debug.LogWarning("[CrossPromoVideoPlayer] Media3 backend is only supported on Android. Video will not play.");
                SetState(PlaybackState.Error);
                OnError?.Invoke("Media3 backend is only supported on Android.");
#endif
            }
            else
            {
                if (_player == null)
                    EnsureUnityVideoPlayer();
                PrepareVideo();
            }
        }

        public void LoadFromStreamingAssets(string fileName)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            Load(path);
        }

        #endregion

        #region Public API — Preloading

        public bool IsPreloaded => _isPreloaded;
        public string PreloadedUrl => _preloadedUrl;

        /// <summary>
        /// Preloads (buffers) a video URL without starting playback.
        /// When ready, <see cref="OnReady"/> fires and <see cref="IsPreloaded"/> becomes true.
        /// Call <see cref="PlayPreloaded"/> to start playback instantly.
        /// </summary>
        public void Preload(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            _isPreloaded = false;
            _preloadedUrl = url;

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Preload(url);
#endif
            }
            else
            {
                _autoPlay = false;
                if (_player == null) EnsureUnityVideoPlayer();
                _videoUrl = url;
                PrepareVideo();
            }
        }

        /// <summary>
        /// Starts playback of the preloaded video. If the URL doesn't match or nothing
        /// was preloaded, falls through to a normal <see cref="Load"/>.
        /// Returns true if the preloaded video was used.
        /// </summary>
        public bool PlayPreloaded(string url)
        {
            if (_isPreloaded && _preloadedUrl == url && _state == PlaybackState.Ready)
            {
                Debug.Log("[CrossPromoVideoPlayer] Playing preloaded video.");
                _isPreloaded = false;
                _preloadedUrl = null;

                ApplyTextureToTargets();

                if (_backend == VideoPlayerBackend.Media3)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    Media3Resume();
                    SetState(PlaybackState.Playing);
                    OnStarted?.Invoke();
#endif
                }
                else
                {
                    _player?.Play();
                    SetState(PlaybackState.Playing);
                }

                return true;
            }

            Debug.Log("[CrossPromoVideoPlayer] No matching preload; falling back to normal Load.");
            _isPreloaded = false;
            _preloadedUrl = null;
            _autoPlay = true;
            Load(url);
            return false;
        }

        #endregion

        #region Public API — Playback Control

        public void Play()
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Resume();
                SetState(PlaybackState.Playing);
#endif
                return;
            }

            if (_player == null) return;

            switch (_state)
            {
                case PlaybackState.Ready:
                case PlaybackState.Paused:
                    _player.Play();
                    SetState(PlaybackState.Playing);
                    break;

                case PlaybackState.Completed:
                    _player.time = 0;
                    _player.Play();
                    SetState(PlaybackState.Playing);
                    break;

                case PlaybackState.Idle when !string.IsNullOrWhiteSpace(_videoUrl):
                    _autoPlay = true;
                    Load(_videoUrl);
                    break;
            }
        }

        public void Pause()
        {
            if (_state != PlaybackState.Playing) return;
            PauseInternal();
        }

        public void Resume()
        {
            if (_state != PlaybackState.Paused) return;
            ResumeInternal();
        }

        public void TogglePause()
        {
            if (_state == PlaybackState.Playing) Pause();
            else if (_state == PlaybackState.Paused) Resume();
        }

        public void Stop()
        {
            CancelRetry();

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Stop();
#endif
            }
            else
            {
                if (_player == null) return;
                _player.Stop();
            }

            SetState(PlaybackState.Idle);
        }

        public void Restart()
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_media3Plugin != null && _media3Ready)
                {
                    _media3Plugin.Call("seekTo", 0L);
                    Media3Resume();
                    SetState(PlaybackState.Playing);
                }
#endif
                return;
            }

            if (_player == null || !_player.isPrepared) return;
            _player.time = 0;
            _player.Play();
            SetState(PlaybackState.Playing);
        }

        public void Retry()
        {
            if (string.IsNullOrWhiteSpace(_videoUrl)) return;
            _retryCount = 0;

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Load(_videoUrl);
#endif
            }
            else
            {
                PrepareVideo();
            }
        }

        #endregion

        #region Public API — Seeking

        public void SeekTo(double seconds)
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_media3Plugin != null && _media3Ready)
                    _media3Plugin.Call("seekTo", (long)(seconds * 1000));
#endif
                return;
            }

            if (_player == null || !_player.isPrepared) return;
            _player.time = Math.Max(0, Math.Min(seconds, _player.length));
        }

        public void SeekNormalized(float t)
        {
            SeekTo(Mathf.Clamp01(t) * Duration);
        }

        public void SeekRelative(double offsetSeconds)
        {
            SeekTo(CurrentTime + offsetSeconds);
        }

        #endregion

        #region Public API — Audio

        public void SetMute(bool mute)
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                _media3Muted = mute;
                if (_media3Plugin != null)
                {
                    if (mute)
                    {
                        _media3SavedVolume = _volume;
                        _media3Plugin.Call("setVolume", 0f);
                    }
                    else
                    {
                        _media3Plugin.Call("setVolume", _media3SavedVolume);
                    }
                }
#endif
                return;
            }

            if (_player != null)
                _player.SetDirectAudioMute(0, mute);
        }

        public bool IsMuted()
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _media3Muted;
#else
                return false;
#endif
            }

            return _player != null && _player.GetDirectAudioMute(0);
        }

        public void ToggleMute()
        {
            SetMute(!IsMuted());
        }

        #endregion

        #region Public API — Render Targets

        public void SetTargetMeshRenderers(List<MeshRenderer> renderers)
        {
            _targetMeshRenderers = renderers ?? new List<MeshRenderer>();
            ApplyTextureToTargets();
        }

        public void SetTargetRawImage(RawImage rawImage)
        {
            _targetRawImage = rawImage;
            ApplyTextureToTargets();
        }

        #endregion

        #region Internal — Unity VideoPlayer Setup

        private void EnsureUnityVideoPlayer()
        {
            _player = GetComponent<VideoPlayer>();
            if (_player == null)
                _player = gameObject.AddComponent<VideoPlayer>();

            _player.playOnAwake = false;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.isLooping = _loop;
            _player.SetDirectAudioVolume(0, _volume);

            if (_targetTexture != null)
                _player.targetTexture = _targetTexture;

            SubscribePlayerEvents();
        }

        private void SubscribePlayerEvents()
        {
            _player.prepareCompleted += HandlePrepareCompleted;
            _player.errorReceived += HandleErrorReceived;
            _player.loopPointReached += HandleLoopPointReached;
            _player.started += HandleStarted;
        }

        private void UnsubscribePlayerEvents()
        {
            if (_player == null) return;

            _player.prepareCompleted -= HandlePrepareCompleted;
            _player.errorReceived -= HandleErrorReceived;
            _player.loopPointReached -= HandleLoopPointReached;
            _player.started -= HandleStarted;
        }

        #endregion

        #region Internal — Unity VideoPlayer Prepare & Retry

        private void PrepareVideo()
        {
            SetState(PlaybackState.Loading);

            _player.Stop();
            _player.source = VideoSource.Url;
            _player.url = _videoUrl;
            _player.isLooping = _loop;
            _player.Prepare();
        }

        private void CancelRetry()
        {
            if (_retryCoroutine != null)
            {
                StopCoroutine(_retryCoroutine);
                _retryCoroutine = null;
            }
        }

        private IEnumerator RetryAfterDelay()
        {
            yield return new WaitForSecondsRealtime(_retryDelay);
            _retryCoroutine = null;

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Load(_videoUrl);
#endif
            }
            else
            {
                PrepareVideo();
            }
        }

        #endregion

        #region Internal — Unity VideoPlayer Event Handlers

        private void HandlePrepareCompleted(VideoPlayer source)
        {
            SetState(PlaybackState.Ready);
            EnsureRenderTexture();
            ApplyTextureToTargets();
            OnReady?.Invoke();

            if (_autoPlay)
            {
                Play();
            }
            else if (!string.IsNullOrEmpty(_preloadedUrl))
            {
                _isPreloaded = true;
                Debug.Log($"[CrossPromoVideoPlayer] Preload ready for: {_preloadedUrl}");
            }
        }

        private void HandleStarted(VideoPlayer source)
        {
            SetState(PlaybackState.Playing);
            OnStarted?.Invoke();
        }

        private void HandleLoopPointReached(VideoPlayer source)
        {
            OnLoopPointReached?.Invoke();

            if (!_loop)
            {
                SetState(PlaybackState.Completed);
                OnCompleted?.Invoke();
            }
        }

        private void HandleErrorReceived(VideoPlayer source, string message)
        {
            Debug.LogError($"[CrossPromoVideoPlayer] Error: {message}");

            if (_retryCount < _maxRetries)
            {
                _retryCount++;
                Debug.Log($"[CrossPromoVideoPlayer] Retrying ({_retryCount}/{_maxRetries}) in {_retryDelay}s...");
                CancelRetry();
                _retryCoroutine = StartCoroutine(RetryAfterDelay());
            }
            else
            {
                SetState(PlaybackState.Error);
                OnError?.Invoke(message);
            }
        }

        #endregion

        #region Internal — Rendering (Unity VideoPlayer)

        private void EnsureRenderTexture()
        {
            int w = (int)_player.width;
            int h = (int)_player.height;

            if (w <= 0 || h <= 0)
            {
                w = 1920;
                h = 1080;
            }

            if (_player.targetTexture != null
                && _player.targetTexture.width == w
                && _player.targetTexture.height == h)
                return;

            ReleaseOwnedTexture();

            _targetTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _targetTexture.Create();
            _renderTextureOwned = true;
            _player.targetTexture = _targetTexture;
        }

        private void ReleaseOwnedTexture()
        {
            if (_player != null && _player.targetTexture == _targetTexture)
                _player.targetTexture = null;

            if (_renderTextureOwned && _targetTexture != null)
            {
                _targetTexture.Release();
                Destroy(_targetTexture);
            }

            _targetTexture = null;
            _renderTextureOwned = false;
        }

        private void ApplyTextureToTargets()
        {
            Texture texture;

            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                texture = _media3Texture;
#else
                texture = null;
#endif
            }
            else
            {
                texture = _player?.targetTexture;
            }

            if (texture == null) return;

            if (_targetRawImage != null)
            {
                _targetRawImage.texture = texture;
                // Flip only video UVs; keep UI hierarchy transform unchanged.
                _targetRawImage.uvRect = _backend == VideoPlayerBackend.Media3
                    ? new Rect(0f, 1f, 1f, -1f)
                    : new Rect(0f, 0f, 1f, 1f);
                ApplyRawImageAspect(texture);
            }

            for (int i = 0; i < _targetMeshRenderers.Count; i++)
            {
                if (_targetMeshRenderers[i] != null)
                    _targetMeshRenderers[i].material.mainTexture = texture;
            }
        }

        private void ApplyRawImageAspect(Texture texture)
        {
            if (_targetRawImage == null || texture == null || texture.width <= 0 || texture.height <= 0)
                return;

            var fitter = _targetRawImage.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = _targetRawImage.gameObject.AddComponent<AspectRatioFitter>();

            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)texture.width / texture.height;
        }

        #endregion

        #region Internal — State Machine

        private void PauseInternal()
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Pause();
#endif
            }
            else
            {
                if (_player == null) return;
                _player.Pause();
            }

            SetState(PlaybackState.Paused);
        }

        private void ResumeInternal()
        {
            if (_backend == VideoPlayerBackend.Media3)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Media3Resume();
#endif
            }
            else
            {
                if (_player == null) return;
                _player.Play();
            }

            SetState(PlaybackState.Playing);
        }

        private void SetState(PlaybackState newState)
        {
            if (_state == newState) return;

            var prev = _state;
            _state = newState;
            OnStateChanged?.Invoke(prev, newState);
        }

        #endregion

        #region Media3 — Android Implementation

#if UNITY_ANDROID && !UNITY_EDITOR

        private void Media3Load(string url)
        {
            SetState(PlaybackState.Loading);
            Media3Cleanup();

            _media3Ready = false;
            _media3Plugin = new AndroidJavaObject("com.amzngod.media3.Media3VideoPlugin");
            _media3Plugin.Call("init", gameObject.name);
            _media3Plugin.Call("setLooping", _loop);
            _media3Plugin.Call("load", url);
        }

        private void Media3Preload(string url)
        {
            SetState(PlaybackState.Loading);
            Media3Cleanup();

            _media3Ready = false;
            _media3Plugin = new AndroidJavaObject("com.amzngod.media3.Media3VideoPlugin");
            _media3Plugin.Call("init", gameObject.name);
            _media3Plugin.Call("setLooping", _loop);
            _media3Plugin.Call("preload", url);
        }

        private void Media3Resume()
        {
            if (_media3Plugin != null)
                _media3Plugin.Call("play");
        }

        private void Media3Pause()
        {
            if (_media3Plugin != null)
                _media3Plugin.Call("pause");
        }

        private void Media3Stop()
        {
            if (_media3Plugin != null)
            {
                try { _media3Plugin.Call("release"); } catch (Exception) { }
            }
            _media3Ready = false;
        }

        private void Media3Cleanup()
        {
            if (_media3Plugin != null)
            {
                try { _media3Plugin.Call("release"); } catch (Exception) { }
            }

            if (_media3Texture != null)
            {
                Destroy(_media3Texture);
                _media3Texture = null;
            }

            _media3Plugin = null;
            _media3Ready = false;
        }

#endif

        /// <summary>Called from Java via UnitySendMessage with format "textureId|width|height".</summary>
        private void OnMedia3Prepared(string mediaInfo)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_media3Texture != null)
            {
                Destroy(_media3Texture);
                _media3Texture = null;
            }

            if (string.IsNullOrEmpty(mediaInfo))
                return;

            string[] parts = mediaInfo.Split('|');
            if (parts.Length < 3)
            {
                Debug.LogError($"[CrossPromoVideoPlayer] OnMedia3Prepared: invalid data '{mediaInfo}'");
                return;
            }

            int textureId = int.Parse(parts[0]);
            int width = int.Parse(parts[1]);
            int height = int.Parse(parts[2]);

            Debug.Log($"{width}, {height}");

            IntPtr nativePtr = (IntPtr)textureId;
            _media3Texture = Texture2D.CreateExternalTexture(width, height, TextureFormat.ARGB32, false, true, nativePtr);

            ApplyTextureToTargets();

            _media3Ready = true;
            SetState(PlaybackState.Ready);
            OnReady?.Invoke();

            if (_autoPlay)
            {
                Media3Resume();
                SetState(PlaybackState.Playing);
                OnStarted?.Invoke();
            }
            else if (!string.IsNullOrEmpty(_preloadedUrl))
            {
                _isPreloaded = true;
                Debug.Log($"[CrossPromoVideoPlayer] Preload ready for: {_preloadedUrl}");
            }
#endif
        }

        /// <summary>Called from Java via UnitySendMessage when playback ends.</summary>
        private void OnMedia3Completed(string unused)
        {
            OnLoopPointReached?.Invoke();

            if (!_loop)
            {
                SetState(PlaybackState.Completed);
                OnCompleted?.Invoke();
            }
        }

        /// <summary>Called from Java via UnitySendMessage on playback error.</summary>
        private void OnMedia3Error(string message)
        {
            Debug.LogError($"[CrossPromoVideoPlayer] Media3 error: {message}");

            if (_retryCount < _maxRetries)
            {
                _retryCount++;
                Debug.Log($"[CrossPromoVideoPlayer] Retrying ({_retryCount}/{_maxRetries}) in {_retryDelay}s...");
                CancelRetry();
                _retryCoroutine = StartCoroutine(RetryAfterDelay());
            }
            else
            {
                SetState(PlaybackState.Error);
                OnError?.Invoke(message);
            }
        }

        #endregion

        #region Static Helpers

        public static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);

            return ts.Hours > 0
                ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        #endregion
    }
}
#endif
