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
    /// Checks current Gradle inputs for SDK-owned code of disabled modules.
    /// Shared Android libraries are resolved by EDM/Gradle for all consumers.
    /// </summary>
    public sealed class DisabledModuleAndroidArtifactGuard : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 11000;

        private static readonly HashSet<string> OutputDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "build", ".gradle", ".cxx", ".externalNativeBuild", ".kotlin",
                ".idea", ".git", "out"
            };

        private static readonly System.Text.RegularExpressions.Regex JavaPackage =
            new System.Text.RegularExpressions.Regex(
                @"\A\s*package\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;");

        private static readonly System.Text.RegularExpressions.Regex QuotedValue =
            new System.Text.RegularExpressions.Regex("[\"']([^\"'\\r\\n]+)[\"']");

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = SdkSettingsManager.LoadSettings();
            var disabled = ModuleBuildArtifactRegistry.All
                .Where(spec => !ModuleBuildArtifactRegistry.IsEnabled(spec, settings))
                .ToList();
            if (disabled.Count == 0)
                return;

            string root = Directory.GetParent(Path.GetFullPath(path))?.FullName ?? path;
            int removedFiles = 0;
            var leaks = new List<string>();

            // A single pruned traversal is shared by cleanup and validation.
            // Never recurse into previous build outputs or directory links.
            foreach (string file in EnumerateInputFiles(root))
            {
                string relative = file.Substring(root.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Unity occasionally leaves copied Java sources in an incremental
                // export. Both the package declaration and the exact SDK class
                // name must match before we remove one. AAR/JAR/SO names cannot
                // establish ownership and are deliberately left to the resolver.
                if (IsOwnedJavaSource(file, disabled))
                {
                    File.Delete(file);
                    removedFiles++;
                    continue;
                }

                if (file.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase))
                {
                    AuditGradleReferences(file, relative, disabled, leaks);
                }
            }

            if (leaks.Count > 0)
            {
                throw new BuildFailedException(
                    "Сборка остановлена: исходники Gradle-проекта ссылаются на файлы выключенных модулей SDK:\n  • " +
                    string.Join("\n  • ", leaks.Take(25)) +
                    "\nПересоздай Gradle-проект после переключения модулей и убери устаревшие ссылки " +
                    "на файлы SDK из Android Gradle templates. Общие библиотеки сторонних SDK не удаляются.");
            }

            if (removedFiles > 0)
                Debug.Log($"[AMZN GoD SDK] Removed {removedFiles} Java source(s) owned by disabled SDK modules.");
        }

        private static IEnumerable<string> EnumerateInputFiles(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string file in Directory.GetFiles(directory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                        yield return file;
                }

                foreach (string child in Directory.GetDirectories(directory))
                {
                    if (OutputDirectories.Contains(Path.GetFileName(child)) ||
                        (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    pending.Push(child);
                }
            }
        }

        private static bool IsOwnedJavaSource(
            string path, IReadOnlyList<ModuleBuildArtifactSpec> disabled)
        {
            if (!string.Equals(Path.GetExtension(path), ".java", StringComparison.OrdinalIgnoreCase))
                return false;

            string className = Path.GetFileNameWithoutExtension(path);
            var candidates = disabled.SelectMany(module => module.AndroidOwnedJavaTypes)
                .Where(type => type.EndsWith("." + className, StringComparison.Ordinal)).ToArray();
            if (candidates.Length == 0)
                return false;

            // Strip comments so an example package declaration does not establish
            // ownership of a third-party file with the same short class name.
            string source = StripComments(File.ReadAllText(path));
            var package = JavaPackage.Match(source);
            return package.Success &&
                   candidates.Contains(package.Groups[1].Value + "." + className);
        }

        private static void AuditGradleReferences(
            string path, string relative, IReadOnlyList<ModuleBuildArtifactSpec> disabled,
            ICollection<string> leaks)
        {
            string[] lines = StripComments(File.ReadAllText(path)).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                foreach (System.Text.RegularExpressions.Match match in QuotedValue.Matches(lines[index]))
                {
                    string value = match.Groups[1].Value.Replace('\\', '/');
                    foreach (var module in disabled)
                    {
                        if (!ReferencesSdkAsset(module, value))
                            continue;
                        leaks.Add($"{module.Name}: {relative}:{index + 1} ({value})");
                        break;
                    }
                }
            }
        }

        private static bool ReferencesSdkAsset(ModuleBuildArtifactSpec module, string value)
        {
            foreach (string sdkRoot in NativePluginRegistry.SdkRootPrefixes)
            {
                int start = value.IndexOf(sdkRoot, StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && (start == 0 || value[start - 1] == '/') &&
                    ModuleBuildArtifactRegistry.OwnsSdkAssetPath(module, value.Substring(start)))
                    return true;
            }
            return false;
        }

        // Preserve strings (including repository URLs) and line numbers while
        // removing line/block comments from Java and Gradle input.
        private static string StripComments(string value)
        {
            var result = new StringBuilder(value.Length);
            bool lineComment = false;
            bool blockComment = false;
            char quote = '\0';
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                char next = i + 1 < value.Length ? value[i + 1] : '\0';
                if (lineComment)
                {
                    if (current == '\n') lineComment = false;
                    result.Append(current == '\n' || current == '\r' ? current : ' ');
                }
                else if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        result.Append("  ");
                        i++;
                        blockComment = false;
                    }
                    else result.Append(current == '\n' || current == '\r' ? current : ' ');
                }
                else if (quote != '\0')
                {
                    result.Append(current);
                    if (current == '\\' && i + 1 < value.Length) result.Append(value[++i]);
                    else if (current == quote) quote = '\0';
                }
                else if (current == '/' && (next == '/' || next == '*'))
                {
                    lineComment = next == '/';
                    blockComment = next == '*';
                    result.Append("  ");
                    i++;
                }
                else
                {
                    result.Append(current);
                    if (current == '"' || current == '\'') quote = current;
                }
            }
            return result.ToString();
        }
    }
#endif
}
