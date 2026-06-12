using UnityEditor;
using UnityEngine;
using System.IO;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Окно для управления папками модулей
    /// </summary>
    public class ModuleFolderWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private bool _autoUpdate;

        [MenuItem("AMZN GoD/Module Folder Management", false, 2)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModuleFolderWindow>("Module Folder Management");
            window.minSize = new Vector2(500, 600);
        }

        private void OnEnable()
        {
            _autoUpdate = EditorPrefs.GetBool("AMZN_AutoUpdateModuleFolders", true);
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Space(10);
            
            EditorGUILayout.LabelField("AMZN GoD SDK - Module Folder Management", EditorStyles.boldLabel);
            
            GUILayout.Space(10);

            // Info Box
            EditorGUILayout.HelpBox(
                "This system allows you to completely exclude module folders from Unity project.\n\n" +
                "When a module is disabled, its folder is renamed with ~ suffix.\n" +
                "Unity ignores folders with ~ suffix completely - no compilation, no import, no processing.\n\n" +
                "Benefits:\n" +
                "• Complete exclusion (not just compilation)\n" +
                "• Faster Unity startup and asset import\n" +
                "• Smaller project size in Unity\n" +
                "• Cleaner Project window",
                MessageType.Info);

            GUILayout.Space(20);

            // Auto-Update Setting
            DrawAutoUpdateSection();

            GUILayout.Space(20);

            // Actions Section
            DrawActionsSection();

            GUILayout.Space(20);

            // Current Status
            DrawStatusSection();

            GUILayout.Space(20);

            // Documentation
            DrawDocumentationSection();

            GUILayout.Space(10);

            EditorGUILayout.EndScrollView();
        }

        private void DrawAutoUpdateSection()
        {
            EditorGUILayout.LabelField("Auto-Update Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _autoUpdate = EditorGUILayout.Toggle("Auto-Update Module Folders", _autoUpdate);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool("AMZN_AutoUpdateModuleFolders", _autoUpdate);
                Debug.Log($"[Module Folders] Auto-update {(_autoUpdate ? "ENABLED" : "DISABLED")}");
            }

            GUILayout.Space(5);

            if (_autoUpdate)
            {
                EditorGUILayout.HelpBox(
                    "✓ Auto-update is ENABLED\n\n" +
                    "Module folders will be automatically renamed when you save SDK settings.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "✗ Auto-update is DISABLED\n\n" +
                    "You need to manually click 'Update Module Folders' button to apply changes.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionsSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("Update Module Folders Based on Settings", GUILayout.Height(35)))
            {
                ModuleFolderManager.UpdateModuleFolders();
            }

            GUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "This will rename module folders based on current SDK settings:\n" +
                "• Disabled modules → folder renamed with ~ suffix\n" +
                "• Enabled modules → ~ suffix removed",
                MessageType.None);

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Check Folder Status", GUILayout.Height(25)))
            {
                ModuleFolderManager.CheckModuleFoldersStatus();
            }

            if (GUILayout.Button("Open SDK Settings", GUILayout.Height(25)))
            {
                SDKSettingsWindow.ShowWindow();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Current Folder Status", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var settings = SdkSettingsManager.LoadSettings();

            DrawFolderStatus("Cross-Promo", "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo", settings.CrossPromo.Enabled);
            DrawFolderStatus("Adjust", "Assets/AMZNGoDSDK/Runtime/Modules/Adjust", settings.Adjust.Enabled);
            DrawFolderStatus("AppMetrica", "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica", settings.AppMetrica.Enabled);
            DrawFolderStatus("Firebase", "Assets/AMZNGoDSDK/Runtime/Modules/Firebase", settings.Firebase.Enabled);
            DrawFolderStatus("InAppPurchase", "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase", settings.InAppPurchase.Enabled);
            DrawFolderStatus("InternetConnection", "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection", settings.InternetConnection.Enabled);
            DrawFolderStatus("InGameDebugConsole", "Assets/AMZNGoDSDK/Runtime/Modules/InGameDebugConsole", settings.DebugConsole.Enabled);

            EditorGUILayout.EndVertical();
        }

        private void DrawFolderStatus(string moduleName, string folderPath, bool shouldBeEnabled)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            string disabledPath = folderPath + "~";
            
            bool isEnabled = Directory.Exists(folderPath);
            bool isDisabled = Directory.Exists(disabledPath);

            // Module Name
            EditorGUILayout.LabelField(moduleName, GUILayout.Width(150));

            // Settings Status
            string settingsStatus = shouldBeEnabled ? "ON" : "OFF";
            var settingsColor = shouldBeEnabled ? Color.green : Color.gray;
            
            GUI.color = settingsColor;
            EditorGUILayout.LabelField($"Settings: {settingsStatus}", GUILayout.Width(100));
            GUI.color = Color.white;

            // Folder Status
            string folderStatus = "?";
            Color folderColor = Color.yellow;

            if (isEnabled && isDisabled)
            {
                folderStatus = "ERROR (both exist)";
                folderColor = Color.red;
            }
            else if (isEnabled)
            {
                folderStatus = "Active";
                folderColor = Color.green;
            }
            else if (isDisabled)
            {
                folderStatus = "Disabled (~)";
                folderColor = Color.gray;
            }
            else
            {
                folderStatus = "Not Found";
                folderColor = Color.red;
            }

            GUI.color = folderColor;
            EditorGUILayout.LabelField($"Folder: {folderStatus}", GUILayout.Width(150));
            GUI.color = Color.white;

            // Match Status
            bool matches = (isEnabled && shouldBeEnabled) || (isDisabled && !shouldBeEnabled);
            if (matches)
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("✓ OK", GUILayout.Width(50));
            }
            else
            {
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField("⚠ Sync", GUILayout.Width(50));
            }
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDocumentationSection()
        {
            EditorGUILayout.LabelField("How It Works", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Unity Special Folders:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "Folders ending with ~ (tilde) are ignored by Unity completely.\n" +
                "Unity doesn't import, compile, or process any files in such folders.",
                EditorStyles.wordWrappedLabel);

            GUILayout.Space(5);

            EditorGUILayout.LabelField("Example:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "• Modules/Cross-Promo (active, imported by Unity)\n" +
                "• Modules/Cross-Promo~ (ignored by Unity)",
                EditorStyles.wordWrappedLabel);

            GUILayout.Space(5);

            EditorGUILayout.LabelField("Combined Approach:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "You can use BOTH approaches:\n" +
                "1. Define Symbols (#if) - for selective compilation\n" +
                "2. Folder Renaming (~) - for complete exclusion\n\n" +
                "Folder renaming is more aggressive but also more effective.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.EndVertical();
        }
    }
}
