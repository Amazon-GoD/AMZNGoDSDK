using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="AppLovinPackageInstaller"/>: pure static logic only (scoped registry text insertion,
    /// package specs, pinned set filtering, rollback ids, allowed/blocked adapter lists).
    /// Install, sync and status methods are intentionally not called: they touch UPM, SessionState
    /// and the real project manifest.
    /// </summary>
    public class AppLovinPackageInstallerTests
    {
        private const string MaxId = "com.applovin.mediation.ads";
        private const string HyprMxId = "com.applovin.mediation.adapters.hyprmx.android";
        private const string MaioId = "com.applovin.mediation.adapters.maio.android";
        private const string FacebookId = "com.applovin.mediation.adapters.facebook.android";

        private static int CountOccurrences(string text, string fragment)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(fragment, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += fragment.Length;
            }
            return count;
        }

        private static string Insert(string manifest)
        {
            string updated = AppLovinPackageInstaller.InsertRegistry(manifest, out string error);

            Assert.IsNull(error);
            Assert.IsNotNull(updated);
            return updated;
        }

        private static void AssertValidWithSingleRegistry(string updated)
        {
            Assert.DoesNotThrow(() => ManifestJson.Parse(updated));
            Assert.AreEqual(1, CountOccurrences(updated, AppLovinPackageInstaller.RegistryUrl));
            Assert.AreEqual(1, CountOccurrences(updated, "\"scopedRegistries\""));
            Assert.That(updated, Does.Contain("\"" + AppLovinPackageInstaller.RegistryScope + "\""));
            Assert.That(updated, Does.Contain("\"" + AppLovinPackageInstaller.RegistryName + "\""));
        }

        // InsertRegistry

        // Regression 1.0.8: an empty "scopedRegistries": [] got a trailing comma and produced invalid JSON.
        [Test]
        public void InsertRegistry_EmptyScopedRegistries_ProducesValidJson()
        {
            const string manifest = "{\n  \"dependencies\": {},\n  \"scopedRegistries\": []\n}";

            string updated = Insert(manifest);

            AssertValidWithSingleRegistry(updated);
            Assert.That(updated, Does.Not.Contain("},]"));
            Assert.That(updated, Does.Not.Contain(",\r\n    ]"));
        }

        [Test]
        public void InsertRegistry_EmptyScopedRegistriesWithWhitespace_ProducesValidJson()
        {
            const string manifest = "{\n  \"scopedRegistries\": [\n\n  ],\n  \"dependencies\": {}\n}";

            AssertValidWithSingleRegistry(Insert(manifest));
        }

        [Test]
        public void InsertRegistry_MissingKey_AddsBlockAndKeepsValidJson()
        {
            const string manifest = "{\n  \"dependencies\": {\n    \"com.unity.ugui\": \"1.0.0\"\n  }\n}";

            string updated = Insert(manifest);

            AssertValidWithSingleRegistry(updated);
            Assert.That(ManifestJson.Parse(updated).FindRootValue("dependencies"), Is.GreaterThanOrEqualTo(0));
            Assert.That(ManifestJson.Parse(updated).FindRootValue("scopedRegistries"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void InsertRegistry_EmptyRootObject_ProducesValidJson()
        {
            AssertValidWithSingleRegistry(Insert("{}"));
        }

        [Test]
        public void InsertRegistry_ExistingForeignRegistry_KeepsItAndAddsAppLovin()
        {
            const string manifest = "{\n" +
                                    "  \"scopedRegistries\": [\n" +
                                    "    {\n" +
                                    "      \"name\": \"Other\",\n" +
                                    "      \"url\": \"https://registry.example.com\",\n" +
                                    "      \"scopes\": [ \"com.example\" ]\n" +
                                    "    }\n" +
                                    "  ],\n" +
                                    "  \"dependencies\": {}\n" +
                                    "}";

            string updated = Insert(manifest);

            AssertValidWithSingleRegistry(updated);
            Assert.AreEqual(1, CountOccurrences(updated, "https://registry.example.com"));
        }

        [Test]
        public void InsertRegistry_PreservesUnrelatedManifestContent()
        {
            const string dependencies = "  \"dependencies\": {\n    \"com.unity.ugui\": \"1.0.0\",\n    \"com.example.local\": \"file:../Local\"\n  }";
            const string manifest = "{\n" + dependencies + ",\n  \"testables\": [ \"com.unity.ugui\" ]\n}";

            string updated = Insert(manifest);

            Assert.DoesNotThrow(() => ManifestJson.Parse(updated));
            Assert.That(updated, Does.Contain(dependencies));
            Assert.That(updated, Does.Contain("\"testables\": [ \"com.unity.ugui\" ]"));
        }

        [Test]
        public void InsertRegistry_ExistingAppLovinRegistry_ReturnsManifestUnchanged()
        {
            string manifest = "{\n" +
                              "  \"scopedRegistries\": [\n" +
                              "    {\n" +
                              "      \"name\": \"AppLovin MAX\",\n" +
                              "      \"url\": \"" + AppLovinPackageInstaller.RegistryUrl + "\",\n" +
                              "      \"scopes\": [ \"com.applovin\" ]\n" +
                              "    }\n" +
                              "  ],\n" +
                              "  \"dependencies\": {}\n" +
                              "}";

            string updated = AppLovinPackageInstaller.InsertRegistry(manifest, out string error);

            Assert.IsNull(error);
            Assert.AreEqual(manifest, updated);
        }

        [Test]
        public void InsertRegistry_CalledTwice_IsIdempotent()
        {
            const string manifest = "{\n  \"dependencies\": {},\n  \"scopedRegistries\": []\n}";

            string first = Insert(manifest);
            string second = Insert(first);

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, CountOccurrences(second, AppLovinPackageInstaller.RegistryUrl));
        }

        [Test]
        public void InsertRegistry_MissingKeyCalledTwice_IsIdempotent()
        {
            string first = Insert("{\n  \"dependencies\": {}\n}");
            string second = Insert(first);

            Assert.AreEqual(first, second);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("[]")]
        [TestCase("{")]
        [TestCase("{\"dependencies\": }")]
        [TestCase("{\"dependencies\": {},}")]
        [TestCase("{\"a\":1,\"a\":2}")]
        [TestCase("not json")]
        public void InsertRegistry_MalformedManifest_ReturnsNullWithError(string manifest)
        {
            string updated = AppLovinPackageInstaller.InsertRegistry(manifest, out string error);

            Assert.IsNull(updated);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void InsertRegistry_NullManifest_ReturnsNullWithError()
        {
            string updated = AppLovinPackageInstaller.InsertRegistry(null, out string error);

            Assert.IsNull(updated);
            Assert.That(error, Does.Contain("Манифест пуст"));
        }

        [Test]
        public void InsertRegistry_ScopedRegistriesNotArray_ReturnsNullWithError()
        {
            string updated = AppLovinPackageInstaller.InsertRegistry("{\"scopedRegistries\": {}}", out string error);

            Assert.IsNull(updated);
            Assert.That(error, Does.Contain("должно быть массивом"));
        }

        // PackageSpec / Pins / PinnedSpecsIn

        [Test]
        public void PackageSpec_PinnedPackage_AppendsPinnedVersion()
        {
            Assert.That(AppLovinPackageInstaller.Pins.ContainsKey(MaxId));

            Assert.AreEqual(MaxId + "@" + AppLovinPackageInstaller.Pins[MaxId], AppLovinPackageInstaller.PackageSpec(MaxId));
        }

        [Test]
        public void PackageSpec_UnpinnedPackage_ReturnsBareId()
        {
            Assert.AreEqual("com.example.unknown", AppLovinPackageInstaller.PackageSpec("com.example.unknown"));
        }

        [Test]
        public void PackageSpec_EveryPin_HasIdAtVersionForm()
        {
            Assert.That(AppLovinPackageInstaller.Pins, Is.Not.Empty);

            foreach (var pin in AppLovinPackageInstaller.Pins)
            {
                Assert.That(pin.Value, Is.Not.Null.And.Not.Empty, pin.Key);
                Assert.AreEqual(pin.Key + "@" + pin.Value, AppLovinPackageInstaller.PackageSpec(pin.Key));
            }
        }

        [Test]
        public void PinnedSpecsIn_ObsoleteAdaptersAreFilteredOut()
        {
            var specs = AppLovinPackageInstaller.PinnedSpecsIn(new[] { MaxId, HyprMxId, MaioId });

            Assert.AreEqual(1, specs.Count);
            Assert.AreEqual(AppLovinPackageInstaller.PackageSpec(MaxId), specs[0]);
        }

        [Test]
        public void PinnedSpecsIn_UnpinnedIdsAreSkipped()
        {
            var specs = AppLovinPackageInstaller.PinnedSpecsIn(new[] { "com.example.unknown", FacebookId });

            Assert.AreEqual(1, specs.Count);
            Assert.AreEqual(AppLovinPackageInstaller.PackageSpec(FacebookId), specs[0]);
        }

        [Test]
        public void PinnedSpecsIn_EmptyInput_ReturnsEmptyList()
        {
            Assert.That(AppLovinPackageInstaller.PinnedSpecsIn(new string[0]), Is.Empty);
        }

        [Test]
        public void PinnedSpecsIn_AllPinnedIds_AreKeptBecauseNoneIsForbidden()
        {
            var ids = AppLovinPackageInstaller.Pins.Keys.ToList();

            var specs = AppLovinPackageInstaller.PinnedSpecsIn(ids);

            Assert.AreEqual(ids.Count, specs.Count);
        }

        // RollbackRemovalIds

        [Test]
        public void RollbackRemovalIds_ReturnsOnlyAppLovinPackagesNotPreviouslyInstalled()
        {
            const string manifest = "{\"dependencies\":{" +
                                    "\"com.applovin.mediation.ads\":\"8.6.6\"," +
                                    "\"com.applovin.mediation.adapters.facebook.android\":\"1.0.0\"," +
                                    "\"com.unity.ugui\":\"1.0.0\"}}";

            string[] ids = AppLovinPackageInstaller.RollbackRemovalIds(manifest, new[] { MaxId, "com.unity.ugui" });

            Assert.AreEqual(new[] { FacebookId }, ids);
        }

        [Test]
        public void RollbackRemovalIds_EverythingWasInstalled_ReturnsEmpty()
        {
            const string manifest = "{\"dependencies\":{\"com.applovin.mediation.ads\":\"8.6.6\"}}";

            string[] ids = AppLovinPackageInstaller.RollbackRemovalIds(manifest, new[] { MaxId });

            Assert.That(ids, Is.Empty);
        }

        [Test]
        public void RollbackRemovalIds_NoAppLovinDependencies_ReturnsEmpty()
        {
            const string manifest = "{\"dependencies\":{\"com.unity.ugui\":\"1.0.0\"}}";

            Assert.That(AppLovinPackageInstaller.RollbackRemovalIds(manifest, new string[0]), Is.Empty);
        }

        // AllowedAdapterPackageIds / BlockedNetworkNames

        [Test]
        public void AllowedAdapterPackageIds_IsNotEmptyWithoutDuplicates()
        {
            List<string> allowed = AppLovinPackageInstaller.AllowedAdapterPackageIds();

            Assert.That(allowed, Is.Not.Empty);
            Assert.That(allowed, Is.Unique);
        }

        [Test]
        public void AllowedAdapterPackageIds_NoneIsForbidden()
        {
            foreach (string id in AppLovinPackageInstaller.AllowedAdapterPackageIds())
                Assert.IsNull(ForbiddenAdNetworks.MatchByGroup(id), id);
        }

        [Test]
        public void AllowedAdapterPackageIds_AreAndroidAdaptersFromApplovinRegistry()
        {
            foreach (string id in AppLovinPackageInstaller.AllowedAdapterPackageIds())
            {
                Assert.That(id, Does.StartWith("com.applovin.mediation.adapters."), id);
                Assert.That(id, Does.EndWith(".android"), id);
            }
        }

        [Test]
        public void AllowedAdapterPackageIds_EveryAllowedAdapterIsPinned()
        {
            // RequiredSetPresent indexes the pin table by allowed id; a missing pin would throw there.
            foreach (string id in AppLovinPackageInstaller.AllowedAdapterPackageIds())
                Assert.That(AppLovinPackageInstaller.Pins.ContainsKey(id), id);
        }

        [Test]
        public void AllowedAdapterPackageIds_ForbiddenNetworksAreExcluded()
        {
            List<string> allowed = AppLovinPackageInstaller.AllowedAdapterPackageIds();

            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.unityads.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.ironsource.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.fyber.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.yandex.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.mytarget.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.bidmachine.android"));
            Assert.That(allowed, Does.Not.Contain("com.applovin.mediation.adapters.googleadmanager.android"));
            Assert.That(allowed, Does.Not.Contain(HyprMxId));
            Assert.That(allowed, Does.Not.Contain(MaioId));
        }

        [Test]
        public void AllowedAdapterPackageIds_PermittedNetworksRemain()
        {
            List<string> allowed = AppLovinPackageInstaller.AllowedAdapterPackageIds();

            Assert.That(allowed, Does.Contain("com.applovin.mediation.adapters.google.android"));
            Assert.That(allowed, Does.Contain(FacebookId));
            Assert.That(allowed, Does.Contain("com.applovin.mediation.adapters.mintegral.android"));
        }

        [Test]
        public void BlockedNetworkNames_ListsBlockedNetworksWithoutDuplicates()
        {
            List<string> blocked = AppLovinPackageInstaller.BlockedNetworkNames();

            Assert.That(blocked, Is.Not.Empty);
            Assert.That(blocked, Is.Unique);
            Assert.That(blocked, Does.Contain("Yandex Ads"));
            Assert.That(blocked, Does.Contain("BidMachine"));
            Assert.That(blocked, Does.Contain("Google Ad Manager"));
        }

        [Test]
        public void BlockedNetworkNames_AreKnownForbiddenNetworks()
        {
            var known = ForbiddenAdNetworks.All.Select(network => network.DisplayName).ToList();

            foreach (string name in AppLovinPackageInstaller.BlockedNetworkNames())
                Assert.That(known, Does.Contain(name), name);
        }
    }
}
