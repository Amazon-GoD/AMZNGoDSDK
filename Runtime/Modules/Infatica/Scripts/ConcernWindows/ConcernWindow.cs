using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public abstract class ConcernWindow : MonoBehaviour
    {
        public void ShowWindow()
        {
            OnShow();
            gameObject.SetActive(true);
        }

        public void HideWindow()
        {
            gameObject.SetActive(false);
            OnHide();
        }

        protected abstract void OnShow();
        protected abstract void OnHide();
    }
}