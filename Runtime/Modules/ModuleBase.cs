using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public abstract class ModuleBase : MonoBehaviour
    {
        public bool Enabled { get; protected set; }
        
        public abstract void Initialize();
        public abstract void Cleenup();
    }
}
