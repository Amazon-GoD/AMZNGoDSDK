using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Убирает модульные компоненты и prefab-инстансы из копии build-сцены.
    /// Исходные сцены/префабы не изменяются. Это закрывает сериализованные ссылки
    /// старого общего AmznGoDSDK.prefab на Cross-Promo и debug-console assets.
    /// </summary>
    public sealed class DisabledModuleSceneStripper : IProcessSceneWithReport
    {
        public int callbackOrder => -1000;

        private static readonly Dictionary<string, string> LegacyPrefabOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AmznGoDSDK/SDKPrefab/Banner.prefab", "Cross-Promo" },
                { "AmznGoDSDK/SDKPrefab/InternetConnectChecker.prefab", "InternetConnection" },
            };

        private static readonly Dictionary<string, string> LegacyGameObjectOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AdjustModule", "Adjust" },
                { "AppmetricaModule", "AppMetrica" },
                { "CrossPromoModule", "Cross-Promo" },
                { "SubscriptionModule", "InAppPurchase" },
                { "FireBaseModule", "Firebase" },
                { "InternetConnectChecker", "InternetConnection" },
                { "IngameDebugConsole", "InGameDebugConsole" },
            };

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var settings = SdkSettingsManager.LoadSettings();
            var disabled = ModuleBuildArtifactRegistry.All
                .Where(spec => !ModuleBuildArtifactRegistry.IsEnabled(spec, settings))
                .ToList();
            if (disabled.Count == 0)
                return;

            var disabledNames = new HashSet<string>(disabled.Select(spec => spec.Name),
                StringComparer.OrdinalIgnoreCase);
            var objects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject)
                .OrderByDescending(GetDepth)
                .ToList();

            int removedObjects = 0;
            int removedComponents = 0;
            foreach (GameObject gameObject in objects)
            {
                if (gameObject == null)
                    continue;

                string sourcePath = GetPrefabSourcePath(gameObject);
                var owner = disabled.FirstOrDefault(spec =>
                    ModuleBuildArtifactRegistry.OwnsAssetPath(spec, sourcePath));

                if (owner == null)
                {
                    foreach (var legacy in LegacyPrefabOwners)
                    {
                        if (!sourcePath.EndsWith(legacy.Key, StringComparison.OrdinalIgnoreCase))
                            continue;
                        owner = disabled.FirstOrDefault(spec =>
                            string.Equals(spec.Name, legacy.Value, StringComparison.OrdinalIgnoreCase));
                        break;
                    }
                }

                if (owner == null &&
                    disabledNames.Contains("Cross-Promo") &&
                    string.Equals(gameObject.name, "Cross Promo Manager", StringComparison.Ordinal))
                {
                    owner = disabled.First(spec => spec.Name == "Cross-Promo");
                }

                if (owner == null &&
                    LegacyGameObjectOwners.TryGetValue(gameObject.name, out string gameObjectOwner) &&
                    disabledNames.Contains(gameObjectOwner))
                {
                    owner = disabled.First(spec =>
                        string.Equals(spec.Name, gameObjectOwner, StringComparison.OrdinalIgnoreCase));
                }

                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                    removedObjects++;
                    continue;
                }

                foreach (MonoBehaviour component in gameObject.GetComponents<MonoBehaviour>())
                {
                    if (component == null)
                        continue;
                    MonoScript script = MonoScript.FromMonoBehaviour(component);
                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    if (!disabled.Any(spec => ModuleBuildArtifactRegistry.OwnsAssetPath(spec, scriptPath)))
                        continue;

                    UnityEngine.Object.DestroyImmediate(component);
                    removedComponents++;
                }

                // При выключенном asmdef Unity представляет старые сериализованные
                // компоненты общего SDK prefab как Missing Script. Удаляем их только
                // внутри этого prefab и только из build-копии сцены.
                if (sourcePath.EndsWith("AmznGoDSDK/SDKPrefab/AmznGoDSDK.prefab",
                        StringComparison.OrdinalIgnoreCase))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (missing > 0)
                    {
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                        removedComponents += missing;
                    }
                }
            }

            if (removedObjects > 0 || removedComponents > 0)
            {
                Debug.Log($"[AMZN GoD SDK] Scene '{scene.name}': stripped {removedObjects} object(s) " +
                          $"and {removedComponents} component(s) of disabled modules.");
            }
        }

        private static string GetPrefabSourcePath(GameObject gameObject)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
            return source == null ? string.Empty : AssetDatabase.GetAssetPath(source).Replace('\\', '/');
        }

        private static int GetDepth(GameObject gameObject)
        {
            int depth = 0;
            Transform current = gameObject.transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }
    }
}
