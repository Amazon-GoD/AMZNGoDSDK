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

        /// <summary>
        /// Минимальный compileSdk, который переваривает текущий набор зависимостей.
        /// Требуют 34: androidx.core 1.12, work-runtime 2.9.1, datastore 1.1.1,
        /// media3 1.4.1 (ExoPlayer кросс-промо), lifecycle 2.7.0, transition 1.5.0.
        /// В Unity compileSdk берётся из Target API Level.
        /// </summary>
        private const int RequiredCompileSdk = 34;

        /// <summary>
        /// applovin-sdk 13.6.2 объявляет minSdkVersion 24 начиная с 13.6.3, поэтому SDK
        /// пинит плагин на 8.6.3. Сам 13.6.2 требует 23 — ниже manifest merger не пустит.
        /// </summary>
        private const int RequiredMinSdkWithAppLovin = 23;

        private const string LogTag = "[AMZN GoD SDK] [Preflight]";

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled)
                return;

            var errors = new List<string>();
            var warnings = new List<string>();

            CheckCompileSdk(errors);
            CheckMinSdk(settings, errors);
            CheckInternetPermission(warnings);
            CheckAppLovinConfiguration(settings, warnings);

            foreach (var warning in warnings)
                Debug.LogWarning($"{LogTag} {warning}");

            if (errors.Count == 0)
            {
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
        /// В Unity compileSdk = Target API Level. Ниже 34 Gradle валится на
        /// checkReleaseAarMetadata десятками сообщений «requires ... compile against version 34».
        /// </summary>
        private static void CheckCompileSdk(List<string> errors)
        {
            var target = PlayerSettings.Android.targetSdkVersion;

            // Auto = «самый свежий установленный», это всегда ≥ 34 на поддерживаемых редакторах.
            if (target == AndroidSdkVersions.AndroidApiLevelAuto)
                return;

            if ((int)target >= RequiredCompileSdk)
                return;

            errors.Add(
                $"Target API Level = {(int)target}, нужен минимум {RequiredCompileSdk}.\n" +
                $"    В Unity Target API Level задаёт и compileSdk, а зависимости SDK " +
                $"(androidx.core 1.12, work-runtime 2.9.1, datastore 1.1.1, media3 1.4.1, lifecycle 2.7.0) " +
                $"требуют компиляции против API {RequiredCompileSdk}+.\n" +
                $"    Симптом без этой проверки: ':launcher:checkReleaseAarMetadata FAILED' с десятками\n" +
                $"    'requires libraries and applications that depend on it to compile against version 34 or later'.\n" +
                $"    Player Settings → Other Settings → Target API Level → {RequiredCompileSdk} или выше.");
        }

        /// <summary>
        /// minSdk ниже требований AppLovin роняет manifest merger, причём сообщение указывает
        /// на библиотеку, а не на настройку.
        /// </summary>
        private static void CheckMinSdk(Runtime.SdkSettingsData settings, List<string> errors)
        {
            if (settings.AppLovin == null || !settings.AppLovin.Enabled)
                return;

            int min = (int)PlayerSettings.Android.minSdkVersion;
            if (min >= RequiredMinSdkWithAppLovin)
                return;

            errors.Add(
                $"Minimum API Level = {min}, а модуль AppLovin требует {RequiredMinSdkWithAppLovin}+.\n" +
                $"    Симптом без этой проверки: 'uses-sdk:minSdkVersion {min} cannot be smaller than version 23\n" +
                $"    declared in library [com.applovin:applovin-sdk]'.\n" +
                $"    Player Settings → Other Settings → Minimum API Level → {RequiredMinSdkWithAppLovin} или выше,\n" +
                $"    либо выключи модуль AppLovin в AMZN GoD → SDK Settings.");
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
