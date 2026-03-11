using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Exports the SDK as a .unitypackage with a clean state:
    /// all module folders visible, no config file, no defines.
    /// This ensures the Setup Wizard runs on first import in the target project.
    /// </summary>
    public static class SdkPackageExporter
    {
        private const string SdkRoot = "Assets/AMZNGoDSDK";
        private const string ConfigPath = "Assets/Resources/amzn_god_sdk.json";
        private const string ConfigMetaPath = "Assets/Resources/amzn_god_sdk.json.meta";

        private static readonly string[] ModuleFolders =
        {
            "Assets/AMZNGoDSDK/Runtime/Modules/Adjust",
            "Assets/AMZNGoDSDK/Runtime/Modules/AppMetrica",
            "Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo",
            "Assets/AMZNGoDSDK/Runtime/Modules/Firebase",
            "Assets/AMZNGoDSDK/Runtime/Modules/Infatica",
            "Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase",
            "Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection",
        };

        [MenuItem("AMZN GoD/Export SDK Package", false, 50)]
        public static void ExportPackage()
        {
            var hiddenFolders = new List<string>();
            string backupConfigPath = null;

            try
            {
                // 1. Unhide all module folders (remove ~ suffix)
                foreach (var folder in ModuleFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(hidden) && !Directory.Exists(folder))
                    {
                        Directory.Move(hidden, folder);
                        string hiddenMeta = hidden + ".meta";
                        string folderMeta = folder + ".meta";
                        if (File.Exists(hiddenMeta) && !File.Exists(folderMeta))
                            File.Move(hiddenMeta, folderMeta);

                        hiddenFolders.Add(folder);
                    }
                }

                // 2. Temporarily move config file out of the way
                if (File.Exists(ConfigPath))
                {
                    backupConfigPath = ConfigPath + ".export_backup";
                    File.Move(ConfigPath, backupConfigPath);
                    if (File.Exists(ConfigMetaPath))
                        File.Move(ConfigMetaPath, backupConfigPath + ".meta");
                }

                AssetDatabase.Refresh();

                // 3. Collect all assets under SDK root
                var assets = AssetDatabase.GetAllAssetPaths()
                    .Where(p => p.StartsWith(SdkRoot + "/") || p == SdkRoot)
                    .Where(p => !p.Contains("~"))
                    .ToArray();

                if (assets.Length == 0)
                {
                    EditorUtility.DisplayDialog("Export Error",
                        $"No assets found under {SdkRoot}.", "OK");
                    return;
                }

                // 4. Ask where to save
                string savePath = EditorUtility.SaveFilePanel(
                    "Export AMZN GoD SDK",
                    "",
                    "AMZNGoDSDK",
                    "unitypackage");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // 5. Export
                AssetDatabase.ExportPackage(assets, savePath, ExportPackageOptions.Recurse);

                Debug.Log($"[AMZN GoD SDK] Package exported to: {savePath}");
                Debug.Log($"[AMZN GoD SDK] Included {assets.Length} assets, config file excluded.");

                EditorUtility.DisplayDialog("Export Complete",
                    $"SDK exported successfully!\n\n" +
                    $"Assets: {assets.Length}\n" +
                    $"Config file: excluded\n" +
                    $"Module folders: all included\n\n" +
                    $"On import, the Setup Wizard will guide the user.",
                    "OK");
            }
            finally
            {
                // 6. Restore hidden folders
                foreach (var folder in hiddenFolders)
                {
                    string hidden = folder + "~";
                    if (Directory.Exists(folder) && !Directory.Exists(hidden))
                    {
                        Directory.Move(folder, hidden);
                        string folderMeta = folder + ".meta";
                        string hiddenMeta = hidden + ".meta";
                        if (File.Exists(folderMeta) && !File.Exists(hiddenMeta))
                            File.Move(folderMeta, hiddenMeta);
                    }
                }

                // 7. Restore config file
                if (backupConfigPath != null && File.Exists(backupConfigPath))
                {
                    File.Move(backupConfigPath, ConfigPath);
                    string backupMeta = backupConfigPath + ".meta";
                    if (File.Exists(backupMeta))
                        File.Move(backupMeta, ConfigMetaPath);
                }

                AssetDatabase.Refresh();
            }
        }
    }
}
