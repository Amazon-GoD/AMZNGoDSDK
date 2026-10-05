using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Build-time исключение нативных плагинов выключенных модулей и запрещённых Android SDK.
    ///
    /// Почему так: Define Constraints в PluginImporter для нативных плагинов
    /// (.jar/.aar/.so/.java/.mm) Unity игнорирует — By Design, поле работает только
    /// для managed. Единственный рабочий механизм — SetIncludeInBuildDelegate,
    /// зарегистрированный из Editor-кода до старта билда (подтверждено реальным
    /// Android-билд-тестом на 2022.3.60f1, в т.ч. для jar внутри immutable
    /// UPM-пакета; см. temp-docs/editor-test-includeinbuild.md).
    ///
    /// Жизненный цикл: делегат существует только в памяти текущей editor-сессии,
    /// поэтому регистрация повторяется на каждый domain reload
    /// ([InitializeOnLoadMethod]). Смена defines сама по себе вызывает domain reload
    /// (перекомпиляция затронутых сборок), плюс <see cref="Refresh"/> дёргается явно
    /// из ModuleDefineManager при применении тогглов — чтобы состояние фильтра было
    /// корректным ещё до перезагрузки домена.
    ///
    /// Требования к делегату: за один билд он опрашивается многократно (~90 раз),
    /// поэтому это константный `path => false` без логики и побочных эффектов.
    /// Делегат вешается только на плагины выключенных модулей или распознанных
    /// запрещённых Android SDK; остальные плагины сохраняют свои делегаты.
    /// </summary>
    public static class NativePluginBuildFilter
    {
        private static readonly PluginImporter.IncludeInBuildDelegate ExcludeFromBuild = path => false;

        // AssetPath'ы, на которые делегат повешен НАМИ в текущей editor-сессии.
        // Нужен, чтобы при повторном Refresh (модуль включили без domain reload)
        // снимать только свои делегаты, не задевая чужие.
        private static readonly HashSet<string> ManagedPaths = new HashSet<string>();

        [InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            Refresh();
        }

        /// <summary>
        /// Регистрирует исключения по актуальному состоянию defines и снимает
        /// ранее повешенные нами делегаты с плагинов снова включённых модулей.
        /// Идемпотентно, безопасно вызывать многократно.
        /// </summary>
        public static void Refresh()
        {
            // Спецификации модулей, чей define сейчас ВЫКЛЮЧЕН. Реестр включает
            // не только vendored-файлы SDK, но и внешние плагины Firebase/MAX.
            var disabledModules = new List<ModuleBuildArtifactSpec>();
            foreach (var spec in ModuleBuildArtifactRegistry.All)
            {
                if (!ModuleDefineManager.IsModuleEnabled(spec.Define))
                    disabledModules.Add(spec);
            }

            int excluded = 0;
#if UNITY_ANDROID
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            bool stripProhibited = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android && settings != null && settings.Enabled;
#endif
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                string assetPath = importer.assetPath;

                bool underDisabled = false;
                foreach (var module in disabledModules)
                {
                    if (ModuleBuildArtifactRegistry.OwnsAssetPath(module, assetPath))
                    {
                        underDisabled = true;
                        break;
                    }
                }

                bool prohibited = false;
#if UNITY_ANDROID
                prohibited = stripProhibited && AppLovinGradleExclusions.IsRemovableLocalLibrary(assetPath);
#endif
                if (underDisabled || prohibited)
                {
                    importer.SetIncludeInBuildDelegate(ExcludeFromBuild);
                    ManagedPaths.Add(assetPath);
                    excluded++;
                }
                else if (ManagedPaths.Remove(assetPath))
                {
                    // Модуль включили в этой же сессии — возвращаем плагину
                    // дефолтное поведение (решение по platform settings в .meta).
                    importer.SetIncludeInBuildDelegate(null);
                }
            }

            if (excluded > 0)
            {
                Debug.Log($"[AMZN GoD SDK] Native plugin build filter: " +
                          $"{excluded} disabled or prohibited native plugin(s) excluded from build.");
            }
        }
    }
}
