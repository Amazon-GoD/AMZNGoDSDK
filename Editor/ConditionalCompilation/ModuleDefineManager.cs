using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Управляет scripting define symbols для условной компиляции модулей SDK
    /// </summary>
    [InitializeOnLoad]
    public static class ModuleDefineManager
    {
        // Define symbols для каждого модуля
        public const string ADJUST_DEFINE = "AMZN_ADJUST_ENABLED";
        public const string APPMETRICA_DEFINE = "AMZN_APPMETRICA_ENABLED";
        public const string CROSSPROMO_DEFINE = "AMZN_CROSSPROMO_ENABLED";
        public const string INFATICA_DEFINE = "AMZN_INFATICA_ENABLED";
        public const string IAP_DEFINE = "AMZN_IAP_ENABLED";
        public const string FIREBASE_DEFINE = "AMZN_FIREBASE_ENABLED";
        public const string INTERNETCONNECTION_DEFINE = "AMZN_INTERNETCONNECTION_ENABLED";
        public const string SDK_ENABLED_DEFINE = "AMZN_SDK_ENABLED";

        private static readonly string[] ALL_MODULE_DEFINES = 
        {
            ADJUST_DEFINE,
            APPMETRICA_DEFINE,
            CROSSPROMO_DEFINE,
            INFATICA_DEFINE,
            IAP_DEFINE,
            FIREBASE_DEFINE,
            INTERNETCONNECTION_DEFINE,
            SDK_ENABLED_DEFINE
        };

        static ModuleDefineManager()
        {
            // Автоматически обновляем define symbols при загрузке редактора
            EditorApplication.delayCall += () => UpdateDefineSymbolsFromSettings();
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

        private static void UpdateDefineSymbolsForTarget(BuildTargetGroup targetGroup, SdkSettingsData settings)
        {
            var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            var definesList = currentDefines.Split(';').ToList();

            // Удаляем все существующие define symbols SDK
            definesList.RemoveAll(d => ALL_MODULE_DEFINES.Contains(d));

            // Добавляем define symbols для включенных модулей
            if (settings.Enabled)
            {
                definesList.Add(SDK_ENABLED_DEFINE);

                if (settings.Adjust.Enabled)
                    definesList.Add(ADJUST_DEFINE);

                if (settings.AppMetrica.Enabled)
                    definesList.Add(APPMETRICA_DEFINE);

                if (settings.CrossPromo.Enabled)
                    definesList.Add(CROSSPROMO_DEFINE);

                if (settings.Infatica.Enabled)
                    definesList.Add(INFATICA_DEFINE);

                if (settings.InAppPurchase.Enabled)
                    definesList.Add(IAP_DEFINE);

                if (settings.Firebase.Enabled)
                    definesList.Add(FIREBASE_DEFINE);

                if (settings.InternetConnection.Enabled)
                    definesList.Add(INTERNETCONNECTION_DEFINE);
            }

            // Убираем пустые элементы и обновляем
            var newDefines = string.Join(";", definesList.Where(d => !string.IsNullOrEmpty(d)).Distinct());
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, newDefines);
        }

        /// <summary>
        /// Получает список активных define symbols для текущей платформы
        /// </summary>
        public static List<string> GetActiveModuleDefines()
        {
            var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
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
    }
}
