using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Module registries used by build guards and cleaners: <see cref="ModuleManifestRegistry"/>
    /// (enabled map, disabled footprints, permissions, component matching),
    /// <see cref="ModuleBuildArtifactRegistry"/> (module toggles and asset ownership) and
    /// <see cref="NativePluginRegistry.IsUnderFolders"/>. All methods take the Editor SdkSettingsData mirror.
    /// </summary>
    public class ModuleRegistriesTests
    {
        // Module name (as used by the registries) -> action that switches that module on.
        private static readonly Dictionary<string, Action<SdkSettingsData>> ModuleSwitches =
            new Dictionary<string, Action<SdkSettingsData>>
            {
                { "Cross-Promo", settings => settings.CrossPromo.Enabled = true },
                { "Adjust", settings => settings.Adjust.Enabled = true },
                { "AppMetrica", settings => settings.AppMetrica.Enabled = true },
                { "Firebase", settings => settings.Firebase.Enabled = true },
                { "InAppPurchase", settings => settings.InAppPurchase.Enabled = true },
                { "InternetConnection", settings => settings.InternetConnection.Enabled = true },
                { "InGameDebugConsole", settings => settings.DebugConsole.Enabled = true },
                { "Analytics", settings => settings.Analytics.Enabled = true },
                { "AppLovin", settings => settings.AppLovin.Enabled = true },
            };

        private static SdkSettingsData AllEnabled()
        {
            var settings = new SdkSettingsData { Enabled = true };
            foreach (var enable in ModuleSwitches.Values)
                enable(settings);
            return settings;
        }

        private static SdkSettingsData OnlyEnabled(string moduleName)
        {
            var settings = new SdkSettingsData { Enabled = true };
            ModuleSwitches[moduleName](settings);
            return settings;
        }

        private static ModuleBuildArtifactSpec Spec(string name)
        {
            return ModuleBuildArtifactRegistry.All.First(spec => spec.Name == name);
        }

        // ModuleManifestRegistry.GetModuleEnabledMap

        [Test]
        public void GetModuleEnabledMap_NullSettings_AllModulesDisabled()
        {
            var map = ModuleManifestRegistry.GetModuleEnabledMap(null);

            Assert.That(map, Is.Not.Empty);
            Assert.That(map.Values.All(enabled => !enabled));
        }

        [Test]
        public void GetModuleEnabledMap_SdkDisabled_AllModulesDisabledEvenIfToggledOn()
        {
            var settings = AllEnabled();
            settings.Enabled = false;

            var map = ModuleManifestRegistry.GetModuleEnabledMap(settings);

            Assert.That(map.Values.All(enabled => !enabled));
        }

        [Test]
        public void GetModuleEnabledMap_AllToggledOn_AllModulesEnabled()
        {
            var map = ModuleManifestRegistry.GetModuleEnabledMap(AllEnabled());

            Assert.That(map.Keys, Is.EquivalentTo(ModuleSwitches.Keys));
            Assert.That(map.Values.All(enabled => enabled));
        }

        [Test]
        public void GetModuleEnabledMap_EachModuleMapsToItsOwnSettingsFlag()
        {
            foreach (string name in ModuleSwitches.Keys)
            {
                var map = ModuleManifestRegistry.GetModuleEnabledMap(OnlyEnabled(name));

                Assert.IsTrue(map[name], name);
                foreach (var entry in map.Where(item => item.Key != name))
                    Assert.IsFalse(entry.Value, name + " must not enable " + entry.Key);
            }
        }

        [Test]
        public void GetModuleEnabledMap_NullModuleSettings_TreatedAsDisabled()
        {
            var settings = AllEnabled();
            settings.CrossPromo = null;
            settings.Adjust = null;

            var map = ModuleManifestRegistry.GetModuleEnabledMap(settings);

            Assert.IsFalse(map["Cross-Promo"]);
            Assert.IsFalse(map["Adjust"]);
            Assert.IsTrue(map["Firebase"]);
        }

        [Test]
        public void GetModuleEnabledMap_KeysAreCaseInsensitive()
        {
            var map = ModuleManifestRegistry.GetModuleEnabledMap(AllEnabled());

            Assert.IsTrue(map["cross-promo"]);
            Assert.IsTrue(map["APPLOVIN"]);
        }

        // ModuleManifestRegistry.GetDisabledFootprints

        [Test]
        public void GetDisabledFootprints_SdkDisabled_ReturnsEveryOwnedFootprint()
        {
            var settings = AllEnabled();
            settings.Enabled = false;

            var disabled = ModuleManifestRegistry.GetDisabledFootprints(settings);

            Assert.That(disabled, Is.EquivalentTo(ModuleManifestRegistry.Footprints.Where(footprint => footprint.SdkOwned)));
            Assert.That(disabled, Is.Not.Empty);
        }

        [Test]
        public void GetDisabledFootprints_NullSettings_ReturnsEveryOwnedFootprint()
        {
            var disabled = ModuleManifestRegistry.GetDisabledFootprints(null);

            Assert.That(disabled, Is.EquivalentTo(ModuleManifestRegistry.Footprints.Where(footprint => footprint.SdkOwned)));
        }

        [Test]
        public void GetDisabledFootprints_ModuleToggledOff_ItsFootprintIsReported()
        {
            var settings = AllEnabled();
            settings.CrossPromo.Enabled = false;

            var disabled = ModuleManifestRegistry.GetDisabledFootprints(settings);

            Assert.That(disabled.Select(footprint => footprint.ModuleName), Does.Contain("Cross-Promo"));
        }

        [Test]
        public void GetDisabledFootprints_AllModulesEnabled_ReturnsNothing()
        {
            Assert.That(ModuleManifestRegistry.GetDisabledFootprints(AllEnabled()), Is.Empty);
        }

        [Test]
        public void GetDisabledFootprints_NeverReturnsNonOwnedVendorFootprints()
        {
            var settings = new SdkSettingsData { Enabled = false };

            var disabled = ModuleManifestRegistry.GetDisabledFootprints(settings);

            Assert.That(disabled.All(footprint => footprint.SdkOwned));
            Assert.That(disabled.Select(footprint => footprint.ModuleName), Does.Not.Contain("AppLovin"));
            Assert.That(disabled.Select(footprint => footprint.ModuleName), Does.Not.Contain("Firebase"));
        }

        [Test]
        public void Footprints_EveryModuleNameIsKnownToTheEnabledMap()
        {
            var map = ModuleManifestRegistry.GetModuleEnabledMap(null);

            foreach (var footprint in ModuleManifestRegistry.Footprints)
                Assert.That(map.ContainsKey(footprint.ModuleName), footprint.ModuleName);
        }

        // ModuleManifestRegistry.GetEnabledPermissions

        [Test]
        public void GetEnabledPermissions_SdkDisabled_ReturnsEmptySet()
        {
            var settings = AllEnabled();
            settings.Enabled = false;

            Assert.That(ModuleManifestRegistry.GetEnabledPermissions(settings), Is.Empty);
            Assert.That(ModuleManifestRegistry.GetEnabledPermissions(null), Is.Empty);
        }

        [Test]
        public void GetEnabledPermissions_AllEnabled_EqualsUnionOfFootprintPermissions()
        {
            var expected = new HashSet<string>(
                ModuleManifestRegistry.Footprints.SelectMany(footprint => footprint.Permissions),
                StringComparer.OrdinalIgnoreCase);

            var actual = ModuleManifestRegistry.GetEnabledPermissions(AllEnabled());

            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void GetEnabledPermissions_ResultIsCaseInsensitive()
        {
            var actual = ModuleManifestRegistry.GetEnabledPermissions(AllEnabled());

            foreach (string permission in ModuleManifestRegistry.Footprints.SelectMany(footprint => footprint.Permissions))
                Assert.IsTrue(actual.Contains(permission.ToUpperInvariant()), permission);
        }

        [Test]
        public void SharedPermissions_ContainInternetAndAreCaseInsensitive()
        {
            Assert.IsTrue(ModuleManifestRegistry.SharedPermissions.Contains("android.permission.INTERNET"));
            Assert.IsTrue(ModuleManifestRegistry.SharedPermissions.Contains("ANDROID.PERMISSION.internet"));
        }

        // ModuleManifestRegistry.MatchesComponent

        [Test]
        public void MatchesComponent_ByPrefix_ReturnsTrue()
        {
            var footprint = new ModuleManifestFootprint { ModuleName = "Test", ComponentNamePrefixes = new[] { "com.example.sdk." } };

            Assert.IsTrue(ModuleManifestRegistry.MatchesComponent(footprint, "com.example.sdk.MyService"));
        }

        [Test]
        public void MatchesComponent_ByExactName_ReturnsTrue()
        {
            var footprint = new ModuleManifestFootprint { ModuleName = "Test", ComponentNames = new[] { "com.example.Exact" } };

            Assert.IsTrue(ModuleManifestRegistry.MatchesComponent(footprint, "com.example.Exact"));
            Assert.IsFalse(ModuleManifestRegistry.MatchesComponent(footprint, "com.example.ExactOther"));
        }

        [Test]
        public void MatchesComponent_IsCaseSensitive()
        {
            var footprint = new ModuleManifestFootprint
            {
                ModuleName = "Test",
                ComponentNames = new[] { "com.example.Exact" },
                ComponentNamePrefixes = new[] { "com.example.sdk." },
            };

            Assert.IsFalse(ModuleManifestRegistry.MatchesComponent(footprint, "com.example.exact"));
            Assert.IsFalse(ModuleManifestRegistry.MatchesComponent(footprint, "COM.EXAMPLE.SDK.Service"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("org.other.Service")]
        public void MatchesComponent_NullEmptyOrUnrelatedName_ReturnsFalse(string androidName)
        {
            var footprint = new ModuleManifestFootprint
            {
                ModuleName = "Test",
                ComponentNames = new[] { "com.example.Exact" },
                ComponentNamePrefixes = new[] { "com.example.sdk." },
            };

            Assert.IsFalse(ModuleManifestRegistry.MatchesComponent(footprint, androidName));
        }

        [Test]
        public void MatchesComponent_RealCrossPromoFootprint_MatchesOwnComponentsOnly()
        {
            var footprint = ModuleManifestRegistry.Footprints.First(item => item.ModuleName == "Cross-Promo");

            Assert.IsTrue(footprint.SdkOwned);
            Assert.IsTrue(ModuleManifestRegistry.MatchesComponent(footprint, "com.amzngod.exoplayer.SomeService"));
            Assert.IsFalse(ModuleManifestRegistry.MatchesComponent(footprint, "com.google.android.exoplayer2.Service"));
        }

        // ModuleBuildArtifactRegistry.All

        [Test]
        public void ArtifactRegistry_NamesAreUniqueAndMatchTheEnabledMap()
        {
            var names = ModuleBuildArtifactRegistry.All.Select(spec => spec.Name).ToList();

            Assert.That(names, Is.Unique);
            Assert.That(names, Is.EquivalentTo(ModuleManifestRegistry.GetModuleEnabledMap(null).Keys));
        }

        [Test]
        public void ArtifactRegistry_SpecsAreWellFormed()
        {
            foreach (var spec in ModuleBuildArtifactRegistry.All)
            {
                Assert.That(spec.Define, Is.Not.Null.And.Not.Empty, spec.Name);
                Assert.That(spec.RelativeModuleFolder, Does.StartWith("Runtime/Modules/"), spec.Name);
                Assert.That(spec.RelativeModuleFolder, Does.Not.EndWith("/"), spec.Name);
                Assert.That(spec.ManagedAssemblies, Is.Not.Empty, spec.Name);
                Assert.IsNotNull(spec.ExternalAssetPrefixes, spec.Name);
                Assert.IsNotNull(spec.AndroidOwnedJavaTypes, spec.Name);
            }

            Assert.That(ModuleBuildArtifactRegistry.All.Select(spec => spec.Define).ToList(), Is.Unique);
            Assert.That(ModuleBuildArtifactRegistry.All.Select(spec => spec.RelativeModuleFolder).ToList(), Is.Unique);
        }

        // ModuleBuildArtifactRegistry.IsEnabled

        [Test]
        public void IsEnabled_NullSettings_ReturnsFalseForEverySpec()
        {
            foreach (var spec in ModuleBuildArtifactRegistry.All)
                Assert.IsFalse(ModuleBuildArtifactRegistry.IsEnabled(spec, null), spec.Name);
        }

        [Test]
        public void IsEnabled_SdkDisabled_ReturnsFalseForEverySpec()
        {
            var settings = AllEnabled();
            settings.Enabled = false;

            foreach (var spec in ModuleBuildArtifactRegistry.All)
                Assert.IsFalse(ModuleBuildArtifactRegistry.IsEnabled(spec, settings), spec.Name);
        }

        [Test]
        public void IsEnabled_EachSpecFollowsOnlyItsOwnSettingsFlag()
        {
            foreach (var spec in ModuleBuildArtifactRegistry.All)
            {
                Assert.That(ModuleSwitches.ContainsKey(spec.Name), spec.Name);
                var settings = OnlyEnabled(spec.Name);

                foreach (var other in ModuleBuildArtifactRegistry.All)
                {
                    bool expected = other.Name == spec.Name;
                    Assert.AreEqual(expected, ModuleBuildArtifactRegistry.IsEnabled(other, settings),
                        "enabled " + spec.Name + ", checked " + other.Name);
                }
            }
        }

        [Test]
        public void IsEnabled_ModuleToggledOff_ReportsDisabledWhileOthersStayEnabled()
        {
            var settings = AllEnabled();
            settings.Firebase.Enabled = false;

            Assert.IsFalse(ModuleBuildArtifactRegistry.IsEnabled(Spec("Firebase"), settings));
            Assert.IsTrue(ModuleBuildArtifactRegistry.IsEnabled(Spec("Adjust"), settings));
        }

        [Test]
        public void IsEnabled_UnknownSpecName_ReturnsFalse()
        {
            var unknown = new ModuleBuildArtifactSpec { Name = "NoSuchModule" };

            Assert.IsFalse(ModuleBuildArtifactRegistry.IsEnabled(unknown, AllEnabled()));
        }

        // ModuleBuildArtifactRegistry.OwnsSdkAssetPath / OwnsAssetPath

        [TestCase("Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Scripts/File.cs")]
        [TestCase("Packages/com.amzngod.amzngodsdk/Runtime/Modules/Adjust/Scripts/File.cs")]
        [TestCase("assets/amzngodsdk/runtime/modules/adjust/file.cs")]
        public void OwnsSdkAssetPath_PathInsideModuleFolder_ReturnsTrue(string path)
        {
            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsSdkAssetPath(Spec("Adjust"), path));
        }

        [TestCase("Assets/AMZNGoDSDK/Runtime/Modules/AdjustV2/File.cs")]
        [TestCase("Assets/AMZNGoDSDK/Runtime/Modules/Adjust")]
        [TestCase("Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica/File.cs")]
        [TestCase("Assets/Other/Runtime/Modules/Adjust/File.cs")]
        [TestCase("Runtime/Modules/Adjust/File.cs")]
        [TestCase("")]
        [TestCase(null)]
        public void OwnsSdkAssetPath_OtherPaths_ReturnsFalse(string path)
        {
            Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsSdkAssetPath(Spec("Adjust"), path));
        }

        [Test]
        public void OwnsAssetPath_SdkFolderOrExternalPrefix_ReturnsTrue()
        {
            var firebase = Spec("Firebase");

            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsAssetPath(firebase, "Assets/AMZNGoDSDK/Runtime/Modules/Firebase/FirebaseModule.cs"));
            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsAssetPath(firebase, "Assets/Firebase/Plugins/Firebase.App.dll"));
            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsAssetPath(firebase, "Packages/com.google.firebase.app/package.json"));
        }

        [Test]
        public void OwnsAssetPath_ExternalPrefixIsNotSdkOwned()
        {
            var appLovin = Spec("AppLovin");

            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsAssetPath(appLovin, "Assets/MaxSdk/Mediation/Google/Plugin.aar"));
            Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsSdkAssetPath(appLovin, "Assets/MaxSdk/Mediation/Google/Plugin.aar"));
        }

        [Test]
        public void OwnsAssetPath_PathOfAnotherModule_ReturnsFalse()
        {
            Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsAssetPath(Spec("Adjust"), "Assets/Firebase/Plugins/Firebase.App.dll"));
            Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsAssetPath(Spec("Adjust"), "Assets/MaxSdk/Scripts/MaxSdk.cs"));
        }

        [Test]
        public void OwnsAssetPath_NullOrEmpty_ReturnsFalse()
        {
            foreach (var spec in ModuleBuildArtifactRegistry.All)
            {
                Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsAssetPath(spec, null), spec.Name);
                Assert.IsFalse(ModuleBuildArtifactRegistry.OwnsAssetPath(spec, ""), spec.Name);
            }
        }

        [Test]
        public void OwnsAssetPath_ExternalPrefixMatchIsCaseInsensitive()
        {
            Assert.IsTrue(ModuleBuildArtifactRegistry.OwnsAssetPath(Spec("AppLovin"), "assets/maxsdk/Scripts/MaxSdk.cs"));
        }

        // NativePluginRegistry.IsUnderFolders

        // Regression 1.0.0: folder "Adjust" must not match the sibling "AdjustV2".
        [Test]
        public void IsUnderFolders_SiblingWithSamePrefix_DoesNotMatch()
        {
            var folders = new[] { "Runtime/Modules/Adjust" };

            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Assets/AMZNGoDSDK/Runtime/Modules/AdjustV2/Plugin.jar", folders));
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Packages/com.amzngod.amzngodsdk/Runtime/Modules/AdjustV2/Plugin.jar", folders));
        }

        [TestCase("Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Native/iOS/File.mm")]
        [TestCase("Packages/com.amzngod.amzngodsdk/Runtime/Modules/Adjust/Native/iOS/File.mm")]
        public void IsUnderFolders_FileInsideFolderInEitherSdkRoot_Matches(string path)
        {
            Assert.IsTrue(NativePluginRegistry.IsUnderFolders(path, new[] { "Runtime/Modules/Adjust" }));
        }

        [Test]
        public void IsUnderFolders_FolderItselfWithoutTrailingContent_DoesNotMatch()
        {
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Assets/AMZNGoDSDK/Runtime/Modules/Adjust", new[] { "Runtime/Modules/Adjust" }));
        }

        [Test]
        public void IsUnderFolders_AnyOfSeveralFolders_Matches()
        {
            var folders = new[] { "Runtime/Modules/Adjust", "Runtime/Modules/AppMetrica" };

            Assert.IsTrue(NativePluginRegistry.IsUnderFolders("Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica/Plugins/File.java", folders));
        }

        [Test]
        public void IsUnderFolders_IsCaseSensitive()
        {
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Assets/AMZNGoDSDK/Runtime/Modules/adjust/File.mm", new[] { "Runtime/Modules/Adjust" }));
        }

        [Test]
        public void IsUnderFolders_NullEmptyPathOrNoFolders_DoesNotMatch()
        {
            var folders = new[] { "Runtime/Modules/Adjust" };

            Assert.IsFalse(NativePluginRegistry.IsUnderFolders(null, folders));
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("", folders));
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Assets/AMZNGoDSDK/Runtime/Modules/Adjust/File.mm", new string[0]));
        }

        [Test]
        public void IsUnderFolders_PathOutsideSdkRoots_DoesNotMatch()
        {
            Assert.IsFalse(NativePluginRegistry.IsUnderFolders("Assets/Other/Runtime/Modules/Adjust/File.mm", new[] { "Runtime/Modules/Adjust" }));
        }

        [Test]
        public void ModuleNativeFolders_AreOwnedByTheMatchingArtifactSpec()
        {
            // Each registered native folder must be the module folder of some artifact spec.
            foreach (var entry in NativePluginRegistry.ModuleNativeFolders)
            {
                foreach (string folder in entry.Value)
                {
                    bool ownedBySpec = ModuleBuildArtifactRegistry.All.Any(spec =>
                        spec.Define == entry.Key && spec.RelativeModuleFolder == folder);
                    Assert.IsTrue(ownedBySpec, entry.Key + " -> " + folder);
                }
            }
        }
    }
}
