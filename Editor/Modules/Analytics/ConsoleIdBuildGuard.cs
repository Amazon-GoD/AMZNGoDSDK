using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Роняет билд, если поля, которыми first_open заявляет игру для BI (Console ID, токен
    /// Adjust — см. <see cref="Runtime.FirstOpenClaim"/>), привели бы к неверной привязке.
    ///
    /// Console ID (только при включённом Analytics): неверный формат; ID вводили для другого
    /// бандла (копия шаблона унесла чужой ID — BI привязал бы новую игру к старой); буква ID не
    /// совпадает с App Type. Пустой ID — только предупреждение: у старых игр ID нет.
    /// Adjust (при включённом модуле): ключ — не app token из 12 символов; Sandbox в релизной
    /// сборке без явного <c>AllowSandboxInRelease</c>.
    ///
    /// Как и <see cref="AnalyticsAppTypeBuildGuard"/>, окон не показывает (билд может идти из CI):
    /// ошибка с путём до настроек.
    /// </summary>
    internal sealed class ConsoleIdBuildGuard : IPreprocessBuildWithReport
    {
        // Рядом с AnalyticsAppTypeBuildGuard: раньше препроцессоров зависимостей.
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            bool development = (report.summary.options & BuildOptions.Development) != 0;
            // Бандл именно собираемой платформы — он и окажется в Application.identifier.
            string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(report.summary.platformGroup);

            CollectIssues(SdkSettingsManager.LoadRuntimeSettings(), applicationIdentifier, development, errors, warnings);

            foreach (string warning in warnings)
                Debug.LogWarning(warning);
            if (errors.Count > 0)
                throw new BuildFailedException(string.Join("\n\n", errors));
        }

        /// <summary>
        /// Все проблемы сборки по Console ID и Adjust. Чистая функция — без чтения настроек
        /// проекта, поэтому её зовут и хук, и кнопка сборки APK (<see cref="OneClickAndroidBuild"/>),
        /// и EditMode-тесты.
        /// </summary>
        internal static void CollectIssues(Runtime.SdkSettingsData settings, string applicationIdentifier,
            bool developmentBuild, List<string> errors, List<string> warnings)
        {
            // Конфига нет или SDK целиком выключен — модулей в сборке не будет.
            if (settings == null || !settings.Enabled)
                return;

            var analytics = settings.Analytics;
            if (analytics != null && analytics.Enabled)
            {
                CollectConsoleIdIssues(analytics.ConsoleId, analytics.ConsoleIdBundle, analytics.AppType,
                    applicationIdentifier, errors, warnings);
            }

            var adjust = settings.Adjust;
            if (adjust != null && adjust.Enabled)
                CollectAdjustIssues(adjust, developmentBuild, errors);
        }

        /// <summary>
        /// Бандл, к которому окно настроек привязывает Console ID: Android-сборки, а не активной
        /// платформы (<c>PlayerSettings.applicationIdentifier</c>). ID — это листинг Amazon, а кнопка
        /// сборки APK сама переключает платформу на Android и сверяет штамп с этим бандлом: штамп,
        /// снятый на Standalone, остановил бы её ложной «копией шаблона».
        /// </summary>
        internal static string ProjectBundle => PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);

        /// <summary>
        /// Штамп после правки поля Console ID в окне: новый ID привязывается к <paramref name="bundle"/>,
        /// стёртый — отвязывается. Если ID по существу не изменился (окно лишь срезало пробелы из
        /// правленого вручную конфига), штамп прежний: иначе копия шаблона привязала бы чужой ID
        /// к себе одним открытием окна.
        /// </summary>
        internal static string StampAfterEdit(string previousId, string newId, string previousStamp, string bundle)
        {
            newId ??= string.Empty;
            if (newId == (previousId ?? string.Empty).Trim())
                return previousStamp ?? string.Empty;
            return newId.Length == 0 ? string.Empty : bundle;
        }

        private static void CollectConsoleIdIssues(string consoleId, string consoleIdBundle,
            Runtime.AnalyticsAppType appType, string applicationIdentifier, List<string> errors, List<string> warnings)
        {
            if (string.IsNullOrEmpty(consoleId))
            {
                warnings.Add("[AMZNGoDSDK] Analytics: Console ID не задан. Для игры без ID это нормально. " +
                             "ID выдаёт админка BI → «Игры» (новой игре — «+ Добавить игру», существующей — " +
                             "«Выдать Console ID» в её карточке); впишите его до первой сборки " +
                             "(AMZN GoD → SDK Settings → Analytics → Console ID): без него BI не свяжет бандл, " +
                             "токен Adjust и ASIN игры автоматически.");
                return;
            }

            if (!Runtime.ConsoleId.IsValid(consoleId))
            {
                errors.Add($"[AMZNGoDSDK] Analytics: Console ID «{consoleId}» не по формату — сборка остановлена.\n" +
                           "Нужен ID из админки BI → «Игры»: буква F (free) или P (paid), 6 цифр, дефис " +
                           $"и номер копии, например {Runtime.ConsoleId.Example}.\n" +
                           "Как починить: AMZN GoD → SDK Settings → блок Analytics → Console ID → Save Settings.");
                return;
            }

            if (string.IsNullOrEmpty(consoleIdBundle))
            {
                errors.Add($"[AMZNGoDSDK] Analytics: Console ID {consoleId} не привязан к бандлу — сборка остановлена.\n" +
                           "Окно SDK Settings запоминает бандл в момент ввода ID, а в конфиге его нет (конфиг " +
                           "правили вручную?).\n" +
                           "Как починить: AMZN GoD → SDK Settings → Analytics → сотрите Console ID, введите его " +
                           "заново → Save Settings.");
            }
            else if (consoleIdBundle != applicationIdentifier)
            {
                errors.Add($"[AMZNGoDSDK] Analytics: Console ID вводили для {consoleIdBundle}, а собираете " +
                           $"{applicationIdentifier}. Это копия шаблона? Выдайте новый ID — сборка остановлена.\n" +
                           "Один Console ID — один листинг в Amazon: по нему BI связывает бандл, токен Adjust и ASIN.\n" +
                           "Как починить: новой игре (в том числе из шаблона) выдайте ID в админке BI: «Игры» → " +
                           "«+ Добавить игру»; «Копия игры» — только для перезалива, платной версии или той же " +
                           "игры в другом аккаунте. Впишите ID в AMZN GoD → SDK Settings → Analytics → Console ID " +
                           "→ Save Settings. Если бандл этой же игры сменили намеренно — сотрите Console ID и " +
                           "введите его заново.");
            }

            // None ловит AnalyticsAppTypeBuildGuard — второй ошибкой про то же не дублируем.
            char expected = Runtime.ConsoleId.LetterFor(appType);
            if (expected != '\0' && consoleId[0] != expected)
            {
                string idType = consoleId[0] == 'F' ? "free" : "paid";
                errors.Add($"[AMZNGoDSDK] Analytics: Console ID {consoleId} выдан для {idType}-листинга " +
                           $"(буква {consoleId[0]}), а App Type = {appType} — сборка остановлена.\n" +
                           "Буква ID — тип листинга в Amazon: F — free, P — paid. У paid-версии free-игры свой ID " +
                           "(копия с буквой P).\n" +
                           "Как починить: AMZN GoD → SDK Settings → Analytics → выставьте верный App Type или впишите " +
                           "ID этой версии → Save Settings.");
            }
        }

        private static void CollectAdjustIssues(Runtime.AdjustSettingData adjust, bool developmentBuild, List<string> errors)
        {
            // Ключ в сообщение не выводим — только чем он не похож на app token.
            if (!Runtime.FirstOpenClaim.IsValidAdjustAppToken(adjust.Key))
            {
                string actual = string.IsNullOrEmpty(adjust.Key)
                    ? "Key не задан"
                    : $"Key не похож на app token (длина {adjust.Key.Length})";
                errors.Add($"[AMZNGoDSDK] Adjust: {actual} — сборка остановлена.\n" +
                           "App token Adjust — ровно 12 символов: строчные латинские буквы и цифры, без пробелов " +
                           "(Adjust → приложение → App token).\n" +
                           "Как починить: AMZN GoD → SDK Settings → блок Adjust → Key → Save Settings.");
            }

            if (!developmentBuild &&
                adjust.Environment == Runtime.AdjustSettingData.AdjustEnvironment.Sandbox &&
                !adjust.AllowSandboxInRelease)
            {
                errors.Add("[AMZNGoDSDK] Adjust: Environment = Sandbox в релизной сборке — сборка остановлена.\n" +
                           "Установки и события Sandbox не попадают в продовую статистику Adjust.\n" +
                           "Как починить: AMZN GoD → SDK Settings → блок Adjust → Environment = Production → " +
                           "Save Settings. Для тестовой сборки — Development Build, или там же включите " +
                           "Allow Sandbox in release.");
            }
        }
    }
}
