using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>A clean dependency graph alone cannot rule out references inside third-party bytecode.</summary>
    internal sealed class AndroidProhibitedSdkBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        private static int _pendingBuildId;
        private static bool? _pendingExport;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            // Capture the setting used by this build, rather than reading mutable Editor
            // settings when postprocessing runs. A subsequent build replaces a failed build's snapshot.
            _pendingBuildId = report.GetInstanceID();
            _pendingExport = IsProjectExport(report.summary.options, EditorUserBuildSettings.exportAsGoogleAndroidProject);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            bool export = _pendingExport.HasValue && _pendingBuildId == report.GetInstanceID()
                ? _pendingExport.Value : IsProjectExport(report.summary.options, false);
            _pendingExport = null;
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled) return;
            if (export)
            {
                Debug.LogWarning("[AMZN GoD SDK] Gradle export cleaned; final DEX has not been verified. " +
                    "After building the exported project, run AMZN GoD / Android / Check APK or AAB for Prohibited SDKs.");
                return;
            }

            string[] artifacts;
            try
            {
                // Split APK builds also have a directory outputPath. GetFiles records the
                // current build's APKs, including outputs reused by an incremental build.
                artifacts = FindArtifacts(report.summary.outputPath, report.GetFiles()?.Select(file => file.path));
            }
            catch (Exception ex)
            {
                throw new BuildFailedException("[AMZN GoD SDK] Cannot identify Android artifacts for verification: " + ex.Message);
            }
            foreach (string artifact in artifacts) Verify(artifact);
        }

        internal static bool IsProjectExport(BuildOptions options, bool exportAsProject)
        {
            // Matches Unity 2022.3 Android.Utils.WillExportProject, not the shape of outputPath.
            return exportAsProject || (options & (BuildOptions.AcceptExternalModificationsToPlayer | BuildOptions.InstallInBuildFolder)) != 0;
        }

        internal static string[] FindArtifacts(string outputPath, IEnumerable<string> reportedPaths)
        {
            var artifacts = new List<string>();
            var seen = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in reportedPaths ?? Enumerable.Empty<string>())
            {
                if (!IsAndroidArtifact(path)) continue;
                string fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath)) continue;
                if (!File.Exists(fullPath))
                    throw new IOException("A reported Android artifact is missing: " + fullPath);
                artifacts.Add(fullPath);
            }
            // Only the exact requested file is a safe fallback. Enumerating an output
            // directory could select stale APKs left by an earlier build or configuration.
            if (artifacts.Count == 0 && IsAndroidArtifact(outputPath) && File.Exists(outputPath))
                artifacts.Add(Path.GetFullPath(outputPath));
            if (artifacts.Count == 0)
                throw new IOException("The Android build has no APK/AAB outputs in its BuildReport: " + outputPath +
                    ". Final DEX verification was not performed.");
            return artifacts.ToArray();
        }

        private static bool IsAndroidArtifact(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string extension = Path.GetExtension(path);
            return extension.Equals(".apk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".aab", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Verify(string artifact)
        {
            AndroidProhibitedSdkScanner.Result result;
            try { result = AndroidProhibitedSdkScanner.Scan(artifact); }
            catch (Exception ex)
            {
                throw new BuildFailedException("[AMZN GoD SDK] Android artifact verification failed: " + ex.Message);
            }
            if (result.HasProhibitedTypes)
                throw new BuildFailedException("[AMZN GoD SDK] Prohibited SDK types remain in " + artifact + ".\n" +
                    result.Summary() + "\nRemove the owning SDK or adapter, including libraries that reference these types. " +
                    "This artifact must not be published. See Documentation~/AMAZON-SDK-CLEANUP.md.");
            Debug.Log("[AMZN GoD SDK] Android prohibited SDK type check passed: " + artifact + "\n" + result.Summary());
        }

        [MenuItem("AMZN GoD/Android/Check APK or AAB for Prohibited SDKs", false, 210)]
        private static void CheckFromMenu()
        {
            string artifact = EditorUtility.OpenFilePanel("Check Android APK or AAB", "", "apk,aab");
            if (string.IsNullOrEmpty(artifact)) return;
            try { Verify(artifact); }
            catch (BuildFailedException ex) { Debug.LogError(ex.Message); }
        }
    }
}
