using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class InfaticaModule : ModuleBase
    {
        [Header("Battery Optimization Asking")]
        [SerializeField] private bool _batteryOptimizationIgnoreAsking = false;

        [Header("Mode")] 
        [SerializeField] private Mode _mode;

        [Header("Concern Windows")] 
        [SerializeField] private ReviewConcernWindow _reviewConcernWindow;
        [SerializeField] private ProductionConcernWindow _productionConcernWindow;

        private ConcernWindow ActiveConcernWindow => _mode == Mode.Review 
            ? _reviewConcernWindow 
            : _productionConcernWindow;

        private ForegroundServiceManager _foregroundServiceManager;

        public bool IsAgree => PlayerPrefs.GetInt(nameof(InfaticaModule)).AsBool();
        public Mode CurrentMode => _mode;

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
            Review,
            Production
        }
    }
}
