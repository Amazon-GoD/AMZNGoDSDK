using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public sealed class FirstImportSetupWizard : EditorWindow
    {
        private const string ShownPrefKey = "AMZN_SetupWizardShown";

        private List<DependencyDetector.ModuleDependencyInfo> _detectedModules;
        private Dictionary<string, bool> _moduleToggles;
        private Vector2 _scrollPosition;
        private bool _sdkEnabled = true;

        [MenuItem("AMZN GoD/Setup Wizard", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<FirstImportSetupWizard>("AMZN GoD SDK Setup");
            window.minSize = new Vector2(520, 500);
            window.Init();
        }

        public static void ShowIfNeeded()
        {
            if (ModuleDefineManager.ConfigFileExists())
                return;

            if (SessionState.GetBool(ShownPrefKey, false))
                return;

            SessionState.SetBool(ShownPrefKey, true);
            EditorApplication.delayCall += ShowWindow;
        }

        /// <summary>
        /// Вызывается при импорте пакета SDK — показывает мастер безусловно,
        /// минуя SessionState и проверку конфига (пользователь явно переустановил SDK).
        /// </summary>
        public static void ShowOnPackageImport()
        {
            SessionState.SetBool(ShownPrefKey, false);
            // Двойной delayCall — ждём завершения импорта и domain reload
            EditorApplication.delayCall += () =>
                EditorApplication.delayCall += ShowWindow;
        }

        private void Init()
        {
            _detectedModules = DependencyDetector.DetectAll();
            _moduleToggles = new Dictionary<string, bool>();

            foreach (var info in _detectedModules)
            {
                _moduleToggles[info.DefineSymbol] = info.DependenciesPresent;
            }
        }

        private void OnEnable()
        {
            if (_detectedModules == null)
                Init();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("AMZN GoD SDK — Initial Setup", EditorStyles.boldLabel);
            GUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "No SDK configuration found. This wizard will help you enable only the modules " +
                "whose dependencies are installed in this project.\n\n" +
                "Modules with missing dependencies are unchecked — enabling them will cause compilation errors.",
                MessageType.Info);

            GUILayout.Space(10);

            _sdkEnabled = EditorGUILayout.Toggle("Enable SDK", _sdkEnabled);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var info in _detectedModules)
            {
                DrawModuleRow(info);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable All Available", GUILayout.Height(28)))
            {
                foreach (var info in _detectedModules)
                {
                    _moduleToggles[info.DefineSymbol] = info.DependenciesPresent;
                }
            }
            if (GUILayout.Button("Disable All", GUILayout.Height(28)))
            {
                foreach (var info in _detectedModules)
                {
                    _moduleToggles[info.DefineSymbol] = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("Apply & Save", GUILayout.Height(35)))
            {
                ApplySettings();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(10);
        }

        private void DrawModuleRow(DependencyDetector.ModuleDependencyInfo info)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            bool current = _moduleToggles.TryGetValue(info.DefineSymbol, out var val) && val;
            bool newVal = EditorGUILayout.Toggle(current, GUILayout.Width(20));
            _moduleToggles[info.DefineSymbol] = newVal;

            EditorGUILayout.LabelField(info.ModuleName, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            if (!info.HasExternalDependency)
            {
                EditorGUILayout.LabelField("no external deps", EditorStyles.miniLabel, GUILayout.Width(110));
            }
            else if (info.DependenciesPresent)
            {
                var prev = GUI.contentColor;
                GUI.contentColor = new Color(0.2f, 0.8f, 0.2f);
                EditorGUILayout.LabelField("INSTALLED", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUI.contentColor = prev;
            }
            else
            {
                var prev = GUI.contentColor;
                GUI.contentColor = new Color(0.9f, 0.3f, 0.3f);
                EditorGUILayout.LabelField("MISSING", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUI.contentColor = prev;
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(info.DependencyDescription))
            {
                EditorGUILayout.LabelField(
                    $"  {info.DependencyDescription}",
                    EditorStyles.miniLabel);
            }

            if (newVal && info.HasExternalDependency && !info.DependenciesPresent)
            {
                EditorGUILayout.HelpBox(
                    "Dependencies not found. Enabling this module will cause compilation errors.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private void ApplySettings()
        {
            var settings = new SdkSettingsData { Enabled = _sdkEnabled };

            settings.Adjust.Enabled = GetToggle(ModuleDefineManager.ADJUST_DEFINE);
            settings.AppMetrica.Enabled = GetToggle(ModuleDefineManager.APPMETRICA_DEFINE);
            settings.CrossPromo.Enabled = GetToggle(ModuleDefineManager.CROSSPROMO_DEFINE);
            settings.Infatica.Enabled = GetToggle(ModuleDefineManager.INFATICA_DEFINE);
            settings.Infatica.SdkVersion = InfaticaSettingData.InfaticaSdkVersion.WithoutJobs;
            settings.InAppPurchase.Enabled = GetToggle(ModuleDefineManager.IAP_DEFINE);
            settings.Firebase.Enabled = GetToggle(ModuleDefineManager.FIREBASE_DEFINE);
            settings.InternetConnection.Enabled = GetToggle(ModuleDefineManager.INTERNETCONNECTION_DEFINE);
            settings.DebugConsole.Enabled = GetToggle(ModuleDefineManager.DEBUGCONSOLE_DEFINE);

            if (!SdkSettingsManager.SaveSettings(settings))
                return;

            Debug.Log("[AMZN GoD SDK] Setup complete — settings saved, defines updated.");
            EditorUtility.DisplayDialog("Setup Complete",
                "SDK configuration saved.\nUnity will recompile scripts now.",
                "OK");

            Close();
        }

        private bool GetToggle(string define)
        {
            return _moduleToggles.TryGetValue(define, out var val) && val;
        }
    }

    /// <summary>
    /// Срабатывает при импорте файлов из .unitypackage.
    /// Отличается от [InitializeOnLoad] тем, что вызывается только при реальном
    /// добавлении/обновлении файлов на диске, а не при каждом domain reload.
    ///
    /// Два сценария:
    ///   1. Основной SDK (детектируется по asmdef) — показывает Setup Wizard.
    ///   2. Аддон WithJobs (детектируется по Plugins_WithJobs/) — активирует WithJobs-плагины.
    /// </summary>
    internal class SdkImportPostprocessor : AssetPostprocessor
    {
        private const string SdkEditorAsmdef = "Assets/AMZNGoDSDK/Editor/AMZNGoDSDK.Editor.asmdef";

        private const string InfaticaPlugins            = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins";
        private const string InfaticaWithJobsFolder     = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithJobs";
        private const string InfaticaWithoutJobsStorage = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithoutJobs~";

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool sdkImported         = false;
            bool withJobsAddonImported = false;

            foreach (var asset in importedAssets)
            {
                if (asset == SdkEditorAsmdef)
                    sdkImported = true;

                if (asset.StartsWith(InfaticaWithJobsFolder + "/") || asset == InfaticaWithJobsFolder)
                    withJobsAddonImported = true;
            }

            if (sdkImported)
            {
                // Основной SDK: Plugins_WithJobs в пакет больше не включается, ничего прятать не нужно.
                FirstImportSetupWizard.ShowOnPackageImport();
            }
            else if (withJobsAddonImported)
            {
                // Аддон WithJobs импортирован поверх SDK — активируем WithJobs-плагины.
                ActivateWithJobsPlugins();
            }
        }

        private static void ActivateWithJobsPlugins()
        {
            // 1. Убираем текущие WithoutJobs-плагины в хранилище
            if (Directory.Exists(InfaticaPlugins) && !Directory.Exists(InfaticaWithoutJobsStorage))
            {
                Directory.Move(InfaticaPlugins, InfaticaWithoutJobsStorage);
                MoveMeta(InfaticaPlugins, InfaticaWithoutJobsStorage);
            }

            // 2. Активируем: Plugins_WithJobs/ → Plugins/
            if (Directory.Exists(InfaticaWithJobsFolder) && !Directory.Exists(InfaticaPlugins))
            {
                Directory.Move(InfaticaWithJobsFolder, InfaticaPlugins);
                MoveMeta(InfaticaWithJobsFolder, InfaticaPlugins);
            }

            // 3. Обновляем SdkVersion в конфиге
            UpdateSettingsToWithJobs();

            Debug.Log("[AMZN GoD SDK] Infatica WithJobs module activated: Plugins/ = WithJobs.");
        }

        private static void UpdateSettingsToWithJobs()
        {
            const string configPath = "Assets/Resources/amzn_god_sdk.json";
            if (!File.Exists(configPath))
                return;

            try
            {
                string json = File.ReadAllText(configPath);
                var settings = JsonUtility.FromJson<AMZNGoDSDK.Runtime.SdkSettingsData>(json);
                settings.Infatica.SdkVersion = AMZNGoDSDK.Runtime.InfaticaSettingData.InfaticaSdkVersion.WithJobs;
                File.WriteAllText(configPath, JsonUtility.ToJson(settings, true));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AMZN GoD SDK] Could not update settings after WithJobs import: {e.Message}");
            }
        }

        private static void MoveMeta(string fromPath, string toPath)
        {
            string from = fromPath + ".meta";
            string to   = toPath   + ".meta";

            if (!File.Exists(from))
                return;

            if (File.Exists(to))
                File.Delete(to);

            File.Move(from, to);
        }
    }
}
