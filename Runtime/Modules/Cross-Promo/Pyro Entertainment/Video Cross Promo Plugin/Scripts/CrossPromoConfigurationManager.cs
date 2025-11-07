using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Pyro
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
            public List<PromoConfiguration> Videos;

            public PromosConfigurationInfo Copy()
            {
                var confInfo = new PromosConfigurationInfo();
                confInfo.Weight = Weight;
                confInfo.Videos = new List<PromoConfiguration>();
                confInfo.Videos.AddRange(Videos);
                return confInfo;
            }
            public void CheckVideosShowLimit()
            {
                if (Videos.Count <= 0) return;
                List<string> videosToDelete = new List<string>();
                foreach (var videoInfo in Videos)
                {
                    int videoShowCount = PlayerPrefs.GetInt(videoInfo.Title, 0);
                    //Debug.Log(videoInfo.Title + " show count: " + videoShowCount);
                    if (videoShowCount >= videoInfo.MaxShowCount)
                    {
                        videosToDelete.Add(videoInfo.Title);
                    }
                }

                foreach (var video in videosToDelete)
                {
                    var vid = Videos.Where(x => x.Title == video).First();
                    Videos.ForEach(x => x.Weight += vid.Weight / (Videos.Count - 1));
                    Videos.Remove(vid);
                }

                if (Videos.Count > 0)
                    Videos.First().Weight += 1 - Videos.Sum(video => video.Weight);
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
            [Tooltip("Chance value should be a float between 0 and 1. The sum of all video chances should allways be 1.")]
            [Range(0f, 1f)]
            public float Weight;
            public List<string> AppPackageName;
            public int MaxShowCount;
        }

        //[SerializeField]
        //private PromosConfigurationInfo _configuration;

        public async Task<PromosConfigurationInfo> FetchRemoteConfigAsync(string configUrl)
        {
            PromosConfigurationInfo _configuration = new PromosConfigurationInfo();
            int maxRetries = 5;
            float retryDelay = 1f;

            if (configUrl == string.Empty)
                return _configuration;

            int attempt = 0;
            
            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    using (UnityWebRequest request = UnityWebRequest.Get(configUrl))
                    {
                        // Send the request and await its completion
                        var operation = request.SendWebRequest();

                        while (!operation.isDone)
                            await Task.Yield();

                        if (request.result == UnityWebRequest.Result.ConnectionError ||
                            request.result == UnityWebRequest.Result.ProtocolError)
                        {
                            Debug.LogWarning($"Attempt {attempt} failed: {request.error}");

                            // ���� ��� ��������� ������� - ���������� ������� ������������
                            if (attempt >= maxRetries)
                            {
                                Debug.LogError($"All attempts failed. Last error: {request.error}");
                                return _configuration;
                            }

                            // ���� ����� ��������� ��������
                            await Task.Delay((int)(retryDelay * 1000));
                            continue;
                        }

                        string jsonResponse = request.downloadHandler.text;
                        Debug.Log($"Remote config fetched: {jsonResponse}");

                        // Deserialize the JSON response into the RemoteConfig object
                        _configuration = JsonUtility.FromJson<PromosConfigurationInfo>(jsonResponse);

                        List<PromoConfiguration> videosToRemove = new List<PromoConfiguration>();
                        foreach (var video in _configuration.Videos)
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
                foreach (var video in videosToRemove) _configuration.Videos.Remove(video);
#endif
                        float weightNeeded = 1 - _configuration.Videos.Sum(video => video.Weight);
                        foreach (var video in _configuration.Videos) video.Weight += weightNeeded / _configuration.Videos.Count;
                        _configuration.Videos.First().Weight += 1 - _configuration.Videos.Sum(video => video.Weight);
                        return _configuration;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Attempt {attempt} failed with exception: {ex.Message}");
                    if (attempt >= maxRetries)
                    {
                        Debug.LogError($"All attempts failed. Last exception: {ex}");
                        return _configuration;
                    }

                    await Task.Delay((int)(retryDelay * 1000));
                }
            }

            /////////////////////////////////////
            using (UnityWebRequest request = UnityWebRequest.Get(configUrl))
            {
                // Send the request and await its completion
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                    await Task.Yield();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error fetching remote config: {request.error}");
                    return _configuration;
                }

                string jsonResponse = request.downloadHandler.text;
                Debug.Log($"Remote config fetched: {jsonResponse}");

                // Deserialize the JSON response into the RemoteConfig object
                _configuration = JsonUtility.FromJson<PromosConfigurationInfo>(jsonResponse);

                List<PromoConfiguration> videosToRemove = new List<PromoConfiguration>();
                foreach (var video in _configuration.Videos)
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
                foreach (var video in videosToRemove) _configuration.Videos.Remove(video);
#endif
                foreach (var video in _configuration.Videos) video.Weight = 1f / _configuration.Videos.Count;

                return _configuration;
            }
        }
    }
}
