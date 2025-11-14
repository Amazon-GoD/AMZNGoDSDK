using System;
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

            var infaticaSettings = sdkSettingsData.Infatica;
            var crossPromoSettings = sdkSettingsData.CrossPromo;
            var appMetricaSettings = sdkSettingsData.AppMetrica;
            var adjustSettings = sdkSettingsData.Adjust;

            #region Constructs

            _infaticaModule.Construct(
                infaticaSettings.Enabled, 
                infaticaSettings.Mode, 
                infaticaSettings.BatteryOptimizationIgnoreAsking);
            
            _crossPromoModule.Construct(
                crossPromoSettings.Enabled,
                crossPromoSettings.ConfigUrl,
                crossPromoSettings.MaxSdkKey,
                crossPromoSettings.InterstitialId,
                crossPromoSettings.RewardedId);
            
            _appMetricaModule.Construct(
                appMetricaSettings.Enabled,
                appMetricaSettings.Key);
            
            _adjustModule.Construct(
                adjustSettings.Enabled,
                adjustSettings.Key,
                adjustSettings.Environment);

            #endregion
            
            InitializeModules(
                _infaticaModule,
                _crossPromoModule,
                _appMetricaModule,
                _adjustModule);
            
            OnBannerClose = _crossPromoModule.OnClose;
            IsNoAds = _crossPromoModule.IsNoAds;
            OnInfaticaAgree = _infaticaModule.OnAgree;
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
        
        public Action OnBannerClose;
        public Func<bool> IsNoAds; 
        
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
        
        #region Private Members

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
