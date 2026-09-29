using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Перед каждым Android-билдом синхронно очищает нативные входы от запрещённых SDK
    /// (см. <see cref="ForbiddenAdNetworks"/>), затем проверяет неизвестные источники.
    /// <para>
    /// Тот же принцип, что у <see cref="SdkPackageVerifier"/> для экспорта пакета: проверяется
    /// не то, что мы собирались исключить, а то, что фактически лежит в проекте. Ошибиться
    /// в Integration Manager легко, а транзитивную зависимость чужого адаптера там вообще
    /// не видно — поймать её можно только по резолвнутым артефактам.
    /// </para>
    /// <para>
    /// Проверяется Android при включённом SDK, независимо от состояния модуля MAX.
    /// Необязательный модуль Fyber в AppMetrica исключается поздно в Gradle.
    /// </para>
    /// </summary>
    public class AppLovinNetworkGuard : IPreprocessBuildWithReport
    {
        // After EDM and MAX (int.MaxValue - 10), before Unity copies native inputs.
        public int callbackOrder => int.MaxValue - 1;

        private const string MaxMediationPath = "Assets/MaxSdk/Mediation";

        // <androidPackage spec="com.tapjoy:tapjoy-android-sdk:13.2.1" />
        private static readonly Regex AndroidPackageSpec =
            new Regex("spec\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "com.applovin.mediation.adapters.fyber.android": "8.3.1"
        private static readonly Regex PackageId =
            new Regex("\"((?:com|io)\\.[^\"]+)\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkSettingsManager.LoadSettings();

            if (report.summary.platform != BuildTarget.Android || settings == null || !settings.Enabled)
                return;

#if UNITY_ANDROID
            // Synchronous work only: no UPM requests, script changes or domain reload.
            // The same BuildPipeline invocation continues after these inputs are cleaned.
            new AppLovinSettingsSynchronizer().OnPreprocessBuild(report);
            AppLovinGradleExclusions.CleanProjectInputs(Directory.GetCurrentDirectory());
            NativePluginBuildFilter.Refresh();
#endif
            var findings = new List<string>();

            CollectFindings(findings, true);

            if (findings.Count == 0)
            {
                Debug.Log("[AppLovinNetworkGuard] PreProcess: запрещённые нативные SDK исключены, Gradle-шаблоны очищены. Сборка продолжается.");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Сборка остановлена: в проекте найдены запрещённые рекламные сетки.");
            foreach (var finding in findings)
                message.AppendLine("  • " + finding);
            message.AppendLine();
            message.AppendLine("Штатные MAX-адаптеры очищаются в этой же сборке. Указанные неизвестные " +
                               "исходники или обёртки нельзя безопасно удалить автоматически: " +
                               "удалите или обновите пакет-источник. Список SDK — ForbiddenAdNetworks.cs.");

            throw new BuildFailedException(message.ToString());
        }

        /// <summary>Установленные адаптеры MAX: Assets/MaxSdk/Mediation/&lt;Network&gt;.</summary>
        private static void ScanMaxAdapterFolders(List<string> findings, bool allowCleanableAdapters)
        {
            if (!Directory.Exists(MaxMediationPath))
                return;

            if (allowCleanableAdapters)
            {
                // Inspect only: vendor-owned adapters are handled by native filtering.
                // Unknown managed/legacy files still need attribution, never blind deletion.
                AppLovinLegacyInstallation.InspectProhibitedAdapters();
                return;
            }

            foreach (var directory in Directory.GetDirectories(MaxMediationPath))
            {
                string folderName = Path.GetFileName(directory);
                var network = ForbiddenAdNetworks.MatchByAdapterFolder(folderName);

                if (network != null && ImportedFiles(directory).Any(path =>
                    Regex.IsMatch(path, @"\.(cs|dll|aar|jar|so|a|m|mm|h|asmdef|xml|gradle)$", RegexOptions.IgnoreCase)))
                    findings.Add($"адаптер MAX «{network.DisplayName}»: {directory}");
            }
        }

        /// <summary>
        /// Зависимости Packages/manifest.json. Начиная с MAX 8.0 адаптеры ставятся не
        /// папкой в Assets, а UPM-пакетом из scoped registry AppLovin — тогда единственный
        /// след запрещённой сетки до резолва зависимостей лежит именно здесь.
        /// </summary>
        private static void ScanUpmManifest(List<string> findings, bool allowCleanableAdapters)
        {
            const string manifestPath = "Packages/manifest.json";

            if (!File.Exists(manifestPath))
                return;

            string content;
            try
            {
                content = File.ReadAllText(manifestPath);
            }
            catch (Exception ex)
            {
                findings.Add($"не удалось проверить {manifestPath}: {ex.Message}");
                return;
            }

            // Полноценный парсер здесь не нужен: имена пакетов — это ключи-строки, и
            // ложное срабатывание на URL реестра невозможно (он не содержит имён сеток).
            var registered = PackageInfo.GetAllRegisteredPackages();
            foreach (Match match in PackageId.Matches(content))
            {
                string packageId = match.Groups[1].Value;
                if (allowCleanableAdapters && registered.Any(package => package.name == packageId && IsCleanableMaxAdapter(package))) continue;
                var network = ForbiddenAdNetworks.MatchByGroup(packageId);

                if (network != null)
                    findings.Add($"UPM-пакет «{network.DisplayName}»: {packageId} ({manifestPath})");
            }
            foreach (var package in registered)
            {
                if (allowCleanableAdapters && IsCleanableMaxAdapter(package)) continue;
                var network = ForbiddenAdNetworks.MatchByGroup(package.name);
                if (network != null)
                    findings.Add($"зарегистрированный пакет «{network.DisplayName}»: {package.packageId} " +
                        $"(источник {package.source}, {package.resolvedPath})");
            }
        }

        /// <summary>
        /// Dependencies.xml файлы EDM4U: объявленные зависимости видно ещё до резолва,
        /// а адаптеры, установленные UPM-пакетом, кладут свои xml в Packages/.
        /// </summary>
        private static void ScanDependencyManifests(List<string> findings)
        {
            var roots = new[] { "Assets" }.Concat(PackageInfo.GetAllRegisteredPackages()
                .Select(package => package.resolvedPath).Where(path => !string.IsNullOrEmpty(path))).Distinct();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var path in ImportedFiles(root).Where(path => path.EndsWith("Dependencies.xml", StringComparison.OrdinalIgnoreCase)))
                {
                    string content;
                    try
                    {
                        content = File.ReadAllText(path);
                    }
                    catch (Exception ex)
                    {
                        findings.Add($"не удалось проверить {path}: {ex.Message}");
                        continue;
                    }

                    foreach (Match match in AndroidPackageSpec.Matches(content))
                    {
                        string spec = match.Groups[1].Value;
                        string[] coordinate = spec.Split(':');
                        if (coordinate.Length < 2)
                            continue;
                        var network = ForbiddenAdNetworks.MatchMavenCoordinate(coordinate[0], coordinate[1]);

                        if (network != null)
                            Debug.Log($"[AppLovinNetworkGuard] Gradle удалит зависимость «{network.DisplayName}»: {spec} ({path}).");
                    }
                }
            }
        }

        /// <summary>
        /// Уже резолвнутые библиотеки. EDM4U кладёт их в Assets/Plugins/Android именами вида
        /// com.tapjoy.tapjoy-android-sdk-13.2.1.aar — транзитивную зависимость, которой нет
        /// ни в одном Dependencies.xml проекта, видно только здесь.
        /// </summary>
        private static void ScanResolvedAndroidLibraries(List<string> findings)
        {
            const string pluginsPath = "Assets/Plugins/Android";

            if (!Directory.Exists(pluginsPath))
                return;

            foreach (var path in ImportedFiles(pluginsPath))
            {
                string extension = Path.GetExtension(path);
                if (!string.Equals(extension, ".aar", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase))
                    continue;

#if UNITY_ANDROID
                // Gradle works on generated copies. Do not stop before it can remove a
                // precisely identified EDM library (notably AppMetrica's optional Fyber module).
                if (AppLovinGradleExclusions.IsRemovableLocalLibrary(path)) continue;
#endif

                string fileName = Path.GetFileName(path);
                var network = ForbiddenAdNetworks.MatchByGroup(fileName);

                if (network != null)
                    findings.Add($"библиотека «{network.DisplayName}»: {path}");
            }
        }

        /// <summary>
        /// Ручной прогон той же проверки — чтобы не ждать билда. Ничего не роняет,
        /// результат пишет в консоль.
        /// </summary>
        [MenuItem("AMZN GoD/Debug/Check Forbidden Ad Networks", false, 203)]
        public static void CheckFromMenu()
        {
            var findings = new List<string>();
            CollectFindings(findings);

            if (findings.Count == 0)
            {
                Debug.Log("[AppLovinNetworkGuard] Нет блокирующих источников SDK. Итоговый результат определяется проверкой APK/AAB после очистки Gradle.");
                return;
            }

            Debug.LogError($"[AppLovinNetworkGuard] Найдено запрещённых сеток: {findings.Count}");
            foreach (var finding in findings)
                Debug.LogError("  • " + finding);
        }

        private static bool IsCleanableMaxAdapter(PackageInfo package) =>
            package.source == UnityEditor.PackageManager.PackageSource.Registry &&
            package.name.StartsWith("com.applovin.mediation.adapters.", StringComparison.Ordinal);

        private static void CollectFindings(List<string> findings, bool allowCleanableAdapters = false)
        {
            try
            {
                ScanMaxAdapterFolders(findings, allowCleanableAdapters);
                ScanUpmManifest(findings, allowCleanableAdapters);
                ScanDependencyManifests(findings);
                ScanResolvedAndroidLibraries(findings);
            }
            catch (Exception ex)
            {
                findings.Add("Проверка источников SDK не завершена: " + ex.Message);
            }
        }

        private static IEnumerable<string> ImportedFiles(string root)
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) yield break;
            foreach (string file in Directory.GetFiles(root))
                if (!IgnoredName(Path.GetFileName(file)) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    yield return file;
            foreach (string directory in Directory.GetDirectories(root))
                if (!IgnoredName(Path.GetFileName(directory)))
                    foreach (string file in ImportedFiles(directory)) yield return file;
        }

        private static bool IgnoredName(string name)
        {
            return name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal);
        }
    }
}
