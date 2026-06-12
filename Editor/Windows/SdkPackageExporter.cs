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

        [MenuItem("AMZN GoD/Export SDK Package", false, 50)]
        public static void ExportPackage()
        {
            var hiddenFolders = new List<string>();
            string backupConfigPath = null;

            SessionState.SetBool(ExportInProgressKey, true);
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

                // 4. Выбираем путь для сохранения
                string savePath = EditorUtility.SaveFilePanel(
                    "Export AMZN GoD SDK",
                    "",
                    "AMZNGoDSDK",
                    "unitypackage");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // 5. Экспортируем SDK
                AssetDatabase.ExportPackage(new[] { SdkRoot }, savePath, ExportPackageOptions.Recurse);

                Debug.Log($"[AMZN GoD SDK] Package exported to: {savePath}");

                EditorUtility.DisplayDialog("Export Complete",
                    "SDK exported successfully!\n\n" +
                    "Config file: excluded\n" +
                    "Module folders: all included\n\n" +
                    "On import, the Setup Wizard will guide the user.",
                    "OK");
            }
            finally
            {
                // Освобождаем файловые хэндлы Unity перед перемещением папок.
                // AssetDatabase.ImportAsset держит хэндлы на файлы внутри SdkRoot,
                // из-за чего Directory.Move падает с "Access denied" на Windows.
                AssetDatabase.ReleaseCachedFileHandles();

                // 6. Восстанавливаем скрытые папки модулей
                foreach (var folder in hiddenFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(folder) && !Directory.Exists(hidden))
                    {
                        Directory.Move(folder, hidden);
                        MoveMeta(folder, hidden);
                    }
                }

                // 7. Восстанавливаем конфиг
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
