using UnityEditor;
using System.IO;
using UnityEngine;
using System.Linq;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Утилита для автоматического оборачивания файлов модулей в условную компиляцию
    /// </summary>
    public static class ModuleFilesWrapper
    {
        //[MenuItem("AMZN GoD/Tools/Wrap Module Files in Conditional Compilation", false, 400)]
        public static void WrapAllModuleFiles()
        {
            if (!EditorUtility.DisplayDialog("Wrap Module Files",
                "This will add #if directives to all module files that don't have them yet.\n\n" +
                "This is a one-time operation for modules that were created before conditional compilation was added.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            int wrappedCount = 0;
            
            // Cross-Promo files
            wrappedCount += WrapModuleDirectory(
                "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo",
                "AMZN_CROSSPROMO_ENABLED",
                "Cross-Promo");

            Debug.Log($"[AMZN GoD SDK] Wrapped {wrappedCount} files in conditional compilation");
            
            if (wrappedCount > 0)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success", 
                    $"Successfully wrapped {wrappedCount} files!\n\n" +
                    "Unity will now recompile scripts.", 
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
                Debug.LogWarning($"[AMZN GoD SDK] Directory not found: {directory}");
                return 0;
            }

            var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .Where(f => !f.Contains("Editor")) // Skip editor files
                .ToArray();

            int wrappedCount = 0;

            foreach (var filePath in csFiles)
            {
                if (WrapFileIfNeeded(filePath, defineSymbol))
                {
                    wrappedCount++;
                    Debug.Log($"[AMZN GoD SDK] Wrapped: {Path.GetFileName(filePath)}");
                }
            }

            if (wrappedCount > 0)
            {
                Debug.Log($"[AMZN GoD SDK] {moduleName}: Wrapped {wrappedCount} files");
            }

            return wrappedCount;
        }

        private static bool WrapFileIfNeeded(string filePath, string defineSymbol)
        {
            try
            {
                string content = File.ReadAllText(filePath);

                // Check if already wrapped
                if (content.TrimStart().StartsWith($"#if {defineSymbol}"))
                {
                    return false; // Already wrapped
                }

                // Skip if it's just a small enum or struct without dependencies
                if (content.Length < 100 && !content.Contains("using"))
                {
                    return false;
                }

                // Add #if at the beginning
                string wrappedContent = $"#if {defineSymbol}\n{content}";
                
                // Add #endif at the end if not already there
                if (!wrappedContent.TrimEnd().EndsWith("#endif"))
                {
                    wrappedContent = wrappedContent.TrimEnd() + "\n#endif\n";
                }

                File.WriteAllText(filePath, wrappedContent);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AMZN GoD SDK] Error wrapping {filePath}: {e.Message}");
                return false;
            }
        }

        //[MenuItem("AMZN GoD/Tools/Check Module Files Wrapping Status", false, 401)]
        public static void CheckWrappingStatus()
        {
            Debug.Log("=== AMZN GoD SDK - Module Files Wrapping Status ===");

            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo", "AMZN_CROSSPROMO_ENABLED", "Cross-Promo");
            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Adjust", "AMZN_ADJUST_ENABLED", "Adjust");
            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica", "AMZN_APPMETRICA_ENABLED", "AppMetrica");
            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/Firebase", "AMZN_FIREBASE_ENABLED", "Firebase");
            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase", "AMZN_IAP_ENABLED", "InAppPurchase");
            CheckModuleDirectory("Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection", "AMZN_INTERNETCONNECTION_ENABLED", "InternetConnection");

            Debug.Log("================================================");
        }

        private static void CheckModuleDirectory(string directory, string defineSymbol, string moduleName)
        {
            if (!Directory.Exists(directory))
            {
                Debug.LogWarning($"  {moduleName}: Directory not found");
                return;
            }

            var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .Where(f => !f.Contains("Editor"))
                .ToArray();

            int totalFiles = csFiles.Length;
            int wrappedFiles = 0;

            foreach (var filePath in csFiles)
            {
                string content = File.ReadAllText(filePath);
                if (content.TrimStart().StartsWith($"#if {defineSymbol}"))
                {
                    wrappedFiles++;
                }
            }

            string status = wrappedFiles == totalFiles ? "✓" : "⚠";
            Debug.Log($"  {status} {moduleName}: {wrappedFiles}/{totalFiles} files wrapped");

            if (wrappedFiles < totalFiles)
            {
                Debug.LogWarning($"    Missing wrapping in {totalFiles - wrappedFiles} files. Run 'Wrap Module Files' to fix.");
            }
        }
    }
}
