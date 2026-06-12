using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Проверяет нативные зависимости модулей (.java, .jar, .aar) на дубликаты.
    /// Сканирует папки активных модулей и проектный Assets/Plugins/Android.
    /// </summary>
    public static class NativeDependencyValidator
    {
        private static readonly string[] NativeExtensions = { ".java", ".jar", ".aar" };

        private static readonly string ProjectPluginsAndroid = "Assets/Plugins/Android";

        /// <summary>
        /// Маппинг имён модулей к их корневым папкам (должен совпадать с ModuleFolderManager).
        /// </summary>
        private static readonly Dictionary<string, string> ModuleRoots = new Dictionary<string, string>
        {
            { "Cross-Promo",        "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo" },
            { "Adjust",             "Assets/AMZNGoDSDK/Runtime/Modules/Adjust" },
            { "AppMetrica",         "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica" },
            { "Firebase",           "Assets/AMZNGoDSDK/Runtime/Modules/Firebase" },
            { "InAppPurchase",      "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase" },
            { "InternetConnection", "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection" },
            { "InGameDebugConsole", "Assets/AMZNGoDSDK/Runtime/Modules/InGameDebugConsole" },
        };

        public struct DuplicateEntry
        {
            public string FileName;
            public List<string> Sources; // module names or "Project (Assets/Plugins/Android)"
            public List<string> FullPaths;
        }

        public struct ValidationResult
        {
            public List<DuplicateEntry> Duplicates;
            public int TotalFilesScanned;
            public bool HasDuplicates => Duplicates != null && Duplicates.Count > 0;
        }

        /// <summary>
        /// Проверяет дубликаты нативных файлов для набора модулей, которые будут включены.
        /// </summary>
        /// <param name="enabledModules">Словарь: имя модуля → будет ли включён</param>
        public static ValidationResult Validate(Dictionary<string, bool> enabledModules)
        {
            // fileName → list of (source, fullPath)
            var fileMap = new Dictionary<string, List<(string source, string fullPath)>>(StringComparer.OrdinalIgnoreCase);

            int totalFiles = 0;

            foreach (var kvp in enabledModules)
            {
                if (!kvp.Value) continue;

                if (!ModuleRoots.TryGetValue(kvp.Key, out string root))
                    continue;

                string activePath = Directory.Exists(root) ? root : null;
                string disabledPath = Directory.Exists(root + "~") ? root + "~" : null;
                string scanPath = activePath ?? disabledPath;

                if (scanPath == null) continue;

                var files = CollectNativeFiles(scanPath);
                totalFiles += files.Count;

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileMap.ContainsKey(fileName))
                        fileMap[fileName] = new List<(string, string)>();

                    fileMap[fileName].Add((kvp.Key, file));
                }
            }

            // Scan project-level Plugins/Android
            if (Directory.Exists(ProjectPluginsAndroid))
            {
                var projectFiles = CollectNativeFiles(ProjectPluginsAndroid);
                totalFiles += projectFiles.Count;

                foreach (var file in projectFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileMap.ContainsKey(fileName))
                        fileMap[fileName] = new List<(string, string)>();

                    fileMap[fileName].Add(("Project (Plugins/Android)", file));
                }
            }

            var duplicates = new List<DuplicateEntry>();

            foreach (var kvp in fileMap)
            {
                if (kvp.Value.Count <= 1) continue;

                var uniqueSources = kvp.Value.Select(v => v.source).Distinct().ToList();
                if (uniqueSources.Count <= 1) continue; // same module — not a cross-module conflict

                duplicates.Add(new DuplicateEntry
                {
                    FileName = kvp.Key,
                    Sources = kvp.Value.Select(v => v.source).ToList(),
                    FullPaths = kvp.Value.Select(v => v.fullPath).ToList()
                });
            }

            return new ValidationResult
            {
                Duplicates = duplicates,
                TotalFilesScanned = totalFiles
            };
        }

        /// <summary>
        /// Проверяет дубликаты на основе текущих SDK настроек.
        /// </summary>
        public static ValidationResult ValidateFromSettings(SdkSettingsData settings)
        {
            var enabledModules = new Dictionary<string, bool>
            {
                { "Cross-Promo",        settings.CrossPromo.Enabled },
                { "Adjust",             settings.Adjust.Enabled },
                { "AppMetrica",         settings.AppMetrica.Enabled },
                { "Firebase",           settings.Firebase.Enabled },
                { "InAppPurchase",      settings.InAppPurchase.Enabled },
                { "InternetConnection", settings.InternetConnection.Enabled },
                { "InGameDebugConsole", settings.DebugConsole.Enabled },
            };

            return Validate(enabledModules);
        }

        /// <summary>
        /// Показывает диалог с результатами проверки. Возвращает true если дубликатов нет или пользователь решил продолжить.
        /// </summary>
        public static bool ValidateAndPrompt(SdkSettingsData settings)
        {
            var result = ValidateFromSettings(settings);

            if (!result.HasDuplicates)
                return true;

            string message = FormatDuplicatesMessage(result);

            return EditorUtility.DisplayDialog(
                "Native Dependency Conflict",
                message + "\n\nЭто может привести к ошибкам сборки Android.\nПродолжить сохранение?",
                "Продолжить",
                "Отмена");
        }

        private static string FormatDuplicatesMessage(ValidationResult result)
        {
            var lines = new List<string>
            {
                $"Обнаружены дубликаты нативных файлов ({result.Duplicates.Count}):\n"
            };

            foreach (var dup in result.Duplicates)
            {
                string sources = string.Join(", ", dup.Sources.Distinct());
                lines.Add($"  {dup.FileName}  →  [{sources}]");
            }

            return string.Join("\n", lines);
        }

        private static List<string> CollectNativeFiles(string rootPath)
        {
            var results = new List<string>();

            if (!Directory.Exists(rootPath))
                return results;

            try
            {
                foreach (var file in Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext)) continue;

                    // Skip .meta files and files inside ~ directories (inactive storage)
                    if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (file.Contains("~" + Path.DirectorySeparatorChar) || file.Contains("~" + Path.AltDirectorySeparatorChar))
                        continue;

                    if (NativeExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(file);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativeDependencyValidator] Error scanning {rootPath}: {e.Message}");
            }

            return results;
        }

        [MenuItem("AMZN GoD/Debug/Check Native Duplicates", false, 203)]
        public static void LogNativeDuplicates()
        {
            var settings = SdkSettingsManager.LoadSettings();
            var result = ValidateFromSettings(settings);

            Debug.Log($"=== AMZN GoD SDK — Native Dependency Check ({result.TotalFilesScanned} files scanned) ===");

            if (!result.HasDuplicates)
            {
                Debug.Log("  No duplicate native files detected across enabled modules.");
            }
            else
            {
                Debug.LogWarning($"  Found {result.Duplicates.Count} duplicate(s):");
                foreach (var dup in result.Duplicates)
                {
                    Debug.LogWarning($"    {dup.FileName}:");
                    for (int i = 0; i < dup.FullPaths.Count; i++)
                    {
                        Debug.LogWarning($"      [{dup.Sources[i]}] {dup.FullPaths[i]}");
                    }
                }
            }

            Debug.Log("=================================================================");
        }
    }
}
