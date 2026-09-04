#if AMZN_APPLOVIN_ENABLED
using System;
using System.Collections;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Обёртка над AppLovin MAX. Сам плагин в пакет SDK не входит и живёт отдельно
    /// (Assets/MaxSdk либо UPM-пакет) — по той же схеме, что модуль Firebase: здесь только
    /// тонкий адаптер под фасад AmznGoDSDKCore.
    /// <para>
    /// Модуль держит interstitial и rewarded ПРОГРЕТЫМИ с момента инициализации, а не
    /// подгружает их в момент запроса. Иначе переключение с кросс-промо на медиацию было бы
    /// рваным: кросс-промо исчерпывает капы посреди сессии, и первый же показ после этого
    /// упёрся бы в незагруженный ad unit.
    /// </para>
    /// </summary>
    public class AppLovinModule : ModuleBase
    {
        // Плейсменты MAX — попадают в отчётность AppLovin, зеркалят названия воронок
        // кросс-промо (CrossPromoModule.InterstitialPlacement / RewardedPlacement).
        private const string InterstitialPlacement = "interstitial";
        private const string RewardedPlacement = "rewarded";

        // Бэкофф ретраев из документации MAX: 2^n секунд с потолком 2^6 = 64с.
        private const int MaxRetryExponent = 6;

        private string _sdkKey;
        private string _interstitialAdUnitId;
        private string _rewardedAdUnitId;
        private bool _verboseLogging;

        private bool _sdkInitialized;
        private bool _callbacksSubscribed;

        private int _interstitialRetryAttempt;
        private int _rewardedRetryAttempt;
        private Coroutine _interstitialRetryCoroutine;
        private Coroutine _rewardedRetryCoroutine;

        // Колбэки текущего показа. Обнуляются ПЕРЕД вызовом, чтобы onClose не ушёл дважды
        // (MAX умеет прислать и display_failed, и hidden по одному и тому же показу).
        private Action _interstitialOnClose;
        private Action _rewardedOnClose;
        private Action _rewardedOnEarned;
        private bool _rewardEarned;

        /// <summary>SDK поднялся и ad unit'ы можно грузить.</summary>
        public bool IsInitialized => _sdkInitialized;

        /// <summary>
        /// Interstitial загружен и готов к мгновенному показу. Роутер обязан проверять
        /// именно это перед тем, как отдавать показ в медиацию: незагруженный ad unit
        /// означает «фила нет», и запрос должен закрыться без рекламы, а не подвиснуть.
        /// </summary>
        public bool IsInterstitialReady =>
            _sdkInitialized
            && !string.IsNullOrWhiteSpace(_interstitialAdUnitId)
            && MaxSdk.IsInterstitialReady(_interstitialAdUnitId);

        /// <summary>Rewarded загружен и готов к мгновенному показу.</summary>
        public bool IsRewardedReady =>
            _sdkInitialized
            && !string.IsNullOrWhiteSpace(_rewardedAdUnitId)
            && MaxSdk.IsRewardedAdReady(_rewardedAdUnitId);

        public void Construct(bool enable, string sdkKey, string interstitialAdUnitId, string rewardedAdUnitId, bool verboseLogging)
        {
            Enabled = enable;
            _sdkKey = sdkKey;
            _interstitialAdUnitId = interstitialAdUnitId;
            _rewardedAdUnitId = rewardedAdUnitId;
            _verboseLogging = verboseLogging;

            Debug.Log($"[AppLovinModule] Construct() called. Enabled={enable}, " +
                      $"interstitial='{interstitialAdUnitId}', rewarded='{rewardedAdUnitId}', verbose={verboseLogging}");
        }

        public override void Initialize()
        {
            if (!Enabled)
                return;

            if (string.IsNullOrWhiteSpace(_interstitialAdUnitId) && string.IsNullOrWhiteSpace(_rewardedAdUnitId))
            {
                Debug.LogError("[AppLovinModule] Не задан ни один ad unit id — медиация выключена. " +
                               "Заполни поля в AMZN GoD > SDK Settings > AppLovin.");
                Enabled = false;
                return;
            }

            SubscribeCallbacks();

            MaxSdk.SetVerboseLogging(_verboseLogging);

            // Ключ дублирует значение из AppLovin Integration Manager (AppLovinSettings).
            // Ставим его только если он задан в настройках SDK: пустая строка затёрла бы
            // рабочее значение из Integration Manager и уронила бы инициализацию.
            if (!string.IsNullOrWhiteSpace(_sdkKey))
                MaxSdk.SetSdkKey(_sdkKey);

            Debug.Log("[AppLovinModule] MaxSdk.InitializeSdk()...");
            MaxSdk.InitializeSdk();
        }

        public override void Cleanup()
        {
            UnsubscribeCallbacks();
            StopRetryCoroutines();
            _sdkInitialized = false;
        }

        private void OnDestroy()
        {
            // Подписки на статические события MaxSdkCallbacks переживают уничтожение объекта
            // и утекли бы вместе со всем модулем: MAX продолжил бы дёргать колбэки уничтоженного
            // MonoBehaviour, а следующий экземпляр подписался бы вторым.
            Cleanup();

            _interstitialOnClose = null;
            _rewardedOnClose = null;
            _rewardedOnEarned = null;
        }

        #region Show

        /// <summary>
        /// Показывает interstitial. Возвращает false, если показывать нечего — тогда вызывающий
        /// сам решает, что делать с запросом (<paramref name="onClose"/> в этом случае НЕ дёргается,
        /// его вызовет роутер).
        /// </summary>
        public bool ShowInterstitial(Action onClose)
        {
            if (!Enabled || !IsInterstitialReady)
            {
                Debug.LogWarning($"[AppLovinModule] ShowInterstitial: нет готового ad'а (Enabled={Enabled}, initialized={_sdkInitialized}).");

                // Отказ репортим только при включённом модуле: у выключенного роутер сюда даже
                // не заходит, и событие означало бы несуществующий запрос к медиации.
                if (Enabled)
                    AppLovinAnalytics.ReportNoFill(InterstitialPlacement, _sdkInitialized);

                return false;
            }

            _interstitialOnClose = onClose;
            Debug.Log($"[AppLovinModule] ShowInterstitial('{_interstitialAdUnitId}')");

            // Запрос репортим ДО показа: если MAX не сумеет отрисовать, придёт display_failed,
            // и пара «запрос → ошибка» сойдётся. Отчёт после показа такую ошибку бы потерял.
            AppLovinAnalytics.ReportInterRequested(InterstitialPlacement);

            MaxSdk.ShowInterstitial(_interstitialAdUnitId, InterstitialPlacement);
            return true;
        }

        /// <summary>
        /// Показывает rewarded. <paramref name="onRewarded"/> дёргается только если MAX прислал
        /// награду; порядок гарантирован — сначала onRewarded, потом onClose.
        /// </summary>
        public bool ShowRewarded(Action onClose, Action onRewarded)
        {
            if (!Enabled || !IsRewardedReady)
            {
                Debug.LogWarning($"[AppLovinModule] ShowRewarded: нет готового ad'а (Enabled={Enabled}, initialized={_sdkInitialized}).");

                if (Enabled)
                    AppLovinAnalytics.ReportNoFill(RewardedPlacement, _sdkInitialized);

                return false;
            }

            _rewardedOnClose = onClose;
            _rewardedOnEarned = onRewarded;
            _rewardEarned = false;
            Debug.Log($"[AppLovinModule] ShowRewarded('{_rewardedAdUnitId}')");

            AppLovinAnalytics.ReportRewardRequested(RewardedPlacement);

            MaxSdk.ShowRewardedAd(_rewardedAdUnitId, RewardedPlacement);
            return true;
        }

        #endregion

        #region Callbacks

        private void SubscribeCallbacks()
        {
            if (_callbacksSubscribed)
                return;

            MaxSdkCallbacks.OnSdkInitializedEvent += OnSdkInitialized;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayed;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClicked;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHidden;

            // Impression-level revenue. Без этой подписки выручка показа не доезжает ни до
            // Adjust, ни до AppMetrica — ROAS и LTV по рекламе перестают считаться.
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnInterstitialRevenuePaid;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedDisplayed;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedClicked;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedReceivedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedRevenuePaid;

            _callbacksSubscribed = true;
        }

        private void UnsubscribeCallbacks()
        {
            if (!_callbacksSubscribed)
                return;

            MaxSdkCallbacks.OnSdkInitializedEvent -= OnSdkInitialized;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent -= OnInterstitialDisplayed;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent -= OnInterstitialClicked;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= OnInterstitialDisplayFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent -= OnInterstitialRevenuePaid;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent -= OnRewardedDisplayed;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent -= OnRewardedClicked;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= OnRewardedReceivedReward;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent -= OnRewardedRevenuePaid;

            _callbacksSubscribed = false;
        }

        private void OnSdkInitialized(MaxSdkBase.SdkConfiguration configuration)
        {
            _sdkInitialized = true;
            Debug.Log($"[AppLovinModule] SDK initialized. CountryCode={configuration.CountryCode}");

            LoadInterstitial();
            LoadRewarded();
        }

        private void OnInterstitialLoaded(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            _interstitialRetryAttempt = 0;
            Debug.Log($"[AppLovinModule] Interstitial loaded: network='{adInfo.NetworkName}', revenue={adInfo.Revenue}");
        }

        private void OnInterstitialLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            Debug.LogWarning($"[AppLovinModule] Interstitial load failed: {errorInfo.Code} {errorInfo.Message}");
            ScheduleInterstitialRetry();
        }

        private void OnInterstitialDisplayed(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"[AppLovinModule] Interstitial displayed: network='{adInfo.NetworkName}'");
            AppLovinAnalytics.ReportDisplayed(InterstitialPlacement, adInfo);
        }

        private void OnInterstitialClicked(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            AppLovinAnalytics.ReportClicked(InterstitialPlacement, adInfo);
        }

        /// <summary>
        /// Выручка показа. MAX присылает событие один раз на показ, и это единственный источник
        /// impression-level revenue — ретранслируем его в трекеры как есть, без агрегации.
        /// </summary>
        private void OnInterstitialRevenuePaid(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            AppLovinAnalytics.ReportAdRevenue(InterstitialPlacement, adInfo);
        }

        private void OnInterstitialDisplayFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            Debug.LogWarning($"[AppLovinModule] Interstitial display failed: {errorInfo.Code} {errorInfo.Message}");
            AppLovinAnalytics.ReportDisplayFailed(InterstitialPlacement, errorInfo, adInfo);
            InvokeOnce(ref _interstitialOnClose);
            LoadInterstitial();
        }

        private void OnInterstitialHidden(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log("[AppLovinModule] Interstitial hidden");
            AppLovinAnalytics.ReportHidden(InterstitialPlacement, adInfo);
            InvokeOnce(ref _interstitialOnClose);
            LoadInterstitial();
        }

        private void OnRewardedLoaded(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            _rewardedRetryAttempt = 0;
            Debug.Log($"[AppLovinModule] Rewarded loaded: network='{adInfo.NetworkName}', revenue={adInfo.Revenue}");
        }

        private void OnRewardedLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            Debug.LogWarning($"[AppLovinModule] Rewarded load failed: {errorInfo.Code} {errorInfo.Message}");
            ScheduleRewardedRetry();
        }

        private void OnRewardedDisplayed(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"[AppLovinModule] Rewarded displayed: network='{adInfo.NetworkName}'");
            AppLovinAnalytics.ReportDisplayed(RewardedPlacement, adInfo);
        }

        private void OnRewardedClicked(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            AppLovinAnalytics.ReportClicked(RewardedPlacement, adInfo);
        }

        private void OnRewardedRevenuePaid(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            AppLovinAnalytics.ReportAdRevenue(RewardedPlacement, adInfo);
        }

        private void OnRewardedDisplayFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            Debug.LogWarning($"[AppLovinModule] Rewarded display failed: {errorInfo.Code} {errorInfo.Message}");
            AppLovinAnalytics.ReportDisplayFailed(RewardedPlacement, errorInfo, adInfo);
            _rewardedOnEarned = null;   // награды не было — колбэк награды не должен пережить показ
            InvokeOnce(ref _rewardedOnClose);
            LoadRewarded();
        }

        private void OnRewardedReceivedReward(string adUnitId, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            _rewardEarned = true;
            Debug.Log($"[AppLovinModule] Reward received: {reward.Amount} {reward.Label}");
            AppLovinAnalytics.ReportRewardEarned(reward, adInfo);
        }

        private void OnRewardedHidden(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            Debug.Log($"[AppLovinModule] Rewarded hidden (earned={_rewardEarned})");
            AppLovinAnalytics.ReportHidden(RewardedPlacement, adInfo);

            // Награду отдаём ДО onClose: игра обычно закрывает свой UI по onClose и начисление
            // после этого прилетело бы в уже разобранный экран.
            if (_rewardEarned)
                InvokeOnce(ref _rewardedOnEarned);
            else
                _rewardedOnEarned = null;

            _rewardEarned = false;
            InvokeOnce(ref _rewardedOnClose);
            LoadRewarded();
        }

        /// <summary>
        /// Дёргает колбэк ровно один раз: сначала забирает ссылку и обнуляет поле, потом
        /// вызывает. MAX по одному показу может прислать и display_failed, и hidden.
        /// </summary>
        private static void InvokeOnce(ref Action callback)
        {
            var toInvoke = callback;
            callback = null;

            if (toInvoke == null)
                return;

            try
            {
                toInvoke.Invoke();
            }
            catch (Exception ex)
            {
                // Исключение из игрового колбэка не должно оборвать перезагрузку ad'а.
                Debug.LogError($"[AppLovinModule] Исключение в колбэке игры: {ex}");
            }
        }

        #endregion

        #region Load & retry

        private void LoadInterstitial()
        {
            if (!Enabled || !_sdkInitialized || string.IsNullOrWhiteSpace(_interstitialAdUnitId))
                return;

            MaxSdk.LoadInterstitial(_interstitialAdUnitId);
        }

        private void LoadRewarded()
        {
            if (!Enabled || !_sdkInitialized || string.IsNullOrWhiteSpace(_rewardedAdUnitId))
                return;

            MaxSdk.LoadRewardedAd(_rewardedAdUnitId);
        }

        private void ScheduleInterstitialRetry()
        {
            if (_interstitialRetryCoroutine != null)
                return;

            _interstitialRetryAttempt++;
            float delay = RetryDelaySeconds(_interstitialRetryAttempt);
            Debug.Log($"[AppLovinModule] Interstitial retry #{_interstitialRetryAttempt} через {delay}с");
            _interstitialRetryCoroutine = StartCoroutine(RetryAfter(delay, isInterstitial: true));
        }

        private void ScheduleRewardedRetry()
        {
            if (_rewardedRetryCoroutine != null)
                return;

            _rewardedRetryAttempt++;
            float delay = RetryDelaySeconds(_rewardedRetryAttempt);
            Debug.Log($"[AppLovinModule] Rewarded retry #{_rewardedRetryAttempt} через {delay}с");
            _rewardedRetryCoroutine = StartCoroutine(RetryAfter(delay, isInterstitial: false));
        }

        private static float RetryDelaySeconds(int attempt)
        {
            return Mathf.Pow(2f, Mathf.Min(MaxRetryExponent, attempt));
        }

        private IEnumerator RetryAfter(float delay, bool isInterstitial)
        {
            // Именно unscaled: игра может стоять на паузе с timeScale = 0, и обычный
            // WaitForSeconds не дотикал бы никогда.
            yield return new WaitForSecondsRealtime(delay);

            if (isInterstitial)
            {
                _interstitialRetryCoroutine = null;
                LoadInterstitial();
            }
            else
            {
                _rewardedRetryCoroutine = null;
                LoadRewarded();
            }
        }

        private void StopRetryCoroutines()
        {
            if (_interstitialRetryCoroutine != null)
            {
                StopCoroutine(_interstitialRetryCoroutine);
                _interstitialRetryCoroutine = null;
            }

            if (_rewardedRetryCoroutine != null)
            {
                StopCoroutine(_rewardedRetryCoroutine);
                _rewardedRetryCoroutine = null;
            }
        }

        #endregion
    }
}
#endif
