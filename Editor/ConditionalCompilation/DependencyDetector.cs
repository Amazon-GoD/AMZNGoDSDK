using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Detects which SDK modules / third-party dependencies are actually present in the project.
    ///
    /// Bundled modules are detected by their asmdef files
    /// (CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName) — this works
    /// regardless of the current define symbols (unlike GetAssemblies, which drops
    /// assemblies excluded by defineConstraints) and regardless of the install location
    /// (Assets or immutable UPM package). Folder-existence checks were removed in
    /// Phase 3 of the UPM transition: module folders are never hidden with "~" anymore.
    ///
    /// External dependencies (Firebase) are detected by DLL presence under Assets.
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
            /// <summary>Assembly names whose asmdef files must exist in the project.</summary>
            public string[] RequiredAsmdefs;
            public string[] RequiredDlls;
            public string Name;
            public string Description;
        }

        private static readonly Dictionary<string, ModuleSpec> ModuleSpecs = new Dictionary<string, ModuleSpec>
        {
            {
                ModuleDefineManager.ADJUST_DEFINE,
                new ModuleSpec
                {
                    Name = "Adjust",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.Adjust", "AdjustSdk.Scripts" },
                    Description = "Adjust SDK (bundled)"
                }
            },
            {
                ModuleDefineManager.APPMETRICA_DEFINE,
                new ModuleSpec
                {
                    Name = "AppMetrica",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.AppMetrica" },
                    Description = "AppMetrica runtime (bundled)"
                }
            },
            {
                ModuleDefineManager.CROSSPROMO_DEFINE,
                new ModuleSpec
                {
                    Name = "Cross-Promo",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.CrossPromo" },
                    Description = "Video cross-promo (bundled)"
                }
            },
            {
                ModuleDefineManager.FIREBASE_DEFINE,
                new ModuleSpec
                {
                    Name = "Firebase",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.Firebase" },
                    RequiredDlls = new[] { "Firebase.Analytics.dll", "Firebase.Crashlytics.dll" },
                    Description = "Firebase.Analytics.dll, Firebase.Crashlytics.dll (installed by the consumer)"
                }
            },
            {
                ModuleDefineManager.IAP_DEFINE,
                new ModuleSpec
                {
                    Name = "In-App Purchase (Amazon)",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.InAppPurchase" },
                    Description = "Amazon IAP V2 (bundled)"
                }
            },
            {
                ModuleDefineManager.INTERNETCONNECTION_DEFINE,
                new ModuleSpec
                {
                    Name = "Internet Connection",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.InternetConnection" },
                    Description = "no external deps"
                }
            },
            {
                ModuleDefineManager.DEBUGCONSOLE_DEFINE,
                new ModuleSpec
                {
                    Name = "In-Game Debug Console",
                    RequiredAsmdefs = new[] { "IngameDebugConsole.Runtime" },
                    Description = "yasirkula In-Game Debug Console (bundled)"
                }
            },
            {
                ModuleDefineManager.APPLOVIN_DEFINE,
                new ModuleSpec
                {
                    Name = "AppLovin MAX",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.AppLovin", "MaxSdk.Scripts" },
                    Description = "AppLovin MAX Unity plugin (external: com.applovin.mediation.ads or Assets/MaxSdk)"
                }
            },
            {
                ModuleDefineManager.ANALYTICS_DEFINE,
                new ModuleSpec
                {
                    Name = "Analytics",
                    RequiredAsmdefs = new[] { "AMZNGoDSDK.Module.Analytics" },
                    Description = "HTTP tracker /v1/events: first_open, impression, click. No external deps."
                }
            },
        };

        public static List<ModuleDependencyInfo> DetectAll()
        {
            var dllCache = GetProjectDllNames();
            var result = new List<ModuleDependencyInfo>();

            foreach (var kvp in ModuleSpecs)
            {
                bool present = CheckDependencies(kvp.Value, dllCache);
                bool hasExternal = kvp.Value.RequiredDlls != null && kvp.Value.RequiredDlls.Length > 0
                                   || kvp.Key == ModuleDefineManager.APPLOVIN_DEFINE;

                result.Add(new ModuleDependencyInfo
                {
                    ModuleName = kvp.Value.Name,
                    DefineSymbol = kvp.Key,
                    DependenciesPresent = present,
                    HasExternalDependency = hasExternal,
                    DependencyDescription = kvp.Value.Description
                });
            }

            return result;
        }

        public static bool AreDependenciesPresent(string defineSymbol)
        {
            if (!ModuleSpecs.TryGetValue(defineSymbol, out var spec))
                return false;

            return CheckDependencies(spec, GetProjectDllNames());
        }

        private static bool CheckDependencies(ModuleSpec spec, HashSet<string> projectDlls)
        {
            if (spec.RequiredAsmdefs != null)
            {
                foreach (var asmdefName in spec.RequiredAsmdefs)
                {
                    if (!AsmdefExists(asmdefName))
                        return false;
                }
            }

            if (spec.RequiredDlls != null)
            {
                foreach (var dll in spec.RequiredDlls)
                {
                    if (!projectDlls.Contains(dll))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True if an asmdef with the given assembly name exists in the project
        /// (Assets or packages), even when its defineConstraints currently exclude
        /// it from compilation.
        /// </summary>
        private static bool AsmdefExists(string assemblyName)
        {
            string path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            return !string.IsNullOrEmpty(path);
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
