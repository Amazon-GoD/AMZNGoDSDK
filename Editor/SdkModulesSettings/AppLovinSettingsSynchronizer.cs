using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Synchronizes MAX settings and disables Android Ad Review before vendor build hooks.</summary>
    [InitializeOnLoad]
    internal sealed class AppLovinSettingsSynchronizer : IPreprocessBuildWithReport
    {
        private const string ErrorMessage = "[AMZNGoDSDK][AppLovin] Не удалось синхронизировать SDK Key / AdMob Android App ID " +
            "или выключить MAX Ad Review (SafeDK). Проверьте установленный пакет MAX и его AppLovinSettings.";

        public int callbackOrder => -100;

        static AppLovinSettingsSynchronizer()
        {
            EditorApplication.delayCall += SynchronizeAfterReload;
        }

        private static void SynchronizeAfterReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || SessionState.GetBool(SdkPackageExporter.ExportInProgressKey, false))
            {
                EditorApplication.delayCall += SynchronizeAfterReload;
                return;
            }

            Synchronize();
        }

        internal static void Synchronize()
        {
            try
            {
                SynchronizeCore(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
            }
            catch (Exception)
            {
                // Vendor exceptions may contain the key. Never include their text in logs.
                Debug.LogWarning(ErrorMessage);
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS)
                return;

            try
            {
                SynchronizeCore(report.summary.platform == BuildTarget.Android);
            }
            catch (Exception)
            {
                throw new BuildFailedException(ErrorMessage);
            }
        }

        private static void SynchronizeCore(bool android)
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled)
                return;

            bool appLovinEnabled = settings.AppLovin != null && settings.AppLovin.Enabled;
            var sdkKey = appLovinEnabled ? settings.AppLovin.SdkKey : null;
            var adMobAndroidAppId = appLovinEnabled ? settings.AppLovin.AdMobAndroidAppId : null;
            if (!android && string.IsNullOrWhiteSpace(sdkKey) && string.IsNullOrWhiteSpace(adMobAndroidAppId)) return;

            Type settingsType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                settingsType = assembly.GetType("AppLovinSettings", false);
                if (settingsType != null) break;
            }

            // MAX is optional. Its next import/domain reload will retry synchronization.
            if (settingsType == null) return;

            var instanceProperty = settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var instance = instanceProperty?.GetValue(null) as ScriptableObject;
            if (instance == null)
                throw new InvalidOperationException();

            // SafeDK can be injected even when our MAX runtime module is disabled or
            // the publisher manages the SDK key in MAX. Do this before either early return.
            if (android)
            {
                var qualityProperty = settingsType.GetProperty("QualityServiceEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (qualityProperty == null || qualityProperty.PropertyType != typeof(bool)
                    || !qualityProperty.CanRead || !qualityProperty.CanWrite)
                    throw new InvalidOperationException();
                if ((bool)qualityProperty.GetValue(instance))
                {
                    qualityProperty.SetValue(instance, false);
                    EditorUtility.SetDirty(instance);
                    AssetDatabase.SaveAssetIfDirty(instance);
                    Debug.Log("[AMZNGoDSDK][AppLovin] MAX Ad Review выключен: SafeDK содержит ссылки на запрещённые SDK.");
                }
            }

            SynchronizeStringProperty(settingsType, instance, "SdkKey", sdkKey, "SDK Key");
            SynchronizeStringProperty(settingsType, instance, "AdMobAndroidAppId", adMobAndroidAppId, "AdMob Android App ID");
        }

        private static void SynchronizeStringProperty(Type settingsType, ScriptableObject instance,
            string propertyName, string value, string displayName)
        {
            // Empty means the user manages this value in MAX Integration Manager.
            if (string.IsNullOrWhiteSpace(value)) return;
            var property = settingsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string) || !property.CanRead || !property.CanWrite)
                throw new InvalidOperationException();
            if (string.Equals(property.GetValue(instance) as string, value, StringComparison.Ordinal)) return;

            property.SetValue(instance, value);
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssetIfDirty(instance);
            Debug.Log("[AMZNGoDSDK][AppLovin] " + displayName + " синхронизирован с MAX Integration Manager.");
        }
    }
}
