using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;
using static Pyro.CrossPromoConfigurationManager;
using Random = UnityEngine.Random;

namespace Pyro
{
    [RequireComponent(
        typeof(CrossPromoVideoManager),
        typeof(CrossPromoConfigurationManager),
        typeof(Canvas)
    )]
    public class CrossPromoManager : MonoBehaviour
    {
        public static CrossPromoManager Instance;
        public bool IsReady { get { return _videoManager.IsReady; } }
        #region Actions
        public Action NoFillCallback { get { return _noFillCallback; } set { _noFillCallback = value; } }
        public Action AdClickCallback { get { return _adClickCallback; } set { _adClickCallback = value; } }
        public Action OnAdsLoadedCallback { get { return _onAdsLoadedCallback; } set { _onAdsLoadedCallback = value; } }
        public Action AdCloseCallback { get { return _adCloseCallback; } set { _adCloseCallback = value; } }
        public Action<string> OnPlayErrorCallback { get { return _onPlayErrorCallback; } set { _onPlayErrorCallback = value; } }

        private Action _noFillCallback;
        private Action<string> _onPlayErrorCallback;
        private Action _onAdsLoadedCallback;
        private Action _adClickCallback;
        private Action _adCloseCallback;
        #endregion

        private CrossPromoVideoManager _videoManager;
        private Canvas _canvas;

        void Awake()
        {
            _videoManager = GetComponent<CrossPromoVideoManager>();
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            NoFillCallback += () => TrackingManager.MetricaTracking("crosspromo_show_fail", "NoFill");
            _canvas = GetComponent<Canvas>();
            _canvas.sortingOrder = Int16.MaxValue;
        }

        public void Show(PromosConfigurationInfo confAll, PromosConfigurationInfo confNotWatched, Action onClose = null)
        {
            TrackingManager.MetricaTracking("crosspromo_request_show");
            confNotWatched.CheckVideosShowLimit();

            bool r1 = !IsReady;
            bool r2 = MaxMediation.Instance.IsReady;
            bool PyroNotReadyYet = r1 && r2;
            if (confNotWatched.Videos.Count == 0 || PyroNotReadyYet)
            {
                try { MaxMediation.Instance.ShowAd(onClose); return; }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
            float displayChance = Random.Range(0f, 1f);
            if (!_videoManager.IsReady || displayChance > confNotWatched.Weight)
            {
                _noFillCallback?.Invoke();

                return;
            }
            else
            {
                float videoChance = Random.Range(0f, 1f);
                float comulativeChance = 0;

                var _conf = confNotWatched;
                if (_conf.Videos.Count == 0) _conf = confAll;

                //удаление еще не скачанных видео
                var conf = _conf.Copy();
                for (int i = 0; i < conf.Videos.Count; i++)
                {
                    var videoInfo = conf.Videos[i];
                    bool videoDownloaded = _videoManager.isVideoDownloaded(videoInfo.Title);
                    Debug.Log($"Video {videoInfo.Title} downloaded status is: {videoDownloaded}");
                    if (videoDownloaded) continue;

                    foreach (var video in conf.Videos) if (video.Title != videoInfo.Title) video.Weight += videoInfo.Weight / (conf.Videos.Count-1);
                    conf.Videos.Remove(videoInfo);
                    i--;

                    if (conf.Videos.Count > 0)
                        conf.Videos.First().Weight += 1 - conf.Videos.Sum(video => video.Weight);
                }

                //выбор видео для показа
                foreach (var videoInfo in conf.Videos) 
                {
                    comulativeChance += videoInfo.Weight;
                    if (videoChance <= comulativeChance)
                    {
                        string[] nameComponents = videoInfo.FileName.Split('.');
                        if (nameComponents.Length > 0)
                        {
                            string completeFileName = $"{nameComponents[0]}.{videoInfo.FileExtension.ToString()}";

                            _videoManager.PlayVideo(completeFileName, videoInfo, onClose);
                            return;
                        }
                    }
                }

                _noFillCallback?.Invoke();
            }
        }
    }
}


