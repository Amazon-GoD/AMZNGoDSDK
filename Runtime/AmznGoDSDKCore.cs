using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public sealed class AmznGoDSDKCore : MonoBehaviourSingletonPersistent<AmznGoDSDKCore>
    {
        [SerializeField] private InfaticaModule _infaticaModule;
        
        #region Awake
        protected override void OnAwake()
        {
            InitializeModules(_infaticaModule);
        }
        
        #endregion
        
        #region Public Members

        public void ShowInfaticaBanner() => 
            _infaticaModule.ChangeChoice();
        
        #endregion
        
        #region Private Members
        private void InitializeModules(params ModuleBase[] modules)
        {
            foreach (var module in modules)
            {
                module.Initialize();
            }
        }
        
        #endregion
    }
}
