using System.Collections.Generic;
using AMZNGoDSDK.Editor.SdkDependencies;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Windows
{
    public sealed class SDKSettingsWindow : EditorWindow
    {
        private static Dictionary<string, bool> _dependenciesInfo = new();
        
        private Vector2 _scrollPosition;

        [MenuItem("AMZN GoD/Settings", false, 0)]
        public static async void ShowWindow()
        {
            _dependenciesInfo = 
                await SdkDependencyManager.GetSdkDependenciesInstallInfoAsync();
            
            var window = GetWindow<SDKSettingsWindow>("AMZN GoD SDK Settings");
            window.minSize = new Vector2(400, 400);
        }

        private void OnGUI()
        {
            GUILayout.Space(20);
            
            GUILayout.Label("Required External Dependencies:", EditorStyles.boldLabel);
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
            
            foreach (var dependency in _dependenciesInfo)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                bool isInstalled = _dependenciesInfo[dependency.Key];
                EditorGUILayout.LabelField($"{dependency.Key}: {(isInstalled ? "installed" : "missing")}", EditorStyles.boldLabel);
                
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
            
            GUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                $"Total dependencies configured: {_dependenciesInfo.Count}\n\n" +
                "Dependencies will be automatically checked when Unity starts.\nIf any dependencies are missing, SDK will be install it again.", 
                MessageType.Info);
        }
    }
}