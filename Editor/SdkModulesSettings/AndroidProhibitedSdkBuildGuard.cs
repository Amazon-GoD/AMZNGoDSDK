using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>A clean dependency graph alone cannot rule out references inside third-party bytecode.</summary>
    internal sealed class AndroidProhibitedSdkBuildGuard : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled) return;
            string artifact = report.summary.outputPath;
            if (Directory.Exists(artifact))
            {
                Debug.LogWarning("[AMZN GoD SDK] Gradle export cleaned; final DEX has not been verified. " +
                    "After building the exported project, run AMZN GoD / Android / Check APK or AAB for Prohibited SDKs.");
                return;
            }
            Verify(artifact);
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
