using UnityEditor;
using System.IO;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Автоматическое оборачивание ВСЕХ файлов модулей в условную компиляцию
    /// </summary>
    public static class AutoWrapAllFiles
    {
        private static readonly Dictionary<string, string> ModuleDefines = new Dictionary<string, string>
        {
            { "Cross-Promo", "AMZN_CROSSPROMO_ENABLED" },
            { "Adjust", "AMZN_ADJUST_ENABLED" },
            { "Appmetrica", "AMZN_APPMETRICA_ENABLED" },
            { "AppMetrica", "AMZN_APPMETRICA_ENABLED" },
            { "Firebase", "AMZN_FIREBASE_ENABLED" },
            { "InAppPurchase", "AMZN_IAP_ENABLED" },
            { "InternetConnection", "AMZN_INTERNETCONNECTION_ENABLED" }
        };

        //[MenuItem("AMZN GoD/Tools/Auto-Wrap All Module Files (Complete)", false, 420)]
        public static void WrapAllModuleFiles()
        {
            if (!EditorUtility.DisplayDialog("Auto-Wrap All Files",
                "This will automatically wrap ALL module files (Runtime and Editor) in conditional compilation.\n\n" +
                "This includes:\n" +
                "• Runtime scripts\n" +
                "• Editor scripts\n" +
                "• Utility scripts\n\n" +
                "Files already wrapped will be skipped.\n\n" +
                "⚠️ This operation may take a minute.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            int totalWrapped = 0;

            Debug.Log("=== AMZN GoD SDK - Auto-Wrapping All Module Files ===");

            // Runtime modules
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo", "AMZN_CROSSPROMO_ENABLED", "Cross-Promo Runtime");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Adjust", "AMZN_ADJUST_ENABLED", "Adjust Runtime");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica", "AMZN_APPMETRICA_ENABLED", "AppMetrica Runtime");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Firebase", "AMZN_FIREBASE_ENABLED", "Firebase Runtime");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase", "AMZN_IAP_ENABLED", "InAppPurchase Runtime");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection", "AMZN_INTERNETCONNECTION_ENABLED", "InternetConnection Runtime");

            // Editor modules
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/Adjust", "AMZN_ADJUST_ENABLED", "Adjust Editor");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/Appmetrica", "AMZN_APPMETRICA_ENABLED", "AppMetrica Editor");
            totalWrapped += WrapModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/InAppPurchase", "AMZN_IAP_ENABLED", "InAppPurchase Editor");

            Debug.Log($"=== Total wrapped: {totalWrapped} files ===");

            if (totalWrapped > 0)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success",
                    $"Successfully wrapped {totalWrapped} files in conditional compilation!\n\n" +
                    "Unity will now recompile scripts.\n\n" +
                    "Check Console for details.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Info",
                    "All files are already wrapped in conditional compilation.",
                    "OK");
            }
        }

        private static int WrapModuleDirectory(string directory, string defineSymbol, string moduleName)
        {
            if (!Directory.Exists(directory))
            {
                Debug.LogWarning($"[Auto-Wrap] Directory not found: {directory}");
                return 0;
            }

            var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .Where(f => !f.Contains("ConditionalCompilation")) // Skip our own files
                .ToArray();

            int wrappedCount = 0;

            foreach (var filePath in csFiles)
            {
                if (WrapFileIfNeeded(filePath, defineSymbol))
                {
                    wrappedCount++;
                    Debug.Log($"[Auto-Wrap] ✓ {Path.GetFileName(filePath)}");
                }
            }

            if (wrappedCount > 0)
            {
                Debug.Log($"[Auto-Wrap] {moduleName}: Wrapped {wrappedCount} files");
            }

            return wrappedCount;
        }

        private static bool WrapFileIfNeeded(string filePath, string defineSymbol)
        {
            try
            {
                string content = File.ReadAllText(filePath);

                // Skip if already wrapped
                if (content.TrimStart().StartsWith($"#if {defineSymbol}"))
                {
                    return false;
                }

                // Skip empty files
                if (string.IsNullOrWhiteSpace(content))
                {
                    return false;
                }

                // Add #if at the beginning
                string wrappedContent = $"#if {defineSymbol}\n{content}";

                // Check if file already ends with #endif
                string trimmedEnd = wrappedContent.TrimEnd();
                
                if (!trimmedEnd.EndsWith("#endif"))
                {
                    // Add #endif at the end
                    wrappedContent = trimmedEnd + "\n#endif\n";
                }
                else
                {
                    // File already has #endif, just add extra one at the very end
                    wrappedContent = trimmedEnd + "\n#endif\n";
                }

                File.WriteAllText(filePath, wrappedContent);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Auto-Wrap] Error wrapping {filePath}: {e.Message}");
                return false;
            }
        }

        //[MenuItem("AMZN GoD/Tools/Report Unwrapped Files", false, 421)]
        public static void ReportUnwrappedFiles()
        {
            Debug.Log("=== AMZN GoD SDK - Unwrapped Files Report ===");

            int totalUnwrapped = 0;

            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo", "AMZN_CROSSPROMO_ENABLED", "Cross-Promo Runtime");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Adjust", "AMZN_ADJUST_ENABLED", "Adjust Runtime");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica", "AMZN_APPMETRICA_ENABLED", "AppMetrica Runtime");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Firebase", "AMZN_FIREBASE_ENABLED", "Firebase Runtime");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase", "AMZN_IAP_ENABLED", "InAppPurchase Runtime");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection", "AMZN_INTERNETCONNECTION_ENABLED", "InternetConnection Runtime");

            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/Adjust", "AMZN_ADJUST_ENABLED", "Adjust Editor");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/Appmetrica", "AMZN_APPMETRICA_ENABLED", "AppMetrica Editor");
            totalUnwrapped += ReportModuleDirectory("Assets/AMZNGoDSDK/Editor/Modules/InAppPurchase", "AMZN_IAP_ENABLED", "InAppPurchase Editor");

            Debug.Log($"=== Total unwrapped files: {totalUnwrapped} ===");

            if (totalUnwrapped > 0)
            {
                EditorUtility.DisplayDialog("Unwrapped Files Found",
                    $"Found {totalUnwrapped} files that are not wrapped.\n\n" +
                    "Check Console for details.\n\n" +
                    "Run 'Auto-Wrap All Module Files' to fix.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("All Files Wrapped",
                    "All module files are properly wrapped in conditional compilation!",
                    "OK");
            }
        }

        private static int ReportModuleDirectory(string directory, string defineSymbol, string moduleName)
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .Where(f => !f.Contains("ConditionalCompilation"))
                .ToArray();

            int unwrappedCount = 0;
            var unwrappedFiles = new List<string>();

            foreach (var filePath in csFiles)
            {
                string content = File.ReadAllText(filePath);
                if (!content.TrimStart().StartsWith($"#if {defineSymbol}"))
                {
                    unwrappedCount++;
                    unwrappedFiles.Add(Path.GetFileName(filePath));
                }
            }

            if (unwrappedCount > 0)
            {
                Debug.LogWarning($"[{moduleName}] {unwrappedCount} unwrapped files:");
                foreach (var file in unwrappedFiles)
                {
                    Debug.LogWarning($"  • {file}");
                }
            }
            else
            {
                Debug.Log($"[{moduleName}] ✓ All files wrapped");
            }

            return unwrappedCount;
        }
    }
}
