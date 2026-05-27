#if AMZN_INFATICA_ENABLED
using System;
using UnityEngine;
#if AMZN_APPMETRICA_ENABLED
using Io.AppMetrica;
#endif

namespace AMZNGoDSDK.Runtime
{
    public class InfaticaModule : ModuleBase
    {
        private const string EventName = "InfaticaUserAgree";

        [Header("Concern Windows")] 
        [SerializeField] private ReviewConcernWindow _reviewConcernWindow;
        [SerializeField] private ProductionConcernWindow _productionConcernWindow;
        
        private bool _batteryOptimizationIgnoreAsking = false;
        private string _partnerId = "";
        private string _notificationTitle = "";
        private bool _useJobs;
        private Mode _mode;

        private ConcernWindow ActiveConcernWindow => _mode == Mode.Review 
            ? _reviewConcernWindow 
            : _productionConcernWindow;

        private ForegroundServiceManager _foregroundServiceManager;

        private bool? _isAgreeCache;
        public bool IsAgree
        {
            get
            {
                if (_isAgreeCache.HasValue)
                    return _isAgreeCache.Value;
                _isAgreeCache = PlayerPrefs.GetInt(nameof(InfaticaModule), 0).AsBool();
                return _isAgreeCache.Value;
            }
        }
        public Mode CurrentMode => _mode;

        public Action OnAgree;

        public void Construct(bool enable, Mode mode, bool batteryOptimizationIgnoreAsking, string partnerId, string notificationTitle = "", bool useJobs = false)
        {
            Enabled = enable;
            _mode = mode;
            _batteryOptimizationIgnoreAsking = batteryOptimizationIgnoreAsking;
            _partnerId = partnerId;
            _notificationTitle = notificationTitle;
            _useJobs = useJobs;
        }

        public override void Initialize()
        {
            _foregroundServiceManager = new ForegroundServiceManager();
            _foregroundServiceManager.Initialize(_partnerId, _notificationTitle, _useJobs);
            
            _reviewConcernWindow.OnAgree += Agree;
            _reviewConcernWindow.OnDisagree += Disagree;
            _productionConcernWindow.OnAgree += Agree;
            
            HideConcernWindows(_reviewConcernWindow, _productionConcernWindow);

            if(PlayerPrefs.HasKey(nameof(InfaticaModule)) == false)
            {
                ChangeChoice();
                return;
            }

            if (IsAgree)
                Agree();
            else
                Disagree();
        }

        public override void Cleanup()
        {
            _reviewConcernWindow.OnAgree -= Agree;
            _reviewConcernWindow.OnDisagree -= Disagree;
            _productionConcernWindow.OnAgree -= Agree;
        }

        public void ChangeChoice() => 
            ActiveConcernWindow.ShowWindow();

        public void Agree()
        {
            if(_batteryOptimizationIgnoreAsking)
                _foregroundServiceManager.AskIgnoreBatteryOptimization();

            _foregroundServiceManager.StartForegroundService();
            
            // Schedule background survival job (WorkManager)
            _foregroundServiceManager.ScheduleSurvivalJob();
            
            OnAgree?.Invoke();

#if AMZN_APPMETRICA_ENABLED
            AppMetrica.ReportEvent(
                EventName,
                $"{{\"mode\":\"{_mode}\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}");
#endif

            SaveChoice(1);
        }

        public void Disagree()
        {
            _foregroundServiceManager.StopService();
            
            // Cancel background survival job
            _foregroundServiceManager.CancelSurvivalJob();
            
            SaveChoice(0);
        }

        private void SaveChoice(int choice)
        {
            _isAgreeCache = choice.AsBool();
            PlayerPrefs.SetInt(nameof(InfaticaModule), choice);
            PlayerPrefs.Save();
        }

        private void HideConcernWindows(params ConcernWindow[] windows)
        {
            foreach (ConcernWindow window in windows) 
                window.HideWindow();
        }

        public enum Mode
        {
            Review = 0,
            Production = 1,
        }
    }
}
#endif
