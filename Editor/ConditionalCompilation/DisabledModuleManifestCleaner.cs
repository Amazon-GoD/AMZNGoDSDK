#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Автоочистка манифеста от компонентов выключенных модулей.
    ///
    /// Проблема: переименование папки модуля в "~" убирает только то, что лежит
    /// ВНУТРИ папки модуля. Записи в глобальном Assets/Plugins/Android/AndroidManifest.xml
    /// (или остатки чужих манифестов) так не вычищаются и протекают в сборку —
    /// например, нативные service/receiver выключенного модуля могут попасть
    /// в билд другого модуля.
    ///
    /// Решение: на этапе генерации Gradle-проекта (ДО merge-таски манифеста) пройтись
    /// по манифестам, которые формирует Unity (unityLibrary + launcher), и вырезать
    /// компоненты/permissions всех модулей, выключенных в настройках SDK.
    ///
    /// Безопасность: мы правим только Unity-вклад (src/main). Если какую-то запись
    /// реально объявляет ВКЛЮЧЁННАЯ зависимость в своём AAR — gradle-merge вернёт её
    /// обратно после этого шага. permissions общей инфраструктуры защищены
    /// белым списком (<see cref="ModuleManifestRegistry.SharedPermissions"/>).
    /// </summary>
    public class DisabledModuleManifestCleaner : IPostGenerateGradleAndroidProject
    {
        // Запускаемся поздно — после модульных процессоров (например AppMetrica=0),
        // чтобы наша очистка была последним словом.
        public int callbackOrder => 10000;

        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string LogTag = "[AMZN GoD SDK] [ManifestCleaner]";

        private static readonly HashSet<string> ComponentTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "service", "receiver", "provider", "activity", "activity-alias"
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                var settings = SdkSettingsManager.LoadSettings();
                var disabled = ModuleManifestRegistry.GetDisabledFootprints(settings);
                if (disabled.Count == 0)
                    return;

                var protectedPermissions = ModuleManifestRegistry.GetEnabledPermissions(settings);

                int totalRemoved = 0;
                foreach (var manifestPath in EnumerateUnityManifests(path))
                {
                    totalRemoved += CleanManifest(manifestPath, disabled, protectedPermissions);
                }

                if (totalRemoved > 0)
                {
                    var names = string.Join(", ", disabled.ConvertAll(f => f.ModuleName));
                    Debug.Log($"{LogTag} Removed {totalRemoved} manifest entr(ies) for disabled module(s): {names}");
                }
            }
            catch (Exception e)
            {
                // Полное исключение — обязательный инвариант. Если очистку нельзя
                // доказуемо применить, продолжать сборку небезопасно.
                throw new BuildFailedException($"{LogTag} cleanup failed: {e.Message}");
            }
        }

        /// <summary>
        /// Манифесты, которые формирует Unity из Assets/Plugins/Android и плагинов модулей.
        /// path = каталог модуля unityLibrary; launcher лежит рядом.
        /// </summary>
        private static IEnumerable<string> EnumerateUnityManifests(string unityLibraryPath)
        {
            string unityLibManifest = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (File.Exists(unityLibManifest))
                yield return unityLibManifest;

            string gradleRoot = Directory.GetParent(unityLibraryPath)?.FullName;
            if (gradleRoot == null)
                yield break;

            string launcherManifest = Path.Combine(gradleRoot, "launcher", "src", "main", "AndroidManifest.xml");
            if (File.Exists(launcherManifest))
                yield return launcherManifest;
        }

        /// <summary>
        /// Вырезает из одного манифеста компоненты и permissions выключенных модулей.
        /// Возвращает число удалённых узлов.
        /// </summary>
        private static int CleanManifest(
            string manifestPath,
            List<ModuleManifestFootprint> disabled,
            HashSet<string> protectedPermissions)
        {
            var doc = new XmlDocument();
            using (var reader = new XmlTextReader(manifestPath))
            {
                reader.Read();
                doc.Load(reader);
            }

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("android", AndroidNs);

            int removed = 0;
            removed += RemoveComponents(doc, nsMgr, disabled, manifestPath);
            removed += RemovePermissions(doc, nsMgr, disabled, protectedPermissions, manifestPath);

            if (removed > 0)
                Save(doc, manifestPath);

            return removed;
        }

        private static int RemoveComponents(
            XmlDocument doc, XmlNamespaceManager nsMgr,
            List<ModuleManifestFootprint> disabled, string manifestPath)
        {
            var application = doc.SelectSingleNode("/manifest/application", nsMgr) as XmlElement;
            if (application == null)
                return 0;

            int removed = 0;
            // Снимок: правим коллекцию во время обхода.
            var children = new List<XmlElement>();
            foreach (XmlNode node in application.ChildNodes)
            {
                if (node is XmlElement el && ComponentTags.Contains(el.LocalName))
                    children.Add(el);
            }

            foreach (var el in children)
            {
                string androidName = el.GetAttribute("name", AndroidNs);
                if (string.IsNullOrEmpty(androidName))
                    androidName = el.GetAttribute("android:name");

                foreach (var footprint in disabled)
                {
                    if (ModuleManifestRegistry.MatchesComponent(footprint, androidName))
                    {
                        application.RemoveChild(el);
                        removed++;
                        Debug.Log($"{LogTag} [{footprint.ModuleName}] removed <{el.LocalName} {androidName}> " +
                                  $"from {ShortPath(manifestPath)}");
                        break;
                    }
                }
            }

            return removed;
        }

        private static int RemovePermissions(
            XmlDocument doc, XmlNamespaceManager nsMgr,
            List<ModuleManifestFootprint> disabled, HashSet<string> protectedPermissions,
            string manifestPath)
        {
            var manifest = doc.SelectSingleNode("/manifest", nsMgr) as XmlElement;
            if (manifest == null)
                return 0;

            int removed = 0;
            var permNodes = new List<XmlElement>();
            foreach (XmlNode node in manifest.ChildNodes)
            {
                if (node is XmlElement el && el.LocalName == "uses-permission")
                    permNodes.Add(el);
            }

            foreach (var el in permNodes)
            {
                string permName = el.GetAttribute("name", AndroidNs);
                if (string.IsNullOrEmpty(permName))
                    permName = el.GetAttribute("android:name");

                if (string.IsNullOrEmpty(permName))
                    continue;

                // Защита: общая инфраструктура и permissions включённых модулей не трогаем.
                if (ModuleManifestRegistry.SharedPermissions.Contains(permName))
                    continue;
                if (protectedPermissions.Contains(permName))
                    continue;

                foreach (var footprint in disabled)
                {
                    if (Array.IndexOf(footprint.Permissions, permName) >= 0)
                    {
                        manifest.RemoveChild(el);
                        removed++;
                        Debug.Log($"{LogTag} [{footprint.ModuleName}] removed <uses-permission {permName}> " +
                                  $"from {ShortPath(manifestPath)}");
                        break;
                    }
                }
            }

            return removed;
        }

        private static void Save(XmlDocument doc, string path)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),
            };
            using (var writer = XmlWriter.Create(path, settings))
            {
                doc.Save(writer);
            }
        }

        private static string ShortPath(string fullPath)
        {
            int idx = fullPath.Replace('\\', '/').IndexOf("/Gradle/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? fullPath.Substring(idx + 1) : Path.GetFileName(fullPath);
        }

        // ----------------------------------------------------------------------------
        // Dry-run проверка ИСХОДНОГО манифеста проекта (до сборки).
        // ----------------------------------------------------------------------------

        private const string ProjectPluginsManifest = "Assets/Plugins/Android/AndroidManifest.xml";

        /// <summary>
        /// Показывает, какие записи выключенных модулей сейчас лежат в
        /// Assets/Plugins/Android/AndroidManifest.xml — без изменения файла.
        /// Удобно проверить утечку до запуска сборки.
        /// </summary>
        [MenuItem("AMZN GoD/Debug/Scan Plugins Manifest For Disabled Modules", false, 204)]
        public static void ScanProjectManifest()
        {
            if (!File.Exists(ProjectPluginsManifest))
            {
                Debug.Log($"{LogTag} No {ProjectPluginsManifest} — nothing to scan.");
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            var disabled = ModuleManifestRegistry.GetDisabledFootprints(settings);
            var protectedPermissions = ModuleManifestRegistry.GetEnabledPermissions(settings);

            var doc = new XmlDocument();
            doc.Load(ProjectPluginsManifest);
            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("android", AndroidNs);

            var findings = new List<string>();

            var application = doc.SelectSingleNode("/manifest/application", nsMgr) as XmlElement;
            if (application != null)
            {
                foreach (XmlNode node in application.ChildNodes)
                {
                    if (!(node is XmlElement el) || !ComponentTags.Contains(el.LocalName))
                        continue;

                    string androidName = el.GetAttribute("name", AndroidNs);
                    if (string.IsNullOrEmpty(androidName)) androidName = el.GetAttribute("android:name");

                    foreach (var f in disabled)
                        if (ModuleManifestRegistry.MatchesComponent(f, androidName))
                            findings.Add($"  [{f.ModuleName}] <{el.LocalName}> {androidName}");
                }
            }

            var manifest = doc.SelectSingleNode("/manifest", nsMgr) as XmlElement;
            if (manifest != null)
            {
                foreach (XmlNode node in manifest.ChildNodes)
                {
                    if (!(node is XmlElement el) || el.LocalName != "uses-permission")
                        continue;

                    string permName = el.GetAttribute("name", AndroidNs);
                    if (string.IsNullOrEmpty(permName)) permName = el.GetAttribute("android:name");
                    if (string.IsNullOrEmpty(permName)) continue;

                    if (ModuleManifestRegistry.SharedPermissions.Contains(permName)) continue;
                    if (protectedPermissions.Contains(permName)) continue;

                    foreach (var f in disabled)
                        if (Array.IndexOf(f.Permissions, permName) >= 0)
                            findings.Add($"  [{f.ModuleName}] <uses-permission> {permName}");
                }
            }

            if (findings.Count == 0)
            {
                Debug.Log($"{LogTag} ✓ {ProjectPluginsManifest}: no leaked entries from disabled modules.");
            }
            else
            {
                Debug.LogWarning($"{LogTag} ⚠ {ProjectPluginsManifest} contains {findings.Count} entr(ies) " +
                                 $"from DISABLED modules (auto-removed at build time):\n" +
                                 string.Join("\n", findings));
            }
        }
    }
}
#endif
