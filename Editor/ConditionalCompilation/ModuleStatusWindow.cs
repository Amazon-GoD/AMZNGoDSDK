using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Окно для просмотра статуса активных модулей и их define symbols
    /// </summary>
    public class ModuleStatusWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Dictionary<string, bool> _moduleStatus;
        private GUIStyle _enabledStyle;
        private GUIStyle _disabledStyle;
        private GUIStyle _headerStyle;

        //[MenuItem("AMZN GoD/Module Status & Compilation Info", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModuleStatusWindow>("Module Compilation Status");
            window.minSize = new Vector2(500, 400);
            window.RefreshStatus();
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            _moduleStatus = new Dictionary<string, bool>
            {
                { "SDK Enabled", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.SDK_ENABLED_DEFINE) },
                { "Adjust", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.ADJUST_DEFINE) },
                { "AppMetrica", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.APPMETRICA_DEFINE) },
                { "Cross Promo", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.CROSSPROMO_DEFINE) },
                { "Infatica", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.INFATICA_DEFINE) },
                { "In-App Purchase", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.IAP_DEFINE) },
                { "Firebase", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.FIREBASE_DEFINE) },
                { "Internet Connection", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.INTERNETCONNECTION_DEFINE) },
                { "Debug Console", ModuleDefineManager.IsModuleEnabled(ModuleDefineManager.DEBUGCONSOLE_DEFINE) }
            };
        }

        private void OnGUI()
        {
            InitializeStyles();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Header
            EditorGUILayout.LabelField("AMZN GoD SDK - Module Compilation Status", _headerStyle);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "This window shows which modules are currently included in compilation. " +
                "When a module is disabled in settings, its code is excluded from builds using conditional compilation.",
                MessageType.Info);

            GUILayout.Space(10);

            // Current Build Target
            EditorGUILayout.LabelField($"Current Build Target: {EditorUserBuildSettings.selectedBuildTargetGroup}", EditorStyles.boldLabel);
            GUILayout.Space(5);

            // Module Status Table
            EditorGUILayout.LabelField("Module Status:", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foreach (var module in _moduleStatus)
            {
                DrawModuleStatus(module.Key, module.Value);
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Active Define Symbols
            DrawDefineSymbols();

            GUILayout.Space(20);

            // Action Buttons
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Refresh Status", GUILayout.Height(30)))
            {
                RefreshStatus();
            }

            if (GUILayout.Button("Open SDK Settings", GUILayout.Height(30)))
            {
                SDKSettingsWindow.ShowWindow();
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Information
            EditorGUILayout.HelpBox(
                "How it works:\n\n" +
                "• When you disable a module in SDK Settings, its define symbol is removed\n" +
                "• Code wrapped in #if directives for that module is excluded from compilation\n" +
                "• External dependencies (Android/iOS) are also excluded from builds\n" +
                "• This reduces build size and compilation time\n\n" +
                "Note: Changes take effect after saving settings and may require Unity to recompile scripts.",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawModuleStatus(string moduleName, bool isEnabled)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Module Name
            EditorGUILayout.LabelField(moduleName, GUILayout.Width(200));

            // Status Badge
            var statusStyle = isEnabled ? _enabledStyle : _disabledStyle;
            var statusText = isEnabled ? "✓ ENABLED" : "✗ DISABLED";
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(statusText, statusStyle, GUILayout.Width(120));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDefineSymbols()
        {
            EditorGUILayout.LabelField("Active Define Symbols:", EditorStyles.boldLabel);
            
            var activeDefines = ModuleDefineManager.GetActiveModuleDefines();
            
            if (activeDefines.Count == 0)
            {
                EditorGUILayout.HelpBox("No SDK module defines are currently active.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                foreach (var define in activeDefines)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("•", GUILayout.Width(15));
                    EditorGUILayout.SelectableLabel(define, GUILayout.Height(18));
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void InitializeStyles()
        {
            if (_enabledStyle == null)
            {
                _enabledStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.2f, 0.8f, 0.2f) },
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight
                };
            }

            if (_disabledStyle == null)
            {
                _disabledStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.8f, 0.3f, 0.3f) },
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight
                };
            }

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }
    }
}
