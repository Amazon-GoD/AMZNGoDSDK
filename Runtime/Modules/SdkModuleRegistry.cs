using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Реестр живых инстансов модулей. Наполняется ядром (AmznGoDSDKCore) в OnAwake,
    /// читается модулями для межмодульных вызовов (Cross-Promo → AppMetrica/Adjust,
    /// IAP → Analytics и т.п.).
    ///
    /// Зачем: сборки модулей не могут ссылаться на сборку фасада (фасад ссылается на
    /// модули — получился бы цикл), поэтому прежние вызовы AmznGoDSDKCore.Instance из
    /// модулей заменены на выборку целевого модуля из этого реестра в базовой сборке.
    /// Вызовы к конкретному модулю на стороне вызывающего оборачиваются в
    /// #if AMZN_&lt;X&gt;_ENABLED целевого модуля — при выключенном define сборка целевого
    /// модуля не существует, и код вызова компилируется вон.
    /// </summary>
    public static class SdkModuleRegistry
    {
        private static readonly List<ModuleBase> Modules = new List<ModuleBase>();

        /// <summary>
        /// Ядро завершило инициализацию всех включённых модулей. Зеркало
        /// AmznGoDSDKCore.IsInitialized для кода, которому недоступна сборка фасада.
        /// </summary>
        public static bool SdkInitialized { get; set; }

        public static void Register(ModuleBase module)
        {
            if (module == null || Modules.Contains(module))
                return;

            Modules.Add(module);
        }

        public static void Unregister(ModuleBase module)
        {
            Modules.Remove(module);
        }

        /// <summary>Зарегистрированный модуль типа T или null, если модуль не поднят.</summary>
        public static T Get<T>() where T : ModuleBase
        {
            for (int i = 0; i < Modules.Count; i++)
            {
                if (Modules[i] is T typed)
                    return typed;
            }

            return null;
        }
    }
}
