using AMZNGoDSDK.Editor.SdkDependencies;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Windows
{
    public sealed class SDKSettingsWindow : EditorWindow
    {
        private Vector2 _scrollPosition;

        [MenuItem("AMZN GoD/Settings", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<SDKSettingsWindow>("AMZN GoD SDK Settings");
            window.minSize = new Vector2(400, 400);
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            
            GUILayout.Label("SDK Dependency Installer", EditorStyles.boldLabel);
            GUILayout.Space(10);
            
            if (GUILayout.Button("Install Dependencies", GUILayout.Height(30)))
            {
                DependencyInstaller.InstallDependenciesMenu();
            }
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Check Dependencies", GUILayout.Height(25)))
            {
                DependencyInstaller.CheckStatusMenu();
            }
            
            GUILayout.Space(20);
            
            GUILayout.Label("Configured Dependencies:", EditorStyles.boldLabel);
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
            
            var dependencies = DependencyInstaller.GetDependencies();
            foreach (var dependency in dependencies)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.LabelField(dependency.Key, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("URL:", dependency.Value, EditorStyles.wordWrappedLabel);
                
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
            
            GUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                $"Total dependencies configured: {dependencies.Count}\n\n" +
                "Dependencies will be automatically checked when Unity starts. " +
                "You can also manually install them using the buttons above.", 
                MessageType.Info);
        }
    }
}