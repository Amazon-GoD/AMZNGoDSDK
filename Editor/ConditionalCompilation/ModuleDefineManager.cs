using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_2023_1_OR_NEWER
using UnityEditor.Build;
#endif

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Управляет scripting define symbols для условной компиляции модулей SDK
    /// </summary>
    [InitializeOnLoad]
    public static class ModuleDefineManager
    {
        public const string ADJUST_DEFINE = "AMZN_ADJUST_ENABLED";
        public const string APPMETRICA_DEFINE = "AMZN_APPMETRICA_ENABLED";
        public const string CROSSPROMO_DEFINE = "AMZN_CROSSPROMO_ENABLED";
        public const string IAP_DEFINE = "AMZN_IAP_ENABLED";
        public const string FIREBASE_DEFINE = "AMZN_FIREBASE_ENABLED";
        public const string INTERNETCONNECTION_DEFINE = "AMZN_INTERNETCONNECTION_ENABLED";
        public const string DEBUGCONSOLE_DEFINE = "AMZN_DEBUGCONSOLE_ENABLED";
        public const string ANALYTICS_DEFINE = "AMZN_ANALYTICS_ENABLED";
        public const string APPLOVIN_DEFINE = "AMZN_APPLOVIN_ENABLED";
        public const string SDK_ENABLED_DEFINE = "AMZN_SDK_ENABLED";

        private const string ConfigFileName = "amzn_god_sdk.json";
        private const string ResourcesPath = "Assets/Resources/";

        private static readonly string[] ALL_MODULE_DEFINES =
        {
            ADJUST_DEFINE,
            APPMETRICA_DEFINE,
            CROSSPROMO_DEFINE,
            IAP_DEFINE,
            FIREBASE_DEFINE,
            INTERNETCONNECTION_DEFINE,
            DEBUGCONSOLE_DEFINE,
            ANALYTICS_DEFINE,
            APPLOVIN_DEFINE,
            SDK_ENABLED_DEFINE
        };

        static ModuleDefineManager()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            if (!ConfigFileExists())
            {
                RemoveAllSdkDefines();
                Debug.Log("[AMZN GoD SDK] First import detected — all modules disabled. Use AMZN GoD > Setup Wizard to configure.");
                FirstImportSetupWizard.ShowIfNeeded();
                return;
            }

            UpdateDefineSymbolsFromSettings();
        }

        public static bool ConfigFileExists()
        {
            string fullPath = Path.Combine(ResourcesPath, ConfigFileName);
            return File.Exists(fullPath);
        }

        /// <summary>
        /// Обновляет define symbols на основе настроек SDK
        /// </summary>
        public static void UpdateDefineSymbolsFromSettings()
        {
            var settings = SdkSettingsManager.LoadSettings();
            UpdateDefineSymbols(settings);
        }

        /// <summary>
        /// Обновляет define symbols для всех build target groups
        /// </summary>
        public static void UpdateDefineSymbols(SdkSettingsData settings)
        {
            // Обновляем для всех платформ
            var buildTargetGroups = new[]
            {
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.Standalone
            };

            foreach (var targetGroup in buildTargetGroups)
            {
                UpdateDefineSymbolsForTarget(targetGroup, settings);
            }

            Debug.Log("[AMZN GoD SDK] Module define symbols updated successfully");
        }

        private static string GetDefines(BuildTargetGroup targetGroup)
        {
#if UNITY_2023_1_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
#endif
        }

        private static void SetDefines(BuildTargetGroup targetGroup, string defines)
        {
#if UNITY_2023_1_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
#endif
        }

        private static void UpdateDefineSymbolsForTarget(BuildTargetGroup targetGroup, SdkSettingsData settings)
        {
            var currentDefines = GetDefines(targetGroup);
            var definesList = currentDefines.Split(';').ToList();

            definesList.RemoveAll(d => ALL_MODULE_DEFINES.Contains(d));

            if (settings.Enabled)
            {
                definesList.Add(SDK_ENABLED_DEFINE);

                TryAddModuleDefine(definesList, ADJUST_DEFINE, settings.Adjust.Enabled);
                TryAddModuleDefine(definesList, APPMETRICA_DEFINE, settings.AppMetrica.Enabled);
                TryAddModuleDefine(definesList, CROSSPROMO_DEFINE, settings.CrossPromo.Enabled);
                TryAddModuleDefine(definesList, IAP_DEFINE, settings.InAppPurchase.Enabled);
                TryAddModuleDefine(definesList, FIREBASE_DEFINE, settings.Firebase.Enabled);
                TryAddModuleDefine(definesList, INTERNETCONNECTION_DEFINE, settings.InternetConnection.Enabled);
                TryAddModuleDefine(definesList, DEBUGCONSOLE_DEFINE, settings.DebugConsole.Enabled);
                TryAddModuleDefine(definesList, ANALYTICS_DEFINE, settings.Analytics.Enabled);
                TryAddModuleDefine(definesList, APPLOVIN_DEFINE, settings.AppLovin.Enabled);
            }

            var newDefines = string.Join(";", definesList.Where(d => !string.IsNullOrEmpty(d)).Distinct());
            SetDefines(targetGroup, newDefines);
        }

        private static void TryAddModuleDefine(List<string> definesList, string define, bool enabledInSettings)
        {
            if (!enabledInSettings)
                return;

            if (!DependencyDetector.AreDependenciesPresent(define))
            {
                Debug.LogWarning($"[AMZN GoD SDK] Module {define} is enabled but dependencies are missing — skipping define to prevent compilation errors.");
                return;
            }

            definesList.Add(define);
        }

        /// <summary>
        /// Получает список активных define symbols для текущей платформы
        /// </summary>
        public static List<string> GetActiveModuleDefines()
        {
            var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var currentDefines = GetDefines(targetGroup);
            var definesList = currentDefines.Split(';').ToList();

            return definesList.Where(d => ALL_MODULE_DEFINES.Contains(d)).ToList();
        }

        /// <summary>
        /// Проверяет, включен ли определенный модуль
        /// </summary>
        public static bool IsModuleEnabled(string moduleDefine)
        {
            var activeDefines = GetActiveModuleDefines();
            return activeDefines.Contains(moduleDefine);
        }

        /// <summary>
        /// Удаляет все AMZN_ define symbols из проекта
        /// </summary>
        public static void RemoveAllSdkDefines()
        {
            var buildTargetGroups = new[]
            {
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.Standalone
            };

            foreach (var targetGroup in buildTargetGroups)
            {
                var currentDefines = GetDefines(targetGroup);
                var definesList = currentDefines.Split(';').ToList();
                definesList.RemoveAll(d => ALL_MODULE_DEFINES.Contains(d));
                var newDefines = string.Join(";", definesList.Where(d => !string.IsNullOrEmpty(d)).Distinct());
                SetDefines(targetGroup, newDefines);
            }
        }
    }
}
