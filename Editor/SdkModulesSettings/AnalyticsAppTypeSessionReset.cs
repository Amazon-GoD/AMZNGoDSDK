using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Сбрасывает Analytics.AppType в <see cref="Runtime.AnalyticsAppType.None"/> один раз за запуск
    /// Unity Editor.
    ///
    /// Зачем: App Type (free/paid) определяет разбивку событий на бэкенде, и его нельзя
    /// «унаследовать» от прошлой сессии — иначе paid-сборка уезжает с free-типом просто потому,
    /// что вчера собирали free. Значение обнуляется молча, без окон и диалогов: программист сам
    /// открывает AMZN GoD/SDK Settings и проставляет тип, а забытый выбор ловит
    /// <see cref="AnalyticsAppTypeBuildGuard"/> на этапе билда.
    ///
    /// <see cref="SessionState"/> (а не EditorPrefs) выбран специально: он живёт ровно один
    /// запуск редактора и переживает domain reload, поэтому рекомпиляция скриптов и вход в
    /// Play mode не затирают уже сделанный выбор, а перезапуск Unity — затирает.
    /// </summary>
    [InitializeOnLoad]
    internal static class AnalyticsAppTypeSessionReset
    {
        private const string SessionKey = "AMZNGoDSDK.Analytics.AppTypeResetDone";
        private const string Tag = "[AMZNGoDSDK][Analytics]";

        static AnalyticsAppTypeSessionReset()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            // Ассеты на момент статического конструктора могут быть ещё не импортированы,
            // а запись в AssetDatabase из-под загрузки домена приводит к предупреждениям Unity.
            EditorApplication.delayCall += ResetOnce;
        }

        private static void ResetOnce()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            Runtime.SdkSettingsData settings;
            try
            {
                settings = SdkSettingsManager.LoadRuntimeSettings();
            }
            catch (System.Exception e)
            {
                // Битый/недочитанный конфиг не должен ронять загрузку редактора. Флаг сессии
                // не ставим — попробуем ещё раз на следующем domain reload.
                Debug.LogWarning($"{Tag} Не удалось прочитать конфиг для сброса App Type: {e.Message}");
                return;
            }

            // Конфига ещё нет (SDK не настраивали) — сбрасывать нечего, но и повторять не нужно:
            // сам факт отсутствия конфига означает AppType == None по умолчанию.
            if (settings?.Analytics == null)
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            // Полная квалификация обязательна: в AMZNGoDSDK.Editor лежит одноимённое
            // зеркало enum'а, и без Runtime. компилятор выберет его.
            if (settings.Analytics.AppType != Runtime.AnalyticsAppType.None)
            {
                settings.Analytics.AppType = Runtime.AnalyticsAppType.None;
                SdkSettingsManager.WriteRuntimeSettings(settings);
                SDKSettingsWindow.ReloadOpenWindows();
                Debug.Log($"{Tag} App Type сброшен на старте редактора — выставь его в AMZN GoD/SDK Settings перед билдом.");
            }

            SessionState.SetBool(SessionKey, true);
        }
    }
}
