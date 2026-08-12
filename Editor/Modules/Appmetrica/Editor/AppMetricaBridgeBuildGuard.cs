#if AMZN_APPMETRICA_ENABLED
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Fails an Android build early, with an actionable message, when the AppMetrica Java bridge
    /// (io.appmetrica.analytics.plugin.unity.*) is not present as compilable, Android-enabled
    /// sources.
    ///
    /// Rationale: the C# proxies (AppMetricaProxy / ReporterProxy / AppMetricaLibraryAdapterProxy)
    /// call these Java classes by fully-qualified name via AndroidJavaClass. If the loose .java
    /// bridge is dropped during (re)packaging — leaving only C# + the native analytics AAR — the
    /// classes are absent from the APK and the app crashes at runtime with ClassNotFoundException
    /// on the first AppMetrica call. This guard converts that runtime crash into a clear build
    /// error so it is caught before shipping.
    ///
    /// Sources that are present but merely not enabled for Android (a .meta regenerated with
    /// wrong platform flags after repackaging) are re-enabled automatically instead of failing.
    /// </summary>
    internal sealed class AppMetricaBridgeBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // One representative Java class per C# proxy that talks to the native bridge. Each MUST
        // ship as a loose .java under a Plugins/Android folder, enabled for the Android platform.
        private static readonly string[] RequiredBridgeFiles =
        {
            "AppMetricaProxy.java",
            "ReporterProxy.java",
            "AppMetricaLibraryAdapterProxy.java",
        };

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            var present = CollectAndroidEnabledBridgeSources();
            var missing = RequiredBridgeFiles
                .Where(fileName => !present.Contains(fileName))
                .ToArray();

            if (missing.Length == 0)
                return;

            throw new BuildFailedException(
                "[AMZNGoDSDK] AppMetrica Android bridge sources are missing from the project: " +
                string.Join(", ", missing) + ".\n" +
                "These io.appmetrica.analytics.plugin.unity.* sources compile the Java bridge that " +
                "the C# proxies call. Without them the app throws ClassNotFoundException at runtime " +
                "on the first AppMetrica call.\n" +
                "Fix: restore the AppMetrica module's Runtime/Plugins/Android/*.java exactly as they " +
                "ship in the SDK package (they must stay under a Plugins/Android folder). If your " +
                "packaging step strips loose .java, restore them before building. Files that are " +
                "present but not enabled for Android are re-enabled automatically by this guard."
            );
        }

        /// <summary>
        /// Returns the set of required bridge file names that are present as imported assets
        /// compiled for Android. Scans the Assets tree once.
        ///
        /// Only files that are (a) not inside a Unity-ignored "~" folder — this repo's own
        /// module-disable convention (see ModuleFolderManager) — and (b) located under a
        /// Plugins/Android folder, which is the only place Unity compiles loose .java, are
        /// considered. The path checks are case-sensitive on purpose: the AppMetrica module
        /// ships these paths with exact casing.
        /// </summary>
        private static HashSet<string> CollectAndroidEnabledBridgeSources()
        {
            var required = new HashSet<string>(RequiredBridgeFiles);
            var found = new HashSet<string>();
            string dataPath = Application.dataPath.Replace('\\', '/');

            string[] javaFiles;
            try
            {
                javaFiles = Directory.GetFiles(Application.dataPath, "*.java", SearchOption.AllDirectories);
            }
            catch (System.Exception e)
            {
                // Keep failure messaging consistent with the guard's intent instead of surfacing
                // a raw IO/access exception mid-build.
                throw new BuildFailedException(
                    "[AMZNGoDSDK] Failed to scan the project for AppMetrica Android bridge sources: " + e.Message);
            }

            foreach (var raw in javaFiles)
            {
                string path = raw.Replace('\\', '/');
                string fileName = Path.GetFileName(path);

                if (!required.Contains(fileName))
                    continue;
                if (path.Contains("~/"))
                    continue;
                if (!path.Contains("/Plugins/Android/"))
                    continue;

                string assetPath = "Assets" + path.Substring(dataPath.Length);
                if (AssetImporter.GetAtPath(assetPath) is PluginImporter importer)
                {
                    // Самолечение: файл на месте, но платформа Android выключена — типично
                    // после переупаковки SDK, когда .meta пересоздались с чужими настройками.
                    // Ронять сборку ради галочки, которую можем поставить сами, незачем.
                    if (!importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    {
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                        importer.SaveAndReimport();
                        Debug.LogWarning(
                            $"[AMZNGoDSDK] AppMetrica bridge source {assetPath} was not enabled for " +
                            "Android — enabled automatically before the build.");
                    }

                    found.Add(fileName);
                }
            }

            return found;
        }
    }
}
#endif
