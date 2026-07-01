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
            [System.NonSerialized]
            private List<PromoConfiguration> _masterVideos = null;

            public PromosConfigurationInfo Copy()
            {
                var confInfo = new PromosConfigurationInfo();
                confInfo.Weight = Weight;
                confInfo.Videos.AddRange(Videos);
                confInfo._masterVideos = _masterVideos?.Select(v => v.Copy()).ToList();
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
                    RemoveFromMaster(vid.Title);
                }

                if (Videos.Count > 0)
                {
                    Videos.First().Weight += 1 - Videos.Sum(video => video.Weight);
                }
            }

            public void ApplyCooldownFilter(string lastShownTitle)
            {
                if (Videos == null || Videos.Count == 0) return;

                // Master-список инициализируется ОДИН РАЗ из полного Videos (после CheckVideosShowLimit)
                if (_masterVideos == null)
                    _masterVideos = Videos.Select(v => v.Copy()).ToList();

                if (_masterVideos.Count == 0) return;

                // Фильтруем из master-списка (не из Videos!); используем копии, чтобы
                // NormalizeWeights не мутировал оригинальные объекты в _masterVideos
                var available = _masterVideos
                    .Where(v => !VideoCooldownRegistry.IsOnCooldown(v.Title))
                    .Select(v => v.Copy())
                    .ToList();

                // Если все на cooldown — сброс всех кроме последнего показанного
                if (available.Count == 0)
                {
                    VideoCooldownRegistry.ClearAllCooldownsExcept(lastShownTitle, _masterVideos.Select(v => v.Title));
                    available = _masterVideos
                        .Where(v => v.Title != lastShownTitle)
                        .Select(v => v.Copy())
                        .ToList();

                    // Edge-case: единственное видео в конфиге
                    if (available.Count == 0)
                        available = _masterVideos.Select(v => v.Copy()).ToList();
                }

                Videos = available;
                CrossPromoConfigurationManager.NormalizeWeights(this);
            }

            public void RemoveFromMaster(string title)
            {
                _masterVideos?.RemoveAll(v => v.Title == title);
            }

            /// <summary>
            /// Убирает из активного и master-списка видео, чей AppPackageName содержит
            /// собственный bundle id донора либо установленный на устройстве пакет.
            /// Безопасно вызывать повторно (например, при возврате в приложение после
            /// установки промоутируемого пейда).
            /// </summary>
            public void RemoveInstalledOrSelfPromo(string ownPackageId)
            {
                bool Match(PromoConfiguration v)
                {
                    if (v?.AppPackageName == null) return false;
                    foreach (var pkg in v.AppPackageName)
                    {
                        if (string.IsNullOrEmpty(pkg)) continue;
                        if (!string.IsNullOrEmpty(ownPackageId)
                            && string.Equals(pkg, ownPackageId, StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (AppChecker.CheckIfAppInstalled(pkg))
                            return true;
                    }
                    return false;
                }

                _masterVideos?.RemoveAll(Match);
                Videos?.RemoveAll(Match);
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
            [Tooltip("Adjust click tracker; GET on CTA / end-card (and banner) click. Query params campaign, adgroup, creative are appended from fields below.")]
            public string adjust_click_url;
            [Tooltip("Adjust impression tracker; GET when video or banner is shown.")]
            public string adjust_impression_url;
            public string campaign;
            public string adgroup;
            public string creative;
            public int OverlayShowDelayInSeconds;
            public int CloseShowDelayInSeconds;
            public VideoExtension FileExtension;
            [Tooltip("Chance value should be a float between 0 and 1. The sum of all video chances should always be 1.")]
            [Range(0f, 1f)]
            public float Weight;
            public List<string> AppPackageName = new();
            public int MaxShowCount;

            public PromoConfiguration Copy()
            {
                return new PromoConfiguration
                {
                    Title = Title,
                    ButtonText = ButtonText,
                    FileName = FileName,
                    VideoUrl = VideoUrl,
                    BannerUrl = BannerUrl,
                    TrackingUrl = TrackingUrl,
                    RedirectUrl = RedirectUrl,
                    adjust_click_url = adjust_click_url,
                    adjust_impression_url = adjust_impression_url,
                    campaign = campaign,
                    adgroup = adgroup,
                    creative = creative,
                    OverlayShowDelayInSeconds = OverlayShowDelayInSeconds,
                    CloseShowDelayInSeconds = CloseShowDelayInSeconds,
                    FileExtension = FileExtension,
                    Weight = Weight,
                    AppPackageName = AppPackageName != null ? new List<string>(AppPackageName) : new List<string>(),
                    MaxShowCount = MaxShowCount
                };
            }
        }

        public async Task<PromosConfigurationInfo> FetchRemoteConfigAsync(string configUrl)
        {
            Debug.Log($"[CrossPromoConfig] FetchRemoteConfigAsync called. URL='{configUrl}'");
            var configuration = new PromosConfigurationInfo();
            if (string.IsNullOrWhiteSpace(configUrl))
            {
                Debug.LogWarning("[CrossPromoConfig] FetchRemoteConfigAsync: URL is EMPTY — returning empty config");
                return configuration;
            }

            const int maxRetries = 5;
            const float retryDelay = 1f;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                Debug.Log($"[CrossPromoConfig] Fetch attempt {attempt}/{maxRetries} for {configUrl}");
                try
                {
                    using var request = UnityWebRequest.Get(configUrl);
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    Debug.Log($"[CrossPromoConfig] Attempt {attempt}: result={request.result}, responseCode={request.responseCode}, error='{request.error}', downloadedBytes={request.downloadedBytes}");

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Debug.LogWarning($"[CrossPromoConfig] Attempt {attempt} failed: {request.error} (code={request.responseCode})");

                        if (attempt >= maxRetries)
                        {
                            Debug.LogError($"[CrossPromoConfig] All {maxRetries} attempts failed. Last error: {request.error}");
                            return configuration;
                        }

                        await Task.Delay((int)(retryDelay * 1000));
                        continue;
                    }

                    Debug.Log($"[CrossPromoConfig] Attempt {attempt} succeeded. Parsing response...");
                    configuration = ParseConfig(request.downloadHandler.text);
                    NormalizeWeights(configuration);
                    configuration.RemoveInstalledOrSelfPromo(Application.identifier);
                    NormalizeWeights(configuration);
                    Debug.Log($"[CrossPromoConfig] Final config: Videos={configuration.Videos?.Count ?? 0}");
                    return configuration;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CrossPromoConfig] Attempt {attempt} exception: {ex}");
                    if (attempt >= maxRetries)
                    {
                        Debug.LogError($"[CrossPromoConfig] All {maxRetries} attempts failed. Last exception: {ex}");
                        return configuration;
                    }

                    await Task.Delay((int)(retryDelay * 1000));
                }
            }

            Debug.LogError("[CrossPromoConfig] FetchRemoteConfigAsync exited loop without result");
            return configuration;
        }

        private static PromosConfigurationInfo ParseConfig(string json)
        {
            Debug.Log($"[CrossPromoConfig] ParseConfig called. json is {(string.IsNullOrWhiteSpace(json) ? "EMPTY/NULL" : $"length={json.Length}")}");

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[CrossPromoConfig] ParseConfig: JSON is empty, returning empty config");
                return new PromosConfigurationInfo();
            }

            PromosConfigurationInfo configuration;
            try
            {
                configuration = JsonUtility.FromJson<PromosConfigurationInfo>(json) ?? new PromosConfigurationInfo();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CrossPromoConfig] ParseConfig: malformed JSON — {ex.Message}");
                return new PromosConfigurationInfo();
            }

            configuration.Videos = configuration.Videos ?? new List<PromoConfiguration>();
            Debug.Log($"[CrossPromoConfig] Parsed: Weight={configuration.Weight}, Videos.Count={configuration.Videos.Count}");
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

    }
}
#endif
