using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public abstract class MonoBehaviourSingletonPersistent<T> : MonoBehaviour
        where T : Component
    {
        public static T Instance { get; private set; }

        public void Awake()
        {
            if (Instance != null && Instance != (this as T))
            {
                Debug.LogWarning($"[AMZNGoDSDK] {typeof(T).Name} already exists. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this as T;
            DontDestroyOnLoad(this);
            OnAwake();
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this as T)
                Instance = null;
        }

        protected virtual void OnAwake() { }
    }
}
