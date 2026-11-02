using System;
using System.Linq;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="ForbiddenAdNetworks"/>: the single stop-list behind the build guard, Gradle exclusions
    /// and the AppLovin installer. Covers group/artifact/UPM/folder matching (including the shared
    /// com.yandex.android group that must not catch AppMetrica) and basic data-table invariants.
    /// </summary>
    public class ForbiddenAdNetworksTests
    {
        // MatchByGroup

        [TestCase(null)]
        [TestCase("")]
        public void MatchByGroup_NullOrEmpty_ReturnsNull(string value)
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByGroup(value));
        }

        [Test]
        public void MatchByGroup_WholeForbiddenGroupCoordinate_ReturnsNetwork()
        {
            var network = ForbiddenAdNetworks.MatchByGroup("com.tapjoy:tapjoy-android-sdk:13.0.0");

            Assert.IsNotNull(network);
            Assert.AreEqual("Tapjoy", network.DisplayName);
        }

        [Test]
        public void MatchByGroup_MavenGroupMatchIsCaseInsensitive()
        {
            var network = ForbiddenAdNetworks.MatchByGroup("COM.TAPJOY:Sdk:1");

            Assert.IsNotNull(network);
            Assert.AreEqual("Tapjoy", network.DisplayName);
        }

        [Test]
        public void MatchByGroup_ArtifactInSharedGroup_RequiresGroupAndModule()
        {
            var adapter = ForbiddenAdNetworks.MatchByGroup("com.applovin.mediation:tapjoy-adapter:1.0");
            var allowedAdapter = ForbiddenAdNetworks.MatchByGroup("com.applovin.mediation:facebook-adapter:1.0");

            Assert.IsNotNull(adapter);
            Assert.AreEqual("Tapjoy", adapter.DisplayName);
            Assert.IsNull(allowedAdapter);
        }

        // The shared com.yandex.android group also holds AppMetrica, which stays in the SDK.
        [Test]
        public void MatchByGroup_YandexAdsArtifact_IsForbiddenButAppMetricaIsNot()
        {
            var ads = ForbiddenAdNetworks.MatchByGroup("com.yandex.android:mobileads:7.0.0");
            var metrica = ForbiddenAdNetworks.MatchByGroup("com.yandex.android:mobmetricalib:5.0.0");

            Assert.IsNotNull(ads);
            Assert.AreEqual("Yandex Ads", ads.DisplayName);
            Assert.IsNull(metrica);
        }

        [Test]
        public void MatchByGroup_UpmPackageId_MatchesByToken()
        {
            var network = ForbiddenAdNetworks.MatchByGroup("com.applovin.mediation.adapters.tapjoy.android");

            Assert.IsNotNull(network);
            Assert.AreEqual("Tapjoy", network.DisplayName);
        }

        [Test]
        public void MatchByGroup_UpmTokenIsCaseInsensitive()
        {
            Assert.IsNotNull(ForbiddenAdNetworks.MatchByGroup("COM.APPLOVIN.MEDIATION.ADAPTERS.TAPJOY.ANDROID"));
        }

        [Test]
        public void MatchByGroup_UpmGoogleAdapterIsAllowedButAdManagerIsForbidden()
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByGroup("com.applovin.mediation.adapters.google.android"));

            var adManager = ForbiddenAdNetworks.MatchByGroup("com.applovin.mediation.adapters.googleadmanager.android");

            Assert.IsNotNull(adManager);
            Assert.AreEqual("Google Ad Manager", adManager.DisplayName);
        }

        [Test]
        public void MatchByGroup_AarFileNameWithGroupIdentity_MatchesNetwork()
        {
            // Non-coordinate strings (paths, file names) are matched by identity of the group name.
            var network = ForbiddenAdNetworks.MatchByGroup("Assets/Plugins/Android/com.flurry.android-analytics.aar");

            Assert.IsNotNull(network);
            Assert.AreEqual("Flurry", network.DisplayName);
        }

        [Test]
        public void MatchByGroup_GroupNameMustBeWholeIdentity_NotSubstring()
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByGroup("Assets/Plugins/Android/com.tapjoyfoo-lib.aar"));
        }

        [TestCase("com.google.firebase:firebase-analytics:21.0.0")]
        [TestCase("com.google.android.gms:play-services-ads-identifier:18.0.1")]
        [TestCase("io.appmetrica.analytics:analytics:6.0.0")]
        [TestCase("com.applovin:applovin-sdk:13.0.0")]
        public void MatchByGroup_AllowedCoordinates_ReturnNull(string coordinate)
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByGroup(coordinate));
        }

        // MatchMavenCoordinate

        [Test]
        public void MatchMavenCoordinate_ForbiddenGroup_ReturnsNetworkForAnyModule()
        {
            var network = ForbiddenAdNetworks.MatchMavenCoordinate("com.amplitude", "anything");

            Assert.IsNotNull(network);
            Assert.AreEqual("Amplitude", network.DisplayName);
        }

        [Test]
        public void MatchMavenCoordinate_ArtifactEntry_RequiresExactModule()
        {
            Assert.IsNotNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.applovin.mediation", "mytarget-adapter"));
            Assert.IsNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.applovin.mediation", "mytarget-adapter-extra"));
            Assert.IsNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.applovin.mediation", "google-adapter"));
        }

        [Test]
        public void MatchMavenCoordinate_IsExactNotSubstring()
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.tapjoy.extra", "x"));
            Assert.IsNull(ForbiddenAdNetworks.MatchMavenCoordinate("xcom.tapjoy", "x"));
        }

        [Test]
        public void MatchMavenCoordinate_YandexAndroidGroup_OnlyMobileAdsModule()
        {
            Assert.IsNotNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.yandex.android", "mobileads"));
            Assert.IsNull(ForbiddenAdNetworks.MatchMavenCoordinate("com.yandex.android", "mobmetricalib"));
        }

        // IsOptionalArtifact

        [Test]
        public void IsOptionalArtifact_OnlyFyberAdRevenueModule()
        {
            Assert.IsTrue(ForbiddenAdNetworks.IsOptionalArtifact("io.appmetrica.analytics", "analytics-ad-revenue-fyber-v3"));
            Assert.IsFalse(ForbiddenAdNetworks.IsOptionalArtifact("io.appmetrica.analytics", "analytics"));
            Assert.IsFalse(ForbiddenAdNetworks.IsOptionalArtifact("com.other", "analytics-ad-revenue-fyber-v3"));
            Assert.IsFalse(ForbiddenAdNetworks.IsOptionalArtifact(null, null));
        }

        [Test]
        public void IsOptionalArtifact_ArtifactIsAlsoInForbiddenList()
        {
            // The optional module is excluded late in Gradle, but it must still be recognised as forbidden.
            Assert.IsNotNull(ForbiddenAdNetworks.MatchMavenCoordinate("io.appmetrica.analytics", "analytics-ad-revenue-fyber-v3"));
        }

        // MatchByAdapterFolder

        [TestCase(null)]
        [TestCase("")]
        public void MatchByAdapterFolder_NullOrEmpty_ReturnsNull(string folder)
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByAdapterFolder(folder));
        }

        [Test]
        public void MatchByAdapterFolder_KnownFolder_ReturnsNetwork()
        {
            var network = ForbiddenAdNetworks.MatchByAdapterFolder("UnityAds");

            Assert.IsNotNull(network);
            Assert.IsTrue(network.IsCarrier);
        }

        [Test]
        public void MatchByAdapterFolder_IsCaseInsensitive()
        {
            var network = ForbiddenAdNetworks.MatchByAdapterFolder("dtexchange");

            Assert.IsNotNull(network);
            Assert.AreEqual("Inneractive / Fyber / DT Exchange", network.DisplayName);
        }

        [Test]
        public void MatchByAdapterFolder_MultipleFoldersOfOneNetwork_MapToSameNetwork()
        {
            Assert.AreSame(ForbiddenAdNetworks.MatchByAdapterFolder("VKAds"), ForbiddenAdNetworks.MatchByAdapterFolder("MyTarget"));
        }

        [Test]
        public void MatchByAdapterFolder_AllowedOrPartialNames_ReturnNull()
        {
            Assert.IsNull(ForbiddenAdNetworks.MatchByAdapterFolder("Google"));
            Assert.IsNull(ForbiddenAdNetworks.MatchByAdapterFolder("Facebook"));
            Assert.IsNull(ForbiddenAdNetworks.MatchByAdapterFolder("Tapjoy2"));
            Assert.IsNull(ForbiddenAdNetworks.MatchByAdapterFolder("Tap"));
        }

        // Data-table invariants

        [Test]
        public void All_IsNotEmptyAndEntriesAreWellFormed()
        {
            Assert.That(ForbiddenAdNetworks.All, Is.Not.Empty);

            foreach (var network in ForbiddenAdNetworks.All)
            {
                Assert.That(network.DisplayName, Is.Not.Null.And.Not.Empty);
                Assert.IsNotNull(network.MavenGroups, network.DisplayName);
                Assert.IsNotNull(network.Artifacts, network.DisplayName);
                Assert.IsNotNull(network.UpmPackageTokens, network.DisplayName);
                Assert.IsNotNull(network.AdapterFolderNames, network.DisplayName);
                Assert.IsNotNull(network.DexTypePrefixes, network.DisplayName);
            }
        }

        [Test]
        public void All_DisplayNamesAreUnique()
        {
            Assert.That(ForbiddenAdNetworks.All.Select(network => network.DisplayName).ToList(), Is.Unique);
        }

        [Test]
        public void AllMavenGroups_NonEmptyWithoutBlankOrDuplicateEntries()
        {
            var groups = ForbiddenAdNetworks.AllMavenGroups().ToList();

            Assert.That(groups, Is.Not.Empty);
            Assert.That(groups.All(group => !string.IsNullOrWhiteSpace(group)));
            Assert.AreEqual(groups.Count, groups.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Test]
        public void AllMavenGroups_EachGroupMapsBackToItsNetwork()
        {
            foreach (var network in ForbiddenAdNetworks.All)
            {
                foreach (string group in network.MavenGroups)
                    Assert.AreSame(network, ForbiddenAdNetworks.MatchMavenCoordinate(group, "any-module"), group);
            }
        }

        [Test]
        public void AllMavenGroups_DoesNotContainSharedGroups()
        {
            // These groups also hold allowed libraries (AppMetrica, MAX adapters, Google Play services).
            var groups = ForbiddenAdNetworks.AllMavenGroups().ToList();

            Assert.That(groups, Does.Not.Contain("com.yandex.android"));
            Assert.That(groups, Does.Not.Contain("com.applovin.mediation"));
            Assert.That(groups, Does.Not.Contain("com.google.android.gms"));
            Assert.That(groups, Does.Not.Contain("io.appmetrica.analytics"));
        }

        [Test]
        public void AllArtifacts_NonEmptyWithCompleteUniquePairs()
        {
            var artifacts = ForbiddenAdNetworks.AllArtifacts().ToList();

            Assert.That(artifacts, Is.Not.Empty);
            foreach (var artifact in artifacts)
            {
                Assert.That(artifact.Group, Is.Not.Null.And.Not.Empty);
                Assert.That(artifact.Module, Is.Not.Null.And.Not.Empty);
            }

            var pairs = artifacts.Select(artifact => artifact.Group.ToLowerInvariant() + ":" + artifact.Module.ToLowerInvariant()).ToList();
            Assert.That(pairs, Is.Unique);
        }

        [Test]
        public void AllArtifacts_EachArtifactMapsToANetwork()
        {
            foreach (var artifact in ForbiddenAdNetworks.AllArtifacts())
                Assert.IsNotNull(ForbiddenAdNetworks.MatchMavenCoordinate(artifact.Group, artifact.Module), artifact.Group + ":" + artifact.Module);
        }

        [Test]
        public void AdapterFolderNames_AreUniqueAcrossNetworks()
        {
            var folders = ForbiddenAdNetworks.All
                .SelectMany(network => network.AdapterFolderNames)
                .Select(name => name.ToLowerInvariant())
                .ToList();

            Assert.That(folders, Is.Unique);
        }

        [Test]
        public void UpmPackageTokens_EachTokenMapsBackToItsNetwork()
        {
            foreach (var network in ForbiddenAdNetworks.All)
            {
                foreach (string token in network.UpmPackageTokens)
                    Assert.AreSame(network, ForbiddenAdNetworks.MatchByGroup("com.applovin." + token + "android"), token);
            }
        }
    }
}
