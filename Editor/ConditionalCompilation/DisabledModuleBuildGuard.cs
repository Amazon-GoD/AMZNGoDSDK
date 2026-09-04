#if UNITY_ANDROID
using UnityEditor.Android;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Fail-closed проверка: успешный билд не может содержать managed-сборки или
    /// всегда включаемые Resources-ассеты выключенного модуля.
    /// </summary>
    public sealed class DisabledModuleBuildGuard : IPreprocessBuildWithReport
    {
        // EdmDependencyBuildPreprocessor (-100) сначала синхронизирует defines и фильтры.
        public int callbackOrder => -80;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkSettingsManager.LoadSettings();
            var disabled = ModuleBuildArtifactRegistry.All
                .Where(spec => !ModuleBuildArtifactRegistry.IsEnabled(spec, settings))
                .ToList();

            NativePluginBuildFilter.Refresh();

            var errors = new List<string>();
            CheckManagedAssemblies(disabled, errors);
            CheckAlwaysIncludedResources(disabled, errors);
            CheckExternalDependencyFiles(disabled, errors);

            if (errors.Count == 0)
                return;

            var message = new StringBuilder();
            message.AppendLine("Сборка остановлена: выключенный модуль всё ещё оставляет артефакты в Player.");
            foreach (string error in errors)
                message.AppendLine("  • " + error);
            message.AppendLine("Дождись окончания UPM/компиляции после переключения модулей и запусти билд повторно.");
            throw new BuildFailedException(message.ToString());
        }

        private static void CheckManagedAssemblies(
            IEnumerable<ModuleBuildArtifactSpec> disabled,
            ICollection<string> errors)
        {
            var playerAssemblies = new HashSet<string>(
                CompilationPipeline
                    .GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                    .Select(assembly => assembly.name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var module in disabled)
            {
                var leaked = module.ManagedAssemblies.Where(playerAssemblies.Contains).ToArray();
                if (leaked.Length > 0)
                    errors.Add($"{module.Name}: компилируются сборки {string.Join(", ", leaked)}.");
            }
        }

        private static void CheckAlwaysIncludedResources(
            IEnumerable<ModuleBuildArtifactSpec> disabled,
            ICollection<string> errors)
        {
            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                string normalized = assetPath.Replace('\\', '/');
                if (normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                foreach (var module in disabled)
                {
                    if (!ModuleBuildArtifactRegistry.OwnsAssetPath(module, normalized))
                        continue;

                    errors.Add($"{module.Name}: always-included Resources asset {normalized}.");
                    break;
                }
            }
        }

        private static void CheckExternalDependencyFiles(
            IEnumerable<ModuleBuildArtifactSpec> disabled,
            ICollection<string> errors)
        {
            if (!disabled.Any(module => string.Equals(module.Name, "Firebase", StringComparison.OrdinalIgnoreCase)))
                return;

            foreach (string path in ExternalDependencyAssetSynchronizer.ActiveFirebaseDependencyFiles())
                errors.Add($"Firebase: EDM dependency file всё ещё активен: {path}.");
        }
    }

#if UNITY_ANDROID
    /// <summary>
    /// Последний Android-рубеж. Убирает из временного Gradle-проекта строки и
    /// нативные файлы выключенных модулей, затем повторно сканирует результат.
    /// </summary>
    public sealed class DisabledModuleAndroidArtifactGuard : IPostGenerateGradleAndroidProject
    {
        // После manifest cleaner (10000) и всех EDM/модульных процессоров.
        public int callbackOrder => 11000;

        private static readonly HashSet<string> LineFilteredExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".gradle", ".properties", ".pro"
            };

        private static readonly HashSet<string> AuditedTextExtensions =
            new HashSet<string>(LineFilteredExtensions, StringComparer.OrdinalIgnoreCase)
            {
                ".xml"
            };

        private static readonly HashSet<string> NativeExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".aar", ".jar", ".so", ".java", ".kt"
            };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = SdkSettingsManager.LoadSettings();
            var disabled = ModuleBuildArtifactRegistry.All
                .Where(spec => !ModuleBuildArtifactRegistry.IsEnabled(spec, settings))
                .ToList();
            if (disabled.Count == 0)
                return;

            string root = Directory.GetParent(path)?.FullName ?? path;
            int removedLines = 0;
            int removedFiles = 0;

            // Unity может скопировать legacy *.androidlib как обычный каталог,
            // минуя решение PluginImporter. Удаляем только дочерние каталоги
            // с модульным fingerprint; Gradle root в сравнении не участвует.
            foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
            {
                if (!Directory.Exists(directory) || !MatchesFile(directory, disabled, out _))
                    continue;
                Directory.Delete(directory, true);
                removedFiles++;
            }

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (LineFilteredExtensions.Contains(extension))
                    removedLines += RemoveOwnedLines(file, disabled);
                else if (NativeExtensions.Contains(extension) && MatchesFile(file, disabled, out _))
                {
                    File.Delete(file);
                    removedFiles++;
                }
            }

            var leaks = Scan(root, disabled);
            if (leaks.Count > 0)
            {
                throw new BuildFailedException(
                    "Сборка остановлена: после очистки Gradle-проекта найдены артефакты выключенных модулей:\n  • " +
                    string.Join("\n  • ", leaks.Take(25)));
            }

            if (removedLines > 0 || removedFiles > 0)
            {
                Debug.Log($"[AMZN GoD SDK] Disabled-module Android cleanup: " +
                          $"removed {removedLines} Gradle line(s), {removedFiles} native file(s).");
            }
        }

        private static int RemoveOwnedLines(string path, IReadOnlyList<ModuleBuildArtifactSpec> disabled)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception)
            {
                return 0;
            }

            var kept = new List<string>(lines.Length);
            int removed = 0;
            foreach (string line in lines)
            {
                if (MatchesText(line, disabled, out _))
                    removed++;
                else
                    kept.Add(line);
            }

            if (removed > 0)
                File.WriteAllLines(path, kept, new UTF8Encoding(false));
            return removed;
        }

        private static List<string> Scan(string root, IReadOnlyList<ModuleBuildArtifactSpec> disabled)
        {
            var leaks = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string extension = Path.GetExtension(file);

                if (NativeExtensions.Contains(extension) && MatchesFile(file, disabled, out string fileModule))
                    leaks.Add($"{fileModule}: native file {relative}");

                if (!AuditedTextExtensions.Contains(extension))
                    continue;

                int lineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (MatchesText(line, disabled, out string textModule))
                        leaks.Add($"{textModule}: {relative}:{lineNumber}");
                }
            }
            return leaks;
        }

        private static bool MatchesText(
            string value,
            IEnumerable<ModuleBuildArtifactSpec> disabled,
            out string moduleName)
        {
            foreach (var module in disabled)
            {
                if (module.AndroidTextFingerprints.Any(fingerprint =>
                        value.IndexOf(fingerprint, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    moduleName = module.Name;
                    return true;
                }
            }
            moduleName = null;
            return false;
        }

        private static bool MatchesFile(
            string path,
            IEnumerable<ModuleBuildArtifactSpec> disabled,
            out string moduleName)
        {
            string normalized = path.Replace('\\', '/');
            int ownedPathStart = normalized.IndexOf("/src/", StringComparison.OrdinalIgnoreCase);
            if (ownedPathStart < 0)
                ownedPathStart = normalized.IndexOf("/libs/", StringComparison.OrdinalIgnoreCase);
            normalized = ownedPathStart >= 0
                ? normalized.Substring(ownedPathStart)
                : Path.GetFileName(normalized);
            foreach (var module in disabled)
            {
                if (module.AndroidFileFingerprints.Any(fingerprint =>
                        normalized.IndexOf(fingerprint, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    moduleName = module.Name;
                    return true;
                }
            }
            moduleName = null;
            return false;
        }
    }
#endif
}
