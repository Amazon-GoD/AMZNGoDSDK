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
            [NonSerialized]
            private CrossPromoPositionRotation _positionRotation;

            internal CrossPromoPositionRotation PositionRotation =>
                _positionRotation ??= new CrossPromoPositionRotation(Videos);

            public PromosConfigurationInfo Copy()
            {
                var confInfo = new PromosConfigurationInfo();
                confInfo._positionRotation = PositionRotation.CopyLayout();
                confInfo.Weight = Weight;
                confInfo.Videos.AddRange(Videos);
                confInfo._masterVideos = _masterVideos?.Select(v => v.Copy()).ToList();
                return confInfo;
            }

            public void CheckVideosShowLimit()
            {
                // Снимок позиций должен предшествовать любому удалению из исходного JSON.
                bool ordered = PositionRotation.IsOrdered;
                if (Videos == null || Videos.Count == 0)
                    return;

                var videosToDelete = Videos.Where(v => v.IsShowLimitReached()).ToList();

                foreach (var vid in videosToDelete)
                {
                    Debug.Log($"[CrossPromoLimit] drop '{vid.Title}' — показов {PlayerPrefs.GetInt(vid.Title, 0)}/{vid.EffectiveShowLimit}");

                    if (!ordered)
                    {
                        foreach (var other in Videos)
                        {
                            if (other == vid) continue;
                            other.Weight += vid.Weight / Mathf.Max(1, Videos.Count - 1);
                        }
                    }

                    Videos.Remove(vid);

                    // Из master — обязательно: ApplyCooldownFilter пересобирает пул именно
                    // из него, и оставленный там креатив вернулся бы в ротацию сверх лимита.
                    RemoveFromMaster(vid.Title);
                }

                if (!ordered && Videos.Count > 0)
                {
                    Videos.First().Weight += 1 - Videos.Sum(video => video.Weight);
                }
            }

            /// <summary>
            /// Остался ли в пуле хоть один креатив, не выбравший свой лимит показов.
            /// <para>
            /// НЕ мутирует состояние — в отличие от <see cref="CheckVideosShowLimit"/> и
            /// <see cref="ApplyCooldownFilter"/>, которые пересобирают списки и рассчитаны
            /// на вызов только из пути показа. Смотрит в master (полный пул), а не в Videos:
            /// Videos — это прорежённый кулдауном срез одного круга, и его опустошение
            /// означает лишь «в этом круге пока нечего», а не «показывать больше нечего».
            /// Кулдаун здесь сознательно не учитывается: он откладывает показ внутри круга,
            /// пул не исчерпывает.
            /// </para>
            /// </summary>
            public bool HasAvailableVideos()
            {
                // До первого ApplyCooldownFilter master ещё не создан — тогда полный пул
                // это сам Videos (сразу после фетча он не прорежен кулдауном).
                var source = _masterVideos ?? Videos;
                if (source == null)
                    return false;

                for (int i = 0; i < source.Count; i++)
                {
                    if (!source[i].IsShowLimitReached())
                        return true;
                }

                return false;
            }

            public void ApplyCooldownFilter(string lastShownTitle)
            {
                // Позиции обходят общий кулдаун; свободные места фильтрует сама ротация.
                if (PositionRotation.IsOrdered) return;
                // Master-список инициализируется ОДИН РАЗ из полного Videos (после CheckVideosShowLimit)
                if (_masterVideos == null)
                {
                    // Инициализировать не из чего: конфиг ещё не доехал либо пуст.
                    if (Videos == null || Videos.Count == 0) return;
                    _masterVideos = Videos.Select(v => v.Copy()).ToList();
                }

                // ВАЖНО: выход по пустому Videos переехал внутрь инициализации master и больше
                // не блокирует пересборку пула. Videos — это срез ОДНОГО круга, и вычеркнуть
                // из него последние креативы мог только что отработавший CheckVideosShowLimit.
                // Раньше в этом случае метод выходил здесь, пул из master уже не пересобирался
                // никогда, и кросс-промо уходило в no_fill навсегда — хотя в master оставались
                // креативы с незакрытым лимитом, просто сидевшие на кулдауне.
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
                _ = PositionRotation;
                string MatchReason(PromoConfiguration v)
                {
                    if (v?.AppPackageName == null) return null;
                    foreach (var pkg in v.AppPackageName)
                    {
                        if (string.IsNullOrEmpty(pkg)) continue;
                        if (!string.IsNullOrEmpty(ownPackageId)
                            && string.Equals(pkg, ownPackageId, StringComparison.OrdinalIgnoreCase))
                            return $"self ({pkg})";
                        if (AppChecker.CheckIfAppInstalled(pkg))
                            return $"installed ({pkg})";
                    }
                    return null;
                }

                bool Match(PromoConfiguration v)
                {
                    var reason = MatchReason(v);
                    if (reason == null) return false;
                    Debug.Log($"[CrossPromoFilter] drop '{v.Title}' — {reason}");
                    return true;
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

            [Tooltip("Порядковый номер показа в круге (с 1). 0 или отрицательное значение — выбор по весу на свободных местах.")]
            public int position;
            [NonSerialized]
            internal int RotationId = -1;

            [Tooltip("Сколько показов этого креатива разрешено ЗА ВСЁ ВРЕМЯ (не за сессию). " +
                     "0 — без лимита. Имя поля в нижнем регистре: JsonUtility сопоставляет " +
                     "поля по точному совпадению, а в конфиге бэкенда оно приходит как \"cap\".")]
            public int cap;

            /// <summary>
            /// Действующий лимит показов за всё время. <see cref="cap"/> — основной источник;
            /// <see cref="MaxShowCount"/> оставлен фолбэком для старых конфигов, где cap ещё нет
            /// (JsonUtility молча оставит отсутствующее поле нулём, и без фолбэка такой креатив
            /// внезапно стал бы безлимитным). 0 и меньше — лимита нет.
            /// </summary>
            public int EffectiveShowLimit => cap > 0 ? cap : MaxShowCount;

            /// <summary>
            /// Креатив выбрал свой лимит показов и больше показываться не должен.
            /// При отключённом или исключённом модуле AppLovin лимиты не применяются.
            /// <para>
            /// Счётчик показов ведётся в PlayerPrefs по СЫРОМУ Title (см. IncrementShowCount
            /// в оверлеях), поэтому креатив без Title не накапливает показы вообще — такой
            /// считаем безлимитным, иначе он выпал бы из ротации на первом же вызове.
            /// </para>
            /// </summary>
            public bool IsShowLimitReached()
            {
#if AMZN_APPLOVIN_ENABLED
                var mediation = SdkModuleRegistry.Get<AppLovinModule>();
                if (mediation == null || !mediation.Enabled)
                    return false;

                int limit = EffectiveShowLimit;
                if (limit <= 0)
                    return false;

                if (string.IsNullOrWhiteSpace(Title))
                    return false;

                return PlayerPrefs.GetInt(Title, 0) >= limit;
#else
                return false;
#endif
            }

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
                    MaxShowCount = MaxShowCount,
                    cap = cap,
                    position = position,
                    RotationId = RotationId
                };
            }
        }

        public async Task<PromosConfigurationInfo> FetchRemoteConfigAsync(string configUrl)
        {
            Debug.Log($"[CrossPromoConfig] FetchRemoteConfigAsync called. URL='{configUrl}'");
            if (string.IsNullOrWhiteSpace(configUrl))
            {
                Debug.LogWarning("[CrossPromoConfig] FetchRemoteConfigAsync: URL is EMPTY — returning empty config");
                return new PromosConfigurationInfo();
            }

            const int maxRetries = 5;
            const float retryDelay = 1f;
            string packageName = Application.identifier;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                Debug.Log($"[CrossPromoConfig] Fetch attempt {attempt}/{maxRetries} for {configUrl}");
                try
                {
                    // Каждый вызов и каждая повторная попытка начинают с исходного URL:
                    // выбранный маршрут не сохраняется между загрузками или запусками игры.
                    string json = await DownloadConfigJsonAsync(configUrl);
                    if (!CrossPromoConfigResolver.TryParse(json, packageName, true,
                            out var configuration, out var resolvedUrl, out var error))
                        throw new FormatException(error);

                    if (resolvedUrl != null)
                    {
                        Debug.Log($"[CrossPromoConfig] Master route: package='{packageName}', URL='{resolvedUrl}'");
                        json = await DownloadConfigJsonAsync(resolvedUrl);
                        if (!CrossPromoConfigResolver.TryParse(json, packageName, false,
                                out configuration, out _, out error))
                            throw new FormatException(error);
                    }

                    Debug.Log($"[CrossPromoConfig] Parsed: Weight={configuration.Weight}, Videos.Count={configuration.Videos.Count}");
                    _ = configuration.PositionRotation;
                    NormalizeWeights(configuration);
                    int filterBefore = configuration.Videos?.Count ?? 0;
                    Debug.Log($"[CrossPromoFilter] fetch: running filter, ownPackage='{packageName}', videos={filterBefore}");
                    configuration.RemoveInstalledOrSelfPromo(packageName);
                    int filterAfter = configuration.Videos?.Count ?? 0;
                    Debug.Log($"[CrossPromoFilter] fetch: done, videos {filterBefore} → {filterAfter}");
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
                        return new PromosConfigurationInfo();
                    }

                    await Task.Delay((int)(retryDelay * 1000));
                }
            }

            Debug.LogError("[CrossPromoConfig] FetchRemoteConfigAsync exited loop without result");
            return new PromosConfigurationInfo();
        }

        private static async Task<string> DownloadConfigJsonAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = 15;
            request.SetRequestHeader("Cache-Control", "no-cache");
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            Debug.Log($"[CrossPromoConfig] Download '{url}': result={request.result}, responseCode={request.responseCode}, error='{request.error}', downloadedBytes={request.downloadedBytes}");
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"Could not download '{url}': {request.error} (code={request.responseCode})");

            return request.downloadHandler.text;
        }

        private static void NormalizeWeights(PromosConfigurationInfo configuration)
        {
            if (configuration?.Videos == null || configuration.Videos.Count == 0)
            {
                return;
            }

            // В позиционной схеме вес закреплённого креатива не меняет пропорции
            // заполнителей. Выбор сам использует сумму весов только доступного пула.
            if (configuration.PositionRotation.IsOrdered) return;

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
