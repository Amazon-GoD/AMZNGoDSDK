using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Prefab'ы опциональных модулей нельзя держать в общем SDK prefab: при снятии
    /// asmdef defineConstraint Unity теряет типы компонентов уже в Editor и может
    /// повредить сериализованные prefab instances. Исходники хранятся как неимпортируемые
    /// *.prefab.template и копируются в generated Resources только после компиляции
    /// соответствующей module assembly.
    /// </summary>
    public static class ModuleResourceSynchronizer
    {
        private const string GeneratedRoot = "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK";

        public const string GeneratedInternetResourcePath = GeneratedRoot + "/OfflineBanner.prefab";
        public const string GeneratedCrossPromoResourcePath = GeneratedRoot + "/CrossPromoBanner.prefab";
        public const string GeneratedDebugConsoleResourcePath = GeneratedRoot + "/IngameDebugConsole.prefab";

        private sealed class ResourceSpec
        {
            public string Name;
            public string Define;
            public string SourceRelativePath;
            public string GeneratedPath;
        }

        private static readonly ResourceSpec[] Resources =
        {
            new ResourceSpec
            {
                Name = "InternetConnection",
                Define = ModuleDefineManager.INTERNETCONNECTION_DEFINE,
                SourceRelativePath =
                    "Runtime/Modules/InternetConnection/ModuleAssets/AMZNGoDSDK/OfflineBanner.prefab.template",
                GeneratedPath = GeneratedInternetResourcePath,
            },
            new ResourceSpec
            {
                Name = "Cross-Promo",
                Define = ModuleDefineManager.CROSSPROMO_DEFINE,
                SourceRelativePath =
                    "Runtime/Modules/Cross-Promo/ModuleAssets/CrossPromoBanner.prefab.template",
                GeneratedPath = GeneratedCrossPromoResourcePath,
            },
            new ResourceSpec
            {
                Name = "InGameDebugConsole",
                Define = ModuleDefineManager.DEBUGCONSOLE_DEFINE,
                SourceRelativePath =
                    "Runtime/Modules/InGameDebugConsole/ModuleAssets/IngameDebugConsole.prefab.template",
                GeneratedPath = GeneratedDebugConsoleResourcePath,
            },
        };

        [InitializeOnLoadMethod]
        private static void SynchronizeAfterAssemblyReload()
        {
            // После добавления define prefab создаётся только здесь: к этому моменту
            // module assembly уже загружена, и импорт не порождает Missing Script.
            EditorApplication.delayCall += () => Synchronize(SdkSettingsManager.LoadSettings());
        }

        public static void Synchronize(SdkSettingsData settings)
        {
            var enabledMap = settings == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : ModuleManifestRegistry.GetModuleEnabledMap(settings);

            foreach (ResourceSpec resource in Resources)
            {
                bool requested = enabledMap.TryGetValue(resource.Name, out bool enabled) && enabled;
                bool assemblyAvailable = ModuleDefineManager.IsModuleEnabled(resource.Define);

                if (!requested || !assemblyAvailable)
                {
                    DeleteGenerated(resource.GeneratedPath);
                    continue;
                }

                CopyGenerated(resource);
            }

            DeleteGeneratedFoldersIfEmpty();
        }

        private static void CopyGenerated(ResourceSpec resource)
        {
            string source = ResolveSourceFile(resource.SourceRelativePath);
            if (source == null)
            {
                Debug.LogError($"[AMZN GoD SDK] Не найден шаблон ресурса модуля: {resource.SourceRelativePath}");
                return;
            }

            EnsureFolder(GeneratedRoot);
            string destination = Path.GetFullPath(resource.GeneratedPath);
            if (File.Exists(destination) && FilesEqual(source, destination))
                return;

            DeleteGenerated(resource.GeneratedPath);
            File.Copy(source, destination, true);
            AssetDatabase.ImportAsset(resource.GeneratedPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[AMZN GoD SDK] Generated module resource: {resource.GeneratedPath}");
        }

        private static void DeleteGenerated(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
                return;
            }

            string physicalPath = Path.GetFullPath(assetPath);
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
            if (File.Exists(physicalPath + ".meta"))
                File.Delete(physicalPath + ".meta");
        }

        private static void DeleteGeneratedFoldersIfEmpty()
        {
            DeleteFolderIfEmpty(GeneratedRoot);
            DeleteFolderIfEmpty("Assets/AMZNGoDSDKGenerated/Resources");
        }

        private static void DeleteFolderIfEmpty(string assetPath)
        {
            string physicalPath = FileUtil.GetPhysicalPath(assetPath);
            if (string.IsNullOrEmpty(physicalPath) || !Directory.Exists(physicalPath))
                return;

            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(physicalPath).GetEnumerator())
            {
                if (entries.MoveNext())
                    return;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
            else
                Directory.Delete(physicalPath);
        }

        private static string ResolveSourceFile(string relativePath)
        {
            foreach (string root in NativePluginRegistry.SdkRootPrefixes)
            {
                string physical = FileUtil.GetPhysicalPath(root + relativePath);
                if (!string.IsNullOrEmpty(physical) && File.Exists(physical))
                    return physical;
            }

            return null;
        }

        private static bool FilesEqual(string left, string right)
        {
            var leftInfo = new FileInfo(left);
            var rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length)
                return false;

            const int bufferSize = 81920;
            var leftBuffer = new byte[bufferSize];
            var rightBuffer = new byte[bufferSize];
            using (var leftStream = File.OpenRead(left))
            using (var rightStream = File.OpenRead(right))
            {
                while (true)
                {
                    int leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
                    int rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
                    if (leftRead != rightRead)
                        return false;
                    if (leftRead == 0)
                        return true;

                    for (int i = 0; i < leftRead; i++)
                    {
                        if (leftBuffer[i] != rightBuffer[i])
                            return false;
                    }
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
