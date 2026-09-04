using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Окно помощи при ошибках компиляции модулей
    /// </summary>
    public class CompilationErrorHelper : EditorWindow
    {
        [MenuItem("AMZN GoD/Help/Fix Compilation Errors", false, 500)]
        public static void ShowWindow()
        {
            var window = GetWindow<CompilationErrorHelper>("Compilation Error Helper");
            window.minSize = new Vector2(500, 600);
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            
            EditorGUILayout.LabelField("Compilation Error Helper", EditorStyles.boldLabel);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Если вы видите ошибки компиляции после удаления SDK зависимостей, " +
                "используйте инструменты ниже для их устранения.",
                MessageType.Info);

            GUILayout.Space(20);

            // Quick Fix Section
            EditorGUILayout.LabelField("Quick Fix", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox(
                "Шаг 1: Отключите модули, зависимости которых были удалены",
                MessageType.None);

            if (GUILayout.Button("Open SDK Settings", GUILayout.Height(30)))
            {
                SDKSettingsWindow.ShowWindow();
            }

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Шаг 2: Обновите define symbols",
                MessageType.None);

            if (GUILayout.Button("Force Update Define Symbols", GUILayout.Height(30)))
            {
                ModuleDefineManager.UpdateDefineSymbolsFromSettings();
                EditorUtility.DisplayDialog("Success", 
                    "Define symbols updated!\n\nUnity will recompile scripts.", 
                    "OK");
            }

            GUILayout.Space(20);

            // Diagnostic Section
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);

            if (GUILayout.Button("Validate SDK Configuration", GUILayout.Height(25)))
            {
                ModuleToggleUtility.ValidateSDKConfiguration();
            }

            GUILayout.Space(5);

            if (GUILayout.Button("View Module Status", GUILayout.Height(25)))
            {
                ModuleStatusWindow.ShowWindow();
            }

            GUILayout.Space(20);

            // Common Errors Section
            EditorGUILayout.LabelField("Common Errors", EditorStyles.boldLabel);

            DrawError(
                "AdjustSdk not found",
                "Disable Adjust module in Settings");

            DrawError(
                "Firebase not found",
                "Disable Firebase module in Settings or install Firebase package");

            DrawError(
                "Io.AppMetrica not found",
                "Disable AppMetrica module in Settings");

            GUILayout.Space(10);
        }

        private void DrawError(string error, string solution)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"❌ {error}", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField($"✓ Solution: {solution}", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }
    }
}
