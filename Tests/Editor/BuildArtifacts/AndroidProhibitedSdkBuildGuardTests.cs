using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="AndroidProhibitedSdkBuildGuard"/>: Gradle-export detection from build options and
    /// selection of the APK/AAB artifacts to verify (split APKs reported by BuildReport, single-file
    /// fallback, never enumerating an output directory). Only the pure helpers run, in a temp folder;
    /// the build callbacks and artifact scanning are not called.
    /// </summary>
    public class AndroidProhibitedSdkBuildGuardTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AmznSdkTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // Best-effort cleanup of a temp folder.
            }
        }

        private string CreateFile(string name)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllText(path, "x");
            return path;
        }

        // IsProjectExport

        [Test]
        public void IsProjectExport_PlainBuild_ReturnsFalse()
        {
            Assert.IsFalse(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.None, false));
            Assert.IsFalse(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.Development, false));
        }

        [Test]
        public void IsProjectExport_ExportSettingEnabled_ReturnsTrue()
        {
            Assert.IsTrue(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.None, true));
            Assert.IsTrue(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.Development, true));
        }

        [Test]
        public void IsProjectExport_AcceptExternalModificationsOption_ReturnsTrue()
        {
            Assert.IsTrue(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.AcceptExternalModificationsToPlayer, false));
            Assert.IsTrue(AndroidProhibitedSdkBuildGuard.IsProjectExport(
                BuildOptions.Development | BuildOptions.AcceptExternalModificationsToPlayer, false));
        }

        [Test]
        public void IsProjectExport_InstallInBuildFolderOption_ReturnsTrue()
        {
            Assert.IsTrue(AndroidProhibitedSdkBuildGuard.IsProjectExport(BuildOptions.InstallInBuildFolder, false));
        }

        // FindArtifacts

        // Regression 1.0.8: split APK builds have a directory outputPath; the APKs come from the BuildReport.
        [Test]
        public void FindArtifacts_SplitApksFromReport_ReturnsAllReportedApks()
        {
            string baseApk = CreateFile("base.apk");
            string arm = CreateFile("config.arm64_v8a.apk");
            string lang = CreateFile("config.en.apk");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, new[] { baseApk, arm, lang });

            Assert.AreEqual(new[] { Path.GetFullPath(baseApk), Path.GetFullPath(arm), Path.GetFullPath(lang) }, result);
        }

        [Test]
        public void FindArtifacts_ReportContainsOtherFiles_OnlyApkAndAabAreReturned()
        {
            string apk = CreateFile("game.apk");
            string aab = CreateFile("game.aab");
            string mapping = CreateFile("mapping.txt");
            string symbols = CreateFile("game.symbols.zip");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, new[] { mapping, apk, symbols, aab, null, "" });

            Assert.AreEqual(new[] { Path.GetFullPath(apk), Path.GetFullPath(aab) }, result);
        }

        [Test]
        public void FindArtifacts_ExtensionIsCaseInsensitive()
        {
            string apk = CreateFile("GAME.APK");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, new[] { apk });

            Assert.AreEqual(new[] { Path.GetFullPath(apk) }, result);
        }

        [Test]
        public void FindArtifacts_DuplicateReportedPath_ReturnedOnce()
        {
            string apk = CreateFile("game.apk");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, new[] { apk, apk });

            Assert.AreEqual(1, result.Length);
        }

        [Test]
        public void FindArtifacts_ReportedFileMissing_ThrowsIOException()
        {
            string missing = Path.Combine(_root, "missing.apk");

            var ex = Assert.Throws<IOException>(() => AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, new[] { missing }));

            Assert.That(ex.Message, Does.Contain("A reported Android artifact is missing"));
        }

        [Test]
        public void FindArtifacts_ReportedMissingEvenIfOutputPathExists_ThrowsIOException()
        {
            string existing = CreateFile("game.apk");
            string missing = Path.Combine(_root, "other.apk");

            Assert.Throws<IOException>(() => AndroidProhibitedSdkBuildGuard.FindArtifacts(existing, new[] { missing }));
        }

        [Test]
        public void FindArtifacts_NoReportedPaths_FallsBackToOutputFile()
        {
            string apk = CreateFile("game.apk");

            string[] fromNull = AndroidProhibitedSdkBuildGuard.FindArtifacts(apk, null);
            string[] fromEmpty = AndroidProhibitedSdkBuildGuard.FindArtifacts(apk, new string[0]);

            Assert.AreEqual(new[] { Path.GetFullPath(apk) }, fromNull);
            Assert.AreEqual(new[] { Path.GetFullPath(apk) }, fromEmpty);
        }

        [Test]
        public void FindArtifacts_OnlyNonAndroidFilesReported_FallsBackToOutputAab()
        {
            string aab = CreateFile("game.aab");
            string mapping = CreateFile("mapping.txt");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(aab, new[] { mapping });

            Assert.AreEqual(new[] { Path.GetFullPath(aab) }, result);
        }

        [Test]
        public void FindArtifacts_DirectoryOutputWithoutReport_ThrowsAndDoesNotPickStaleApks()
        {
            // A stale APK from an earlier build sits in the output directory.
            CreateFile("stale.apk");

            var ex = Assert.Throws<IOException>(() => AndroidProhibitedSdkBuildGuard.FindArtifacts(_root, null));

            Assert.That(ex.Message, Does.Contain("The Android build has no APK/AAB outputs in its BuildReport"));
        }

        [Test]
        public void FindArtifacts_OutputFileMissingAndNothingReported_Throws()
        {
            string missing = Path.Combine(_root, "game.apk");

            Assert.Throws<IOException>(() => AndroidProhibitedSdkBuildGuard.FindArtifacts(missing, new string[0]));
        }

        [Test]
        public void FindArtifacts_NullOutputPathAndNothingReported_Throws()
        {
            Assert.Throws<IOException>(() => AndroidProhibitedSdkBuildGuard.FindArtifacts(null, null));
        }

        [Test]
        public void FindArtifacts_ReportWinsOverOutputFile()
        {
            string output = CreateFile("game.apk");
            string reported = CreateFile("split.apk");

            string[] result = AndroidProhibitedSdkBuildGuard.FindArtifacts(output, new[] { reported });

            Assert.AreEqual(new[] { Path.GetFullPath(reported) }, result);
        }
    }
}
