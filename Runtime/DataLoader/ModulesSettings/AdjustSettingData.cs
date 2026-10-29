using System;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class AdjustSettingData : ModuleSettingData
    {
        public string Key;
        public AdjustEnvironment Environment;

        // Разрешает Sandbox в релизной (не development) сборке. По умолчанию такую сборку
        // останавливает build-гард: её установки не попадут в продовую статистику Adjust.
        public bool AllowSandboxInRelease;
        
        public enum AdjustEnvironment
        {
            Sandbox = 0,
            Production = 1
        }
    }
}

