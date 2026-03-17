#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoConfigurationManager : MonoBehaviour
    {
        [Serializable]
        public enum VideoExtension
        {
            mp4,
            mov,
            webm,
            m4v,
            avi
        }

        [Serializable]
        public class PromosConfigurationInfo
        {
            [Tooltip("Chance value should be a float between 0 and 1.")]
            [Range(0f, 1f)]
            public float Weight;
            public List<PromoConfiguration> Videos = new();

            public PromosConfigurationInfo Copy()
            {
                var confInfo = new PromosConfigurationInfo();
                confInfo.Weight = Weight;
                confInfo.Videos.AddRange(Videos);
                return confInfo;
            }

            public void CheckVideosShowLimit()
            {
                if (Videos == null || Videos.Count == 0)
                    return;

                var videosToDelete = new List<PromoConfiguration>();
                foreach (var videoInfo in Videos)
                {
                    if (videoInfo.MaxShowCount <= 0)
                        continue;

                    int videoShowCount = PlayerPrefs.GetInt(videoInfo.Title, 0);
                    if (videoShowCount >= videoInfo.MaxShowCount)
                    {
                        videosToDelete.Add(videoInfo);
                    }
                }

                foreach (var video in videosToDelete)
                {
                    var vid = Videos.FirstOrDefault(x => x.Title == video.Title);
                    if (vid == null) continue;

                    foreach (var other in Videos)
                    {
                        if (other == vid) continue;
                        other.Weight += vid.Weight / Mathf.Max(1, Videos.Count - 1);
                    }

                    Videos.Remove(vid);
                }

                if (Videos.Count > 0)
                {
                    Videos.First().Weight += 1 - Videos.Sum(video => video.Weight);
                }
            }
        }

        [Serializable]
        public class PromoConfiguration
        {
            public string Title;
            public string ButtonText;
            public string FileName;
            public string VideoUrl;
            public string BannerUrl;
            public string TrackingUrl;
            public string RedirectUrl;
            public int OverlayShowDelayInSeconds;
            public int CloseShowDelayInSeconds;
            public VideoExtension FileExtension;
            [Tooltip("Chance value should be a float between 0 and 1. The sum of all video chances should always be 1.")]
            [Range(0f, 1f)]
            public float Weight;
            public List<string> AppPackageName = new();
            public int MaxShowCount;
        }

        public async Task<PromosConfigurationInfo> FetchRemoteConfigAsync(string configUrl)
        {
            var configuration = new PromosConfigurationInfo();
            if (string.IsNullOrWhiteSpace(configUrl))
            {
                return configuration;
            }

            const int maxRetries = 5;
            const float retryDelay = 1f;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    using var request = UnityWebRequest.Get(configUrl);
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Debug.LogWarning($"Attempt {attempt} failed: {request.error}");

                        if (attempt >= maxRetries)
                        {
                            Debug.LogError($"All attempts failed. Last error: {request.error}");
                            return configuration;
                        }

                        await Task.Delay((int)(retryDelay * 1000));
                        continue;
                    }

                    configuration = ParseConfig(request.downloadHandler.text);
                    NormalizeWeights(configuration);
                    RemoveInstalledApps(configuration);
                    return configuration;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Attempt {attempt} failed with exception: {ex.Message}");
                    if (attempt >= maxRetries)
                    {
                        Debug.LogError($"All attempts failed. Last exception: {ex}");
                        return configuration;
                    }

                    await Task.Delay((int)(retryDelay * 1000));
                }
            }

            return configuration;
        }

        private static PromosConfigurationInfo ParseConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PromosConfigurationInfo();
            }

            Debug.Log($"Remote config fetched: {json}");
            var configuration = JsonUtility.FromJson<PromosConfigurationInfo>(json) ?? new PromosConfigurationInfo();
            configuration.Videos = configuration.Videos ?? new List<PromoConfiguration>();
            return configuration;
        }

        private static void NormalizeWeights(PromosConfigurationInfo configuration)
        {
            if (configuration?.Videos == null || configuration.Videos.Count == 0)
            {
                return;
            }

            var totalWeight = configuration.Videos.Sum(video => video.Weight);
            var delta = 1f - totalWeight;

            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }

            var perVideo = delta / configuration.Videos.Count;
            for (int i = 0; i < configuration.Videos.Count; i++)
            {
                configuration.Videos[i].Weight += perVideo;
            }

            configuration.Videos[0].Weight += 1 - configuration.Videos.Sum(video => video.Weight);
        }

        private static void RemoveInstalledApps(PromosConfigurationInfo configuration)
        {
            if (configuration?.Videos == null || configuration.Videos.Count == 0)
            {
                return;
            }

            var videosToRemove = new List<PromoConfiguration>();
            foreach (var video in configuration.Videos)
            {
                foreach (var packageName in video.AppPackageName)
                {
                    if (AppChecker.CheckIfAppInstalled(packageName))
                    {
                        videosToRemove.Add(video);
                        break;
                    }
                }
            }

#if !UNITY_EDITOR && UNITY_ANDROID
            foreach (var video in videosToRemove)
            {
                configuration.Videos.Remove(video);
            }
#endif
        }
    }
}
#endif
