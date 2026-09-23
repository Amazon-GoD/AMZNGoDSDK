using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public sealed class AndroidToolchainWindow : EditorWindow
    {
        private bool _advanced;
        private string _javaHome;
        private string _sdkRoot;
        private string _gradleHome;
        private string _error;
        private Vector2 _scroll;

        [MenuItem("AMZN GoD/Android Build Tools", false, 320)]
        public static void ShowWindow()
        {
            GetWindow<AndroidToolchainWindow>("Android Build Tools").minSize = new Vector2(580, 360);
        }

        private void OnEnable() => ReadPaths();
        private void OnInspectorUpdate() => Repaint();

        private void ReadPaths()
        {
            _javaHome = AndroidToolchainSettings.GradleJavaHome;
            _sdkRoot = AndroidToolchainSettings.GradleSdkRoot;
            _gradleHome = AndroidToolchainSettings.GradleHome;
        }

        private void OnGUI()
        {
#if UNITY_ANDROID
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Android Build Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("SDK автоматически подготавливает инструменты для сборки: JDK 17, Gradle " +
                AndroidToolchainSettings.GradleVersion + ", Android SDK " + AndroidToolchainSettings.CompileSdk +
                ". Unity 2022.3 продолжает использовать свои JDK, SDK и NDK. Первая подготовка может потребовать загрузки файлов.", MessageType.Info);
            EditorGUILayout.LabelField(AndroidToolchainInstaller.Status ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            DrawEffectivePath("JDK 17", AndroidToolchainSettings.GradleJavaHome);
            DrawEffectivePath("Android SDK", AndroidToolchainSettings.GradleSdkRoot);
            DrawEffectivePath("Gradle", AndroidToolchainSettings.GradleHome);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);

            using (new EditorGUI.DisabledScope(AndroidToolchainInstaller.IsBusy || BuildPipeline.isBuildingPlayer))
            {
                if (GUILayout.Button("Проверить и подготовить инструменты")) Prepare();
                _advanced = EditorGUILayout.Foldout(_advanced, "Дополнительные настройки", true);
                if (_advanced)
                {
                    EditorGUILayout.HelpBox("При необходимости можно указать собственные установки. Пути сохраняются только для этого проекта на этом компьютере. " +
                        "Переменные CI AMZNGOD_ANDROID_JAVA_HOME, AMZNGOD_ANDROID_SDK_ROOT и AMZNGOD_ANDROID_GRADLE_HOME имеют приоритет.", MessageType.Info);
                    _javaHome = DrawPath("JDK 17", _javaHome);
                    _sdkRoot = DrawPath("Android SDK", _sdkRoot);
                    _gradleHome = DrawPath("Gradle " + AndroidToolchainSettings.GradleVersion, _gradleHome);
                    if (GUILayout.Button("Сохранить локальные пути"))
                    {
                        AndroidToolchainSettings.SaveLocalPaths(_javaHome, _sdkRoot, _gradleHome);
                        var errors = new List<string>();
                        AndroidToolchainSettings.CollectValidationErrors(errors);
                        _error = string.Join("\n", errors);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
#else
            EditorGUILayout.HelpBox("Переключите Build Target на Android: SDK автоматически подготовит инструменты сборки.", MessageType.Info);
#endif
        }

        private static void DrawEffectivePath(string label, string path)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(path) ? "Подготовка выполняется автоматически" : path,
                EditorStyles.wordWrappedLabel, GUILayout.Height(36));
        }

        private static string DrawPath(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string selected = EditorUtility.OpenFolderPanel(label, value, "");
                if (!string.IsNullOrEmpty(selected)) value = selected;
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private async void Prepare()
        {
#if UNITY_ANDROID
            _error = null;
            try { await AndroidToolchainInstaller.EnsureInstalledAsync(); }
            catch (Exception ex) { _error = ex.Message; }
            if (this != null)
            {
                ReadPaths();
                Repaint();
            }
#else
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }
    }
}
