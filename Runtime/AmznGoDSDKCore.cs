using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public sealed class AmznGoDSDKCore : MonoBehaviourSingletonPersistent<AmznGoDSDKCore>
    {
        public const string SdkVersion = "0.5.0";


#if AMZN_ADJUST_ENABLED
        [SerializeField] private AdjustModule _adjustModule;
#endif
#if AMZN_APPMETRICA_ENABLED
        [SerializeField] private AppMetricaModule _appMetricaModule;
#endif
#if AMZN_CROSSPROMO_ENABLED
        [SerializeField] private CrossPromoModule _crossPromoModule;
#endif
#if AMZN_IAP_ENABLED
        [SerializeField] private InAppPurchaseModule _inAppPurchaseModule;
#endif
#if AMZN_FIREBASE_ENABLED
        [SerializeField] private FirebaseModule _firebaseModule;
#endif
#if AMZN_INTERNETCONNECTION_ENABLED
        [SerializeField] private InternetConnectionModule _internetConnectionModule;
#endif
#if AMZN_ANALYTICS_ENABLED
        [SerializeField] private AnalyticsModule _analyticsModule;
#endif

        public bool Enabled { get; private set; }
        public bool IsInitialized { get; private set; }

        public event Action OnInitializationComplete;

        #region Awake
        protected override void OnAwake()
        {
            SdkSettingsData sdkSettingsData = DataLoader.LoadSettings();

            Enabled = sdkSettingsData.Enabled;
            
            if (!Enabled)
            {
                gameObject.SetActive(Enabled);
                return;
            }

            var analyticsSettings = sdkSettingsData.Analytics;
            var internetSettings = sdkSettingsData.InternetConnection;
            var firebaseSettings = sdkSettingsData.Firebase;
            var crossPromoSettings = sdkSettingsData.CrossPromo;
            var appMetricaSettings = sdkSettingsData.AppMetrica;
            var adjustSettings = sdkSettingsData.Adjust;
            var inAppPurchaseSettings = sdkSettingsData.InAppPurchase;

            // Доверенное время для периодов подписки (IAP-14) ходит за заголовком Date на
            // бэкенд аналитики — подставляем настроенный адрес, а не только дефолтный.
            if (!string.IsNullOrWhiteSpace(analyticsSettings.BaseUrl))
                SdkTrustedTime.BaseUrl = analyticsSettings.BaseUrl;

#if AMZN_INTERNETCONNECTION_ENABLED
            EnsureInternetConnectionModule();
#endif
#if AMZN_FIREBASE_ENABLED
            EnsureFirebaseModule();
#endif
#if AMZN_ANALYTICS_ENABLED
            EnsureAnalyticsModule();
#endif

            #region Constructs

#if AMZN_INTERNETCONNECTION_ENABLED
            _internetConnectionModule.Construct(internetSettings.Enabled, internetSettings);
#endif

#if AMZN_ANALYTICS_ENABLED
            _analyticsModule.Construct(
                analyticsSettings.Enabled,
                analyticsSettings.BaseUrl,
                analyticsSettings.ApiKey,
                analyticsSettings.AppType,
                crossPromoSettings.DefaultPromotedAppId);
#endif

#if AMZN_FIREBASE_ENABLED
            _firebaseModule.Construct(
                firebaseSettings.Enabled,
                firebaseSettings.EnableAnalytics,
                firebaseSettings.EnableCrashlytics);
#endif

#if AMZN_CROSSPROMO_ENABLED
            _crossPromoModule.Construct(crossPromoSettings);
#endif
            
#if AMZN_APPMETRICA_ENABLED
            _appMetricaModule.Construct(
                appMetricaSettings.Enabled,
                appMetricaSettings.Key);
#endif
            
#if AMZN_ADJUST_ENABLED
            var adjustEnvironment = adjustSettings.Environment == AdjustSettingData.AdjustEnvironment.Production
                ? AdjustSdk.AdjustEnvironment.Production
                : AdjustSdk.AdjustEnvironment.Sandbox;
            
            _adjustModule.Construct(
                adjustSettings.Enabled,
                adjustSettings.Key,
                adjustEnvironment);
#endif

#if AMZN_IAP_ENABLED
            _inAppPurchaseModule.Construct(inAppPurchaseSettings);
#endif

            #endregion
            
            var modules = new List<ModuleBase>();
#if AMZN_ANALYTICS_ENABLED
            modules.Add(_analyticsModule);
#endif
#if AMZN_FIREBASE_ENABLED
            modules.Add(_firebaseModule);
#endif
#if AMZN_CROSSPROMO_ENABLED
            modules.Add(_crossPromoModule);
#endif
#if AMZN_APPMETRICA_ENABLED
            modules.Add(_appMetricaModule);
#endif
#if AMZN_ADJUST_ENABLED
            modules.Add(_adjustModule);
#endif
#if AMZN_IAP_ENABLED
            modules.Add(_inAppPurchaseModule);
#endif

            StartCoroutine(InitializeWhenReady(modules.ToArray()));
        }
        #endregion

        #region Cross Promo

#if AMZN_CROSSPROMO_ENABLED
        /// <summary>
        /// Shows a weighted-random video cross-promo from remote config.
        /// </summary>
        /// <param name="onClose">Called when the overlay is closed.</param>
        /// <param name="onCTAClick">Called when the user taps the CTA button.</param>
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null)
        {
            if (!_crossPromoModule.Enabled)
            {
                onClose?.Invoke();
                return;
            }

            _crossPromoModule.ShowVideoPromo(onClose, onCTAClick);
        }

        /// <summary>True if a video promo overlay is currently visible.</summary>
        public bool IsVideoPromoVisible => _crossPromoModule.IsVideoPromoVisible;

        /// <summary>True when a cross-promo video is preloaded and ready to play instantly.</summary>
        public bool IsVideoReady => _crossPromoModule.IsVideoReady;

        /// <summary>
        /// True when the next creative is actually sitting in the on-disk cache, so the show
        /// starts without buffering. Diagnostic signal for debug / QA builds — do NOT gate ad
        /// display on it: if the device cache is unavailable it stays false forever and ads
        /// would stop entirely. Use <see cref="IsVideoReady"/> for that.
        /// </summary>
        public bool IsPreloadedVideoCached => _crossPromoModule.IsPreloadedVideoCached;

        /// <summary>
        /// Time.realtimeSinceStartup when the next creative's download was requested, or -1 if
        /// it hasn't been requested this session. Pair it with <see cref="IsPreloadedVideoCached"/>
        /// to measure how long the download actually took: the preload starts when the previous
        /// ad's video ENDS, not when the user closes it, so timing from the close (or from the
        /// moment the previous creative was consumed) also counts the whole playback.
        /// </summary>
        public float LastPreloadRequestedRealtime => _crossPromoModule.LastPreloadRequestedRealtime;

        /// <summary>
        /// Shows a cross-promo video as an interstitial.
        /// </summary>
        public void ShowInterstitial(Action onClose = null, Action onCTAClick = null)
        {
            if (!_crossPromoModule.Enabled)
            {
                onClose?.Invoke();
                return;
            }

            _crossPromoModule.ShowInterstitial(onClose, onCTAClick);
        }

        /// <summary>
        /// Shows a cross-promo video as a rewarded ad.
        /// <paramref name="onRewarded"/> fires when the video completes.
        /// </summary>
        public void ShowRewarded(Action onClose = null, Action onCTAClick = null, Action onRewarded = null)
        {
            if (!_crossPromoModule.Enabled)
            {
                onClose?.Invoke();
                return;
            }

            _crossPromoModule.ShowRewarded(onClose, onCTAClick, onRewarded);
        }

        /// <summary>
        /// Safety hook: ensures the next-video preload is alive and warmed up. No-op if a
        /// preload is already healthy. Intended to be called on scene transitions to
        /// recover from rare cases where the preload was lost mid-flight.
        /// </summary>
        public void RefreshPreloadIfStale()
        {
            if (_crossPromoModule == null || !_crossPromoModule.Enabled) return;
            _crossPromoModule.RefreshPreloadIfStale();
        }
#else
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null) { onClose?.Invoke(); }
        public bool IsVideoPromoVisible => false;
        public bool IsVideoReady => false;
        public bool IsPreloadedVideoCached => false;
        public float LastPreloadRequestedRealtime => -1f;
        public void ShowInterstitial(Action onClose = null, Action onCTAClick = null) { onClose?.Invoke(); }
        public void ShowRewarded(Action onClose = null, Action onCTAClick = null, Action onRewarded = null) { onClose?.Invoke(); }
        public void RefreshPreloadIfStale() { }
#endif

        #endregion
        
        #region AppMetrica

#if AMZN_APPMETRICA_ENABLED
        // Проверка Initialized обязательна (ТЗ IAP-01): если AppMetrica упала на битом
        // ключе, исключение инициализации проглатывается в InitializeModules, Enabled
        // остаётся true — и каждый репорт бился бы о мёртвый модуль. null-гард — на случай
        // вызова до OnAwake (модуль ещё не привязан).
        public void ReportEventAppMetrica(string eventName, Dictionary<string, string> args)
        {
            if (_appMetricaModule == null || !_appMetricaModule.Enabled || !_appMetricaModule.Initialized)
                return;

            _appMetricaModule.ReportEvent(eventName, args);
        }

        public void ReportEventRawAppMetrica(string eventName, string jsonValue)
        {
            if (_appMetricaModule == null || !_appMetricaModule.Enabled || !_appMetricaModule.Initialized)
                return;

            _appMetricaModule.ReportEventRaw(eventName, jsonValue);
        }
#else
        public void ReportEventAppMetrica(string eventName, Dictionary<string, string> args) { }
        public void ReportEventRawAppMetrica(string eventName, string jsonValue) { }
#endif
        
        #endregion
        
        #region Adjust

#if AMZN_ADJUST_ENABLED
        public void ReportEventAdjust(string token, Dictionary<string, string> args)
        {
            if(!_adjustModule.Enabled)
                return;

            _adjustModule.ReportEvent(token, args);
        }
#else
        public void ReportEventAdjust(string token, Dictionary<string, string> args) { }
#endif

        #endregion

        #region Analytics

#if AMZN_ANALYTICS_ENABLED
        public void TrackAnalyticsImpression(string paidAppId) => _analyticsModule?.TrackImpression(paidAppId);
        public void TrackAnalyticsClick(string paidAppId) => _analyticsModule?.TrackClick(paidAppId);

        /// <summary>Связка device ↔ Amazon-покупатель (событие iap_link). Зовётся из
        /// InAppPurchaseModule по завершении полной сверки GetPurchaseUpdates.</summary>
        public void TrackAnalyticsIapLink(string amazonUserId, IReadOnlyList<string> receiptIds) =>
            _analyticsModule?.TrackIapLink(amazonUserId, receiptIds);

        public IEnumerator TrackAnalyticsClickRoutine(string paidAppId)
        {
            if (_analyticsModule != null)
                yield return _analyticsModule.TrackClickRoutine(paidAppId);
        }
#else
        public void TrackAnalyticsImpression(string paidAppId) { }
        public void TrackAnalyticsClick(string paidAppId) { }
        public void TrackAnalyticsIapLink(string amazonUserId, IReadOnlyList<string> receiptIds) { }
        public IEnumerator TrackAnalyticsClickRoutine(string paidAppId) { yield break; }
#endif

        #endregion

        #region In-App Purchase

#if AMZN_IAP_ENABLED
        private bool IapReady => _inAppPurchaseModule != null && _inAppPurchaseModule.Enabled;

        /// <summary>Сервис поднят: можно вызывать BuyProduct. Состоянию прав верить рано —
        /// см. <see cref="IsIAPRestored"/>.</summary>
        public bool IsIAPInitialized => _inAppPurchaseModule != null && _inAppPurchaseModule.IsInitialized;

        /// <summary>Была успешная полная сверка с Amazon в этой сессии — состоянию можно верить.</summary>
        public bool IsIAPRestored => _inAppPurchaseModule != null && _inAppPurchaseModule.IsRestored;

        public DateTime IAPLastReconciliationUtc =>
            _inAppPurchaseModule != null ? _inAppPurchaseModule.LastReconciliationUtc : DateTime.MinValue;

        public void BuyProduct(string productId)
        {
            if (IapReady)
                _inAppPurchaseModule.BuyProduct(productId);
        }

        public bool IsSubscribed(string productId) =>
            IapReady && _inAppPurchaseModule.IsSubscribed(productId);

        public bool HasReceipt(string productId) =>
            IapReady && _inAppPurchaseModule.HasReceipt(productId);

        public bool HasEverPurchasedIAP(string productId) =>
            IapReady && _inAppPurchaseModule.HasEverPurchased(productId);

        /// <summary>Трёхзначное состояние права — для игр, которым важно отличать «не знаю» от «нет».</summary>
        public IapEntitlementState GetIAPEntitlementState(string productId) =>
            IapReady ? _inAppPurchaseModule.GetEntitlementState(productId) : IapEntitlementState.Unknown;

        public IapEntitlement GetIAPEntitlement(string productId) =>
            IapReady ? _inAppPurchaseModule.GetEntitlement(productId) : default;

        public void RestorePurchases(Action<bool> onComplete = null)
        {
            if (IapReady)
                _inAppPurchaseModule.RestorePurchases(onComplete);
            else
                onComplete?.Invoke(false);
        }

        /// <summary>Ручная пересверка прав с Amazon.</summary>
        public void RefreshIAPEntitlements()
        {
            if (IapReady)
                _inAppPurchaseModule.RefreshEntitlements();
        }

        /// <summary>Повтор инициализации IAP после сбоя (каталог, сервис).</summary>
        public void RetryIAPInitialize()
        {
            if (IapReady)
                _inAppPurchaseModule.RetryInitialize();
        }

        // События прав. Оба с доигрыванием последнего снимка поздним подписчикам.
        // Обработчик выдачи ОБЯЗАН быть идемпотентным и не начислять валюту: событие
        // приходит при каждом запуске (см. README модуля IAP).

        public void AddIAPEntitlementGrantedListener(Action<IapEntitlement> listener)
        {
            if (IapReady) _inAppPurchaseModule.AddEntitlementGrantedListener(listener);
        }

        public void RemoveIAPEntitlementGrantedListener(Action<IapEntitlement> listener)
        {
            if (IapReady) _inAppPurchaseModule.RemoveEntitlementGrantedListener(listener);
        }

        public void AddIAPEntitlementRevokedListener(Action<IapEntitlement> listener)
        {
            if (IapReady) _inAppPurchaseModule.AddEntitlementRevokedListener(listener);
        }

        public void RemoveIAPEntitlementRevokedListener(Action<IapEntitlement> listener)
        {
            if (IapReady) _inAppPurchaseModule.RemoveEntitlementRevokedListener(listener);
        }

        /// <summary>Начался новый оплаченный период подписки — игра начисляет награду за период (IAP-14).</summary>
        public void AddIAPPeriodStartedListener(Action<IapPeriodStarted> listener)
        {
            if (IapReady) _inAppPurchaseModule.AddPeriodStartedListener(listener);
        }

        public void RemoveIAPPeriodStartedListener(Action<IapPeriodStarted> listener)
        {
            if (IapReady) _inAppPurchaseModule.RemovePeriodStartedListener(listener);
        }

        public void SetIAPPurchaseCompleteCallback(Action<string> callback)
        {
            if (_inAppPurchaseModule != null)
                _inAppPurchaseModule.SetPurchaseCompleteCallback(callback);
        }

        public void SetIAPPurchaseFailedCallback(Action<string> callback)
        {
            if (_inAppPurchaseModule != null)
                _inAppPurchaseModule.SetPurchaseFailedCallback(callback);
        }
#else
        public bool IsIAPInitialized => false;
        public bool IsIAPRestored => false;
        public DateTime IAPLastReconciliationUtc => DateTime.MinValue;
        public void BuyProduct(string productId) { }
        public bool IsSubscribed(string productId) => false;
        public bool HasReceipt(string productId) => false;
        public bool HasEverPurchasedIAP(string productId) => false;
        public IapEntitlementState GetIAPEntitlementState(string productId) => IapEntitlementState.Unknown;
        public IapEntitlement GetIAPEntitlement(string productId) => default;
        public void RestorePurchases(Action<bool> onComplete = null) { onComplete?.Invoke(false); }
        public void RefreshIAPEntitlements() { }
        public void RetryIAPInitialize() { }
        public void AddIAPEntitlementGrantedListener(Action<IapEntitlement> listener) { }
        public void RemoveIAPEntitlementGrantedListener(Action<IapEntitlement> listener) { }
        public void AddIAPEntitlementRevokedListener(Action<IapEntitlement> listener) { }
        public void RemoveIAPEntitlementRevokedListener(Action<IapEntitlement> listener) { }
        public void AddIAPPeriodStartedListener(Action<IapPeriodStarted> listener) { }
        public void RemoveIAPPeriodStartedListener(Action<IapPeriodStarted> listener) { }
        public void SetIAPPurchaseCompleteCallback(Action<string> callback) { }
        public void SetIAPPurchaseFailedCallback(Action<string> callback) { }
#endif

        #endregion

        #region Private Members

#if AMZN_ANALYTICS_ENABLED
        private void EnsureAnalyticsModule()
        {
            if (_analyticsModule != null)
                return;

            _analyticsModule = GetComponent<AnalyticsModule>();
            if (_analyticsModule == null)
                _analyticsModule = gameObject.AddComponent<AnalyticsModule>();
        }
#endif

        private IEnumerator InitializeWhenReady(params ModuleBase[] modules)
        {
#if AMZN_INTERNETCONNECTION_ENABLED
            if (_internetConnectionModule != null && _internetConnectionModule.Enabled)
            {
                _internetConnectionModule.Initialize();
                if (!_internetConnectionModule.IsConnected)
                    yield return _internetConnectionModule.WaitUntilConnected();
            }
#endif

            InitializeModules(modules);
            yield break;
        }

#if AMZN_INTERNETCONNECTION_ENABLED
        private void EnsureInternetConnectionModule()
        {
            if (_internetConnectionModule != null)
                return;

            _internetConnectionModule = GetComponent<InternetConnectionModule>();
            if (_internetConnectionModule == null)
                _internetConnectionModule = gameObject.AddComponent<InternetConnectionModule>();
        }
#endif

#if AMZN_FIREBASE_ENABLED
        private void EnsureFirebaseModule()
        {
            if (_firebaseModule != null)
                return;

            _firebaseModule = GetComponent<FirebaseModule>();
            if (_firebaseModule == null)
                _firebaseModule = gameObject.AddComponent<FirebaseModule>();
        }

        public bool IsFirebaseReady => _firebaseModule != null && _firebaseModule.IsInitialized;

        public void LogFirebaseEvent(string eventName, Dictionary<string, string> parameters = null)
        {
            if (_firebaseModule == null || !_firebaseModule.Enabled)
                return;

            _firebaseModule.LogEvent(eventName, parameters);
        }

        public void RecordFirebaseException(Exception exception)
        {
            if (_firebaseModule == null || !_firebaseModule.Enabled)
                return;

            _firebaseModule.RecordException(exception);
        }

        public void LogFirebaseCrash(string message)
        {
            if (_firebaseModule == null || !_firebaseModule.Enabled)
                return;

            _firebaseModule.LogCrash(message);
        }
#else
        public bool IsFirebaseReady => false;
        public void LogFirebaseEvent(string eventName, Dictionary<string, string> parameters = null) { }
        public void RecordFirebaseException(Exception exception) { }
        public void LogFirebaseCrash(string message) { }
#endif

        private static int GetModulePriority(ModuleBase module)
        {
            // Lower = earlier. Adjust первым: Analytics резолвит device_id через
            // Adjust.GetAmazonAdId, и запрос к неподнятому SDK возвращает null. Adjust.InitSdk
            // синхронный и дешёвый, так что first_open от этого не задерживается — Analytics
            // идёт сразу следом. Firebase третьим — Crashlytics ловит init crashes остальных.
            switch (module.GetType().Name)
            {
                case "AdjustModule": return 0;
                case "AnalyticsModule": return 1;
                case "FirebaseModule": return 2;
                case "AppMetricaModule": return 3;
                case "CrossPromoModule": return 5;
                case "InAppPurchaseModule": return 6;
                default: return 100;
            }
        }

        private void InitializeModules(params ModuleBase[] modules)
        {
            foreach (var module in modules.OrderBy(GetModulePriority))
            {
                if (module == null)
                    continue;

                if (!module.Enabled)
                {
                    // Выключаем ТОЛЬКО компонент модуля, а не весь GameObject. Модули (в т.ч.
                    // Analytics) висят на общем host-объекте SDK на DontDestroyOnLoad; SetActive(false)
                    // на нём погасил бы вместе с ним ВСЕ остальные модули и убил их корутины.
                    module.enabled = false;
                    continue;
                }

                if (module.Initialized)
                    continue;

                try
                {
                    module.Initialize();
                    module.Initialized = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AMZNGoDSDK] Module {module.GetType().Name} initialization failed: {ex}");
                }
            }

            IsInitialized = true;
            OnInitializationComplete?.Invoke();
        }
        
        #endregion
    }
}
