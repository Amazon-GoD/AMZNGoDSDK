#if UNITY_ANDROID
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Проверяет настройки Android-сборки ДО её запуска и падает с внятным текстом вместо
    /// того, чтобы отдать разработчика на растерзание Gradle.
    ///
    /// <para>Все проверки ниже — это реальные грабли, каждая стоила дня диагностики
    /// (2026-09-03). Ошибки Gradle в этих случаях указывают не на причину, а на следствие:
    /// «37 issues were found when checking AAR metadata» про десятки androidx-библиотек не
    /// подсказывает, что дело в одном поле Player Settings.</para>
    ///
    /// <para>Порядок callbackOrder — раньше <see cref="AppLovinNetworkGuard"/> (0) и
    /// <see cref="DependencyPreprocessor"/> (-100)? Нет: DependencyPreprocessor обновляет
    /// define'ы, и запускать проверки до него бессмысленно — состояние модулей ещё не
    /// синхронизировано. Поэтому -50: после define'ов, до всего остального.</para>
    /// </summary>
    public class AndroidBuildPreflight : IPreprocessBuildWithReport
    {
        public int callbackOrder => -50;

        private const string LogTag = "[AMZN GoD SDK] [Preflight]";

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled)
                return;

            if (SdkDependencyManager.IsBusy || DependencyInstaller.IsBusy || FirebasePackageInstaller.IsBusy)
                throw new BuildFailedException("Подготовка зависимостей SDK ещё выполняется. Дождитесь её завершения в AMZN GoD / SDK Settings.");
            if (settings.AppLovin != null && settings.AppLovin.Enabled && !AppLovinPackageInstaller.IsRequiredSetInstalled)
                throw new BuildFailedException("Комплект AppLovin ещё не готов. Проверьте автоматическую установку в AMZN GoD / SDK Settings.");
            if (settings.Firebase != null && settings.Firebase.Enabled && !FirebasePackageInstaller.IsRequiredInstallationReady)
                throw new BuildFailedException("Комплект Firebase ещё не готов. Проверьте автоматическую установку в AMZN GoD / SDK Settings.");

            var errors = new List<string>();
            var warnings = new List<string>();

            AndroidToolchainInstaller.EnsureInstalled();
            AndroidToolchainSettings.CollectValidationErrors(errors);
            CheckTargetSdk(errors);
            EnsureMinSdk(settings);
            CheckInternetPermission(warnings);
            CheckAppLovinConfiguration(settings, warnings);

            foreach (var warning in warnings)
                Debug.LogWarning($"{LogTag} {warning}");

            if (errors.Count == 0)
            {
                AndroidToolchainSettings.UseGradleForBuild();
                Debug.Log($"{LogTag} настройки Android-сборки в порядке.");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Сборка остановлена: настройки проекта не подходят под зависимости SDK.");
            message.AppendLine();

            foreach (var error in errors)
            {
                message.AppendLine("  • " + error);
                message.AppendLine();
            }

            throw new BuildFailedException(message.ToString());
        }

        /// <summary>
        /// compileSdk задаёт AndroidGradleToolchain. Unity может экспортировать проект
        /// со своей установленной платформой, а Gradle компилирует его SDK из отдельного профиля.
        /// </summary>
        private static void CheckTargetSdk(List<string> errors)
        {
            var target = PlayerSettings.Android.targetSdkVersion;

            if (target == AndroidSdkVersions.AndroidApiLevelAuto)
                return;
            if ((int)target <= AndroidToolchainSettings.CompileSdk)
                return;
            errors.Add(
                $"Target API Level = {(int)target}, но подготовленный compileSdk = {AndroidToolchainSettings.CompileSdk}.\n" +
                "    Target API Level не должен превышать compileSdk. Проверьте Player Settings → Other Settings.");
        }

        /// <summary>
        /// После установки SDK поднимаем минимум до требований включённых модулей автоматически.
        /// Более высокий минимум проекта сохраняется.
        /// </summary>
        private static void EnsureMinSdk(Runtime.SdkSettingsData settings)
        {
            int required = settings.AppLovin != null && settings.AppLovin.Enabled
                ? AndroidToolchainSettings.MinimumSdkWithAppLovin
                : settings.Firebase != null && settings.Firebase.Enabled ? FirebasePackageInstaller.MinimumAndroidSdk : 0;
            int min = (int)PlayerSettings.Android.minSdkVersion;
            if (min >= required) return;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)required;
            Debug.Log($"{LogTag} Minimum API Level автоматически повышен с {min} до {required} для включённых модулей SDK.");
        }

        /// <summary>
        /// Кросс-промо тянет конфиг и ролики по сети; без INTERNET модуль молча остаётся
        /// без конфига, а выглядит это как «прелоад не работает».
        /// </summary>
        private static void CheckInternetPermission(List<string> warnings)
        {
            if (PlayerSettings.Android.forceInternetPermission)
                return;

            warnings.Add(
                "Internet Access = Auto. Разрешение INTERNET приедет из зависимостей, но если " +
                "все сетевые модули выключить, кросс-промо останется без конфига. " +
                "Надёжнее Player Settings → Other Settings → Internet Access → Require.");
        }

        /// <summary>
        /// Не ошибка сборки, но гарантированная тишина в рантайме: модуль сам себя выключает,
        /// и после выжигания капов кросс-промо показывать становится нечем.
        /// </summary>
        private static void CheckAppLovinConfiguration(Runtime.SdkSettingsData settings, List<string> warnings)
        {
            if (settings.AppLovin == null || !settings.AppLovin.Enabled)
                return;

            bool noAdUnits = string.IsNullOrWhiteSpace(settings.AppLovin.InterstitialAdUnitId)
                             && string.IsNullOrWhiteSpace(settings.AppLovin.RewardedAdUnitId);

            if (noAdUnits)
            {
                warnings.Add(
                    "Модуль AppLovin включён, но не задан ни один ad unit id — на старте он выключит " +
                    "сам себя, и после исчерпания капов кросс-промо реклама показываться не будет. " +
                    "AMZN GoD → SDK Settings → AppLovin.");
            }
        }
    }
}
#endif
