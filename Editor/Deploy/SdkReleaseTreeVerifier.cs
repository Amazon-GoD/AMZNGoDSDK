using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Верификация релизного UPM-дерева ПЕРЕД коммитом в ветку Releases.
    /// Файловый аналог SdkPackageVerifier (тот читает .unitypackage): проверяет
    /// запрещённые фрагменты путей, валидность package.json, наличие всех asmdef,
    /// отсутствие осиротевших "~.meta" и отсутствующих .meta.
    /// Введено после инцидента 2026-08-20 (IAPTestPanel утекла в деплой).
    /// </summary>
    internal static class SdkReleaseTreeVerifier
    {
        /// <summary>
        /// Фрагменты, которых не должно быть ни в одном относительном пути
        /// релизного дерева. Сравнение без учёта регистра, пути нормализованы к '/'.
        /// </summary>
        public static readonly string[] ForbiddenPathFragments =
        {
            "/InAppPurchase/Testing",   // тестовый IAP-контент (инцидент 2026-08-20)
            "IAPTestPanel",
            "SimulatedAmazonStore",
            "Editor/Deploy",            // сам деплой-инструмент — внутренний
            "GoDSDKDeploy",             // старое имя деплой-инструмента
            "Infatica",                 // не входит в safety/UPM-поставку
            "AMZNGoDSDKGenerated",      // генерируется в Assets потребителя, в пакете быть не может
        };

        /// <summary>
        /// Все asmdef, обязанные присутствовать в релизном дереве
        /// (относительные пути от корня пакета).
        /// </summary>
        public static readonly string[] RequiredAsmdefPaths =
        {
            "Runtime/AMZNGoDSDK.Runtime.asmdef",
            "Runtime/Core/AMZNGoDSDK.Core.asmdef",
            "Editor/AMZNGoDSDK.Editor.asmdef",
            "Editor/Modules/Adjust/Scripts/Editor/AdjustSdk.Editor.asmdef",
            "Runtime/Modules/Adjust/AMZNGoDSDK.Module.Adjust.asmdef",
            "Runtime/Modules/Adjust/Adjust/Scripts/AdjustSdk.Scripts.asmdef",
            "Runtime/Modules/Analytics/AMZNGoDSDK.Module.Analytics.asmdef",
            "Runtime/Modules/AppMetrica/AMZNGoDSDK.Module.AppMetrica.asmdef",
            "Runtime/Modules/Cross-Promo/AMZNGoDSDK.Module.CrossPromo.asmdef",
            "Runtime/Modules/Cross-Promo/Pyro Entertainment/UniWebView/UniWebView-CSharp.asmdef",
            "Runtime/Modules/Cross-Promo/Pyro Entertainment/UniWebView/Editor/UniWebView-CSharp.Editor.asmdef",
            "Runtime/Modules/Firebase/AMZNGoDSDK.Module.Firebase.asmdef",
            "Runtime/Modules/InAppPurchase/AMZNGoDSDK.Module.InAppPurchase.asmdef",
            "Runtime/Modules/InGameDebugConsole/Plugins/IngameDebugConsole/IngameDebugConsole.Runtime.asmdef",
            "Runtime/Modules/InGameDebugConsole/Plugins/IngameDebugConsole/Editor/IngameDebugConsole.Editor.asmdef",
            "Runtime/Modules/InternetConnection/AMZNGoDSDK.Module.InternetConnection.asmdef",
        };

        public const string ExpectedPackageName = "com.amzngod.amzngodsdk";
        public const string ExpectedUnityVersion = "2022.3";
        public const string HiddenSamplePathFragment = "AmznGoDSDK~/SDKPrefab";

        [Serializable]
        private class PackageManifest
        {
            public string name;
            public string version;
            public string unity;
        }

        /// <summary>
        /// true — дерево чисто. false — список проблем в error (все сразу,
        /// не только первая). expectedVersion — версия, введённая в UI релиза.
        /// </summary>
        public static bool Verify(string treeRoot, string expectedVersion, out string error)
        {
            error = null;
            var problems = new List<string>();

            if (string.IsNullOrEmpty(treeRoot) || !Directory.Exists(treeRoot))
            {
                error = $"Release tree does not exist: {treeRoot}";
                return false;
            }

            var relativePaths = CollectRelativePaths(treeRoot);

            CheckForbiddenFragments(relativePaths, problems);
            CheckPackageJson(treeRoot, expectedVersion, problems);
            CheckRequiredAsmdefs(treeRoot, problems);
            CheckOrphanTildeMetas(treeRoot, relativePaths, problems);
            CheckMissingMetas(treeRoot, relativePaths, problems);

            if (problems.Count > 0)
            {
                error = "Release tree verification failed:\n  - " + string.Join("\n  - ", problems);
                return false;
            }

            return true;
        }

        /// <summary>Все файлы и папки дерева, пути относительные, разделитель '/'.</summary>
        private static List<string> CollectRelativePaths(string treeRoot)
        {
            var result = new List<string>();
            string prefix = Path.GetFullPath(treeRoot).TrimEnd(Path.DirectorySeparatorChar);

            foreach (var entry in Directory.EnumerateFileSystemEntries(prefix, "*", SearchOption.AllDirectories))
            {
                string rel = entry.Substring(prefix.Length).TrimStart(Path.DirectorySeparatorChar);
                result.Add(rel.Replace('\\', '/'));
            }

            return result;
        }

        private static void CheckForbiddenFragments(List<string> relativePaths, List<string> problems)
        {
            foreach (var path in relativePaths)
            {
                foreach (var fragment in ForbiddenPathFragments)
                {
                    if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        problems.Add($"forbidden path fragment '{fragment}': {path}");
                        break;
                    }
                }
            }
        }

        private static void CheckPackageJson(string treeRoot, string expectedVersion, List<string> problems)
        {
            string packageJsonPath = Path.Combine(treeRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                problems.Add("package.json is missing at tree root");
                return;
            }

            string text;
            PackageManifest manifest;
            try
            {
                text = File.ReadAllText(packageJsonPath);
                manifest = JsonUtility.FromJson<PackageManifest>(text);
            }
            catch (Exception e)
            {
                problems.Add($"package.json is not readable/parsable: {e.Message}");
                return;
            }

            if (manifest == null || string.IsNullOrEmpty(manifest.name))
                problems.Add("package.json parsed to empty manifest (invalid JSON?)");
            else
            {
                if (manifest.name != ExpectedPackageName)
                    problems.Add($"package.json name is '{manifest.name}', expected '{ExpectedPackageName}'");
                if (!string.IsNullOrEmpty(expectedVersion) && manifest.version != expectedVersion)
                    problems.Add($"package.json version is '{manifest.version}', expected '{expectedVersion}'");
                if (manifest.unity != ExpectedUnityVersion)
                    problems.Add($"package.json unity is '{manifest.unity}', expected '{ExpectedUnityVersion}'");
            }

            if (!text.Contains(HiddenSamplePathFragment))
                problems.Add($"package.json samples path is not transformed to '{HiddenSamplePathFragment}'");
        }

        private static void CheckRequiredAsmdefs(string treeRoot, List<string> problems)
        {
            foreach (var rel in RequiredAsmdefPaths)
            {
                string full = Path.Combine(treeRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    problems.Add($"required asmdef missing: {rel}");
            }
        }

        /// <summary>
        /// "X~.meta" без соответствующей папки/файла "X~" — паразитная запись:
        /// именно такие осиротевшие .meta утекали пустыми folder-энтри в пакеты.
        /// Плюс .meta у скрытых ~-папок вообще не должно быть (Unity их игнорирует).
        /// </summary>
        private static void CheckOrphanTildeMetas(string treeRoot, List<string> relativePaths, List<string> problems)
        {
            foreach (var path in relativePaths)
            {
                if (!path.EndsWith("~.meta", StringComparison.Ordinal))
                    continue;

                problems.Add($"tilde .meta must not exist (hidden entries need no meta): {path}");
            }
        }

        /// <summary>
        /// Каждому видимому файлу/папке пакета нужен сосед ".meta" — иначе Unity
        /// сгенерирует GUID на стороне потребителя и ссылки поедут между машинами.
        /// Скрытые (~) поддеревья, dot-файлы и содержимое container-ассетов
        /// (.bundle/.framework/...) пропускаются — Unity их не импортирует пофайлово.
        /// </summary>
        private static void CheckMissingMetas(string treeRoot, List<string> relativePaths, List<string> problems)
        {
            foreach (var path in relativePaths)
            {
                if (path.EndsWith(".meta", StringComparison.Ordinal))
                    continue;
                if (IsUnityIgnored(path))
                    continue;

                string metaFull = Path.Combine(treeRoot, path.Replace('/', Path.DirectorySeparatorChar)) + ".meta";
                if (!File.Exists(metaFull))
                    problems.Add($"missing .meta for: {path}");
            }
        }

        /// <summary>Папки, которые Unity импортирует единым ассетом (один .meta на контейнер).</summary>
        private static readonly string[] ContainerAssetExtensions =
        {
            ".bundle", ".framework", ".xcframework", ".androidlib", ".plugin",
        };

        /// <summary>
        /// Путь, который Unity не импортирует отдельным ассетом: любой сегмент
        /// с '~' на конце, с '.' в начале, либо путь ВНУТРИ container-ассета
        /// (сам контейнер metа имеет, его содержимое — нет).
        /// </summary>
        private static bool IsUnityIgnored(string relativePath)
        {
            var segments = relativePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0)
                    continue;
                if (segment.EndsWith("~", StringComparison.Ordinal))
                    return true;
                if (segment.StartsWith(".", StringComparison.Ordinal))
                    return true;

                // Содержимое контейнера (не сам контейнер: у него i == последний сегмент).
                if (i < segments.Length - 1)
                {
                    foreach (var ext in ContainerAssetExtensions)
                    {
                        if (segment.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
