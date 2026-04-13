using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Экспортирует SDK в двух режимах:
    ///
    ///   1. ExportPackage            — основной SDK-пакет.
    ///      Infatica: только Plugins/ (WithoutJobs). Plugins_WithJobs в пакет НЕ включается.
    ///
    ///   2. ExportWithJobsPackage    — аддон-пакет только с Plugins_WithJobs/.
    ///      Импортируется поверх основного SDK; постпроцессор автоматически активирует
    ///      WithJobs-плагины (Plugins/ ← WithJobs, WithoutJobs уходит в хранилище).
    /// </summary>
    public static class SdkPackageExporter
    {
        private const string SdkRoot = "Assets/AMZNGoDSDK";
        private const string ConfigPath = "Assets/Resources/amzn_god_sdk.json";
        private const string ConfigMetaPath = "Assets/Resources/amzn_god_sdk.json.meta";

        private const string InfaticaFolder          = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica";
        private const string InfaticaPlugins          = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins";
        private const string InfaticaWithJobsStorage  = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithJobs~";
        private const string InfaticaWithoutJobsStorage = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithoutJobs~";
        private const string InfaticaWithJobsExport   = "Assets/AMZNGoDSDK/Runtime/Modules/Infatica/Plugins_WithJobs";

        private static readonly string[] ModuleFolders =
        {
            "Assets/AMZNGoDSDK/Runtime/Modules/Adjust",
            "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica",
            "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo",
            "Assets/AMZNGoDSDK/Runtime/Modules/Firebase",
            "Assets/AMZNGoDSDK/Runtime/Modules/Infatica",
            "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase",
            "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection",
            "Assets/AMZNGoDSDK/Runtime/Modules/InGameDebugConsole",
        };

        // ─────────────────────────────────────────────────────────────────────
        //  ОСНОВНОЙ ПАКЕТ  (WithoutJobs only, без Plugins_WithJobs)
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("AMZN GoD/Export SDK Package", false, 50)]
        public static void ExportPackage()
        {
            var hiddenFolders = new List<string>();
            string backupConfigPath = null;
            bool infaticaDidSwapToWithoutJobs = false;

            EditorApplication.LockReloadAssemblies();

            try
            {
                // 1. Раскрываем скрытые папки модулей (убираем ~)
                foreach (var folder in ModuleFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(hidden) && !Directory.Exists(folder))
                    {
                        Directory.Move(hidden, folder);
                        MoveMeta(hidden, folder);
                        hiddenFolders.Add(folder);
                    }
                }

                // 2. Если активен WithJobs (Plugins/ = WithJobs, Plugins_WithoutJobs~ = хранилище),
                //    временно переключаемся на WithoutJobs для экспорта.
                //    Plugins_WithJobs~ остаётся скрытой и в пакет НЕ попадёт.
                if (Directory.Exists(InfaticaPlugins) && Directory.Exists(InfaticaWithoutJobsStorage))
                {
                    Directory.Move(InfaticaPlugins, InfaticaWithJobsStorage);
                    MoveMeta(InfaticaPlugins, InfaticaWithJobsStorage);

                    Directory.Move(InfaticaWithoutJobsStorage, InfaticaPlugins);
                    MoveMeta(InfaticaWithoutJobsStorage, InfaticaPlugins);

                    infaticaDidSwapToWithoutJobs = true;
                }
                // Если WithoutJobs уже активен (Plugins_WithJobs~ есть) — ничего делать не нужно.

                // 3. Временно убираем конфиг
                if (File.Exists(ConfigPath))
                {
                    backupConfigPath = ConfigPath + ".export_backup";
                    if (File.Exists(backupConfigPath)) File.Delete(backupConfigPath);
                    File.Move(ConfigPath, backupConfigPath);

                    if (File.Exists(ConfigMetaPath))
                    {
                        string backupMetaPath = backupConfigPath + ".meta";
                        if (File.Exists(backupMetaPath)) File.Delete(backupMetaPath);
                        File.Move(ConfigMetaPath, backupMetaPath);
                    }
                }

                // 4. Регистрируем изменения.
                // Refresh() нужен первым: папки перемещённые из ~ не имеют .meta (Unity их игнорировал),
                // Refresh создаёт .meta для новых/перемещённых папок, после чего ImportAsset их видит.
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(SdkRoot, ImportAssetOptions.ImportRecursive);

                // 5. Выбираем путь для сохранения
                string savePath = EditorUtility.SaveFilePanel(
                    "Export AMZN GoD SDK",
                    "",
                    "AMZNGoDSDK",
                    "unitypackage");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // 6. Экспортируем SDK (Plugins/ = WithoutJobs, Plugins_WithJobs~ скрыта и не войдёт)
                AssetDatabase.ExportPackage(new[] { SdkRoot }, savePath, ExportPackageOptions.Recurse);

                Debug.Log($"[AMZN GoD SDK] Package exported to: {savePath}");

                EditorUtility.DisplayDialog("Export Complete",
                    "SDK exported successfully!\n\n" +
                    "Config file: excluded\n" +
                    "Module folders: all included\n" +
                    "Infatica: Plugins/ (WithoutJobs only — WithJobs not included)\n\n" +
                    "On import, the Setup Wizard will guide the user.\n" +
                    "To add WithJobs support, use: AMZN GoD → Export Infatica WithJobs Module",
                    "OK");
            }
            finally
            {
                // Освобождаем файловые хэндлы Unity перед перемещением папок.
                // AssetDatabase.ImportAsset держит хэндлы на файлы внутри SdkRoot,
                // из-за чего Directory.Move падает с "Access denied" на Windows.
                AssetDatabase.ReleaseCachedFileHandles();

                // 7. Откатываем своп Infatica если делали
                if (infaticaDidSwapToWithoutJobs)
                {
                    if (Directory.Exists(InfaticaPlugins))
                    {
                        Directory.Move(InfaticaPlugins, InfaticaWithoutJobsStorage);
                        MoveMeta(InfaticaPlugins, InfaticaWithoutJobsStorage);
                    }
                    if (Directory.Exists(InfaticaWithJobsStorage))
                    {
                        Directory.Move(InfaticaWithJobsStorage, InfaticaPlugins);
                        MoveMeta(InfaticaWithJobsStorage, InfaticaPlugins);
                    }
                }

                // 8. Восстанавливаем скрытые папки модулей
                foreach (var folder in hiddenFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(folder) && !Directory.Exists(hidden))
                    {
                        Directory.Move(folder, hidden);
                        MoveMeta(folder, hidden);
                    }
                }

                // 9. Восстанавливаем конфиг
                if (backupConfigPath != null && File.Exists(backupConfigPath))
                {
                    if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                    File.Move(backupConfigPath, ConfigPath);

                    string backupMeta = backupConfigPath + ".meta";
                    if (File.Exists(backupMeta))
                    {
                        if (File.Exists(ConfigMetaPath)) File.Delete(ConfigMetaPath);
                        File.Move(backupMeta, ConfigMetaPath);
                    }
                }

                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  АДДОН WithJobs  (только Plugins_WithJobs/, импортируется поверх SDK)
        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("AMZN GoD/Export Infatica WithJobs Module", false, 51)]
        public static void ExportWithJobsPackage()
        {
            bool infaticaWasHidden   = false;
            bool withJobsMadeVisible = false;  // Plugins_WithJobs~ → Plugins_WithJobs/
            bool withJobsFromActive  = false;  // Plugins/ (WithJobs active) → Plugins_WithJobs/ (temp)

            EditorApplication.LockReloadAssemblies();

            try
            {
                // 1. Раскрываем папку Infatica-модуля если скрыта
                string infaticaHidden = InfaticaFolder + "~";
                if (Directory.Exists(infaticaHidden) && !Directory.Exists(InfaticaFolder))
                {
                    Directory.Move(infaticaHidden, InfaticaFolder);
                    MoveMeta(infaticaHidden, InfaticaFolder);
                    infaticaWasHidden = true;
                }

                // 2. Делаем WithJobs-плагины видимой папкой Plugins_WithJobs/ для экспорта
                bool withoutJobsIsActive = Directory.Exists(InfaticaWithJobsStorage);    // Plugins/ = WithoutJobs
                bool withJobsIsActive    = Directory.Exists(InfaticaWithoutJobsStorage); // Plugins/ = WithJobs

                if (withoutJobsIsActive)
                {
                    // Раскрываем хранилище: Plugins_WithJobs~ → Plugins_WithJobs/
                    Directory.Move(InfaticaWithJobsStorage, InfaticaWithJobsExport);
                    MoveMeta(InfaticaWithJobsStorage, InfaticaWithJobsExport);
                    withJobsMadeVisible = true;
                }
                else if (withJobsIsActive)
                {
                    // Plugins/ уже содержит WithJobs — временно переименовываем: Plugins/ → Plugins_WithJobs/
                    Directory.Move(InfaticaPlugins, InfaticaWithJobsExport);
                    MoveMeta(InfaticaPlugins, InfaticaWithJobsExport);
                    withJobsFromActive = true;
                }
                else
                {
                    EditorUtility.DisplayDialog("Export Failed",
                        "Не удалось определить WithJobs-плагины.\n" +
                        "Убедитесь, что Infatica-модуль настроен корректно.",
                        "OK");
                    return;
                }

                // 3. Регистрируем новую папку
                AssetDatabase.ImportAsset(InfaticaWithJobsExport, ImportAssetOptions.ImportRecursive);

                // 4. Выбираем путь
                string savePath = EditorUtility.SaveFilePanel(
                    "Export Infatica WithJobs Module",
                    "",
                    "AMZNGoDSDK_Infatica_WithJobs",
                    "unitypackage");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // 5. Экспортируем только Plugins_WithJobs/
                AssetDatabase.ExportPackage(
                    new[] { InfaticaWithJobsExport },
                    savePath,
                    ExportPackageOptions.Recurse);

                Debug.Log($"[AMZN GoD SDK] Infatica WithJobs module exported to: {savePath}");

                EditorUtility.DisplayDialog("Export Complete",
                    $"Infatica WithJobs module exported!\n\n" +
                    "Import this package on top of the main SDK to enable WithJobs plugins.\n" +
                    "The postprocessor will activate them automatically.",
                    "OK");
            }
            finally
            {
                AssetDatabase.ReleaseCachedFileHandles();

                // 6. Откатываем состояние папок
                if (withJobsMadeVisible && Directory.Exists(InfaticaWithJobsExport))
                {
                    Directory.Move(InfaticaWithJobsExport, InfaticaWithJobsStorage);
                    MoveMeta(InfaticaWithJobsExport, InfaticaWithJobsStorage);
                }
                else if (withJobsFromActive && Directory.Exists(InfaticaWithJobsExport))
                {
                    Directory.Move(InfaticaWithJobsExport, InfaticaPlugins);
                    MoveMeta(InfaticaWithJobsExport, InfaticaPlugins);
                }

                if (infaticaWasHidden && Directory.Exists(InfaticaFolder))
                {
                    Directory.Move(InfaticaFolder, InfaticaFolder + "~");
                    MoveMeta(InfaticaFolder, InfaticaFolder + "~");
                }

                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        // Copy-then-delete instead of Move so we never race with Unity's file watcher
        // recreating the destination .meta between our Delete and Move calls.
        private static void MoveMeta(string fromPath, string toPath)
        {
            string from = fromPath + ".meta";
            string to   = toPath   + ".meta";

            if (!File.Exists(from))
                return;

            File.Copy(from, to, overwrite: true);
            File.Delete(from);
        }
    }
}
