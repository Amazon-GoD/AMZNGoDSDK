using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
#if UNITY_2023_1_OR_NEWER
using UnityEditor.Build;
#endif

namespace AMZNGoDSDK.Bootstrap
{
    /// <summary>
    /// Сторож соответствия «настройки ↔ asmdef ↔ define» для модулей, зависящих от ВНЕШНИХ
    /// (не входящих в поставку) плагинов. Сейчас такой один — AppLovin MAX.
    ///
    /// <para><b>Почему отдельная сборка.</b> Сборка <c>AMZNGoD.Bootstrap.Editor</c> намеренно
    /// не имеет ни одной ссылки: она компилируется, даже когда <c>AMZNGoD.Runtime</c> сломан.
    /// Иначе сторож бесполезен ровно в том случае, для которого написан: несогласованное
    /// состояние (define <c>AMZN_APPLOVIN_ENABLED</c> есть, ссылки на <c>MaxSdk.Scripts</c> нет,
    /// либо ссылка есть, а плагина нет) роняет компиляцию <c>AMZNGoD.Runtime</c>, а следом и
    /// <c>AMZNGoDSDK.Editor</c>, который на рантайм ссылается. Тогда ни
    /// <c>ModuleDefineManager</c>, ни Setup Wizard, ни импорт-постпроцессор SDK не запустятся —
    /// у партнёра проект встанет с ошибками компиляции и без работающего тулинга.</para>
    ///
    /// <para><b>Продуктовый кейс.</b> В поставляемом .unitypackage ссылки на MAX нет
    /// (её снимает <c>SdkPackageExporter</c>). Если партнёр обновляет SDK поверх проекта, где
    /// модуль AppLovin включён, импорт перезапишет asmdef «чистой» версией, а define останется
    /// в ProjectSettings — импорт сам создаёт рассогласование. Сторож ловит это на
    /// <c>OnPostprocessAllAssets</c> и на каждом domain reload, приводя состояние к
    /// единственному согласованному варианту по конфигу SDK и фактическому наличию плагина.</para>
    ///
    /// <para>Класс ничего не знает про <c>SdkSettingsManager</c> и <c>DependencyDetector</c>
    /// (они в сломанном состоянии недоступны) и читает конфиг и состояние проекта сам.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class SdkAsmdefReferenceGuard
    {
        private const string RuntimeAsmdefRelativePath = "Runtime/Modules/AppLovin/AMZNGoDSDK.Module.AppLovin.asmdef";

        public static string RuntimeAsmdefPath
        {
            get
            {
                string discovered = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                    "AMZNGoDSDK.Module.AppLovin");
                if (!string.IsNullOrEmpty(discovered))
                    return discovered;

                string[] roots =
                {
                    "Assets/AMZNGoDSDK/",
                    "Packages/com.amzngod.amzngodsdk/",
                };

                foreach (var root in roots)
                {
                    string assetPath = root + RuntimeAsmdefRelativePath;
                    string physicalPath = FileUtil.GetPhysicalPath(assetPath);
                    if (!string.IsNullOrEmpty(physicalPath) && File.Exists(physicalPath))
                        return assetPath;
                }

                return roots[0] + RuntimeAsmdefRelativePath;
            }
        }

        /// <summary>
        /// Имя сборки плагина MAX. Одинаково для обоих способов установки — UPM-пакет
        /// com.applovin.mediation.ads и .unitypackage в Assets/MaxSdk, — поэтому ссылка
        /// пишется именем, а не GUID: GUID у этих двух вариантов разный.
        /// </summary>
        public const string MaxSdkAssemblyName = "MaxSdk.Scripts";

        /// <summary>
        /// GUID asmdef'а MAX из UPM-пакета. Нужен только чтобы найти ссылку для снятия: если
        /// asmdef открывали в Inspector с включённым "Use GUIDs", Unity сама переписывает наше
        /// имя в GUID-форму, и по имени такую запись уже не найти.
        /// </summary>
        private const string MaxSdkUpmAsmdefGuid = "a4cfc1a18fa3a469b96d885db522f42e";

        private const string AppLovinDefine = "AMZN_APPLOVIN_ENABLED";
        private const string ConfigPath = "Assets/Resources/amzn_god_sdk.json";
        private const string GuidPrefix = "GUID:";

        /// <summary>
        /// Дубликат <c>SdkPackageExporter.ExportInProgressKey</c>: на константу из
        /// AMZNGoDSDK.Editor ссылаться нельзя — эта сборка намеренно без ссылок.
        /// Значение менять только синхронно с экспортёром.
        /// </summary>
        private const string ExportInProgressKey = "AMZN_SDK_EXPORT_IN_PROGRESS";

        private static readonly BuildTargetGroup[] TargetGroups =
        {
            BuildTargetGroup.Android,
            BuildTargetGroup.iOS,
            BuildTargetGroup.Standalone
        };

        // Массив references в asmdef плоский (без вложенных скобок), поэтому [^\]] безопасен.
        private static readonly Regex ReferencesBlockRegex = new Regex(
            "\"references\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]",
            RegexOptions.Singleline);

        private static readonly Regex QuotedEntryRegex = new Regex("\"(?<value>[^\"]*)\"");

        static SdkAsmdefReferenceGuard()
        {
            // delayCall, а не сам конструктор: на момент [InitializeOnLoad] AssetDatabase
            // ещё может не отдать пути и список сборок.
            EditorApplication.delayCall += () => Reconcile();
        }

        #region Reconcile

        /// <summary>
        /// Сверяет три состояния — конфиг SDK, наличие плагина MAX и ссылку в asmdef рантайма —
        /// и приводит их к согласованному виду.
        ///
        /// Ссылка нужна ровно тогда, когда модуль включён в конфиге И плагин реально стоит
        /// отдельной сборкой. Если ссылка не нужна, но define остался (типовой след обновления
        /// SDK поверх проекта), define снимается здесь же: define без ссылки = CS0246 в
        /// AppLovinModule, и убрать его больше некому — ModuleDefineManager в этом состоянии
        /// не компилируется.
        /// </summary>
        /// <param name="importAsset">Импортировать изменённый asmdef сразу (перекомпиляция).</param>
        /// <returns>true, если что-то пришлось починить.</returns>
        public static bool Reconcile(bool importAsset = true)
        {
            // Экспорт пакета сам снимает и возвращает ссылку — не мешаем ему.
            if (SessionState.GetBool(ExportInProgressKey, false))
                return false;

            bool enabledInSettings = IsAppLovinEnabledInSettings();
            bool maxPresent = IsMaxSdkAssemblyPresent();
            bool shouldHaveReference = enabledInSettings && maxPresent;

            bool changed = false;

            if (HasAppLovinReference() != shouldHaveReference)
            {
                Debug.Log($"[AMZN GoD SDK] Assembly reference drift detected: " +
                          $"module enabled in settings = {enabledInSettings}, " +
                          $"{MaxSdkAssemblyName} present = {maxPresent}. Fixing {Path.GetFileName(RuntimeAsmdefPath)}.");

                changed |= SetAppLovinReference(shouldHaveReference, importAsset);
            }

            if (!shouldHaveReference && RemoveDefine(AppLovinDefine))
            {
                Debug.LogWarning(
                    $"[AMZN GoD SDK] Removed stale define {AppLovinDefine}: AppLovin module cannot compile " +
                    (maxPresent
                        ? "while it is disabled in SDK settings."
                        : $"without the {MaxSdkAssemblyName} assembly (install AppLovin MAX to enable the module)."));

                changed = true;
            }

            return changed;
        }

        [MenuItem("AMZN GoD/Tools/Validate Assembly References", false, 420)]
        private static void ValidateFromMenu()
        {
            bool changed = Reconcile();

            EditorUtility.DisplayDialog(
                "Validate Assembly References",
                changed
                    ? "State was inconsistent and has been fixed.\nUnity will recompile scripts now.\n\nDetails are in the Console."
                    : "Settings, installed plugins and asmdef references are consistent.\nNothing to fix.",
                "OK");
        }

        #endregion

        #region State probes

        /// <summary>
        /// Читает конфиг SDK с диска (не через Resources.Load: TextAsset кэшируется, а нас
        /// вызывают сразу после импорта). Конфига нет — значит модуль включить не могли.
        /// </summary>
        private static bool IsAppLovinEnabledInSettings()
        {
            if (!File.Exists(ConfigPath))
                return false;

            try
            {
                var config = JsonUtility.FromJson<GuardConfig>(File.ReadAllText(ConfigPath));
                return config != null
                       && config.Enabled
                       && config.AppLovin != null
                       && config.AppLovin.Enabled;
            }
            catch (Exception e)
            {
                // Битый конфиг — не повод трогать asmdef: считаем модуль выключенным только
                // после явного чтения, иначе можно снести рабочую ссылку из-за опечатки в JSON.
                Debug.LogWarning($"[AMZN GoD SDK] Failed to read {ConfigPath}: {e.Message}. Assembly reference left as is.");
                return HasAppLovinReference();
            }
        }

        /// <summary>
        /// Плагин MAX должен быть ОТДЕЛЬНОЙ сборкой — только на такую можно сослаться из
        /// asmdef. Установка старым .unitypackage без asmdef (типы уезжают в Assembly-CSharp)
        /// для обёртки бесполезна: asmdef не может ссылаться на предопределённые сборки.
        /// Критерий совпадает с DependencyDetector.RequiredAssemblies — иначе сторож и
        /// ModuleDefineManager будут спорить и гонять перекомпиляцию по кругу.
        /// </summary>
        private static bool IsMaxSdkAssemblyPresent()
        {
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                if (string.Equals(assembly.name, MaxSdkAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                if (string.Equals(assembly.name, MaxSdkAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Страховка: сборка ещё не в графе (первый импорт пакета), но asmdef уже на диске.
            return !string.IsNullOrEmpty(
                CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(MaxSdkAssemblyName));
        }

        /// <summary>Есть ли сейчас в asmdef рантайма ссылка на MaxSdk.Scripts.</summary>
        public static bool HasAppLovinReference()
        {
            if (!TryReadReferences(RuntimeAsmdefPath, out _, out var entries, out _))
                return false;

            return entries.Any(IsMaxSdkReference);
        }

        #endregion

        #region Defines

        private static bool RemoveDefine(string define)
        {
            bool changed = false;

            foreach (var targetGroup in TargetGroups)
            {
                var defines = GetDefines(targetGroup)
                    .Split(';')
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();

                if (defines.RemoveAll(d => string.Equals(d, define, StringComparison.Ordinal)) == 0)
                    continue;

                SetDefines(targetGroup, string.Join(";", defines));
                changed = true;
            }

            return changed;
        }

        private static string GetDefines(BuildTargetGroup targetGroup)
        {
#if UNITY_2023_1_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
#endif
        }

        private static void SetDefines(BuildTargetGroup targetGroup, string defines)
        {
#if UNITY_2023_1_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
#endif
        }

        #endregion

        #region asmdef editing

        /// <summary>
        /// Приводит ссылку на MaxSdk.Scripts к нужному состоянию.
        /// </summary>
        /// <param name="present">true — ссылка должна быть; false — должна отсутствовать.</param>
        /// <param name="importAsset">
        /// Импортировать изменённый asmdef сразу (перекомпиляция). Экспортёр пропускает импорт:
        /// он сам делает Refresh/ImportAsset и работает под LockReloadAssemblies.
        /// </param>
        /// <returns>true, если файл действительно менялся.</returns>
        public static bool SetAppLovinReference(bool present, bool importAsset = true)
        {
            return SetReference(RuntimeAsmdefPath, MaxSdkAssemblyName, IsMaxSdkReference, present, importAsset);
        }

        private static bool SetReference(
            string asmdefPath,
            string assemblyName,
            Func<string, bool> matcher,
            bool present,
            bool importAsset)
        {
            if (!TryReadReferences(asmdefPath, out string text, out var entries, out Match block))
                return false;

            bool alreadyPresent = entries.Any(matcher);
            if (alreadyPresent == present)
                return false;

            if (present)
                entries.Add(assemblyName);
            else
                entries.RemoveAll(e => matcher(e));

            string newText = ReplaceReferencesBlock(text, block, entries);
            if (string.Equals(newText, text, StringComparison.Ordinal))
                return false;

            try
            {
                File.WriteAllText(FileUtil.GetPhysicalPath(asmdefPath), newText);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZN GoD SDK] Failed to write {asmdefPath}: {e.Message}");
                return false;
            }

            if (importAsset)
                AssetDatabase.ImportAsset(asmdefPath, ImportAssetOptions.ForceUpdate);

            Debug.Log(present
                ? $"[AMZN GoD SDK] Added assembly reference '{assemblyName}' to {Path.GetFileName(asmdefPath)}."
                : $"[AMZN GoD SDK] Removed assembly reference '{assemblyName}' from {Path.GetFileName(asmdefPath)}.");

            return true;
        }

        private static bool TryReadReferences(
            string asmdefPath,
            out string text,
            out List<string> entries,
            out Match block)
        {
            text = null;
            entries = new List<string>();
            block = null;

            string physicalPath = FileUtil.GetPhysicalPath(asmdefPath);
            if (string.IsNullOrEmpty(physicalPath) || !File.Exists(physicalPath))
            {
                Debug.LogWarning($"[AMZN GoD SDK] Assembly definition not found: {asmdefPath}");
                return false;
            }

            try
            {
                text = File.ReadAllText(physicalPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AMZN GoD SDK] Failed to read {asmdefPath}: {e.Message}");
                return false;
            }

            block = ReferencesBlockRegex.Match(text);
            if (!block.Success)
            {
                Debug.LogWarning($"[AMZN GoD SDK] No \"references\" array found in {asmdefPath} — skipping.");
                return false;
            }

            foreach (Match entry in QuotedEntryRegex.Matches(block.Groups["body"].Value))
                entries.Add(entry.Groups["value"].Value);

            return true;
        }

        /// <summary>
        /// Пересобирает только блок references, остальной файл остаётся байт в байт.
        /// Отступ и перевод строки берутся из самого файла — asmdef переписывает и Unity,
        /// и мы, форматы не должны расходиться и плодить шум в диффе.
        /// </summary>
        private static string ReplaceReferencesBlock(string text, Match block, List<string> entries)
        {
            string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            string indent = GetLineIndent(text, block.Index);
            string entryIndent = indent + "    ";

            string rebuilt;
            if (entries.Count == 0)
            {
                rebuilt = "\"references\": []";
            }
            else
            {
                string body = string.Join("," + newLine + entryIndent, entries.Select(e => "\"" + e + "\""));
                rebuilt = "\"references\": [" + newLine + entryIndent + body + newLine + indent + "]";
            }

            return text.Substring(0, block.Index) + rebuilt + text.Substring(block.Index + block.Length);
        }

        private static string GetLineIndent(string text, int index)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int i = lineStart;
            while (i < index && (text[i] == ' ' || text[i] == '\t'))
                i++;

            return text.Substring(lineStart, i - lineStart);
        }

        private static bool IsMaxSdkReference(string entry)
        {
            if (string.Equals(entry, MaxSdkAssemblyName, StringComparison.Ordinal))
                return true;

            if (!entry.StartsWith(GuidPrefix, StringComparison.Ordinal))
                return false;

            string guid = entry.Substring(GuidPrefix.Length);
            if (string.Equals(guid, MaxSdkUpmAsmdefGuid, StringComparison.Ordinal))
                return true;

            // Установка из .unitypackage даёт свой GUID — сверяем по имени файла asmdef.
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path)
                   && string.Equals(Path.GetFileNameWithoutExtension(path), MaxSdkAssemblyName, StringComparison.Ordinal);
        }

        #endregion

        #region Config DTO

        // Минимальная проекция конфига: JsonUtility молча игнорирует остальные поля, поэтому
        // сторожу не нужен ни SdkSettingsData, ни ссылка на сборки SDK.
        [Serializable]
        private class GuardConfig
        {
            public bool Enabled;
            public GuardModuleConfig AppLovin;
        }

        [Serializable]
        private class GuardModuleConfig
        {
            public bool Enabled;
        }

        #endregion
    }

    /// <summary>
    /// Точка входа продуктового кейса: импорт .unitypackage с SDK. Именно импорт создаёт
    /// рассогласование — asmdef приезжает из пакета без ссылки на MAX, а define остаётся
    /// в ProjectSettings проекта партнёра.
    ///
    /// Живёт в той же сборке без ссылок, что и сторож: импорт-постпроцессор из
    /// AMZNGoDSDK.Editor в этот момент может быть уже не скомпилирован.
    /// </summary>
    internal sealed class SdkAsmdefReferenceGuardImportHook : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!TouchesSdk(importedAssets) && !TouchesSdk(deletedAssets) && !TouchesSdk(movedAssets))
                return;

            // Отложенный вызов: правка asmdef и define'ов внутри колбэка импорта дёрнула бы
            // импорт повторно, посреди ещё не завершённой пачки ассетов.
            EditorApplication.delayCall += () => SdkAsmdefReferenceGuard.Reconcile();
        }

        private static bool TouchesSdk(string[] assets)
        {
            if (assets == null)
                return false;

            foreach (var asset in assets)
            {
                if (!string.IsNullOrEmpty(asset)
                    && (asset.StartsWith("Assets/AMZNGoDSDK/", StringComparison.Ordinal)
                        || asset.StartsWith("Packages/com.amzngod.amzngodsdk/", StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }
    }
}
