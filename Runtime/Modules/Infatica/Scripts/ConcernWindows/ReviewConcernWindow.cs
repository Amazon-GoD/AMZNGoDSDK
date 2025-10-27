using System;
using UnityEngine;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    public class ReviewConcernWindow : ConcernWindow
    {
        [SerializeField] private Button _agreeButton;
        [SerializeField] private Button _disagreeButton;

        public event Action OnAgree;
        public event Action OnDisagree;
        
        protected override void OnShow()
        {
            _agreeButton.onClick.AddListener(OnAgreeClicked);
            _agreeButton.onClick.AddListener(HideWindow);
            _disagreeButton.onClick.AddListener(OnDisagreeClicked);
            _disagreeButton.onClick.AddListener(HideWindow);
        }

        protected override void OnHide()
        {
            _agreeButton.onClick.RemoveListener(OnAgreeClicked);
            _agreeButton.onClick.RemoveListener(HideWindow);
            _disagreeButton.onClick.RemoveListener(OnDisagreeClicked);
            _disagreeButton.onClick.RemoveListener(HideWindow);
        }

        private void OnAgreeClicked() => 
            OnAgree?.Invoke();
        
        private void OnDisagreeClicked() =>
            OnDisagree?.Invoke();
    }
}