using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public sealed class AmznGoDSDKCore : MonoBehaviourSingletonPersistent<AmznGoDSDKCore>
    {
        [SerializeField] private AdjustModule _adjustModule;
        [SerializeField] private AppMetricaModule _appMetricaModule;
        [SerializeField] private CrossPromoModule _crossPromoModule;
        [SerializeField] private InfaticaModule _infaticaModule;
        [SerializeField] private InAppPurchaseModule _inAppPurchaseModule;
        [SerializeField] private FirebaseModule _firebaseModule;
        [SerializeField] private InternetConnectionModule _internetConnectionModule;
        
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

            EnsureInternetConnectionModule();
            EnsureFirebaseModule();

            #region Constructs

            _internetConnectionModule.Construct(internetSettings.Enabled, internetSettings);

            _firebaseModule.Construct(
                firebaseSettings.Enabled,
                firebaseSettings.EnableAnalytics,
                firebaseSettings.EnableCrashlytics);

            _infaticaModule.Construct(
                infaticaSettings.Enabled, 
                infaticaSettings.Mode, 
                infaticaSettings.BatteryOptimizationIgnoreAsking);
            
            _crossPromoModule.Construct(
                crossPromoSettings.Enabled,
                crossPromoSettings.ConfigUrl,
                crossPromoSettings.AppodealSdkKey,
                crossPromoSettings.MaxSdkKey,
                crossPromoSettings.InterstitialId,
                crossPromoSettings.RewardedId,
                crossPromoSettings.ProviderType);
            
            _appMetricaModule.Construct(
                appMetricaSettings.Enabled,
                appMetricaSettings.Key);
            
            _adjustModule.Construct(
                adjustSettings.Enabled,
                adjustSettings.Key,
                adjustSettings.Environment);

            _inAppPurchaseModule.Construct(inAppPurchaseSettings);

            #endregion
            
            StartCoroutine(InitializeWhenReady(
                _firebaseModule,
                _infaticaModule,
                _crossPromoModule,
                _appMetricaModule,
                _adjustModule,
                _inAppPurchaseModule));
            
            OnInfaticaAgree = _infaticaModule.OnAgree;
        }
        public void SetBannerFuncs(Action onClose, Func<bool> isNoAds)
        {
            _crossPromoModule.SetBannerFuncs(onClose, isNoAds);
        }
        #endregion
        
        #region Infatica
        
        public bool IsInfaticaAgree => _infaticaModule.IsAgree;
        public InfaticaModule.Mode InfaticaMode => _infaticaModule.CurrentMode;
        
        public Action OnInfaticaAgree;
        
        public void ShowInfaticaBanner()
        {
            if(!_infaticaModule.Enabled)
                return;
            
            _infaticaModule.ChangeChoice();
        }
        
        #endregion
        
        #region Cross Promo

        public bool IsAdsReady => _crossPromoModule.IsAdsReady;
        
        public void ShowInterstitial()
        {
            if(!_crossPromoModule.Enabled)
                return;
            
            _crossPromoModule.ShowInterstitial();
        }
        
        public void ShowRewarded(Action callback)
        {
            if(!_crossPromoModule.Enabled)
                return;
            
            _crossPromoModule.ShowRewarded(callback);
        }

        #endregion
        
        #region AppMetrica

        public void ReportEventAppMetrica(string eventName, Dictionary<string, string> args)
        {
            if(!_appMetricaModule.Enabled)
                return;
            
            _appMetricaModule.ReportEvent(eventName, args);
        }
        
        #endregion
        
        #region Adjust

        public void ReportEventAdjust(string token, Dictionary<string, string> args)
        {
            if(!_adjustModule.Enabled)
                return;
            
            _adjustModule.ReportEvent(token, args);
        }
        
        #endregion

        #region In-App Purchase

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

        #endregion

        #region Private Members

        private IEnumerator InitializeWhenReady(params ModuleBase[] modules)
        {
            if (_internetConnectionModule != null && _internetConnectionModule.Enabled)
            {
                _internetConnectionModule.Initialize();
                if (!_internetConnectionModule.IsConnected)
                    yield return _internetConnectionModule.WaitUntilConnected();
            }

            InitializeModules(modules);
        }

        private void EnsureInternetConnectionModule()
        {
            if (_internetConnectionModule != null)
                return;

            _internetConnectionModule = GetComponent<InternetConnectionModule>();
            if (_internetConnectionModule == null)
                _internetConnectionModule = gameObject.AddComponent<InternetConnectionModule>();
        }

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
