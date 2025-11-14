using System;
using UnityEngine;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    public class ProductionConcernWindow : ConcernWindow
    {
        [SerializeField] private Button _continueButton;
        
        public event Action OnAgree;

        protected override void OnShow()
        {
            _continueButton.onClick.AddListener(OnAgreeClicked);
            _continueButton.onClick.AddListener(HideWindow);
        }

        protected override void OnHide()
        {
            _continueButton.onClick.RemoveListener(OnAgreeClicked);
            _continueButton.onClick.RemoveListener(HideWindow);
        }
        
        private void OnAgreeClicked() => 
            OnAgree?.Invoke();
    }
}