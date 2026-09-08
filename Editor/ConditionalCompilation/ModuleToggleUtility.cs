using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Утилиты для быстрого управления модулями SDK
    /// </summary>
    public static class ModuleToggleUtility
    {
        //[MenuItem("AMZN GoD/Quick Actions/Enable All Modules", false, 100)]
        public static void EnableAllModules()
        {
            if (!EditorUtility.DisplayDialog("Enable All Modules",
                "This will enable all SDK modules and update define symbols.\n\n" +
                "Unity will recompile scripts after this action.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            
            settings.Enabled = true;
            settings.Adjust.Enabled = true;
            settings.AppMetrica.Enabled = true;
            settings.CrossPromo.Enabled = true;
            settings.InAppPurchase.Enabled = true;
            settings.Firebase.Enabled = true;
            settings.InternetConnection.Enabled = true;
            settings.DebugConsole.Enabled = true;

            if (!SdkSettingsManager.SaveSettings(settings)) return;
            
            Debug.Log("[AMZN GoD SDK] All modules have been enabled");
            EditorUtility.DisplayDialog("Success", "All modules enabled successfully!", "OK");
        }

        //[MenuItem("AMZN GoD/Quick Actions/Disable All Modules", false, 101)]
        public static void DisableAllModules()
        {
            if (!EditorUtility.DisplayDialog("Disable All Modules",
                "This will disable all SDK modules and update define symbols.\n\n" +
                "Unity will recompile scripts after this action.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            
            settings.Adjust.Enabled = false;
            settings.AppMetrica.Enabled = false;
            settings.CrossPromo.Enabled = false;
            settings.InAppPurchase.Enabled = false;
            settings.Firebase.Enabled = false;
            settings.InternetConnection.Enabled = false;
            settings.DebugConsole.Enabled = false;

            if (!SdkSettingsManager.SaveSettings(settings)) return;
            
            Debug.Log("[AMZN GoD SDK] All modules have been disabled");
            EditorUtility.DisplayDialog("Success", "All modules disabled successfully!", "OK");
        }

        //[MenuItem("AMZN GoD/Quick Actions/Enable Analytics Only", false, 102)]
        public static void EnableAnalyticsOnly()
        {
            if (!EditorUtility.DisplayDialog("Enable Analytics Only",
                "This will enable only Adjust, AppMetrica, and Firebase modules.\n\n" +
                "All other modules will be disabled.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            
            settings.Enabled = true;
            settings.Adjust.Enabled = true;
            settings.AppMetrica.Enabled = true;
            settings.Firebase.Enabled = true;
            
            settings.CrossPromo.Enabled = false;
            settings.InAppPurchase.Enabled = false;
            settings.InternetConnection.Enabled = false;
            settings.DebugConsole.Enabled = false;

            if (!SdkSettingsManager.SaveSettings(settings)) return;
            
            Debug.Log("[AMZN GoD SDK] Analytics modules enabled, others disabled");
            EditorUtility.DisplayDialog("Success", "Analytics modules enabled!", "OK");
        }

        //[MenuItem("AMZN GoD/Quick Actions/Disable SDK Completely", false, 103)]
        public static void DisableSDKCompletely()
        {
            if (!EditorUtility.DisplayDialog("Disable SDK Completely",
                "This will disable the entire SDK.\n\n" +
                "All modules and the SDK core will be excluded from compilation.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            
            settings.Enabled = false;
            settings.Adjust.Enabled = false;
            settings.AppMetrica.Enabled = false;
            settings.CrossPromo.Enabled = false;
            settings.InAppPurchase.Enabled = false;
            settings.Firebase.Enabled = false;
            settings.InternetConnection.Enabled = false;
            settings.DebugConsole.Enabled = false;

            if (!SdkSettingsManager.SaveSettings(settings)) return;
            
            Debug.Log("[AMZN GoD SDK] SDK completely disabled");
            EditorUtility.DisplayDialog("Success", "SDK completely disabled!", "OK");
        }

        //[MenuItem("AMZN GoD/Quick Actions/Force Update Define Symbols", false, 150)]
        public static void ForceUpdateDefineSymbols()
        {
            if (!EditorUtility.DisplayDialog("Force Update Define Symbols",
                "This will force update all define symbols based on current settings.\n\n" +
                "Use this if define symbols are out of sync.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            ModuleDefineManager.UpdateDefineSymbolsFromSettings();
            
            Debug.Log("[AMZN GoD SDK] Define symbols force updated");
            EditorUtility.DisplayDialog("Success", "Define symbols updated successfully!", "OK");
        }

        //[MenuItem("AMZN GoD/Quick Actions/Clear All SDK Defines", false, 151)]
        public static void ClearAllSDKDefines()
        {
            if (!EditorUtility.DisplayDialog("Clear All SDK Defines",
                "This will remove ALL AMZN SDK define symbols from project settings.\n\n" +
                "Use this only for debugging or cleanup.\n\n" +
                "⚠️ WARNING: This is a destructive action!\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var buildTargetGroups = new[]
            {
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.Standalone
            };

            foreach (var targetGroup in buildTargetGroups)
            {
                var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
                var definesList = System.Linq.Enumerable.ToList(currentDefines.Split(';'));

                definesList.RemoveAll(d => d.StartsWith("AMZN_"));

                var newDefines = string.Join(";", System.Linq.Enumerable.Where(definesList, d => !string.IsNullOrEmpty(d)));
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, newDefines);
            }

            Debug.Log("[AMZN GoD SDK] All SDK define symbols cleared");
            EditorUtility.DisplayDialog("Success", "All SDK defines cleared!", "OK");
        }

        //[MenuItem("AMZN GoD/Debug/Log Current Module Status", false, 200)]
        public static void LogCurrentModuleStatus()
        {
            var settings = SdkSettingsManager.LoadSettings();
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();

            Debug.Log("=== AMZN GoD SDK - Current Module Status ===");
            Debug.Log($"SDK Enabled: {settings.Enabled}");
            Debug.Log($"Adjust: {settings.Adjust.Enabled}");
            Debug.Log($"AppMetrica: {settings.AppMetrica.Enabled}");
            Debug.Log($"CrossPromo: {settings.CrossPromo.Enabled}");
            Debug.Log($"In-App Purchase: {settings.InAppPurchase.Enabled}");
            Debug.Log($"Firebase: {settings.Firebase.Enabled}");
            Debug.Log($"Internet Connection: {settings.InternetConnection.Enabled}");
            Debug.Log($"Debug Console: {settings.DebugConsole.Enabled}");
            Debug.Log($"\nActive Define Symbols ({activeDefines.Count}):");
            
            foreach (var define in activeDefines)
            {
                Debug.Log($"  • {define}");
            }
            
            Debug.Log("===========================================");
        }

        //[MenuItem("AMZN GoD/Debug/Validate SDK Configuration", false, 201)]
        public static void ValidateSDKConfiguration()
        {
            Debug.Log("=== AMZN GoD SDK - Configuration Validation ===");

            var settings = SdkSettingsManager.LoadSettings();
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
            var issues = new System.Collections.Generic.List<string>();

            // Check SDK enabled state
            if (settings.Enabled && !activeDefines.Contains(ModuleDefineManager.SDK_ENABLED_DEFINE))
            {
                issues.Add("SDK is enabled but SDK_ENABLED_DEFINE is missing");
            }

            // Check each module
            CheckModule("Adjust", settings.Adjust.Enabled, ModuleDefineManager.ADJUST_DEFINE, activeDefines, issues);
            CheckModule("AppMetrica", settings.AppMetrica.Enabled, ModuleDefineManager.APPMETRICA_DEFINE, activeDefines, issues);
            CheckModule("CrossPromo", settings.CrossPromo.Enabled, ModuleDefineManager.CROSSPROMO_DEFINE, activeDefines, issues);
            CheckModule("IAP", settings.InAppPurchase.Enabled, ModuleDefineManager.IAP_DEFINE, activeDefines, issues);
            CheckModule("Firebase", settings.Firebase.Enabled, ModuleDefineManager.FIREBASE_DEFINE, activeDefines, issues);
            CheckModule("Internet Connection", settings.InternetConnection.Enabled, ModuleDefineManager.INTERNETCONNECTION_DEFINE, activeDefines, issues);
            CheckModule("Debug Console", settings.DebugConsole.Enabled, ModuleDefineManager.DEBUGCONSOLE_DEFINE, activeDefines, issues);

            if (issues.Count == 0)
            {
                Debug.Log("✓ Configuration is valid - no issues found");
                EditorUtility.DisplayDialog("Validation Success", 
                    "SDK configuration is valid!\n\nNo issues detected.", 
                    "OK");
            }
            else
            {
                Debug.LogWarning($"✗ Found {issues.Count} configuration issues:");
                foreach (var issue in issues)
                {
                    Debug.LogWarning($"  • {issue}");
                }
                
                EditorUtility.DisplayDialog("Validation Issues", 
                    $"Found {issues.Count} configuration issues.\n\nCheck Console for details.\n\nTry 'Force Update Define Symbols' to fix.", 
                    "OK");
            }

            Debug.Log("=============================================");
        }

        private static void CheckModule(string moduleName, bool isEnabled, string defineSymbol, 
            System.Collections.Generic.List<string> activeDefines, System.Collections.Generic.List<string> issues)
        {
            bool hasDefine = activeDefines.Contains(defineSymbol);

            if (isEnabled && !hasDefine)
            {
                issues.Add($"{moduleName} is enabled but {defineSymbol} is missing");
            }
            else if (!isEnabled && hasDefine)
            {
                issues.Add($"{moduleName} is disabled but {defineSymbol} is still present");
            }
        }
    }
}
