using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public abstract class ModuleBase : MonoBehaviour
    {
        public abstract void Initialize();
        public abstract void Cleenup();
    }
}
