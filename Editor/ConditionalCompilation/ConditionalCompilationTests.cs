using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Набор тестов для проверки работы системы условной компиляции
    /// </summary>
    public static class ConditionalCompilationTests
    {
        //[MenuItem("AMZN GoD/Tests/Run All Tests", false, 300)]
        public static void RunAllTests()
        {
            Debug.Log("=== AMZN GoD SDK - Conditional Compilation Tests ===");
            
            int passed = 0;
            int failed = 0;
            
            // Test 1: Settings Load
            if (TestSettingsLoad())
                passed++;
            else
                failed++;
            
            // Test 2: Define Symbols Sync
            if (TestDefineSymbolsSync())
                passed++;
            else
                failed++;
            
            // Test 3: Module Manager
            if (TestModuleManager())
                passed++;
            else
                failed++;
            
            // Test 4: Build Target Groups
            if (TestBuildTargetGroups())
                passed++;
            else
                failed++;
            
            Debug.Log($"\n=== Test Results ===");
            Debug.Log($"Passed: {passed}");
            Debug.Log($"Failed: {failed}");
            Debug.Log($"Total: {passed + failed}");
            
            if (failed == 0)
            {
                Debug.Log("✓ All tests passed!");
                EditorUtility.DisplayDialog("Tests Passed", 
                    $"All conditional compilation tests passed!\n\n" +
                    $"Passed: {passed}\n" +
                    $"Failed: {failed}", 
                    "OK");
            }
            else
            {
                Debug.LogWarning($"✗ {failed} test(s) failed. Check console for details.");
                EditorUtility.DisplayDialog("Tests Failed", 
                    $"Some tests failed. Check console for details.\n\n" +
                    $"Passed: {passed}\n" +
                    $"Failed: {failed}", 
                    "OK");
            }
            
            Debug.Log("====================================");
        }
        
        private static bool TestSettingsLoad()
        {
            Debug.Log("\n[Test 1] Settings Load");
            try
            {
                var settings = SdkSettingsManager.LoadSettings();
                if (settings != null)
                {
                    Debug.Log("✓ Settings loaded successfully");
                    return true;
                }
                else
                {
                    Debug.LogError("✗ Settings is null");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"✗ Exception: {e.Message}");
                return false;
            }
        }
        
        private static bool TestDefineSymbolsSync()
        {
            Debug.Log("\n[Test 2] Define Symbols Sync");
            try
            {
                var settings = SdkSettingsManager.LoadSettings();
                var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
                
                bool allMatch = true;
                
                // Check SDK
                if (settings.Enabled != activeDefines.Contains(ModuleDefineManager.SDK_ENABLED_DEFINE))
                {
                    Debug.LogWarning($"✗ SDK enabled state mismatch");
                    allMatch = false;
                }
                
                // Check Adjust
                if (settings.Adjust.Enabled != activeDefines.Contains(ModuleDefineManager.ADJUST_DEFINE))
                {
                    Debug.LogWarning($"✗ Adjust enabled state mismatch");
                    allMatch = false;
                }
                
                // Check AppMetrica
                if (settings.AppMetrica.Enabled != activeDefines.Contains(ModuleDefineManager.APPMETRICA_DEFINE))
                {
                    Debug.LogWarning($"✗ AppMetrica enabled state mismatch");
                    allMatch = false;
                }
                
                if (allMatch)
                {
                    Debug.Log("✓ Define symbols are in sync with settings");
                    return true;
                }
                else
                {
                    Debug.LogWarning("✗ Some define symbols are out of sync. Run 'Force Update Define Symbols'");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"✗ Exception: {e.Message}");
                return false;
            }
        }
        
        private static bool TestModuleManager()
        {
            Debug.Log("\n[Test 3] Module Manager");
            try
            {
                var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
                Debug.Log($"  Active defines count: {activeDefines.Count}");
                
                foreach (var define in activeDefines)
                {
                    Debug.Log($"  • {define}");
                }
                
                Debug.Log("✓ Module Manager working correctly");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"✗ Exception: {e.Message}");
                return false;
            }
        }
        
        private static bool TestBuildTargetGroups()
        {
            Debug.Log("\n[Test 4] Build Target Groups");
            try
            {
                var targetGroups = new[]
                {
                    BuildTargetGroup.Android,
                    BuildTargetGroup.iOS,
                    BuildTargetGroup.Standalone
                };
                
                bool allHaveDefines = true;
                
                foreach (var group in targetGroups)
                {
                    var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                    var hasSDKDefines = defines.Contains("AMZN_");
                    
                    Debug.Log($"  {group}: {(hasSDKDefines ? "Has SDK defines" : "No SDK defines")}");
                    
                    if (!hasSDKDefines)
                    {
                        Debug.LogWarning($"  ⚠ {group} has no SDK defines");
                        allHaveDefines = false;
                    }
                }
                
                if (allHaveDefines)
                {
                    Debug.Log("✓ All build target groups have SDK defines");
                    return true;
                }
                else
                {
                    Debug.LogWarning("✗ Some build target groups are missing SDK defines");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"✗ Exception: {e.Message}");
                return false;
            }
        }
        
        //[MenuItem("AMZN GoD/Tests/Test Enable All Modules", false, 301)]
        public static void TestEnableAllModules()
        {
            Debug.Log("=== Test: Enable All Modules ===");
            
            var settingsBefore = SdkSettingsManager.LoadSettings();
            Debug.Log($"Adjust before: {settingsBefore.Adjust.Enabled}");
            
            ModuleToggleUtility.EnableAllModules();
            
            var settingsAfter = SdkSettingsManager.LoadSettings();
            Debug.Log($"Adjust after: {settingsAfter.Adjust.Enabled}");
            
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
            Debug.Log($"Active defines: {activeDefines.Count}");
            
            Debug.Log("=================================");
        }
        
        //[MenuItem("AMZN GoD/Tests/Test Disable All Modules", false, 302)]
        public static void TestDisableAllModules()
        {
            Debug.Log("=== Test: Disable All Modules ===");
            
            var settingsBefore = SdkSettingsManager.LoadSettings();
            Debug.Log($"Adjust before: {settingsBefore.Adjust.Enabled}");
            
            ModuleToggleUtility.DisableAllModules();
            
            var settingsAfter = SdkSettingsManager.LoadSettings();
            Debug.Log($"Adjust after: {settingsAfter.Adjust.Enabled}");
            
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
            Debug.Log($"Active defines: {activeDefines.Count}");
            
            Debug.Log("=================================");
        }
        
        //[MenuItem("AMZN GoD/Tests/Simulate Build Test", false, 303)]
        public static void SimulateBuildTest()
        {
            Debug.Log("=== Simulate Build Test ===");
            
            var settings = SdkSettingsManager.LoadSettings();
            
            Debug.Log("Checking which modules would be included in build:");
            Debug.Log($"  SDK: {settings.Enabled}");
            Debug.Log($"  Adjust: {settings.Adjust.Enabled}");
            Debug.Log($"  AppMetrica: {settings.AppMetrica.Enabled}");
            Debug.Log($"  CrossPromo: {settings.CrossPromo.Enabled}");
            Debug.Log($"  IAP: {settings.InAppPurchase.Enabled}");
            Debug.Log($"  Firebase: {settings.Firebase.Enabled}");
            Debug.Log($"  Internet Connection: {settings.InternetConnection.Enabled}");
            
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
            Debug.Log($"\nActive defines for compilation: {activeDefines.Count}");
            foreach (var define in activeDefines)
            {
                Debug.Log($"  • {define}");
            }
            
            // Check generated EDM dependencies file (built from templates of enabled modules)
            Debug.Log("\nGenerated EDM dependencies:");
            if (System.IO.File.Exists(EdmDependencyGenerator.GeneratedFilePath))
            {
                Debug.Log($"  ✓ {EdmDependencyGenerator.GeneratedFilePath} present");
            }
            else
            {
                Debug.Log("  ✗ Generated file absent (no enabled modules with EDM dependencies, or toggles not applied yet)");
            }

            Debug.Log("============================");
        }
    }
}
