#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    public class CrossPromoModule : ModuleBase
    {
        public static CrossPromoModule Instance { get; private set; }

        public static event Action<PromosConfigurationInfo> OnConfigLoaded;
        public static event Action<Action, Func<bool>> OnBannerFuncsUpdated;

        [SerializeField] private CrossPromoConfigurationManager _configurationManager;
        [SerializeField] private CrossPromoVideoOverlay _videoOverlay;

        private string _configUrl;
        private string _defaultPromotedAppId;
        private PromosConfigurationInfo _crossPromoConfig;
        private Action _currentBannerOnClose;
        private Func<bool> _currentIsNoAds;
        private VideoPlayerBackend _videoBackend;
        private PromoConfiguration _preloadedConfig;
        private CrossPromoVideoPlayer _preloadPlayer;
        private CrossPromoExoNativeOverlay _exoOverlay;
        private PromoConfiguration _lastShownConfig;
        private string _lastShownTitleFromPrefs;
        private bool _firstWarmupTriggered;
        private Coroutine _initCoroutine;
        private Coroutine _showCoroutine;
        private Coroutine _configRetryCoroutine;
        private bool _configRetryRunning;

        // На смене сцены дёргаем подстраховочный re-warmup preload-плеера (на случай
        // если прогретое видео потерялось / отвалилось между показами). Throttle, чтобы
        // быстрые цепочки переходов (Menu→Level→Menu) не плодили лишнюю работу.
        private float _lastSceneReloadRefreshTime = -10f;
        private const float SceneReloadRefreshThrottleSeconds = 5f;

        // Ленивая дозагрузка конфига. Первый фетч может вернуться ПУСТЫМ, а не упасть:
        // FetchRemoteConfigAsync после всех своих попыток отдаёт пустой PromosConfigurationInfo,
        // поэтому таск завершается успешно, _crossPromoConfig становится не-null — и модуль
        // до конца сессии отвечает no_fill, хотя проблема была лишь в сети на старте.
        // Разброс интервала — чтобы клиенты не долбили CDN синхронно.
        private const float ConfigRetryMinSeconds = 10f;
        private const float ConfigRetryMaxSeconds = 15f;

        // Плейсменты аналитики. ShowVideoPromo() намеренно ходит под InterstitialPlacement:
        // отдельной воронки у него нет, а без плейсмента события не отправлялись бы вообще.
        private const string InterstitialPlacement = "interstitial";
        private const string RewardedPlacement = "rewarded";

        public PromosConfigurationInfo LoadedConfig => _crossPromoConfig;

        /// <summary>True when a video is ready to play. For the UnityVideoPlayer backend this
        /// means a clip is preloaded for an instant swap; for the ExoPlayer backend (native
        /// overlay buffers on show) it means the remote config has at least one video.</summary>
        public bool IsVideoReady =>
            _videoBackend == VideoPlayerBackend.ExoPlayer
                ? (_crossPromoConfig?.Videos != null && _crossPromoConfig.Videos.Count > 0)
                : (_preloadPlayer != null && _preloadPlayer.IsPreloaded);

        /// <summary>
        /// Последний фетч конфига вернул хотя бы одно видео.
        /// <para>
        /// Проверять надо именно это, а не <c>_crossPromoConfig == null</c>: провалившийся
        /// фетч возвращает ПУСТОЙ конфиг, а не null и не ошибку, так что по null «не
        /// загружен» не отличить. И не текущий <c>Videos.Count</c>: его прореживают
        /// CheckVideosShowLimit / ApplyCooldownFilter / RemoveInstalledOrSelfPromo прямо
        /// по ходу сессии, а это уже «конфиг есть, но показывать нечего» — случай, который
        /// дозагрузкой не лечится.
        /// </para>
        /// </summary>
        private bool _configFetchReturnedVideos;

        public Action CurrentBannerOnClose => _currentBannerOnClose;
        public Func<bool> CurrentIsNoAds => _currentIsNoAds;

        private void Awake()
        {
            // Если при загрузке сцены появился второй SDK-префаб, он НЕ должен захватывать
            // static Instance: иначе, уничтожаясь (AmznGoDSDKCore рушит дубликат), он в своём
            // OnDestroy занулит Instance у живого модуля — и весь трекинг молча умрёт до конца
            // сессии (клики в Adjust, клики на бэкенд, показы видео на бэкенд).
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CrossPromoModule] Duplicate instance in Awake — keeping the live Instance, skipping takeover.");
                return;
            }

            Instance = this;

            // Подписка статичная — живёт всю сессию вне зависимости от того,
            // активен ли host-GO модуля (модуль на DontDestroyOnLoad-объекте SDK).
            // remove-then-add гарантирует отсутствие двойной подписки.
            SceneManager.sceneLoaded -= OnSceneLoadedRefreshPreload;
            SceneManager.sceneLoaded += OnSceneLoadedRefreshPreload;
        }

        // При каждой смене сцены тыкаем preload-плеер подстраховочным re-warmup'ом.
        // Если preload здоров — RefreshPreloadIfStale внутри сделает no-op. Throttle 5с
        // защищает от спама на быстрых переходах.
        private void OnSceneLoadedRefreshPreload(Scene scene, LoadSceneMode mode)
        {
            float sinceLast = Time.unscaledTime - _lastSceneReloadRefreshTime;
            if (sinceLast < SceneReloadRefreshThrottleSeconds)
            {
                Debug.Log($"[CrossPromoModule] OnSceneLoaded('{scene.name}'): refresh throttled ({sinceLast:F2}s < {SceneReloadRefreshThrottleSeconds}s).");
                return;
            }

            _lastSceneReloadRefreshTime = Time.unscaledTime;
            Debug.Log($"[CrossPromoModule] OnSceneLoaded('{scene.name}'): дёргаем RefreshPreloadIfStale (IsVideoReady={IsVideoReady}).");
            RefreshPreloadIfStale();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedRefreshPreload;
            if (_initCoroutine != null) { StopCoroutine(_initCoroutine); _initCoroutine = null; }
            if (_showCoroutine != null) { StopCoroutine(_showCoroutine); _showCoroutine = null; }
            if (_configRetryCoroutine != null) { StopCoroutine(_configRetryCoroutine); _configRetryCoroutine = null; }
            _configRetryRunning = false;
            DisposePreloadPlayer();
            if (_exoOverlay != null)
            {
                Destroy(_exoOverlay.gameObject);
                _exoOverlay = null;
            }
            if (Instance == this)
            {
                Instance = null;
                OnConfigLoaded = null;
                OnBannerFuncsUpdated = null;
            }
        }

        public void Construct(CrossPromoSettingData settings)
        {
            Debug.Log($"[CrossPromoModule] Construct() called. Enabled={settings.Enabled}, ConfigUrl='{settings.ConfigUrl}', VideoBackend={settings.VideoBackend}");
            Enabled = settings.Enabled;
            _configUrl = settings.ConfigUrl;
            _videoBackend = settings.VideoBackend;
            _defaultPromotedAppId = settings.DefaultPromotedAppId;
        }

        private bool _initializeRequested;
        private bool _initRunning;

        // Cross-Promo через UnityVideoPlayer backend выводит звук видео по пути
        // VideoPlayer.audioOutputMode = Direct. Этот путь требует работающего Unity
        // Audio engine. Если в проекте стоит галка PlayerSettings → Audio → "Disable
        // Unity Audio" (типично для проектов на чистом FMOD/Wwise), Unity audio output
        // не инициализируется, и реклама проигрывается БЕЗ ЗВУКА — причём молча, без
        // единой ошибки. Это съедает дни на диагностику. Здесь ловим этот кейс заранее
        // и громко об этом сообщаем. ExoPlayer backend не затрагивается — у него свой
        // нативный AudioTrack, от Unity Audio не зависит.
        private void WarnIfUnityAudioDisabled()
        {
            if (_videoBackend != VideoPlayerBackend.UnityVideoPlayer)
                return;

            AudioConfiguration cfg = AudioSettings.GetConfiguration();

            // При выключенном Unity Audio движок не инициализируется и
            // GetConfiguration() возвращает обнулённый конфиг (dspBufferSize == 0,
            // sampleRate == 0). При включённом — там валидные значения
            // (dspBufferSize обычно 256/512/1024). Проверяем оба признака, чтобы
            // не словить ложное срабатывание.
            bool audioLikelyDisabled = cfg.dspBufferSize == 0 && cfg.sampleRate == 0;

            if (audioLikelyDisabled)
            {
                Debug.LogError(
                    "[CrossPromoModule] ВНИМАНИЕ: похоже, Unity Audio отключён " +
                    "(PlayerSettings → Audio → 'Disable Unity Audio'). С backend'ом " +
                    "UnityVideoPlayer видео-реклама будет проигрываться БЕЗ ЗВУКА — " +
                    "VideoPlayer.audioOutputMode=Direct требует инициализированный " +
                    "Unity Audio engine. Решение: либо снять 'Disable Unity Audio' в " +
                    "PlayerSettings, либо переключить VideoBackend на ExoPlayer (Android). " +
                    $"(AudioConfiguration: sampleRate={cfg.sampleRate}, dspBufferSize={cfg.dspBufferSize}, speakerMode={cfg.speakerMode})");
            }
            else
            {
                Debug.Log($"[CrossPromoModule] Unity Audio check OK: sampleRate={cfg.sampleRate}, dspBufferSize={cfg.dspBufferSize}, speakerMode={cfg.speakerMode}");
            }
        }

        public override void Initialize()
        {
            Debug.Log($"[CrossPromoModule] Initialize() called. Enabled={Enabled}, configUrl='{_configUrl}', videoBackend={_videoBackend}");
            WarnIfUnityAudioDisabled();
            DisposePreloadPlayer();

            // Повторный Initialize() начинает всё заново — старая дозагрузка не должна
            // пережить сброс и параллелить фетчи с новым init'ом.
            if (_configRetryCoroutine != null) { StopCoroutine(_configRetryCoroutine); _configRetryCoroutine = null; }
            _configRetryRunning = false;

            _crossPromoConfig = null;
            _configFetchReturnedVideos = false;
            _preloadedConfig = null;
            _firstWarmupTriggered = false;
            _initializeRequested = true;
            _lastShownTitleFromPrefs = VideoCooldownRegistry.GetLastShownTitle();
            StartInitCoroutine();
        }

        private void StartInitCoroutine()
        {
            if (_initRunning) return;
            if (!isActiveAndEnabled) return;   // OnEnable will retry
            _initRunning = true;
            _initCoroutine = StartCoroutine(InitCrossPromoModulesCorAsync());
        }

        private void OnEnable()
        {
            // If a prior deactivation killed the init coroutine mid-flight (config fetch
            // or preload), pick up where we left off on re-activation.
            if (!_initializeRequested) return;
            if (_crossPromoConfig == null)
            {
                StartInitCoroutine();
            }
            else if (_preloadPlayer == null || (!_preloadPlayer.IsPreloaded && !_preloadPlayer.IsLoading))
            {
                // Preload was lost while host was deactivated — restart it so the next
                // Show can swap instantly instead of loading from scratch.
                PreloadNextVideo();
            }

            // Дозагрузку конфига тоже могло погасить деактивацией. No-op, если конфиг уже есть
            // или если init выше перезапустился — он догрузит сам.
            EnsureConfigRetryRunning("host re-enabled");
        }

        private void OnDisable()
        {
            // StartCoroutine stops silently when the host GO is deactivated; the finally
            // block in InitCrossPromoModulesCorAsync may not run in that path. Reset the
            // guard here so OnEnable can legitimately restart the init.
            _initRunning = false;

            // Дозагрузка конфига гаснет вместе с host-объектом по той же причине — сбрасываем
            // флаг, иначе EnsureConfigRetryRunning навсегда счёл бы её живой.
            _configRetryRunning = false;
            _configRetryCoroutine = null;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Пользователь мог кликнуть промо, установить пейд и вернуться в донор —
            // перепрогоняем фильтр, чтобы следующий показ не рекламировал уже
            // установленное приложение и не оффер сам себя.
            // Публичный IP мог не резолвиться на старте (не было сети) — пробуем снова при
            // возврате фокуса, чтобы Adjust-ссылки получили ip_address.
            if (hasFocus && Enabled && string.IsNullOrEmpty(CrossPromoPublicIp.Value))
                StartCoroutine(CrossPromoPublicIp.Prefetch());

            if (!hasFocus || !Enabled || _crossPromoConfig == null) return;

            int before = _crossPromoConfig.Videos?.Count ?? 0;
            Debug.Log($"[CrossPromoFilter] focus: running filter, ownPackage='{Application.identifier}', videos={before}");
            _crossPromoConfig.RemoveInstalledOrSelfPromo(Application.identifier);
            int after = _crossPromoConfig.Videos?.Count ?? 0;
            Debug.Log($"[CrossPromoFilter] focus: done, videos {before} → {after}");
            if (before == after) return;

            // Если предзагруженное видео больше не разрешено, сбрасываем префилл и
            // подтягиваем следующее допустимое.
            if (_preloadedConfig != null && (_crossPromoConfig.Videos == null
                || !_crossPromoConfig.Videos.Exists(v => v.Title == _preloadedConfig.Title)))
            {
                DisposePreloadPlayer();
                _preloadedConfig = null;
                PreloadNextVideo();
            }
        }

        private void DisposePreloadPlayer()
        {
            if (_preloadPlayer == null) return;

            _preloadPlayer.OnError -= HandlePreloadError;
            if (_preloadPlayer.gameObject != null)
                Destroy(_preloadPlayer.gameObject);
            _preloadPlayer = null;
        }

        private void HandlePreloadError(string error)
        {
            Debug.LogWarning($"[CrossPromoModule] Preload failed: {error}. Clearing preloaded config.");
            _preloadedConfig = null;
        }

        /// <summary>
        /// Подстраховочный перезапуск preload+warmup, если предзагруженного видео нет
        /// или оно "застряло" (не Preloaded и не IsLoading — то есть в ошибке или idle).
        /// Если preload здоров (или уже грузится) — no-op. Безопасно вызывать часто:
        /// внутренний guard не позволит дёргать дублирующую работу.
        /// </summary>
        public void RefreshPreloadIfStale()
        {
            if (!Enabled) return;
            if (_videoBackend != VideoPlayerBackend.UnityVideoPlayer) return;   // ExoPlayer: нет Unity-preload
            if (_crossPromoConfig == null) return;   // Init ещё не завершён

            // Не дёргаем preload, пока оверлей активен на экране. Иначе откроем
            // второй параллельный MediaCodec поверх живого — на MTK MT8169
            // (Amazon Fire) это deadlock'ит main-поток Unity. Оверлей живёт на
            // DontDestroyOnLoad, так что смена сцены может прилететь во время
            // показа промо.
            if (_videoOverlay != null && _videoOverlay.IsVisible)
            {
                Debug.Log("[CrossPromoModule] RefreshPreloadIfStale: overlay visible, skipping preload to avoid parallel MediaCodec.");
                return;
            }

            if (_preloadPlayer == null || (!_preloadPlayer.IsPreloaded && !_preloadPlayer.IsLoading))
            {
                Debug.Log("[CrossPromoModule] RefreshPreloadIfStale: preload missing/stale, restarting.");
                PreloadNextVideo();
            }
        }

        private IEnumerator InitCrossPromoModulesCorAsync()
        {
            Debug.Log("[CrossPromoModule] InitCrossPromoModulesCorAsync started");
            try
            {
                // Резолвим публичный IP заранее (fire-and-forget), чтобы к первому показу/клику
                // он уже был готов для ip_address в Adjust-ссылках.
                StartCoroutine(CrossPromoPublicIp.Prefetch());

                yield return LoadJson();
                Debug.Log($"[CrossPromoModule] LoadJson finished. _crossPromoConfig is {(_crossPromoConfig == null ? "NULL" : "set")}, Videos count = {_crossPromoConfig?.Videos?.Count ?? -1}");
                OnConfigLoaded?.Invoke(_crossPromoConfig);

                PreloadNextVideo();
            }
            finally
            {
                _initRunning = false;
            }

            // Стартовый фетч мог вернуться пустым (сеть легла) — тогда без этого модуль до
            // конца сессии отвечал бы no_fill. Вызов после finally: _initRunning уже сброшен,
            // иначе guard внутри отбил бы запуск.
            EnsureConfigRetryRunning("стартовый фетч не дал ни одного видео");
        }

        /// <summary>
        /// Запускает фоновую дозагрузку конфига, если тот так и не доехал. Идемпотентно:
        /// no_fill может прилетать на каждый запрос показа, но параллельные корутины не
        /// плодятся и частота запросов от этого не растёт.
        /// </summary>
        private void EnsureConfigRetryRunning(string reason)
        {
            if (!Enabled) return;
            if (_configRetryRunning) return;
            if (_configFetchReturnedVideos) return;            // конфиг доехал; пусто — значит отфильтровано
            if (_initRunning) return;                          // основной init сам всё догрузит
            if (string.IsNullOrWhiteSpace(_configUrl)) return; // грузить неоткуда — ретрай бессмыслен
            if (!isActiveAndEnabled) return;                   // OnEnable перезапустит

            Debug.Log($"[CrossPromoModule] Config retry: старт (причина: {reason}).");
            _configRetryRunning = true;
            _configRetryCoroutine = StartCoroutine(RetryConfigLoadCoroutine());
        }

        private IEnumerator RetryConfigLoadCoroutine()
        {
            int attempt = 0;

            while (!_configFetchReturnedVideos)
            {
                // Пауза ПЕРЕД запросом, а не после: no_fill прилетает пачками, и без этого
                // первый же залп ушёл бы в сеть немедленно — то есть без троттлинга вовсе.
                float delay = UnityEngine.Random.Range(ConfigRetryMinSeconds, ConfigRetryMaxSeconds);
                yield return new WaitForSecondsRealtime(delay);

                if (!Enabled) break;

                attempt++;
                Debug.Log($"[CrossPromoModule] Config retry: попытка {attempt} (пауза была {delay:F1}s).");

                // LoadJson перезапишет _crossPromoConfig в любом случае, в том числе пустым
                // конфигом при неудаче. Здесь это безопасно: терять нечего — корутина крутится
                // только пока конфиг ни разу не доехал, и на первом же успехе выходит.
                yield return LoadJson();

                if (!_configFetchReturnedVideos) continue;

                Debug.Log($"[CrossPromoModule] Config retry: конфиг добран с попытки {attempt}, videos={_crossPromoConfig.Videos.Count}.");
                OnConfigLoaded?.Invoke(_crossPromoConfig);
                PreloadNextVideo();
            }

            _configRetryRunning = false;
            _configRetryCoroutine = null;
        }

        private IEnumerator LoadJson()
        {
            Debug.Log($"[CrossPromoModule] LoadJson() called. _configUrl='{_configUrl}'");

            if (string.IsNullOrWhiteSpace(_configUrl))
            {
                Debug.LogWarning("[CrossPromoModule] LoadJson: _configUrl is EMPTY — skipping config fetch!");
                yield break;
            }

            Debug.Log($"[CrossPromoModule] Starting FetchRemoteConfigAsync for URL: {_configUrl}");
            var operation = _configurationManager.FetchRemoteConfigAsync(_configUrl);

            float waitStart = Time.realtimeSinceStartup;
            while (!operation.IsCompleted)
            {
                yield return null;
            }
            float elapsed = Time.realtimeSinceStartup - waitStart;

            Debug.Log($"[CrossPromoModule] FetchRemoteConfigAsync completed in {elapsed:F2}s. Status={operation.Status}, IsCompletedSuccessfully={operation.IsCompletedSuccessfully}, IsFaulted={operation.IsFaulted}, IsCanceled={operation.IsCanceled}");

            if (operation.IsCompletedSuccessfully)
            {
                _crossPromoConfig = operation.Result;

                // Успешный таск ещё не значит успешный фетч: исчерпав свои попытки,
                // FetchRemoteConfigAsync отдаёт пустой конфиг обычным return'ом. Наличие
                // видео — единственный надёжный признак, что конфиг реально доехал.
                _configFetchReturnedVideos = _crossPromoConfig?.Videos != null
                                             && _crossPromoConfig.Videos.Count > 0;

                Debug.Log($"[CrossPromoModule] Config loaded OK. Videos={_crossPromoConfig?.Videos?.Count ?? 0}");

                if (_crossPromoConfig?.Videos != null)
                {
                    for (int i = 0; i < _crossPromoConfig.Videos.Count; i++)
                    {
                        var v = _crossPromoConfig.Videos[i];
                        Debug.Log($"[CrossPromoModule]   Video[{i}]: Title='{v.Title}', URL='{v.VideoUrl}', Weight={v.Weight}, MaxShow={v.MaxShowCount}, Packages=[{string.Join(",", v.AppPackageName ?? new())}]");
                        CrossPromoAdjustTracking.LogConfigWarnings(v);
                    }
                }
            }
            else
            {
                Debug.LogError($"[CrossPromoModule] Remote config load FAILED. Exception: {operation.Exception}");
            }
        }

        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            _currentBannerOnClose = onClose;
            _currentIsNoAds = isNoAds;
            OnBannerFuncsUpdated?.Invoke(onClose, isNoAds);
        }

        public void TrackImpression(string paidAppId)
        {
            var resolved = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            Debug.Log($"[CrossPromoModule] TrackImpression called, paidAppId={resolved ?? "null"} → delegating to Analytics");
            AmznGoDSDKCore.Instance?.TrackAnalyticsImpression(resolved);
        }

        public void TrackClick(string paidAppId)
        {
            var resolved = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            Debug.Log($"[CrossPromoModule] TrackClick called, paidAppId={resolved ?? "null"} → delegating to Analytics");
            AmznGoDSDKCore.Instance?.TrackAnalyticsClick(resolved);
        }

        /// <summary>
        /// Awaitable-версия <see cref="TrackClick"/>: отправка клика на наш бэкенд, которую можно
        /// дождаться перед открытием стора. Живёт на модуле (он переживает закрытие оверлея),
        /// поэтому незавершённые ретраи не обрываются вместе с оверлеем.
        /// </summary>
        public IEnumerator TrackClickRoutine(string paidAppId)
        {
            var resolved = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            Debug.Log($"[CrossPromoModule] TrackClickRoutine called, paidAppId={resolved ?? "null"} → delegating to Analytics (awaited)");
            var core = AmznGoDSDKCore.Instance;
            if (core == null)
                yield break;
            yield return core.TrackAnalyticsClickRoutine(resolved);
        }

        #region Interstitial

        /// <summary>
        /// Shows a cross-promo video as an interstitial ad.
        /// Identical to <see cref="ShowVideoPromo"/> but sends inter_requested / inter_displayed analytics.
        /// </summary>
        public void ShowInterstitial(Action onClose = null, Action onCTAClick = null)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowInterstitial: module is DISABLED");
                onClose?.Invoke();
                return;
            }

            // Запрос считается внутри ShowVideoInternalCoroutine — уже ПОСЛЕ busy-проверки,
            // когда вызов настоящий (см. задачу «request только когда он настоящий»).
            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, InterstitialPlacement, onRewarded: null));
        }

        #endregion

        #region Rewarded

        /// <summary>
        /// Shows a cross-promo video as a rewarded ad.
        /// <paramref name="onRewarded"/> is called when the video completes (the user earns the reward).
        /// </summary>
        public void ShowRewarded(Action onClose = null, Action onCTAClick = null, Action onRewarded = null)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowRewarded: module is DISABLED");
                onClose?.Invoke();
                return;
            }

            // Запрос считается внутри ShowVideoInternalCoroutine — уже ПОСЛЕ busy-проверки.
            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, RewardedPlacement, onRewarded));
        }

        #endregion

        #region Video Promo

        /// <summary>
        /// Shows a weighted-random video promo from the loaded configuration.
        /// Falls back to invoking <paramref name="onClose"/> immediately if no videos are available.
        ///
        /// <para>Аналитически это АЛИАС интерстишела: показ/клик/ошибка уходят в воронку
        /// <c>crosspromo_inter_*</c>. Отдельной воронки у этого API нет — без плейсмента события
        /// вообще никуда бы не отправлялись.</para>
        /// </summary>
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null)
        {
            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, InterstitialPlacement, onRewarded: null));
        }

        /// <summary>Shows a specific video promo by <see cref="PromoConfiguration"/>.
        /// Routed through the same preload-aware path as the parameterless overload, so if the
        /// preloaded player happens to hold this config's URL, playback starts instantly.
        /// Аналитически — тоже алиас интерстишела (см. перегрузку без config).</summary>
        public void ShowVideoPromo(PromoConfiguration config, Action onClose = null, Action onCTAClick = null)
        {
            if (config == null)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoPromo: config is null.");
                onClose?.Invoke();
                return;
            }

            _showCoroutine = StartCoroutine(ShowVideoInternalCoroutine(onClose, onCTAClick, InterstitialPlacement, onRewarded: null, forcedConfig: config));
        }

        /// <summary>Returns true if a video overlay is currently being shown.</summary>
        public bool IsVideoPromoVisible =>
            (_videoOverlay != null && _videoOverlay.IsVisible)
            || (_exoOverlay != null && _exoOverlay.IsVisible);

        private IEnumerator ShowVideoInternalCoroutine(Action onClose, Action onCTAClick, string placement, Action onRewarded, PromoConfiguration forcedConfig = null)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoInternal: module is DISABLED");
                onClose?.Invoke();
                yield break;
            }

            if (IsVideoPromoVisible)
            {
                Debug.LogWarning("[CrossPromoModule] ShowVideoInternal: another video promo is already visible, ignoring.");
                // Реклама уже на экране — это НЕ ошибка показа и запрос для неё не считается.
                if (placement == InterstitialPlacement || placement == RewardedPlacement)
                    CrossPromoAnalytics.ReportRejectedBusy(placement);
                onClose?.Invoke();
                yield break;
            }

            // Запрос считаем ЗДЕСЬ — вызов настоящий: busy-проверка пройдена, показ ещё
            // не начат. Раньше «запрос» уходил до этой проверки, поэтому запросов в статистике
            // было заметно больше, чем показов.
            if (placement == InterstitialPlacement)   CrossPromoAnalytics.ReportInterRequested(placement);
            else if (placement == RewardedPlacement)  CrossPromoAnalytics.ReportRewardRequested(placement);

            _crossPromoConfig?.CheckVideosShowLimit();
            _crossPromoConfig?.ApplyCooldownFilter(_lastShownConfig?.Title ?? _lastShownTitleFromPrefs);

            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
            {
                Debug.LogWarning("[CrossPromoModule] No video promos available in config.");
                CrossPromoAnalytics.ReportDisplayFailed(placement, "no_fill", null, null);
                // Показывать нечего — возможно, конфиг просто не доехал на старте. Заводим
                // фоновую дозагрузку, чтобы сессия не осталась без рекламы навсегда.
                EnsureConfigRetryRunning("no_fill");
                onClose?.Invoke();
                yield break;
            }

            Debug.Log($"[CrossPromoModule] Show: forcedConfig='{forcedConfig?.Title}', _preloadedConfig='{_preloadedConfig?.Title}', _lastShownConfig='{_lastShownConfig?.Title}'");
            // Прелоаженный креатив ищем в актуальном списке по Title, а НЕ сравниваем ссылку:
            // ApplyCooldownFilter каждый показ пересобирает Videos свежими Copy()-объектами,
            // поэтому ссылка, сохранённая при прелоаде, в текущем списке отсутствует ВСЕГДА.
            // Заодно берём актуальный объект, а не сохранённый снимок.
            PromoConfiguration config = forcedConfig;
            if (config == null && _preloadedConfig != null)
                config = _crossPromoConfig.Videos.Find(v => v.Title == _preloadedConfig.Title);

            // Прелоад потреблён — либо отброшен, если креатив успел выпасть из пула (юзер
            // поставил рекламируемый пейд / кулдаун / лимит показов). Чистим в обоих случаях:
            // иначе следующий показ повторил бы тот же ролик в обход кулдауна, а докачка
            // следующего креатива не запустилась бы из-за дедупа.
            _preloadedConfig = null;

            if (config == null)
                config = SelectWeightedRandom(_crossPromoConfig.Videos, _lastShownConfig);

            Debug.Log($"[CrossPromoModule] Show: selected config='{config?.Title}'");

            if (config == null || (string.IsNullOrWhiteSpace(config.VideoUrl) && string.IsNullOrWhiteSpace(config.FileName)))
            {
                Debug.LogWarning("[CrossPromoModule] Selected video promo has no URL or file name.");
                CrossPromoAnalytics.ReportDisplayFailed(placement, "no_url", null, config != null ? BuildBannerData(config) : null);
                onClose?.Invoke();
                yield break;
            }

            // ExoPlayer backend renders through a native StyledPlayerView overlay (no Unity
            // RawImage / preload-swap pipeline). Analytics/tracking/redirect/cooldown stay on
            // the C# bridge; we only hand it the selected config and the callbacks.
            if (_videoBackend == VideoPlayerBackend.ExoPlayer)
            {
                _lastShownConfig = config;

                // «Показ» (inter/reward_displayed + cp_impression + Adjust-impression + счётчик
                // показов) теперь репортит сам оверлей — на первом отрисованном кадре, а не здесь,
                // ещё до фактического показа. Если видео не загрузится, показ не засчитается, а
                // уйдёт crosspromo_*_display_failed.
                EnsureExoOverlay();
                _exoOverlay.Show(
                    config,
                    placement,
                    onClose: () => onClose?.Invoke(),
                    onCTA: onCTAClick,
                    onCompleted: onRewarded);
                yield break;
            }

            if (_videoOverlay == null)
            {
                Debug.LogError("[CrossPromoModule] VideoOverlay is not assigned. Cannot show video promo.");
                onClose?.Invoke();
                yield break;
            }

            bool usedPreload = false;
            if (_preloadPlayer != null)
            {
                string preloadUrl = ResolvePromoUrl(config);
                if (_preloadPlayer.PreloadedUrl == preloadUrl && _preloadPlayer.IsLoading)
                {
                    Debug.Log("[CrossPromoModule] Preload still loading, waiting...");
                    while (_preloadPlayer != null && _preloadPlayer.IsLoading)
                        yield return null;
                }

                if (_preloadPlayer != null
                    && _preloadPlayer.IsPreloaded
                    && _preloadPlayer.PreloadedUrl == preloadUrl)
                {
                    _preloadPlayer.OnError -= HandlePreloadError;
                    _preloadPlayer.Loop = false;
                    _videoOverlay.SwapVideoPlayer(_preloadPlayer);
                    _preloadPlayer = null;
                    _preloadedConfig = null;
                    usedPreload = true;
                }
            }

            if (!usedPreload && _videoOverlay.VideoPlayer != null)
                _videoOverlay.VideoPlayer.SetBackend(_videoBackend);

            _lastShownConfig = config;

            if (placement == InterstitialPlacement)
            {
                var data = BuildBannerData(config);
                CrossPromoAnalytics.ReportInterDisplayed(data, placement);
            }
            else if (placement == RewardedPlacement)
            {
                var data = BuildBannerData(config);
                CrossPromoAnalytics.ReportRewardDisplayed(data, placement);
            }

            if (onRewarded != null)
            {
                Action rewardHandler = null;
                rewardHandler = () =>
                {
                    _videoOverlay.OnVideoCompleted -= rewardHandler;
                    onRewarded.Invoke();
                };
                _videoOverlay.OnVideoCompleted += rewardHandler;
            }

            _videoOverlay.Show(config, placement, () =>
            {
                onClose?.Invoke();
            }, onCTAClick);

            StartCoroutine(DeferredPreloadNextVideo());
        }

        private IEnumerator DeferredPreloadNextVideo()
        {
            // Wait until the current video has finished and its MediaCodec has been
            // fully released. Preloading while the current MediaCodec is still alive
            // competes for hardware decoders on Android (MTK MT8169 / Amazon Fire
            // deadlocks the Unity main thread — see Log MGH 2805 freeze).
            float waitStart = Time.realtimeSinceStartup;
            const float maxWait = 120f;

            while (Time.realtimeSinceStartup - waitStart < maxWait)
            {
                if (_videoOverlay == null || !_videoOverlay.IsVisible)
                    break;

                var p = _videoOverlay.VideoPlayer;
                // ВАЖНО: Completed НЕ выпускает — на этом состоянии Stop() ещё не
                // успел сработать (либо вообще не позван), MediaCodec жив.
                // Idle/Error = декодер уже точно отпущен Stop()'ом или ошибкой.
                if (p == null
                    || p.State == CrossPromoVideoPlayer.PlaybackState.Idle
                    || p.State == CrossPromoVideoPlayer.PlaybackState.Error)
                    break;

                yield return null;
            }

            // Грейс с 0.15с до 1.5с: даём native MediaCodec.release() добежать.
            // MTK OMX teardown (~5 потоков: ColorConvert/Decode/Poll/MDP) тянется
            // 300–800мс, plus IPC до mediaserver. С 0.15с ловили race: новый
            // Prepare стартовал, пока старый decoder ещё держал handle.
            yield return new WaitForSecondsRealtime(1.5f);

            Debug.Log($"[CrossPromoModule][T={Time.realtimeSinceStartup:F2}] Deferred preload trigger (waited {Time.realtimeSinceStartup - waitStart:F2}s)");
            PreloadNextVideo();
        }

        private static BannerData BuildBannerData(PromoConfiguration config)
        {
            return new BannerData(
                config.Title, null, config.RedirectUrl, config.TrackingUrl,
                config.AppPackageName?.Count > 0 ? config.AppPackageName[0] : null);
        }

        private void EnsureExoOverlay()
        {
            if (_exoOverlay != null) return;

            // The GameObject name is the UnitySendMessage target for the native overlay's
            // callbacks, so it must stay unique and alive across scene loads.
            var go = new GameObject("CrossPromoExoOverlayBridge");
            _exoOverlay = go.AddComponent<CrossPromoExoNativeOverlay>();
            DontDestroyOnLoad(go);
        }

        private CrossPromoVideoPlayer EnsurePreloadPlayer()
        {
            if (_preloadPlayer != null) return _preloadPlayer;

            // Preloader lives on a detached DontDestroyOnLoad GameObject so that:
            //  * deactivation of the CrossPromo module's host GO doesn't silently cancel
            //    Unity VideoPlayer.Prepare() (which happens on inactive hierarchy);
            //  * scene changes don't wipe the buffered video.
            // It has no RawImage / MeshRenderer target, so its RenderTexture is never
            // displayed and audio stays silent (we only Prepare, not Play, until swap).
            //
            // Create inactive first so Awake() is deferred — lets us set the backend
            // BEFORE Awake() spins up a default Unity VideoPlayer component that
            // would be useless for the ExoPlayer backend.
            var go = new GameObject("CrossPromoPreloader");
            go.SetActive(false);
            _preloadPlayer = go.AddComponent<CrossPromoVideoPlayer>();
            _preloadPlayer.SetBackend(_videoBackend);
            _preloadPlayer.OnError += HandlePreloadError;
            go.SetActive(true);
            DontDestroyOnLoad(go);
            return _preloadPlayer;
        }

        /// <summary>
        /// Готовит следующий креатив к показу. Для ExoPlayer-бэкенда это фоновая докачка
        /// ролика в дисковый кэш ExoPlayer (нативный <c>CrossPromoExoCache</c>), для
        /// Unity-бэкенда — прогрев выделенного <see cref="CrossPromoVideoPlayer"/>.
        /// Вызывается на старте (после загрузки конфига) и на каждом конце показа —
        /// см. <see cref="CrossPromoExoNativeOverlay"/>.
        /// </summary>
        internal void PreloadNextVideo()
        {
            if (!Enabled) return;

            if (_crossPromoConfig?.Videos == null || _crossPromoConfig.Videos.Count == 0)
                return;

            // Дедуп только для Exo: нативная докачка уходит в фон и о своём завершении не
            // сообщает, поэтому единственный признак «уже готов / уже качается» —
            // непустой _preloadedConfig. Unity-путь опирается на состояние _preloadPlayer и
            // обязан уметь перезапускаться (RefreshPreloadIfStale) — его guard не касается.
            if (_videoBackend == VideoPlayerBackend.ExoPlayer && _preloadedConfig != null)
                return;

            _crossPromoConfig.CheckVideosShowLimit();
            _crossPromoConfig.ApplyCooldownFilter(_lastShownConfig?.Title ?? _lastShownTitleFromPrefs);
            if (_crossPromoConfig.Videos.Count == 0)
                return;

            Debug.Log($"[CrossPromoModule] PreloadNextVideo: _lastShownConfig='{_lastShownConfig?.Title}', videos.Count={_crossPromoConfig.Videos.Count}");
            var next = SelectWeightedRandom(_crossPromoConfig.Videos, _lastShownConfig);
            if (next == null || (string.IsNullOrWhiteSpace(next.VideoUrl) && string.IsNullOrWhiteSpace(next.FileName)))
                return;

            // Резолвим URL ДО того, как занять _preloadedConfig: иначе креатив с пустой
            // ссылкой навсегда залипал бы в дедупе и глушил всю дальнейшую докачку.
            string url = ResolvePromoUrl(next);
            if (string.IsNullOrWhiteSpace(url))
                return;

            _preloadedConfig = next;

            if (_videoBackend == VideoPlayerBackend.ExoPlayer)
            {
                NativePreload(url, next.Title);
                return;
            }

            var player = EnsurePreloadPlayer();
            player.SetBackend(_videoBackend);
            player.Loop = false;
            // Warmup'им КАЖДОЕ предзагружаемое видео, не только первое. Изначально SDK
            // прогревал MediaCodec только на первом preload (через _firstWarmupTriggered),
            // но на практике на слабых Android-устройствах следующие preload'ы стартуют
            // с холодным декодером — отсюда 5-7 секунд лага при показе. Лишний muted-loop
            // в фоне на одном дополнительном MediaCodec — приемлемая цена, чтобы каждый
            // последующий swap был мгновенным.
            player.WarmupOnReady = true;
            _firstWarmupTriggered = true;
            player.Preload(url);
            Debug.Log($"[CrossPromoModule] Preloading next video on dedicated player: {next.Title} (warmup={player.WarmupOnReady})");

            // Запрос («requested») больше НЕ шлётся из preload: preload — это внутренняя
            // подготовка, а не запрос показа. Событие уходит из ShowVideoInternalCoroutine,
            // когда пользователь реально запрашивает показ.
        }

        /// <summary>
        /// Фоновая докачка ролика в дисковый кэш нативного ExoPlayer. Fire-and-forget:
        /// нативная сторона качает на своём потоке и о результате не сообщает. Любой сбой
        /// здесь безвреден — показ просто отработает как раньше, стримом.
        /// </summary>
        private static void NativePreload(string url, string title)
        {
            // streamingAssets / локальный файл кэшировать незачем — он и так на устройстве.
            if (!CrossPromoAdjustTracking.IsHttpUrl(url))
                return;

            // Платформенный guard обязателен: PreloadNextVideo зовётся из Initialize, где
            // try/finally без catch — в Editor непойманный AndroidJavaException оборвал бы
            // инициализацию модуля целиком.
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                ExoCacheClass?.CallStatic("preload", url);
                Debug.Log($"[CrossPromoModule] Native preload requested: '{title}'");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CrossPromoModule] Native preload call failed: {e.Message}");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaClass _exoCacheClass;

        /// <summary>
        /// Ленивая ссылка на нативный класс кэша, живущая весь процесс. Пересоздавать
        /// AndroidJavaClass на каждое обращение нельзя: <see cref="IsPreloadedVideoCached"/>
        /// опрашивают из Update.
        /// </summary>
        private static AndroidJavaClass ExoCacheClass
        {
            get
            {
                if (_exoCacheClass != null) return _exoCacheClass;
                try
                {
                    _exoCacheClass = new AndroidJavaClass("com.amzngod.exoplayer.CrossPromoExoCache");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CrossPromoModule] CrossPromoExoCache is unavailable: {e.Message}");
                }
                return _exoCacheClass;
            }
        }
#endif

        /// <summary>
        /// True, когда следующий креатив реально лежит в дисковом кэше и стартует без
        /// буферизации.
        /// <para>
        /// В отличие от <see cref="IsVideoReady"/> (который для ExoPlayer-бэкенда лишь
        /// сообщает, что конфиг не пуст) это честный признак готовности. Именно поэтому он
        /// НЕ годится как условие показа рекламы: если кэш на устройстве недоступен или
        /// докачка не прошла, он останется false навсегда — и показы прекратились бы совсем.
        /// Предназначен для отладочных и тестовых сборок.
        /// </para>
        /// </summary>
        public bool IsPreloadedVideoCached
        {
            get
            {
#if !UNITY_ANDROID || UNITY_EDITOR
                // Нативного кэша здесь нет — не блокируем отладку в Editor.
                return IsVideoReady;
#else
                if (_videoBackend != VideoPlayerBackend.ExoPlayer)
                    return IsVideoReady;   // Unity-бэкенд: готовность = прогретый preload-плеер

                if (_preloadedConfig == null)
                    return false;          // докачка ещё не выбрала креатив либо уже потреблена

                try
                {
                    string url = ResolvePromoUrl(_preloadedConfig);
                    if (string.IsNullOrWhiteSpace(url))
                        return false;

                    // Локальный креатив кэшировать нечего — он и так на устройстве.
                    if (!CrossPromoAdjustTracking.IsHttpUrl(url))
                        return true;

                    return ExoCacheClass != null && ExoCacheClass.CallStatic<bool>("isCached", url);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CrossPromoModule] isCached call failed: {e.Message}");
                    return false;
                }
#endif
            }
        }

        private static string ResolvePromoUrl(PromoConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.VideoUrl))
                return config.VideoUrl;

            if (!string.IsNullOrWhiteSpace(config.FileName))
            {
                string ext = config.FileExtension.ToString();
                string fileName = config.FileName.EndsWith($".{ext}", System.StringComparison.OrdinalIgnoreCase)
                    ? config.FileName
                    : $"{config.FileName}.{ext}";
                return System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            }

            return null;
        }

        private static PromoConfiguration SelectWeightedRandom(List<PromoConfiguration> videos, PromoConfiguration exclude = null)
        {
            if (videos == null || videos.Count == 0)
                return null;

            // Title-based exclude comparison: reference equality breaks if the Videos
            // list was rebuilt (e.g. config reparse), producing a silently biased pool.
            var pool = (exclude != null && videos.Count > 1)
                ? videos.Where(v => v.Title != exclude.Title).ToList()
                : videos;

            if (pool.Count == 0)
                pool = videos;

            float total = pool.Sum(v => v.Weight);
            Debug.Log($"[CrossPromoModule] SelectWeightedRandom: pool={pool.Count}/{videos.Count}, excludeTitle='{exclude?.Title}', total={total:F4}, weights=[{string.Join(",", pool.Select(v => $"{v.Title}:{v.Weight:F3}"))}]");

            if (total <= 0)
            {
                Debug.LogWarning($"[CrossPromoModule] SelectWeightedRandom: total weight <= 0, falling back to pool[0]='{pool[0].Title}'");
                return pool[0];
            }

            float random = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var video in pool)
            {
                cumulative += video.Weight;
                if (random < cumulative)
                {
                    Debug.Log($"[CrossPromoModule] SelectWeightedRandom: random={random:F4} → picked '{video.Title}'");
                    return video;
                }
            }

            Debug.Log($"[CrossPromoModule] SelectWeightedRandom: random={random:F4} → fallthrough, picked '{pool[pool.Count - 1].Title}'");
            return pool[pool.Count - 1];
        }

        #endregion

        public override void Cleanup()
        {
        }
    }
}
#endif
