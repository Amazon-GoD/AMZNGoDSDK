using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Detects which third-party SDKs are actually installed in the project.
    /// For bundled modules, checks folder existence (no ~ suffix).
    /// For external dependencies, checks assembly presence via CompilationPipeline.
    /// </summary>
    public static class DependencyDetector
    {
        public struct ModuleDependencyInfo
        {
            public string ModuleName;
            public string DefineSymbol;
            public bool DependenciesPresent;
            public bool HasExternalDependency;
            public string DependencyDescription;
        }

        private struct ModuleSpec
        {
            public string Name;
            public string[] RequiredAssemblies;
            public string[] RequiredDlls;
            public string[] RequiredFolders;

            /// <summary>
            /// Полные имена типов, которые обязаны существовать в проекте. Нужны там, где
            /// SDK можно поставить несколькими способами и путь/имя сборки не фиксированы:
            /// AppLovin MAX ставится и как Assets/MaxSdk, и как UPM-пакет, и без asmdef —
            /// тогда его типы попадают в Assembly-CSharp, по которой ничего не отличить.
            /// Наличие самого типа — единственный устойчивый признак.
            /// </summary>
            public string[] RequiredTypes;

            public string Description;
        }

        private static readonly Dictionary<string, ModuleSpec> ModuleSpecs = new Dictionary<string, ModuleSpec>
        {
            {
                ModuleDefineManager.ADJUST_DEFINE,
                new ModuleSpec
                {
                    Name = "Adjust",
                    RequiredAssemblies = new[] { "AdjustSdk.Scripts" },
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/Adjust" },
                    Description = "AdjustSdk.Scripts (bundled)"
                }
            },
            {
                ModuleDefineManager.APPMETRICA_DEFINE,
                new ModuleSpec
                {
                    Name = "AppMetrica",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica" },
                    Description = "AppMetrica runtime (bundled, may be hidden with ~)"
                }
            },
            {
                ModuleDefineManager.CROSSPROMO_DEFINE,
                new ModuleSpec
                {
                    Name = "Cross-Promo",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo" },
                    Description = "Video cross-promo (bundled)"
                }
            },
            {
                ModuleDefineManager.FIREBASE_DEFINE,
                new ModuleSpec
                {
                    Name = "Firebase",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredDlls = new[] { "Firebase.Analytics.dll", "Firebase.Crashlytics.dll" },
                    RequiredFolders = Array.Empty<string>(),
                    Description = "Firebase.Analytics.dll, Firebase.Crashlytics.dll"
                }
            },
            {
                ModuleDefineManager.IAP_DEFINE,
                new ModuleSpec
                {
                    Name = "In-App Purchase (Amazon)",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase" },
                    Description = "Amazon IAP V2 (bundled)"
                }
            },
            {
                ModuleDefineManager.INTERNETCONNECTION_DEFINE,
                new ModuleSpec
                {
                    Name = "Internet Connection",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection" },
                    Description = "no external deps"
                }
            },
            {
                ModuleDefineManager.DEBUGCONSOLE_DEFINE,
                new ModuleSpec
                {
                    Name = "In-Game Debug Console",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/InGameDebugConsole" },
                    Description = "yasirkula In-Game Debug Console (bundled)"
                }
            },
            {
                ModuleDefineManager.APPLOVIN_DEFINE,
                new ModuleSpec
                {
                    Name = "AppLovin MAX",
                    // Плагин обязан быть ОТДЕЛЬНОЙ сборкой: обёртка живёт в asmdef
                    // AMZNGoD.Runtime, а asmdef не может ссылаться на предопределённые
                    // сборки. Установка старым .unitypackage без asmdef (типы уезжают в
                    // Assembly-CSharp) для модуля бесполезна — типы есть, а сослаться на них
                    // нельзя. Критерий совпадает с SdkAsmdefReferenceGuard: разойдись они —
                    // сторож и ModuleDefineManager начнут спорить и гонять перекомпиляцию.
                    RequiredAssemblies = new[] { "MaxSdk.Scripts" },
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/AppLovin" },
                    RequiredTypes = new[] { "MaxSdk", "MaxSdkCallbacks" },
                    Description = "AppLovin MAX Unity plugin (внешний: UPM-пакет com.applovin.mediation.ads или Assets/MaxSdk)"
                }
            },
            {
                ModuleDefineManager.ANALYTICS_DEFINE,
                new ModuleSpec
                {
                    Name = "Analytics",
                    RequiredAssemblies = Array.Empty<string>(),
                    RequiredFolders = new[] { "Assets/AMZNGoDSDK/Runtime/Modules/Analytics" },
                    Description = "HTTP tracker /v1/events: first_open, impression, click. No external deps."
                }
            },
        };

        private static HashSet<string> _dllCache;

        public static List<ModuleDependencyInfo> DetectAll()
        {
            var projectAssemblies = GetProjectAssemblyNames();
            _dllCache = GetProjectDllNames();
            var result = new List<ModuleDependencyInfo>();

            foreach (var kvp in ModuleSpecs)
            {
                bool present = CheckDependencies(kvp.Value, projectAssemblies, _dllCache);
                bool hasExternal = kvp.Value.RequiredAssemblies.Length > 0
                                   || (kvp.Value.RequiredDlls != null && kvp.Value.RequiredDlls.Length > 0);

                result.Add(new ModuleDependencyInfo
                {
                    ModuleName = kvp.Value.Name,
                    DefineSymbol = kvp.Key,
                    DependenciesPresent = present,
                    HasExternalDependency = hasExternal,
                    DependencyDescription = kvp.Value.Description
                });
            }

            _dllCache = null;
            return result;
        }

        public static bool AreDependenciesPresent(string defineSymbol)
        {
            if (!ModuleSpecs.TryGetValue(defineSymbol, out var spec))
                return false;

            return CheckDependencies(spec, GetProjectAssemblyNames(), GetProjectDllNames());
        }

        private static bool CheckDependencies(ModuleSpec spec, HashSet<string> projectAssemblies, HashSet<string> projectDlls)
        {
            foreach (var folder in spec.RequiredFolders)
            {
                if (!Directory.Exists(folder))
                    return false;
            }

            foreach (var asm in spec.RequiredAssemblies)
            {
                if (!projectAssemblies.Contains(asm))
                    return false;
            }

            if (spec.RequiredDlls != null)
            {
                foreach (var dll in spec.RequiredDlls)
                {
                    if (!projectDlls.Contains(dll))
                        return false;
                }
            }

            if (spec.RequiredTypes != null)
            {
                foreach (var typeName in spec.RequiredTypes)
                {
                    if (!TypeExists(typeName))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Ищет тип по короткому имени во всех загруженных сборках домена. Плагин, лежащий
        /// вне нашего define, к этому моменту уже скомпилирован редактором, поэтому его типы
        /// в домене есть — а вот полное имя сборки зависит от способа установки, и брать
        /// Type.GetType с квалификацией нельзя.
        /// </summary>
        private static bool TypeExists(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(typeName, throwOnError: false) != null)
                        return true;
                }
                catch (Exception)
                {
                    // Сборку могло не получиться прочитать (динамическая, битая ссылка) —
                    // это не повод валить всю детекцию.
                }
            }

            return false;
        }

        private static HashSet<string> GetProjectAssemblyNames()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            var editorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in assemblies)
                names.Add(asm.name);
            foreach (var asm in editorAssemblies)
                names.Add(asm.name);

            return names;
        }

        private static HashSet<string> GetProjectDllNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists("Assets"))
                return names;

            foreach (var path in Directory.GetFiles("Assets", "*.dll", SearchOption.AllDirectories))
            {
                names.Add(Path.GetFileName(path));
            }

            return names;
        }

        [MenuItem("AMZN GoD/Debug/Detect Installed Dependencies", false, 202)]
        public static void LogDetectedDependencies()
        {
            var results = DetectAll();

            Debug.Log("=== AMZN GoD SDK - Dependency Detection ===");
            foreach (var info in results)
            {
                string status = info.DependenciesPresent ? "AVAILABLE" : "MISSING";
                Debug.Log($"  {info.ModuleName}: {status} — {info.DependencyDescription}");
            }
            Debug.Log("============================================");
        }
    }
}
