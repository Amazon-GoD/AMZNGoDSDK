using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Единый реестр артефактов, которые принадлежат опциональным модулям SDK.
    /// Успешная сборка с выключенным модулем не должна содержать ни один из них.
    /// </summary>
    public sealed class ModuleBuildArtifactSpec
    {
        public string Name;
        public string Define;
        public string RelativeModuleFolder;
        public string[] ExternalAssetPrefixes = Array.Empty<string>();
        public string[] ManagedAssemblies = Array.Empty<string>();
        public string[] AndroidTextFingerprints = Array.Empty<string>();
        public string[] AndroidFileFingerprints = Array.Empty<string>();
    }

    public static class ModuleBuildArtifactRegistry
    {
        public static readonly IReadOnlyList<ModuleBuildArtifactSpec> All =
            new List<ModuleBuildArtifactSpec>
            {
                new ModuleBuildArtifactSpec
                {
                    Name = "Adjust",
                    Define = ModuleDefineManager.ADJUST_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/Adjust",
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.Adjust", "AdjustSdk.Scripts" },
                    AndroidTextFingerprints = new[]
                    {
                        "com.adjust.sdk", "adjust-android", "com.android.installreferrer:installreferrer:"
                    },
                    AndroidFileFingerprints = new[] { "adjust" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "AppMetrica",
                    Define = ModuleDefineManager.APPMETRICA_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/AppMetrica",
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.AppMetrica" },
                    AndroidTextFingerprints = new[] { "io.appmetrica", "appmetrica" },
                    AndroidFileFingerprints = new[] { "appmetrica" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "Cross-Promo",
                    Define = ModuleDefineManager.CROSSPROMO_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/Cross-Promo",
                    ExternalAssetPrefixes = new[]
                    {
                        "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK/CrossPromoBanner.prefab"
                    },
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.CrossPromo", "UniWebView-CSharp" },
                    AndroidTextFingerprints = new[]
                    {
                        "com.google.android.exoplayer:exoplayer:", "UniWebView", "CrossPromoExo",
                        "/Runtime/Modules/Cross-Promo/"
                    },
                    AndroidFileFingerprints = new[] { "uniwebview", "exoplayer", "crosspromo" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "InAppPurchase",
                    Define = ModuleDefineManager.IAP_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/InAppPurchase",
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.InAppPurchase" },
                    AndroidTextFingerprints = new[]
                    {
                        "com.amazon.device.iap", "com.amazon.device.drm", "com.amazon.inapp.purchasing",
                        "AmazonIapV2", "amazon-appstore-sdk"
                    },
                    AndroidFileFingerprints = new[]
                    {
                        "amazoniap", "amazon-appstore-sdk", "amazoncptplugins", "gson-2.2.4"
                    },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "Firebase",
                    Define = ModuleDefineManager.FIREBASE_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/Firebase",
                    ExternalAssetPrefixes = new[] { "Assets/Firebase/", "Packages/com.google.firebase." },
                    ManagedAssemblies = new[]
                    {
                        "AMZNGoDSDK.Module.Firebase", "Firebase.App", "Firebase.Analytics",
                        "Firebase.Crashlytics", "Firebase.Platform"
                    },
                    AndroidTextFingerprints = new[]
                    {
                        "com.google.firebase:", "com.google.firebase.", "FirebaseCpp", "/Assets/Firebase/"
                    },
                    AndroidFileFingerprints = new[] { "firebase" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "InternetConnection",
                    Define = ModuleDefineManager.INTERNETCONNECTION_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/InternetConnection",
                    ExternalAssetPrefixes = new[]
                    {
                        "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK/OfflineBanner.prefab"
                    },
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.InternetConnection" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "InGameDebugConsole",
                    Define = ModuleDefineManager.DEBUGCONSOLE_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/InGameDebugConsole",
                    ExternalAssetPrefixes = new[]
                    {
                        "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK/IngameDebugConsole.prefab"
                    },
                    ManagedAssemblies = new[] { "IngameDebugConsole.Runtime" },
                    AndroidTextFingerprints = new[] { "IngameDebugConsole" },
                    AndroidFileFingerprints = new[] { "ingamedebugconsole" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "Analytics",
                    Define = ModuleDefineManager.ANALYTICS_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/Analytics",
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.Analytics" },
                },
                new ModuleBuildArtifactSpec
                {
                    Name = "AppLovin",
                    Define = ModuleDefineManager.APPLOVIN_DEFINE,
                    RelativeModuleFolder = "Runtime/Modules/AppLovin",
                    ExternalAssetPrefixes = new[]
                    {
                        "Assets/MaxSdk/", "Packages/com.applovin."
                    },
                    ManagedAssemblies = new[] { "AMZNGoDSDK.Module.AppLovin", "MaxSdk.Scripts" },
                    AndroidTextFingerprints = new[] { "com.applovin", "/Packages/com.applovin.", "MaxSdk" },
                    AndroidFileFingerprints = new[] { "applovin", "maxsdk" },
                },
            };

        public static bool IsEnabled(ModuleBuildArtifactSpec spec, SdkSettingsData settings)
        {
            if (settings == null || !settings.Enabled)
                return false;

            var map = ModuleManifestRegistry.GetModuleEnabledMap(settings);
            return map.TryGetValue(spec.Name, out bool enabled) && enabled;
        }

        public static bool OwnsAssetPath(ModuleBuildArtifactSpec spec, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            foreach (var root in NativePluginRegistry.SdkRootPrefixes)
            {
                if (assetPath.StartsWith(root + spec.RelativeModuleFolder + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var prefix in spec.ExternalAssetPrefixes)
            {
                if (assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
