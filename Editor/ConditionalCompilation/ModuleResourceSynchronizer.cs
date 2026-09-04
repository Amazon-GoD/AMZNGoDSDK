using System;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Resources нельзя отключить defineConstraint'ом: Unity всегда кладёт их в
    /// Player. Поэтому исходный prefab InternetConnection хранится вне Resources,
    /// а сюда копируется только пока модуль включён.
    /// </summary>
    public static class ModuleResourceSynchronizer
    {
        private const string SourceRelativePath =
            "Runtime/Modules/InternetConnection/ModuleAssets/AMZNGoDSDK/OfflineBanner.prefab";
        public const string GeneratedResourcePath =
            "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK/OfflineBanner.prefab";

        public static void Synchronize(SdkSettingsData settings)
        {
            bool enabled = settings != null
                           && settings.Enabled
                           && settings.InternetConnection != null
                           && settings.InternetConnection.Enabled;

            if (!enabled)
            {
                if (AssetDatabase.LoadMainAssetAtPath(GeneratedResourcePath) != null)
                    AssetDatabase.DeleteAsset(GeneratedResourcePath);
                return;
            }

            string source = ResolveSourceAssetPath();
            if (source == null)
            {
                Debug.LogError($"[AMZN GoD SDK] Не найден ресурс модуля: {SourceRelativePath}");
                return;
            }

            EnsureFolder("Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK");
            var current = AssetDatabase.LoadMainAssetAtPath(GeneratedResourcePath);
            if (current != null &&
                AssetDatabase.GetAssetDependencyHash(source) ==
                AssetDatabase.GetAssetDependencyHash(GeneratedResourcePath))
                return;

            if (current != null)
                AssetDatabase.DeleteAsset(GeneratedResourcePath);
            if (!AssetDatabase.CopyAsset(source, GeneratedResourcePath))
                Debug.LogError($"[AMZN GoD SDK] Не удалось создать {GeneratedResourcePath}");
        }

        private static string ResolveSourceAssetPath()
        {
            foreach (string root in NativePluginRegistry.SdkRootPrefixes)
            {
                string path = root + SourceRelativePath;
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    return path;
            }
            return null;
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
