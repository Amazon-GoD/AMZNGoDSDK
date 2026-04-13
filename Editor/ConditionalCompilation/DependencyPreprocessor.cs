using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Препроцессор для управления зависимостями модулей при сборке
    /// Отключает Dependencies.xml файлы для неактивных модулей
    /// </summary>
    public class DependencyPreprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -100; // Выполняется раньше других препроцессоров

        private readonly List<string> _disabledFiles = new List<string>();

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[AMZN GoD SDK] Preprocessing build - managing module dependencies...");
            
            var settings = SdkSettingsManager.LoadSettings();
            
            // Сначала обновляем define symbols
            ModuleDefineManager.UpdateDefineSymbols(settings);

            if (!settings.Enabled)
            {
                Debug.Log("[AMZN GoD SDK] SDK is disabled, skipping dependency management");
                return;
            }

            _disabledFiles.Clear();

            // Adjust
            if (!settings.Adjust.Enabled)
            {
                DisableDependencyFiles(new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Native/Editor/Dependencies.xml"
                });
            }

            // AppMetrica
            if (!settings.AppMetrica.Enabled)
            {
                DisableDependencyFiles(new[]
                {
                    "Assets/AMZNGoDSDK/Editor/Modules/Appmetrica/Editor/AppMetricaDependencies.xml"
                });
            }

            // Infatica
            if (!settings.Infatica.Enabled)
            {
                DisableDependencyFiles(new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins/Editor/Dependencies.xml"
                });
            }

            // CrossPromo
            if (!settings.CrossPromo.Enabled)
            {
                Debug.Log("[AMZN GoD SDK] CrossPromo disabled");
            }

            Debug.Log($"[AMZN GoD SDK] Disabled {_disabledFiles.Count} dependency files for inactive modules");
            
            // Обновляем AssetDatabase
            AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            Debug.Log("[AMZN GoD SDK] Postprocessing build - restoring dependencies...");
            
            // Восстанавливаем все отключенные файлы после билда
            foreach (var disabledFile in _disabledFiles)
            {
                string originalPath = disabledFile.Replace(".disabled", "");
                if (File.Exists(disabledFile) && !File.Exists(originalPath))
                {
                    File.Move(disabledFile, originalPath);
                    Debug.Log($"[AMZN GoD SDK] Restored: {originalPath}");
                }
            }

            _disabledFiles.Clear();
            
            // Обновляем AssetDatabase
            AssetDatabase.Refresh();
            
            Debug.Log("[AMZN GoD SDK] Build postprocessing completed");
        }

        private void DisableDependencyFiles(string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    string disabledPath = path + ".disabled";
                    
                    // Удаляем старый .disabled файл если существует
                    if (File.Exists(disabledPath))
                    {
                        File.Delete(disabledPath);
                    }

                    // Переименовываем оригинальный файл
                    File.Move(path, disabledPath);
                    _disabledFiles.Add(disabledPath);
                    
                    Debug.Log($"[AMZN GoD SDK] Disabled dependency: {path}");
                    
                    // Также отключаем .meta файл
                    string metaPath = path + ".meta";
                    string disabledMetaPath = disabledPath + ".meta";
                    if (File.Exists(metaPath))
                    {
                        if (File.Exists(disabledMetaPath))
                        {
                            File.Delete(disabledMetaPath);
                        }
                        File.Move(metaPath, disabledMetaPath);
                        _disabledFiles.Add(disabledMetaPath);
                    }
                }
                else
                {
                    Debug.LogWarning($"[AMZN GoD SDK] Dependency file not found: {path}");
                }
            }
        }
    }
}
