using System;
using Io.AppMetrica;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class InfaticaModule : ModuleBase
    {
        private const string EventName = "InfaticaUserAgree";

        [Header("Concern Windows")] 
        [SerializeField] private ReviewConcernWindow _reviewConcernWindow;
        [SerializeField] private ProductionConcernWindow _productionConcernWindow;
        
        private bool _batteryOptimizationIgnoreAsking = false;
        private Mode _mode;

        private ConcernWindow ActiveConcernWindow => _mode == Mode.Review 
            ? _reviewConcernWindow 
            : _productionConcernWindow;

        private ForegroundServiceManager _foregroundServiceManager;

        public bool IsAgree => PlayerPrefs.GetInt(nameof(InfaticaModule)).AsBool();
        public Mode CurrentMode => _mode;

        public Action OnAgree;

        public void Construct(bool enable, Mode mode, bool batteryOptimizationIgnoreAsking)
        {
            Enabled = enable;
            _mode = mode;
            _batteryOptimizationIgnoreAsking = batteryOptimizationIgnoreAsking;
        }

        public override void Initialize()
        {
            _foregroundServiceManager = new ForegroundServiceManager();
            _foregroundServiceManager.Initialize();
            
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

        public override void Cleenup()
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
            
            OnAgree?.Invoke();

            AppMetrica.ReportEvent(EventName);

            SaveChoice(1);
        }

        public void Disagree()
        {
            _foregroundServiceManager.StopService();
            
            SaveChoice(0);
        }

        private void SaveChoice(int choice)
        {
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
