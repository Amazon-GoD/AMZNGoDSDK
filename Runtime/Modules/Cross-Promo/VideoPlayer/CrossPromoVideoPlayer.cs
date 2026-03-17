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
    /// Cross-platform video player built on Unity's <see cref="VideoPlayer"/>.
    /// Provides a clean state machine, automatic retry, multiple render targets,
    /// application lifecycle handling, and a rich C# event API.
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

        /// <summary>Fired on every state transition with (previousState, newState).</summary>
        public event Action<PlaybackState, PlaybackState> OnStateChanged;

        /// <summary>Video is prepared and ready to play.</summary>
        public event Action OnReady;

        /// <summary>First frame rendered after Play().</summary>
        public event Action OnStarted;

        /// <summary>Video reached the end (only when <see cref="Loop"/> is false).</summary>
        public event Action OnCompleted;

        /// <summary>Loop point reached (fires even when looping).</summary>
        public event Action OnLoopPointReached;

        /// <summary>Unrecoverable error after all retries exhausted.</summary>
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

        #region Private State

        private VideoPlayer _player;
        private PlaybackState _state = PlaybackState.Idle;
        private int _retryCount;
        private bool _wasPausedByApp;
        private bool _renderTextureOwned;
        private Coroutine _retryCoroutine;

        #endregion

        #region Properties

        public PlaybackState State => _state;
        public bool IsPlaying => _state == PlaybackState.Playing;
        public bool IsPaused => _state == PlaybackState.Paused;
        public bool IsLoading => _state == PlaybackState.Loading;

        /// <summary>Total video duration in seconds. Zero if not yet prepared.</summary>
        public double Duration => _player != null && _player.isPrepared ? _player.length : 0;

        /// <summary>Current playback position in seconds.</summary>
        public double CurrentTime => _player != null ? _player.time : 0;

        /// <summary>Playback progress normalized 0..1.</summary>
        public float Progress => Duration > 0 ? (float)(CurrentTime / Duration) : 0f;

        /// <summary>Total frame count. Zero if not yet prepared.</summary>
        public ulong FrameCount => _player != null ? _player.frameCount : 0;

        /// <summary>Current frame index.</summary>
        public long CurrentFrame => _player != null ? _player.frame : 0;

        /// <summary>Video width in pixels. Zero if not prepared.</summary>
        public int Width => _player != null && _player.isPrepared ? (int)_player.width : 0;

        /// <summary>Video height in pixels. Zero if not prepared.</summary>
        public int Height => _player != null && _player.isPrepared ? (int)_player.height : 0;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                if (_player != null)
                    _player.SetDirectAudioVolume(0, _volume);
            }
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                if (_player != null)
                    _player.isLooping = value;
            }
        }

        public bool AutoPlay
        {
            get => _autoPlay;
            set => _autoPlay = value;
        }

        /// <summary>The RenderTexture the video is rendered into (may be auto-created).</summary>
        public RenderTexture TargetTexture => _targetTexture;

        /// <summary>The texture currently produced by the VideoPlayer (null until first frame).</summary>
        public Texture CurrentTexture => _player?.texture;

        /// <summary>Underlying Unity VideoPlayer for advanced use.</summary>
        public VideoPlayer UnityPlayer => _player;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            EnsureVideoPlayer();
        }

        private void Start()
        {
            if (_autoPlay && !string.IsNullOrWhiteSpace(_videoUrl))
                Load(_videoUrl);
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
            UnsubscribePlayerEvents();

            if (_player != null)
                _player.Stop();

            ReleaseOwnedTexture();
        }

        #endregion

        #region Public API — Loading

        /// <summary>
        /// Loads a video from URL or StreamingAssets path and prepares it.
        /// If <see cref="AutoPlay"/> is true, playback starts automatically when ready.
        /// </summary>
        public void Load(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[CrossPromoVideoPlayer] Cannot load: URL is empty.");
                return;
            }

            if (_player == null)
                EnsureVideoPlayer();

            CancelRetry();
            _videoUrl = url;
            _retryCount = 0;
            PrepareVideo();
        }

        /// <summary>
        /// Loads a video from a local file in StreamingAssets.
        /// Convenience wrapper that resolves the platform-correct path.
        /// </summary>
        public void LoadFromStreamingAssets(string fileName)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            Load(path);
        }

        #endregion

        #region Public API — Playback Control

        public void Play()
        {
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
            if (_player == null) return;

            CancelRetry();
            _player.Stop();
            SetState(PlaybackState.Idle);
        }

        /// <summary>Restarts from the beginning.</summary>
        public void Restart()
        {
            if (_player == null || !_player.isPrepared) return;

            _player.time = 0;
            _player.Play();
            SetState(PlaybackState.Playing);
        }

        /// <summary>Reloads the current URL (resets retry counter).</summary>
        public void Retry()
        {
            if (string.IsNullOrWhiteSpace(_videoUrl)) return;

            _retryCount = 0;
            PrepareVideo();
        }

        #endregion

        #region Public API — Seeking

        /// <summary>Seek to an absolute time in seconds.</summary>
        public void SeekTo(double seconds)
        {
            if (_player == null || !_player.isPrepared) return;
            _player.time = Math.Max(0, Math.Min(seconds, _player.length));
        }

        /// <summary>Seek to a normalized position (0 = start, 1 = end).</summary>
        public void SeekNormalized(float t)
        {
            SeekTo(Mathf.Clamp01(t) * Duration);
        }

        /// <summary>Seek forward or backward by the given number of seconds.</summary>
        public void SeekRelative(double offsetSeconds)
        {
            SeekTo(CurrentTime + offsetSeconds);
        }

        #endregion

        #region Public API — Audio

        public void SetMute(bool mute)
        {
            if (_player != null)
                _player.SetDirectAudioMute(0, mute);
        }

        public bool IsMuted()
        {
            return _player != null && _player.GetDirectAudioMute(0);
        }

        public void ToggleMute()
        {
            SetMute(!IsMuted());
        }

        #endregion

        #region Public API — Render Targets

        /// <summary>Assigns MeshRenderers whose main texture will receive the video.</summary>
        public void SetTargetMeshRenderers(List<MeshRenderer> renderers)
        {
            _targetMeshRenderers = renderers ?? new List<MeshRenderer>();
            ApplyTextureToTargets();
        }

        /// <summary>Assigns a RawImage that will display the video.</summary>
        public void SetTargetRawImage(RawImage rawImage)
        {
            _targetRawImage = rawImage;
            ApplyTextureToTargets();
        }

        #endregion

        #region Internal — VideoPlayer Setup

        private void EnsureVideoPlayer()
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

        #region Internal — Prepare & Retry

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
            PrepareVideo();
        }

        #endregion

        #region Internal — Event Handlers

        private void HandlePrepareCompleted(VideoPlayer source)
        {
            SetState(PlaybackState.Ready);
            EnsureRenderTexture();
            ApplyTextureToTargets();
            OnReady?.Invoke();

            if (_autoPlay)
                Play();
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

        #region Internal — Rendering

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
            Texture texture = _player.targetTexture;
            if (texture == null) return;

            if (_targetRawImage != null)
                _targetRawImage.texture = texture;

            for (int i = 0; i < _targetMeshRenderers.Count; i++)
            {
                if (_targetMeshRenderers[i] != null)
                    _targetMeshRenderers[i].material.mainTexture = texture;
            }
        }

        #endregion

        #region Internal — State Machine

        private void PauseInternal()
        {
            if (_player == null) return;
            _player.Pause();
            SetState(PlaybackState.Paused);
        }

        private void ResumeInternal()
        {
            if (_player == null) return;
            _player.Play();
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

        #region Static Helpers

        /// <summary>Formats seconds into MM:SS or HH:MM:SS.</summary>
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
