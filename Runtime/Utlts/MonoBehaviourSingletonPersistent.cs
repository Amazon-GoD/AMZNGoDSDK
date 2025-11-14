using System;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public abstract class MonoBehaviourSingletonPersistent<T> : MonoBehaviour
        where T : Component
    {
        public static T Instance { get; private set; }

        public void Awake()
        {
            if (Instance == null)
            {
                Instance = this as T;
                DontDestroyOnLoad(this);
            }
            else
            {
                Destroy(gameObject);
                throw new Exception($"{typeof(T).Name} already exists.");
            }
            
            OnAwake();
        }

        protected virtual void OnAwake() { }
    }
}