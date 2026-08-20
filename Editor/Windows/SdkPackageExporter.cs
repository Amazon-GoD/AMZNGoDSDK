using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Экспортирует основной SDK-пакет (ExportPackage): раскрывает скрытые папки
    /// модулей, временно убирает конфиг и формирует .unitypackage.
    /// </summary>
    public static class SdkPackageExporter
    {
        private const string SdkRoot = "Assets/AMZNGoDSDK";
        private const string ConfigPath = "Assets/Resources/amzn_god_sdk.json";
        private const string ConfigMetaPath = "Assets/Resources/amzn_god_sdk.json.meta";

        /// <summary>
        /// SessionState guard. While an export is running we deliberately
        /// move/Refresh/ImportAsset module folders — that fires
        /// SdkImportPostprocessor.OnPostprocessAllAssets, which would mistake
        /// it for a real package import. The postprocessor checks this flag and bails.
        /// SessionState survives domain reloads, clears on editor restart.
        /// </summary>
        internal const string ExportInProgressKey = "AMZN_SDK_EXPORT_IN_PROGRESS";

        private static readonly string[] ModuleFolders =
        {
            "Assets/AMZNGoDSDK/Runtime/Modules/Adjust",
            "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica",
            "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo",
            "Assets/AMZNGoDSDK/Runtime/Modules/Firebase",
            "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase",
            "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection",
            "Assets/AMZNGoDSDK/Runtime/Modules/InGameDebugConsole",
            "Assets/AMZNGoDSDK/Runtime/Modules/Analytics",
        };

        /// <summary>
        /// Внутренний/тестовый контент, которому не место в поставляемом пакете.
        /// На время экспорта папки прячутся суффиксом ~ (Unity их перестаёт видеть),
        /// в finally возвращаются на место. Пути указываются в видимом (без ~) виде.
        /// Инцидент 2026-08-20: IAPTestPanel уехала в деплой, потому что экспорт
        /// брал весь SdkRoot рекурсивно без исключений.
        /// </summary>
        private static readonly string[] ExcludedFolders =
        {
            "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase/Testing",
        };

        /// <summary>
        /// Фрагменты, которых не должно быть ни в одном asset-пути готового пакета.
        /// Пост-экспортная проверка (SdkPackageVerifier) блокирует экспорт при находке —
        /// страховка на случай, если тестовый файл переедет мимо ExcludedFolders.
        /// </summary>
        private static readonly string[] ForbiddenPathFragments =
        {
            "/InAppPurchase/Testing",
            "IAPTestPanel",
            "SimulatedAmazonStore",
        };

        [MenuItem("AMZN GoD/Export SDK Package", false, 50)]
        public static void ExportPackage()
        {
            string savePath = EditorUtility.SaveFilePanel(
                "Export AMZN GoD SDK",
                "",
                "AMZNGoDSDK",
                "unitypackage");

            if (string.IsNullOrEmpty(savePath))
                return;

            if (ExportPackageToPath(savePath, out string error))
            {
                Debug.Log($"[AMZN GoD SDK] Package exported to: {savePath}");
                EditorUtility.DisplayDialog("Export Complete",
                    "SDK exported successfully!\n\n" +
                    "Config file: excluded\n" +
                    "Test content (IAP Testing): excluded\n" +
                    "Module folders: all included\n\n" +
                    "On import, the Setup Wizard will guide the user.",
                    "OK");
            }
            else
            {
                Debug.LogError($"[AMZN GoD SDK] Export failed: {error}");
                EditorUtility.DisplayDialog("Export Failed", error, "OK");
            }
        }

        /// <summary>
        /// Non-interactive export: writes .unitypackage to savePath, returns success.
        /// Same preparation flow as ExportPackage() (unhides module ~ folders, hides
        /// runtime config), but no dialogs — meant for scripted/CI deploy pipelines.
        /// </summary>
        public static bool ExportPackageToPath(string savePath, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(savePath))
            {
                error = "Save path is empty.";
                return false;
            }

            var hiddenFolders = new List<string>();
            var hiddenExcluded = new List<string>();
            string backupConfigPath = null;

            SessionState.SetBool(ExportInProgressKey, true);
            EditorApplication.LockReloadAssemblies();

            try
            {
                // 1. Раскрываем скрытые папки модулей (убираем ~).
                // С Фазы 3 UPM-перехода модули ВСЕГДА видимы (folder-rename
                // тогглы выведены из эксплуатации), так что цикл в норме no-op.
                // Оставлен как защита: если ~папка модуля всё же появится
                // (старое рабочее дерево, ручное скрытие), экспорт её раскроет.
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

                // 1.5. Прячем тестовый контент — он не должен попасть в пакет.
                // Meta папки уводим в Temp/ (вне Assets), а не в "~.meta": скрытая
                // ~папка с лежащим рядом ~.meta экспортируется как пустая folder-запись
                // (так в пакет 2026-08-20 утекли записи Adjust~, Infatica~ и др.),
                // а backup внутри SdkRoot сам попал бы в пакет как DefaultAsset.
                foreach (var folder in ExcludedFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(folder) && !Directory.Exists(hidden))
                    {
                        Directory.Move(folder, hidden);
                        StashMetaOutsideAssets(folder);
                        hiddenExcluded.Add(folder);
                    }
                }

                // 2. Временно убираем конфиг
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

                // 3. Регистрируем изменения.
                // Refresh() нужен первым: папки перемещённые из ~ не имеют .meta (Unity их игнорировал),
                // Refresh создаёт .meta для новых/перемещённых папок, после чего ImportAsset их видит.
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(SdkRoot, ImportAssetOptions.ImportRecursive);

                // 4. Экспортируем SDK
                AssetDatabase.ExportPackage(new[] { SdkRoot }, savePath, ExportPackageOptions.Recurse);

                if (!File.Exists(savePath))
                {
                    error = $"AssetDatabase.ExportPackage did not produce a file at {savePath}.";
                    return false;
                }

                // 4.5. Контроль содержимого: если тестовый контент всё же попал в пакет,
                // деплой останавливается здесь, а не у партнёра.
                if (!SdkPackageVerifier.VerifyNoForbiddenPaths(savePath, ForbiddenPathFragments, out string verifyError))
                {
                    File.Delete(savePath);
                    error = $"Export blocked by content check: {verifyError}";
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                // Освобождаем файловые хэндлы Unity перед перемещением папок.
                // AssetDatabase.ImportAsset держит хэндлы на файлы внутри SdkRoot,
                // из-за чего Directory.Move падает с "Access denied" на Windows.
                AssetDatabase.ReleaseCachedFileHandles();

                // 4.9. Возвращаем спрятанный тестовый контент. Строго ДО скрытия модулей:
                // путь Testing лежит внутри видимой папки модуля, после переименования
                // родителя в ~ восстановление по сохранённому пути уже не сработает.
                foreach (var folder in hiddenExcluded)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(hidden) && !Directory.Exists(folder))
                    {
                        Directory.Move(hidden, folder);
                        RestoreStashedMeta(folder);
                    }
                }

                // 5. Восстанавливаем скрытые папки модулей
                foreach (var folder in hiddenFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(folder) && !Directory.Exists(hidden))
                    {
                        Directory.Move(folder, hidden);
                        MoveMeta(folder, hidden);
                    }
                }

                // 6. Восстанавливаем конфиг
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
                SessionState.SetBool(ExportInProgressKey, false);
            }
        }

        /// <summary>
        /// Папка Temp живёт только пока открыт Editor — при краше мид-экспорта
        /// теряется лишь GUID самой папки (на folder-GUID никто не ссылается),
        /// метаданные файлов внутри едут вместе с папкой и не затрагиваются.
        /// </summary>
        private const string MetaStashDir = "Temp/AMZNGoDSDK_ExportMetaStash";

        private static string StashPathFor(string folderPath) =>
            Path.Combine(MetaStashDir, folderPath.Replace('/', '_').Replace('\\', '_') + ".meta");

        private static void StashMetaOutsideAssets(string folderPath)
        {
            string meta = folderPath + ".meta";
            if (!File.Exists(meta))
                return;

            Directory.CreateDirectory(MetaStashDir);
            string stash = StashPathFor(folderPath);
            if (File.Exists(stash)) File.Delete(stash);
            File.Move(meta, stash);
        }

        private static void RestoreStashedMeta(string folderPath)
        {
            string stash = StashPathFor(folderPath);
            if (!File.Exists(stash))
                return;

            string meta = folderPath + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            File.Move(stash, meta);
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
