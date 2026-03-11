using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Управляет папками модулей - переименовывает их с суффиксом ~ для полного исключения из Unity
    /// </summary>
    public static class ModuleFolderManager
    {
        // Маппинг модулей к их папкам
        private static readonly Dictionary<string, string[]> ModuleFolders = new Dictionary<string, string[]>
        {
            {
                "Cross-Promo", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo"
                }
            },
            {
                "Adjust", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Adjust"
                }
            },
            {
                "AppMetrica", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica"
                }
            },
            {
                "Firebase", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Firebase"
                }
            },
            {
                "Infatica", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/Infatica"
                }
            },
            {
                "InAppPurchase", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase"
                }
            },
            {
                "InternetConnection", new[]
                {
                    "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection"
                }
            }
        };

        /// <summary>
        /// Обновляет состояние папок модулей на основе настроек SDK
        /// </summary>
        //[MenuItem("AMZN GoD/Tools/Update Module Folders", false, 410)]
        public static void UpdateModuleFolders()
        {
            if (!EditorUtility.DisplayDialog("Update Module Folders",
                "This will rename module folders based on SDK settings.\n\n" +
                "Disabled modules will be renamed with ~ suffix (Unity will ignore them).\n" +
                "Enabled modules will have ~ suffix removed.\n\n" +
                "This operation can take a few seconds.\n\n" +
                "Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            var settings = SdkSettingsManager.LoadSettings();
            
            int renamedCount = 0;
            
            Debug.Log("=== AMZN GoD SDK - Updating Module Folders ===");

            // Cross-Promo
            if (UpdateModuleFolderState("Cross-Promo", settings.CrossPromo.Enabled))
                renamedCount++;

            // Adjust
            if (UpdateModuleFolderState("Adjust", settings.Adjust.Enabled))
                renamedCount++;

            // AppMetrica
            if (UpdateModuleFolderState("AppMetrica", settings.AppMetrica.Enabled))
                renamedCount++;

            // Firebase
            if (UpdateModuleFolderState("Firebase", settings.Firebase.Enabled))
                renamedCount++;

            // Infatica
            if (UpdateModuleFolderState("Infatica", settings.Infatica.Enabled))
                renamedCount++;

            if (settings.Infatica.Enabled && SwapInfaticaPlugins(settings.Infatica.SdkVersion))
                renamedCount++;

            // InAppPurchase
            if (UpdateModuleFolderState("InAppPurchase", settings.InAppPurchase.Enabled))
                renamedCount++;

            // InternetConnection
            if (UpdateModuleFolderState("InternetConnection", settings.InternetConnection.Enabled))
                renamedCount++;

            Debug.Log($"=== Updated {renamedCount} module folders ===");

            if (renamedCount > 0)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success",
                    $"Successfully updated {renamedCount} module folders!\n\n" +
                    "Unity will now refresh the Asset Database.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Info",
                    "All module folders are already in correct state.",
                    "OK");
            }
        }

        /// <summary>
        /// Обновляет состояние папки модуля (добавляет/убирает суффикс ~)
        /// </summary>
        private static bool UpdateModuleFolderState(string moduleName, bool isEnabled)
        {
            if (!ModuleFolders.ContainsKey(moduleName))
            {
                Debug.LogWarning($"[Module Folders] Module '{moduleName}' not found in mapping");
                return false;
            }

            bool anyChanged = false;
            
            foreach (var folderPath in ModuleFolders[moduleName])
            {
                if (isEnabled)
                {
                    // Модуль включен - убрать ~ если есть
                    if (EnableFolder(folderPath))
                    {
                        Debug.Log($"[Module Folders] ✓ Enabled: {moduleName}");
                        anyChanged = true;
                    }
                }
                else
                {
                    // Модуль отключен - добавить ~ если нет
                    if (DisableFolder(folderPath))
                    {
                        Debug.Log($"[Module Folders] ✗ Disabled: {moduleName}");
                        anyChanged = true;
                    }
                }
            }

            return anyChanged;
        }

        /// <summary>
        /// Включает папку (убирает суффикс ~)
        /// ВАЖНО: Unity игнорирует папки с ~, поэтому AssetDatabase.MoveAsset НЕ РАБОТАЕТ
        /// Используем только Directory.Move
        /// </summary>
        private static bool EnableFolder(string folderPath)
        {
            string disabledPath = folderPath + "~";
            
            if (Directory.Exists(disabledPath))
            {
                // Папка отключена, включаем её
                if (Directory.Exists(folderPath))
                {
                    Debug.LogError($"[Module Folders] ❌ Both {folderPath} and {disabledPath} exist!");
                    return false;
                }

                Debug.Log($"[Module Folders] 🔄 Enabling folder: {disabledPath} → {folderPath}");
                
                // ВАЖНО: Unity полностью игнорирует папки с ~, они не существуют в AssetDatabase
                // Поэтому AssetDatabase.MoveAsset НЕ СРАБОТАЕТ - нужно использовать Directory.Move
                try
                {
                    // Переименовываем папку через файловую систему
                    Directory.Move(disabledPath, folderPath);
                    
                    // Переименовываем .meta файл
                    string disabledMeta = disabledPath + ".meta";
                    string enabledMeta = folderPath + ".meta";
                    if (File.Exists(disabledMeta))
                    {
                        if (File.Exists(enabledMeta))
                        {
                            // Удаляем старый .meta если он существует
                            File.Delete(enabledMeta);
                        }
                        File.Move(disabledMeta, enabledMeta);
                        Debug.Log($"[Module Folders] ✓ Renamed .meta file");
                    }
                    
                    Debug.Log($"[Module Folders] ✅ Successfully enabled folder");
                    return true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Module Folders] ❌ Failed to enable folder: {e.Message}");
                    Debug.LogError($"[Module Folders] ❌ Exception: {e.GetType().Name}");
                    return false;
                }
            }

            return false; // Папка уже включена
        }

        /// <summary>
        /// Отключает папку (добавляет суффикс ~)
        /// Сначала пробуем AssetDatabase (папка видна Unity), если не работает - используем Directory.Move
        /// </summary>
        private static bool DisableFolder(string folderPath)
        {
            string disabledPath = folderPath + "~";
            
            if (Directory.Exists(folderPath))
            {
                // Папка включена, отключаем её
                if (Directory.Exists(disabledPath))
                {
                    Debug.LogError($"[Module Folders] ❌ Both {folderPath} and {disabledPath} exist!");
                    return false;
                }

                Debug.Log($"[Module Folders] 🔄 Disabling folder: {folderPath} → {disabledPath}");
                
                // Пробуем через AssetDatabase (папка БЕЗ ~ видна Unity)
                string moveError = AssetDatabase.MoveAsset(folderPath, disabledPath);
                
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogWarning($"[Module Folders] ⚠ AssetDatabase.MoveAsset failed: {moveError}");
                    Debug.LogWarning($"[Module Folders] 🔄 Trying fallback: Directory.Move...");
                    
                    // Fallback: прямое переименование через файловую систему
                    try
                    {
                        Directory.Move(folderPath, disabledPath);
                        
                        // Переименовываем .meta файл
                        string enabledMeta = folderPath + ".meta";
                        string disabledMeta = disabledPath + ".meta";
                        if (File.Exists(enabledMeta))
                        {
                            if (File.Exists(disabledMeta))
                            {
                                File.Delete(disabledMeta);
                            }
                            File.Move(enabledMeta, disabledMeta);
                        }
                        
                        Debug.Log($"[Module Folders] ✅ Fallback successful");
                        return true;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Module Folders] ❌ Fallback also failed: {e.Message}");
                        Debug.LogError($"[Module Folders] ❌ Exception: {e.GetType().Name}");
                        return false;
                    }
                }
                
                // AssetDatabase успешно переименовал папку
                // .meta файл уже должен быть обработан AssetDatabase
                Debug.Log($"[Module Folders] ✅ Successfully disabled folder via AssetDatabase");
                return true;
            }

            return false; // Папка уже отключена
        }

        /// <summary>
        /// Проверяет текущее состояние папок модулей
        /// </summary>
        //[MenuItem("AMZN GoD/Tools/Check Module Folders Status", false, 411)]
        public static void CheckModuleFoldersStatus()
        {
            Debug.Log("=== AMZN GoD SDK - Module Folders Status ===");

            var settings = SdkSettingsManager.LoadSettings();

            CheckModuleFolderStatus("Cross-Promo", settings.CrossPromo.Enabled);
            CheckModuleFolderStatus("Adjust", settings.Adjust.Enabled);
            CheckModuleFolderStatus("AppMetrica", settings.AppMetrica.Enabled);
            CheckModuleFolderStatus("Firebase", settings.Firebase.Enabled);
            CheckModuleFolderStatus("Infatica", settings.Infatica.Enabled);
            if (settings.Infatica.Enabled)
                CheckInfaticaPluginsStatus(settings.Infatica.SdkVersion);
            CheckModuleFolderStatus("InAppPurchase", settings.InAppPurchase.Enabled);
            CheckModuleFolderStatus("InternetConnection", settings.InternetConnection.Enabled);

            Debug.Log("============================================");

            EditorUtility.DisplayDialog("Check Console",
                "Module folders status logged to Console.\n\nCheck Unity Console window.",
                "OK");
        }

        private static void CheckModuleFolderStatus(string moduleName, bool shouldBeEnabled)
        {
            if (!ModuleFolders.ContainsKey(moduleName))
                return;

            foreach (var folderPath in ModuleFolders[moduleName])
            {
                string disabledPath = folderPath + "~";
                
                bool isEnabled = Directory.Exists(folderPath);
                bool isDisabled = Directory.Exists(disabledPath);

                if (isEnabled && isDisabled)
                {
                    Debug.LogError($"  ⚠ {moduleName}: Both enabled and disabled folders exist!");
                }
                else if (isEnabled && shouldBeEnabled)
                {
                    Debug.Log($"  ✓ {moduleName}: Enabled (folder exists)");
                }
                else if (isDisabled && !shouldBeEnabled)
                {
                    Debug.Log($"  ✓ {moduleName}: Disabled (folder renamed with ~)");
                }
                else if (isEnabled && !shouldBeEnabled)
                {
                    Debug.LogWarning($"  ⚠ {moduleName}: Should be disabled but folder is active");
                }
                else if (isDisabled && shouldBeEnabled)
                {
                    Debug.LogWarning($"  ⚠ {moduleName}: Should be enabled but folder has ~");
                }
                else
                {
                    Debug.LogWarning($"  ⚠ {moduleName}: Folder not found!");
                }
            }
        }

        /// <summary>
        /// Автоматически вызывается при сохранении настроек
        /// </summary>
        public static void OnSettingsSaved(SdkSettingsData settings)
        {
            Debug.Log("[Module Folders] ===== OnSettingsSaved CALLED =====");
            Debug.Log($"[Module Folders] Infatica.Enabled = {settings.Infatica.Enabled}, SdkVersion = {settings.Infatica.SdkVersion}");
            
            // По умолчанию авто-обновление ВКЛЮЧЕНО (true)
            bool autoUpdate = EditorPrefs.GetBool("AMZN_AutoUpdateModuleFolders", true);
            Debug.Log($"[Module Folders] Auto-update setting: {autoUpdate}");
            
            if (autoUpdate)
            {
                Debug.Log("[Module Folders] ✓ Auto-update is ENABLED, starting update...");
                UpdateModuleFoldersInternal(settings);
                Debug.Log("[Module Folders] ===== Auto-update COMPLETED =====");
            }
            else
            {
                Debug.LogWarning("[Module Folders] ⚠ Auto-update is DISABLED. Use 'Update Module Folders' manually.");
            }
        }

        private static void UpdateModuleFoldersInternal(SdkSettingsData settings)
        {
            int updated = 0;
            
            // Обновляем папки по очереди
            Debug.Log("[Module Folders] Checking Cross-Promo...");
            if (UpdateModuleFolderState("Cross-Promo", settings.CrossPromo.Enabled)) updated++;
            
            Debug.Log("[Module Folders] Checking Adjust...");
            if (UpdateModuleFolderState("Adjust", settings.Adjust.Enabled)) updated++;
            
            Debug.Log("[Module Folders] Checking AppMetrica...");
            if (UpdateModuleFolderState("AppMetrica", settings.AppMetrica.Enabled)) updated++;
            
            Debug.Log("[Module Folders] Checking Firebase...");
            if (UpdateModuleFolderState("Firebase", settings.Firebase.Enabled)) updated++;
            
            Debug.Log("[Module Folders] Checking Infatica...");
            if (UpdateModuleFolderState("Infatica", settings.Infatica.Enabled)) updated++;

            if (settings.Infatica.Enabled && SwapInfaticaPlugins(settings.Infatica.SdkVersion))
                updated++;
            
            Debug.Log("[Module Folders] Checking InAppPurchase...");
            if (UpdateModuleFolderState("InAppPurchase", settings.InAppPurchase.Enabled)) updated++;
            
            Debug.Log("[Module Folders] Checking InternetConnection...");
            if (UpdateModuleFolderState("InternetConnection", settings.InternetConnection.Enabled)) updated++;
            
            if (updated > 0)
            {
                Debug.Log($"[Module Folders] ✅ Updated {updated} folder(s), refreshing AssetDatabase...");
                AssetDatabase.Refresh();
                Debug.Log($"[Module Folders] ✅ AssetDatabase refresh complete");
            }
            else
            {
                Debug.Log($"[Module Folders] ℹ All folders already in correct state");
            }
        }

        #region Infatica Plugin Swap

        private const string InfaticaPluginsPath = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins";
        private const string InfaticaStorageWithJobs = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithJobs~";
        private const string InfaticaStorageWithoutJobs = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithoutJobs~";

        /// <summary>
        /// Переключает набор нативных плагинов Infatica.
        /// Plugins/ всегда содержит активный набор (Unity видит только эту папку).
        /// Неактивный набор хранится в Plugins_WithJobs~ или Plugins_WithoutJobs~.
        /// </summary>
        private static bool SwapInfaticaPlugins(InfaticaSettingData.InfaticaSdkVersion targetVersion)
        {
            bool storageWithJobsExists = Directory.Exists(InfaticaStorageWithJobs);
            bool storageWithoutJobsExists = Directory.Exists(InfaticaStorageWithoutJobs);

            if (storageWithJobsExists && storageWithoutJobsExists)
            {
                Debug.LogError("[Module Folders] ❌ Infatica: both plugin storages exist — invalid state");
                return false;
            }

            bool pluginsExist = Directory.Exists(InfaticaPluginsPath);
            if (!pluginsExist)
            {
                Debug.LogWarning("[Module Folders] ⚠ Infatica: Plugins folder not found");
                return false;
            }

            if (targetVersion == InfaticaSettingData.InfaticaSdkVersion.WithJobs)
            {
                if (!storageWithJobsExists)
                    return false; // Plugins already has WithJobs

                return DoSwap(InfaticaStorageWithoutJobs, InfaticaStorageWithJobs, "WithJobs");
            }
            else
            {
                if (!storageWithoutJobsExists)
                    return false; // Plugins already has WithoutJobs

                return DoSwap(InfaticaStorageWithJobs, InfaticaStorageWithoutJobs, "WithoutJobs");
            }
        }

        private static bool DoSwap(string archiveCurrentAs, string restoreFrom, string label)
        {
            Debug.Log($"[Module Folders] 🔄 Swapping Infatica plugins → {label}");

            try
            {
                // 1. Архивируем текущий Plugins/ в хранилище
                Directory.Move(InfaticaPluginsPath, archiveCurrentAs);
                MoveMetaFile(InfaticaPluginsPath + ".meta", archiveCurrentAs + ".meta");

                // 2. Достаём нужный набор из хранилища в Plugins/
                Directory.Move(restoreFrom, InfaticaPluginsPath);
                MoveMetaFile(restoreFrom + ".meta", InfaticaPluginsPath + ".meta");

                Debug.Log($"[Module Folders] ✅ Infatica plugins switched to {label}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Module Folders] ❌ Infatica plugin swap failed: {e.Message}");
                return false;
            }
        }

        private static void MoveMetaFile(string from, string to)
        {
            if (!File.Exists(from))
                return;

            if (File.Exists(to))
                File.Delete(to);

            File.Move(from, to);
        }

        private static void CheckInfaticaPluginsStatus(InfaticaSettingData.InfaticaSdkVersion targetVersion)
        {
            bool storageWithJobsExists = Directory.Exists(InfaticaStorageWithJobs);
            bool storageWithoutJobsExists = Directory.Exists(InfaticaStorageWithoutJobs);

            string currentInPlugins = storageWithoutJobsExists ? "WithJobs"
                : storageWithJobsExists ? "WithoutJobs"
                : "Unknown";

            bool isCorrect = (targetVersion == InfaticaSettingData.InfaticaSdkVersion.WithJobs && currentInPlugins == "WithJobs")
                || (targetVersion == InfaticaSettingData.InfaticaSdkVersion.WithoutJobs && currentInPlugins == "WithoutJobs");

            if (isCorrect)
                Debug.Log($"  ✓ Infatica Plugins: {currentInPlugins} (correct)");
            else
                Debug.LogWarning($"  ⚠ Infatica Plugins: {currentInPlugins} in Plugins/, expected {targetVersion}");
        }

        #endregion

        /// <summary>
        /// Настройки автообновления папок
        /// </summary>
        //[MenuItem("AMZN GoD/Settings/Auto-Update Module Folders", false, 1000)]
        public static void ToggleAutoUpdateFolders()
        {
            bool current = EditorPrefs.GetBool("AMZN_AutoUpdateModuleFolders", true);
            bool newValue = !current;
            EditorPrefs.SetBool("AMZN_AutoUpdateModuleFolders", newValue);

            string status = newValue ? "ENABLED" : "DISABLED";
            Debug.Log($"[Module Folders] Auto-update {status}");
            
            EditorUtility.DisplayDialog("Auto-Update Module Folders",
                $"Auto-update is now {status}.\n\n" +
                (newValue 
                    ? "Module folders will be automatically renamed when you save SDK settings." 
                    : "You'll need to manually run 'Update Module Folders' from Tools menu."),
                "OK");
        }

        //[MenuItem("AMZN GoD/Settings/Auto-Update Module Folders", true)]
        public static bool ToggleAutoUpdateFoldersValidate()
        {
            bool isEnabled = EditorPrefs.GetBool("AMZN_AutoUpdateModuleFolders", true);
            Menu.SetChecked("AMZN GoD/Settings/Auto-Update Module Folders", isEnabled);
            return true;
        }
        
        /// <summary>
        /// Принудительно синхронизирует папки модулей с текущими настройками
        /// </summary>
        //[MenuItem("AMZN GoD/Tools/Force Sync Module Folders", false, 412)]
        public static void ForceSyncModuleFolders()
        {
            var settings = SdkSettingsManager.LoadSettings();
            
            Debug.Log("=== AMZN GoD SDK - Force Syncing Module Folders ===");
            UpdateModuleFoldersInternal(settings);
            Debug.Log("=== Force Sync Complete ===");
            
            EditorUtility.DisplayDialog("Force Sync",
                "Module folders have been synchronized with current settings.\n\n" +
                "Check Console for details.",
                "OK");
        }
    }
}
