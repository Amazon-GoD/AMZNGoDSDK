using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public sealed class AmznGoDSDKCore : MonoBehaviourSingletonPersistent<AmznGoDSDKCore>
    {
#if AMZN_ADJUST_ENABLED
        [SerializeField] private AdjustModule _adjustModule;
#endif
#if AMZN_APPMETRICA_ENABLED
        [SerializeField] private AppMetricaModule _appMetricaModule;
#endif
#if AMZN_CROSSPROMO_ENABLED
        [SerializeField] private CrossPromoModule _crossPromoModule;
#endif
#if AMZN_INFATICA_ENABLED
        [SerializeField] private InfaticaModule _infaticaModule;
#endif
#if AMZN_IAP_ENABLED
        [SerializeField] private InAppPurchaseModule _inAppPurchaseModule;
        [SerializeField] private InAppPurchaseModuleInitializer _inAppPurchaseModuleInitializer;
#endif
#if AMZN_FIREBASE_ENABLED
        [SerializeField] private FirebaseModule _firebaseModule;
#endif
#if AMZN_INTERNETCONNECTION_ENABLED
        [SerializeField] private InternetConnectionModule _internetConnectionModule;
#endif
        
        public bool Enabled { get; private set; }
        
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

            var internetSettings = sdkSettingsData.InternetConnection;
            var firebaseSettings = sdkSettingsData.Firebase;
            var infaticaSettings = sdkSettingsData.Infatica;
            var crossPromoSettings = sdkSettingsData.CrossPromo;
            var appMetricaSettings = sdkSettingsData.AppMetrica;
            var adjustSettings = sdkSettingsData.Adjust;
            var inAppPurchaseSettings = sdkSettingsData.InAppPurchase;

#if AMZN_INTERNETCONNECTION_ENABLED
            EnsureInternetConnectionModule();
#endif
#if AMZN_FIREBASE_ENABLED
            EnsureFirebaseModule();
#endif

            #region Constructs

#if AMZN_INTERNETCONNECTION_ENABLED
            _internetConnectionModule.Construct(internetSettings.Enabled, internetSettings);
#endif

#if AMZN_FIREBASE_ENABLED
            _firebaseModule.Construct(
                firebaseSettings.Enabled,
                firebaseSettings.EnableAnalytics,
                firebaseSettings.EnableCrashlytics);
#endif

#if AMZN_INFATICA_ENABLED
            var infaticaMode = infaticaSettings.Mode == InfaticaSettingData.InfaticaMode.Review
                ? InfaticaModule.Mode.Review
                : InfaticaModule.Mode.Production;
            
            _infaticaModule.Construct(
                infaticaSettings.Enabled, 
                infaticaMode, 
                infaticaSettings.BatteryOptimizationIgnoreAsking,
                infaticaSettings.PartnerId,
                infaticaSettings.NotificationTitle,
                infaticaSettings.SdkVersion == InfaticaSettingData.InfaticaSdkVersion.WithJobs);
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

            EnsureInAppPurchaseModuleInitializer();
            _inAppPurchaseModuleInitializer.Initialize(_inAppPurchaseModule);
#endif

            #endregion
            
            var modules = new List<ModuleBase>();
#if AMZN_FIREBASE_ENABLED
            modules.Add(_firebaseModule);
#endif
#if AMZN_INFATICA_ENABLED
            modules.Add(_infaticaModule);
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
            
#if AMZN_INFATICA_ENABLED
            OnInfaticaAgree = _infaticaModule.OnAgree;
#endif
        }
        #endregion
        
        #region Infatica
        
#if AMZN_INFATICA_ENABLED
        public bool IsInfaticaAgree => _infaticaModule.IsAgree;
        public InfaticaSettingData.InfaticaMode InfaticaMode => 
            _infaticaModule.CurrentMode == InfaticaModule.Mode.Review 
                ? InfaticaSettingData.InfaticaMode.Review 
                : InfaticaSettingData.InfaticaMode.Production;
        
        public Action OnInfaticaAgree;
        
        public void ShowInfaticaBanner()
        {
            if(!_infaticaModule.Enabled)
                return;
            
            _infaticaModule.ChangeChoice();
        }
#else
        public bool IsInfaticaAgree => false;
        public Action OnInfaticaAgree;
        public void ShowInfaticaBanner() { }
#endif
        
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
#else
        public void ShowVideoPromo(Action onClose = null, Action onCTAClick = null) { onClose?.Invoke(); }
        public bool IsVideoPromoVisible => false;
        public bool IsVideoReady => false;
        public void ShowInterstitial(Action onClose = null, Action onCTAClick = null) { onClose?.Invoke(); }
        public void ShowRewarded(Action onClose = null, Action onCTAClick = null, Action onRewarded = null) { onClose?.Invoke(); }
#endif

        #endregion
        
        #region AppMetrica

#if AMZN_APPMETRICA_ENABLED
        public void ReportEventAppMetrica(string eventName, Dictionary<string, string> args)
        {
            if(!_appMetricaModule.Enabled)
                return;
            
            _appMetricaModule.ReportEvent(eventName, args);
        }
#else
        public void ReportEventAppMetrica(string eventName, Dictionary<string, string> args) { }
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

        #region In-App Purchase

#if AMZN_IAP_ENABLED
        public bool IsIAPInitialized => _inAppPurchaseModule.IsInitialized;

        public void BuyProduct(string productId)
        {
            if (!_inAppPurchaseModule.Enabled)
                return;

            _inAppPurchaseModule.BuyProduct(productId);
        }

        public bool IsSubscribed(string productId)
        {
            if (!_inAppPurchaseModule.Enabled)
                return false;

            return _inAppPurchaseModule.IsSubscribed(productId);
        }

        public bool HasReceipt(string productId)
        {
            if (!_inAppPurchaseModule.Enabled)
                return false;

            return _inAppPurchaseModule.HasReceipt(productId);
        }

        public void RestorePurchases(Action<bool> onComplete = null)
        {
            if (!_inAppPurchaseModule.Enabled)
                return;

            _inAppPurchaseModule.RestorePurchases(onComplete);
        }

        public void SetIAPPurchaseCompleteCallback(Action<string> callback)
        {
            _inAppPurchaseModule.SetPurchaseCompleteCallback(callback);
        }

        public void SetIAPPurchaseFailedCallback(Action<string> callback)
        {
            _inAppPurchaseModule.SetPurchaseFailedCallback(callback);
        }

        public void SetIAPConsumableRewardSetter(Action<string, int> rewardSetter)
        {
            _inAppPurchaseModule.SetConsumableRewardSetter(rewardSetter);
        }
#else
        public bool IsIAPInitialized => false;
        public void BuyProduct(string productId) { }
        public bool IsSubscribed(string productId) => false;
        public bool HasReceipt(string productId) => false;
        public void RestorePurchases(Action<bool> onComplete = null) { }
        public void SetIAPPurchaseCompleteCallback(Action<string> callback) { }
        public void SetIAPPurchaseFailedCallback(Action<string> callback) { }
        public void SetIAPConsumableRewardSetter(Action<string, int> rewardSetter) { }
#endif

        #endregion

        #region Private Members

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

#if AMZN_IAP_ENABLED
        private void EnsureInAppPurchaseModuleInitializer()
        {
            if (_inAppPurchaseModuleInitializer != null)
                return;

            _inAppPurchaseModuleInitializer = GetComponent<InAppPurchaseModuleInitializer>();
            if (_inAppPurchaseModuleInitializer == null)
                _inAppPurchaseModuleInitializer = gameObject.AddComponent<InAppPurchaseModuleInitializer>();
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

        private void InitializeModules(params ModuleBase[] modules)
        {
            foreach (var module in modules)
            {
                if (!module.Enabled)
                {
                    module.gameObject.SetActive(false);
                    continue;
                }
                
                module.Initialize();
            }
        }
        
        #endregion
    }
}
