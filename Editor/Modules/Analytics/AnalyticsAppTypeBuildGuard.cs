using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Роняет билд, если в модуле Analytics не выбран App Type.
    ///
    /// <see cref="AnalyticsAppTypeSessionReset"/> обнуляет тип при каждом запуске редактора,
    /// и этот гард — вторая половина той же механики: без него сброшенное значение просто
    /// уехало бы в сборку как None, и события first_open потеряли бы разбивку free/paid.
    ///
    /// Окон намеренно не показываем (в т.ч. потому что билд может идти из CI): выбрасываем
    /// ошибку с путём до настроек, программист открывает окно и проставляет тип руками.
    /// </summary>
    internal sealed class AnalyticsAppTypeBuildGuard : IPreprocessBuildWithReport
    {
        // Раньше DependencyPreprocessor (-100) и остальных препроцессоров: не даём билду
        // начать раскладывать зависимости и define-символы ради заведомо провальной сборки.
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();

            // Конфига нет или SDK целиком выключен — модуля Analytics в сборке не будет.
            if (settings == null || !settings.Enabled)
                return;

            if (settings.Analytics == null || !settings.Analytics.Enabled)
                return;

            // Полная квалификация обязательна: в AMZNGoDSDK.Editor лежит одноимённое
            // зеркало enum'а, и без Runtime. компилятор выберет его.
            if (settings.Analytics.AppType != Runtime.AnalyticsAppType.None)
                return;

            throw new BuildFailedException(
                "[AMZNGoDSDK] Analytics: не выбран App Type — сборка остановлена.\n" +
                "App Type сбрасывается при каждом запуске Unity Editor, чтобы free/paid не " +
                "унаследовался от прошлой сессии и не сломал разбивку событий на бэкенде.\n" +
                "Как починить: AMZN GoD → SDK Settings → блок Analytics → App Type (Free или Paid) " +
                "→ Save Settings, затем собрать заново."
            );
        }
    }
}
