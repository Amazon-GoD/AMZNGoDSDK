using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Останавливает билд, если в проект просочилась запрещённая рекламная сетка
    /// (см. <see cref="ForbiddenAdNetworks"/>).
    /// <para>
    /// Тот же принцип, что у <see cref="SdkPackageVerifier"/> для экспорта пакета: проверяется
    /// не то, что мы собирались исключить, а то, что фактически лежит в проекте. Ошибиться
    /// в Integration Manager легко, а транзитивную зависимость чужого адаптера там вообще
    /// не видно — поймать её можно только по резолвнутым артефактам.
    /// </para>
    /// <para>
    /// Работает только при включённом модуле AppLovin: без медиации адаптеров в проекте нет,
    /// а Amplitude/Flurry/Branch игра может использовать самостоятельно — рушить ей билд
    /// из-за собственной аналитики мы не вправе.
    /// </para>
    /// </summary>
    public class AppLovinNetworkGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private const string MaxMediationPath = "Assets/MaxSdk/Mediation";

        // <androidPackage spec="com.tapjoy:tapjoy-android-sdk:13.2.1" />
        private static readonly Regex AndroidPackageSpec =
            new Regex("spec\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkSettingsManager.LoadSettings();

            if (settings == null || !settings.Enabled || !settings.AppLovin.Enabled)
                return;

            var findings = new List<string>();

            ScanMaxAdapterFolders(findings);
            ScanDependencyManifests(findings);
            ScanResolvedAndroidLibraries(findings);

            if (findings.Count == 0)
            {
                Debug.Log("[AppLovinNetworkGuard] Запрещённых рекламных сеток не найдено.");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Сборка остановлена: в проекте найдены запрещённые рекламные сетки.");
            foreach (var finding in findings)
                message.AppendLine("  • " + finding);
            message.AppendLine();
            message.AppendLine("Убери адаптеры через AppLovin > Integration Manager либо удали зависимость. " +
                               "Список сеток — ForbiddenAdNetworks.cs.");

            throw new BuildFailedException(message.ToString());
        }

        /// <summary>Установленные адаптеры MAX: Assets/MaxSdk/Mediation/&lt;Network&gt;.</summary>
        private static void ScanMaxAdapterFolders(List<string> findings)
        {
            if (!Directory.Exists(MaxMediationPath))
                return;

            foreach (var directory in Directory.GetDirectories(MaxMediationPath))
            {
                string folderName = Path.GetFileName(directory);
                var network = ForbiddenAdNetworks.MatchByAdapterFolder(folderName);

                if (network != null)
                    findings.Add($"адаптер MAX «{network.DisplayName}»: {directory}");
            }
        }

        /// <summary>
        /// Dependencies.xml файлы EDM4U: объявленные зависимости видно ещё до резолва,
        /// а адаптеры, установленные UPM-пакетом, кладут свои xml в Packages/.
        /// </summary>
        private static void ScanDependencyManifests(List<string> findings)
        {
            foreach (var root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var path in Directory.GetFiles(root, "*Dependencies.xml", SearchOption.AllDirectories))
                {
                    string content;
                    try
                    {
                        content = File.ReadAllText(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AppLovinNetworkGuard] Не удалось прочитать {path}: {ex.Message}");
                        continue;
                    }

                    foreach (Match match in AndroidPackageSpec.Matches(content))
                    {
                        string spec = match.Groups[1].Value;
                        var network = ForbiddenAdNetworks.MatchByGroup(spec);

                        if (network != null)
                            findings.Add($"зависимость «{network.DisplayName}»: {spec} ({path})");
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

            foreach (var path in Directory.GetFiles(pluginsPath, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(path);
                if (!string.Equals(extension, ".aar", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase))
                    continue;

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
            ScanMaxAdapterFolders(findings);
            ScanDependencyManifests(findings);
            ScanResolvedAndroidLibraries(findings);

            if (findings.Count == 0)
            {
                Debug.Log("[AppLovinNetworkGuard] Чисто: запрещённых рекламных сеток не найдено.");
                return;
            }

            Debug.LogError($"[AppLovinNetworkGuard] Найдено запрещённых сеток: {findings.Count}");
            foreach (var finding in findings)
                Debug.LogError("  • " + finding);
        }
    }
}
