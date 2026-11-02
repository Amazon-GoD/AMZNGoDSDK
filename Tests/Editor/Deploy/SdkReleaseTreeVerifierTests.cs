using System;
using System.IO;
using NUnit.Framework;
using AMZNGoDSDK.Editor.Deploy;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="SdkReleaseTreeVerifier.Verify"/> on a synthesized temp release tree: a minimal valid
    /// tree passes, and each rule (package.json fields, runtime version constant, required asmdefs,
    /// forbidden path fragments, orphan "~.meta", missing .meta) fails with its own message.
    /// </summary>
    public class SdkReleaseTreeVerifierTests
    {
        private const string Version = "1.2.3";

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

        private static string PackageJson(string name, string version, string unity, bool withSamples)
        {
            string samples = withSamples
                ? "[{\"path\":\"" + SdkReleaseTreeVerifier.HiddenSamplePathFragment + "\"}," +
                  "{\"path\":\"" + SdkReleaseTreeVerifier.HiddenBannerSamplePathFragment + "\"}]"
                : "[]";
            return "{\"name\":\"" + name + "\",\"version\":\"" + version + "\",\"unity\":\"" + unity + "\",\"samples\":" + samples + "}";
        }

        private static string VersionSource(string version)
        {
            return "namespace AMZNGoDSDK\n{\n    internal static class SdkPackageVersion\n    {\n" +
                   "        public const string Value = \"" + version + "\";\n    }\n}\n";
        }

        private string FullPath(string relativePath)
        {
            return Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void Write(string relativePath, string content)
        {
            string full = FullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        private static bool IsUnityIgnored(string relativePath)
        {
            foreach (string segment in relativePath.Split('/'))
            {
                if (segment.EndsWith("~", StringComparison.Ordinal) || segment.StartsWith(".", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // Creates a sibling .meta for every visible file and folder, like Unity does in a real package.
        private void AddMetas()
        {
            string[] entries = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories);
            foreach (string entry in entries)
            {
                if (entry.EndsWith(".meta", StringComparison.Ordinal))
                    continue;

                string relative = entry.Substring(_root.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (IsUnityIgnored(relative))
                    continue;

                File.WriteAllText(entry + ".meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000000\n");
            }
        }

        // Smallest tree that satisfies every rule of the verifier.
        private void BuildValidTree()
        {
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, Version,
                SdkReleaseTreeVerifier.ExpectedUnityVersion, true));
            Write(SdkReleaseTreeVerifier.SdkVersionSourcePath, VersionSource(Version));
            Write(SdkReleaseTreeVerifier.HiddenSamplePathFragment + "/AmznGoDSDK.prefab", "prefab");
            Write(SdkReleaseTreeVerifier.HiddenBannerSamplePathFragment + "/CrossPromoBanner.prefab", "prefab");

            foreach (string asmdef in SdkReleaseTreeVerifier.RequiredAsmdefPaths)
                Write(asmdef, "{}");

            AddMetas();
        }

        private string VerifyFails(string expectedVersion = Version)
        {
            bool ok = SdkReleaseTreeVerifier.Verify(_root, expectedVersion, out string error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.StartWith("Release tree verification failed"));
            return error;
        }

        // Valid tree

        [Test]
        public void Verify_ValidTree_Passes()
        {
            BuildValidTree();

            bool ok = SdkReleaseTreeVerifier.Verify(_root, Version, out string error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error);
        }

        [Test]
        public void Verify_ValidTree_PassesWithoutExpectedVersionCheckWhenVersionIsEmpty()
        {
            BuildValidTree();
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, "0.0.1",
                SdkReleaseTreeVerifier.ExpectedUnityVersion, true));
            Write(SdkReleaseTreeVerifier.SdkVersionSourcePath, VersionSource("0.0.2"));

            bool ok = SdkReleaseTreeVerifier.Verify(_root, "", out string error);

            Assert.IsTrue(ok, error);
        }

        [Test]
        public void Verify_HiddenTildeFoldersWithoutMeta_Pass()
        {
            BuildValidTree();
            Write("Documentation~/readme.md", "docs");
            Write(".hidden/file.txt", "x");

            bool ok = SdkReleaseTreeVerifier.Verify(_root, Version, out string error);

            Assert.IsTrue(ok, error);
        }

        [Test]
        public void Verify_ContainerAssetContentWithoutMeta_Passes()
        {
            BuildValidTree();
            Write("Runtime/Plugin.bundle/Contents/Info.plist", "plist");
            File.WriteAllText(FullPath("Runtime/Plugin.bundle.meta"), "guid");

            bool ok = SdkReleaseTreeVerifier.Verify(_root, Version, out string error);

            Assert.IsTrue(ok, error);
        }

        // Tree root

        [Test]
        public void Verify_MissingRoot_Fails()
        {
            string missing = Path.Combine(_root, "does-not-exist");

            bool ok = SdkReleaseTreeVerifier.Verify(missing, Version, out string error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("Release tree does not exist"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Verify_NullOrEmptyRoot_Fails(string root)
        {
            bool ok = SdkReleaseTreeVerifier.Verify(root, Version, out string error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("Release tree does not exist"));
        }

        // package.json

        [Test]
        public void Verify_PackageJsonMissing_Fails()
        {
            BuildValidTree();
            File.Delete(FullPath("package.json"));

            Assert.That(VerifyFails(), Does.Contain("package.json is missing at tree root"));
        }

        [Test]
        public void Verify_PackageJsonWithoutName_Fails()
        {
            BuildValidTree();
            Write("package.json", "{}");

            Assert.That(VerifyFails(), Does.Contain("package.json parsed to empty manifest"));
        }

        [Test]
        public void Verify_PackageJsonVersionDiffersFromExpected_Fails()
        {
            BuildValidTree();
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, "1.0.0",
                SdkReleaseTreeVerifier.ExpectedUnityVersion, true));

            string error = VerifyFails();

            Assert.That(error, Does.Contain("package.json version is '1.0.0', expected '" + Version + "'"));
            Assert.That(error, Does.Not.Contain("SdkPackageVersion.cs version is"));
        }

        [Test]
        public void Verify_PackageJsonWrongName_Fails()
        {
            BuildValidTree();
            Write("package.json", PackageJson("com.other.package", Version,
                SdkReleaseTreeVerifier.ExpectedUnityVersion, true));

            Assert.That(VerifyFails(), Does.Contain("package.json name is 'com.other.package'"));
        }

        [Test]
        public void Verify_PackageJsonWrongUnityVersion_Fails()
        {
            BuildValidTree();
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, Version, "2021.3", true));

            Assert.That(VerifyFails(), Does.Contain("package.json unity is '2021.3'"));
        }

        [Test]
        public void Verify_PackageJsonWithoutHiddenSamples_Fails()
        {
            BuildValidTree();
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, Version,
                SdkReleaseTreeVerifier.ExpectedUnityVersion, false));

            string error = VerifyFails();

            Assert.That(error, Does.Contain("package.json is missing the hidden sample path '" +
                                            SdkReleaseTreeVerifier.HiddenSamplePathFragment + "'"));
            Assert.That(error, Does.Contain("package.json is missing the hidden sample path '" +
                                            SdkReleaseTreeVerifier.HiddenBannerSamplePathFragment + "'"));
        }

        [Test]
        public void Verify_SamplePrefabMissing_Fails()
        {
            BuildValidTree();
            File.Delete(FullPath(SdkReleaseTreeVerifier.HiddenBannerSamplePathFragment + "/CrossPromoBanner.prefab"));

            Assert.That(VerifyFails(), Does.Contain("sample prefab is missing: " +
                                                    SdkReleaseTreeVerifier.HiddenBannerSamplePathFragment + "/CrossPromoBanner.prefab"));
        }

        // Runtime version constant

        // Regression 1.0.9: SdkPackageVersion.Value stayed on the previous release version (stale SdkVersion).
        [Test]
        public void Verify_SdkVersionConstantDiffersFromExpected_Fails()
        {
            BuildValidTree();
            Write(SdkReleaseTreeVerifier.SdkVersionSourcePath, VersionSource("1.2.2"));

            string error = VerifyFails();

            Assert.That(error, Does.Contain(SdkReleaseTreeVerifier.SdkVersionSourcePath + " version is '1.2.2', expected '" + Version + "'"));
            Assert.That(error, Does.Not.Contain("package.json version is"));
        }

        [Test]
        public void Verify_SdkVersionFileMissing_Fails()
        {
            BuildValidTree();
            File.Delete(FullPath(SdkReleaseTreeVerifier.SdkVersionSourcePath));

            Assert.That(VerifyFails(), Does.Contain("runtime version file is missing: " + SdkReleaseTreeVerifier.SdkVersionSourcePath));
        }

        [Test]
        public void Verify_SdkVersionFileWithoutConstant_Fails()
        {
            BuildValidTree();
            Write(SdkReleaseTreeVerifier.SdkVersionSourcePath, "namespace AMZNGoDSDK { internal static class SdkPackageVersion { } }");

            Assert.That(VerifyFails(), Does.Contain(SdkReleaseTreeVerifier.SdkVersionSourcePath + " has no version constant"));
        }

        // Required asmdefs

        [Test]
        public void Verify_RequiredAsmdefMissing_Fails()
        {
            BuildValidTree();
            const string asmdef = "Runtime/AMZNGoDSDK.Runtime.asmdef";
            Assert.That(SdkReleaseTreeVerifier.RequiredAsmdefPaths, Does.Contain(asmdef));
            File.Delete(FullPath(asmdef));
            File.Delete(FullPath(asmdef) + ".meta");

            Assert.That(VerifyFails(), Does.Contain("required asmdef missing: " + asmdef));
        }

        // .meta rules

        // Regression 1.0.0: an orphan "Foo~.meta" leaked into the package as an empty folder entry.
        [Test]
        public void Verify_OrphanTildeMeta_Fails()
        {
            BuildValidTree();
            File.WriteAllText(FullPath("Foo~.meta"), "guid");

            string error = VerifyFails();

            Assert.That(error, Does.Contain("tilde .meta must not exist"));
            Assert.That(error, Does.Contain("Foo~.meta"));
        }

        [Test]
        public void Verify_TildeMetaInsideSubfolder_Fails()
        {
            BuildValidTree();
            Write("Runtime/Samples~.meta", "guid");

            Assert.That(VerifyFails(), Does.Contain("Runtime/Samples~.meta"));
        }

        [Test]
        public void Verify_FileWithoutMeta_Fails()
        {
            BuildValidTree();
            Write("Runtime/Extra.cs", "class Extra {}");

            Assert.That(VerifyFails(), Does.Contain("missing .meta for: Runtime/Extra.cs"));
        }

        [Test]
        public void Verify_FolderWithoutMeta_Fails()
        {
            BuildValidTree();
            Directory.CreateDirectory(FullPath("Runtime/NewFolder"));

            Assert.That(VerifyFails(), Does.Contain("missing .meta for: Runtime/NewFolder"));
        }

        [Test]
        public void Verify_MetaFileItselfDoesNotNeedMeta()
        {
            BuildValidTree();

            // Every file in the valid tree has a .meta, and those .meta files are not reported themselves.
            bool ok = SdkReleaseTreeVerifier.Verify(_root, Version, out string error);

            Assert.IsTrue(ok, error);
            Assert.That(error, Is.Null);
        }

        // Forbidden fragments

        [Test]
        public void Verify_ForbiddenPathFragment_Fails()
        {
            BuildValidTree();
            Write("Runtime/Modules/InAppPurchase/IAPTestPanel.cs", "class X {}");
            AddMetas();

            Assert.That(VerifyFails(), Does.Contain("forbidden path fragment 'IAPTestPanel'"));
        }

        [Test]
        public void Verify_DeployToolFolder_Fails()
        {
            BuildValidTree();
            Write("Editor/Deploy/Tool.cs", "class X {}");
            AddMetas();

            Assert.That(VerifyFails(), Does.Contain("forbidden path fragment 'Editor/Deploy'"));
        }

        [Test]
        public void Verify_ForbiddenFragmentIsCaseInsensitive()
        {
            BuildValidTree();
            Write("Runtime/infatica/Lib.cs", "class X {}");
            AddMetas();

            Assert.That(VerifyFails(), Does.Contain("forbidden path fragment 'Infatica'"));
        }

        // Reporting

        [Test]
        public void Verify_SeveralProblems_AllReportedAtOnce()
        {
            BuildValidTree();
            Write("package.json", PackageJson(SdkReleaseTreeVerifier.ExpectedPackageName, "1.0.0",
                SdkReleaseTreeVerifier.ExpectedUnityVersion, true));
            Write(SdkReleaseTreeVerifier.SdkVersionSourcePath, VersionSource("1.0.0"));
            File.WriteAllText(FullPath("Foo~.meta"), "guid");

            string error = VerifyFails();

            Assert.That(error, Does.Contain("package.json version is '1.0.0'"));
            Assert.That(error, Does.Contain(SdkReleaseTreeVerifier.SdkVersionSourcePath + " version is '1.0.0'"));
            Assert.That(error, Does.Contain("Foo~.meta"));
        }

        [Test]
        public void Verify_Success_LeavesErrorNull()
        {
            BuildValidTree();

            SdkReleaseTreeVerifier.Verify(_root, Version, out string error);

            Assert.IsNull(error);
        }
    }
}
