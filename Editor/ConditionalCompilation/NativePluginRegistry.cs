using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Реестр нативных плагинов модулей SDK: define модуля → корневые папки,
    /// внутри которых лежат его нативные плагины (.jar/.aar/.so/.java/.mm/.xcframework).
    ///
    /// Пути — ОТНОСИТЕЛЬНЫЕ от корня SDK и резолвятся в оба варианта поставки:
    /// классическая (Assets/AMZNGoDSDK) и UPM-пакет (Packages/com.amzngod.amzngodsdk),
    /// см. <see cref="SdkRootPrefixes"/>. Пофайлового списка сознательно нет:
    /// любой PluginImporter под корневой папкой ВЫКЛЮЧЕННОГО модуля исключается
    /// из билда (<see cref="NativePluginBuildFilter"/>), поэтому новые нативные
    /// файлы внутри папки модуля подхватываются автоматически. На не-плагины
    /// (скрипты, ассеты) реестр не влияет — их гейтят asmdef defineConstraints.
    /// </summary>
    public static class NativePluginRegistry
    {
        /// <summary>
        /// Возможные корни SDK (со слэшем на конце). AssetPath любого файла SDK
        /// начинается ровно с одного из них — в зависимости от способа установки.
        /// </summary>
        public static readonly string[] SdkRootPrefixes =
        {
            "Assets/AMZNGoDSDK/",
            "Packages/com.amzngod.amzngodsdk/",
        };

        /// <summary>
        /// define модуля → корневые папки его нативных плагинов (относительно корня
        /// SDK, без завершающего слэша). Достаточно корня модуля: делегат вешается
        /// только на PluginImporter'ы, C#-код модуля он не затрагивает.
        ///
        /// В реестре только модули, у которых есть нативные файлы:
        /// - InAppPurchase — 6 .jar + 3 .so (Plugins/Android);
        /// - Cross-Promo — ExoPlayer.jar, UnityExoOutput.jar, UniWebView.aar,
        ///   UniWebView.xcframework, 2 .java, Plugins/Android/AndroidManifest.xml;
        /// - AppMetrica — 26 .java (Runtime/Plugins/Android);
        /// - Adjust — 2 .mm (Native/iOS);
        /// - InGameDebugConsole — IngameDebugConsole.aar + .mm.
        /// Analytics и InternetConnection нативных файлов не имеют; Firebase содержит
        /// только FirebaseModule.cs (нативные Firebase-зависимости ставит потребитель).
        ///
        /// Runtime/Plugins/Android/AmznGoDIapBootstrapProvider.java сюда НЕ входит
        /// намеренно: он обязан оставаться в билде всегда — работает через reflection
        /// и безопасен при выключенном IAP (см. javadoc самого файла).
        /// </summary>
        public static readonly Dictionary<string, string[]> ModuleNativeFolders =
            new Dictionary<string, string[]>
            {
                { ModuleDefineManager.IAP_DEFINE,          new[] { "Runtime/Modules/InAppPurchase" } },
                { ModuleDefineManager.CROSSPROMO_DEFINE,   new[] { "Runtime/Modules/Cross-Promo" } },
                { ModuleDefineManager.APPMETRICA_DEFINE,   new[] { "Runtime/Modules/AppMetrica" } },
                { ModuleDefineManager.ADJUST_DEFINE,       new[] { "Runtime/Modules/Adjust" } },
                { ModuleDefineManager.DEBUGCONSOLE_DEFINE, new[] { "Runtime/Modules/InGameDebugConsole" } },
            };

        /// <summary>
        /// Проверяет, лежит ли assetPath под одной из папок (в любом из корней SDK).
        /// Сравнение Ordinal: asset path'ы Unity всегда с прямыми слэшами.
        /// </summary>
        public static bool IsUnderFolders(string assetPath, string[] relativeFolders)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            foreach (var root in SdkRootPrefixes)
            {
                foreach (var folder in relativeFolders)
                {
                    // "+/" — чтобы "…/Adjust" не матчил "…/AdjustV2".
                    if (assetPath.StartsWith(root + folder + "/", System.StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }
    }
}
