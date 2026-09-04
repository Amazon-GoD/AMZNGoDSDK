#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Net;
using System.Text;
using AMZNGoDSDK.Runtime;
using UnityEditor;
using UnityEngine;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Ручное управление счётчиками показов креативов — чтобы проверять переход кросс-промо
    /// на медиацию, не дожидаясь, пока капы выберутся сами.
    /// <para>
    /// В Play Mode они не выберутся вообще: ExoPlayer-оверлей Android-only, в редакторе он
    /// закрывается сразу с unsupported_platform, а <c>IncrementShowCount</c> вызывается только
    /// на первом отрисованном кадре нативного плеера. Счётчик не растёт → капы не сгорают →
    /// <c>HasFill</c> не гаснет → до медиации дело не доходит. Здесь мы ставим счётчики руками.
    /// </para>
    /// <para>
    /// Правит PlayerPrefs РЕДАКТОРА (на Windows это реестр, ключ на проект). На устройство
    /// это не влияет никак — там счётчики свои.
    /// </para>
    /// </summary>
    public static class CrossPromoCapDebug
    {
        private const string MenuRoot = "AMZN GoD/Debug/Cross-Promo Caps/";

        [MenuItem(MenuRoot + "Show Status", false, 210)]
        public static void ShowStatus()
        {
            var config = LoadConfig();
            if (config == null)
                return;

            var report = new StringBuilder();
            report.AppendLine($"[CrossPromoCapDebug] Креативов в конфиге: {config.Videos.Count}");
            report.AppendLine("  Title | cap | MaxShowCount | лимит | показов | исчерпан");

            int exhausted = 0;
            int unlimited = 0;

            foreach (var video in config.Videos)
            {
                int limit = video.EffectiveShowLimit;
                int shown = string.IsNullOrWhiteSpace(video.Title) ? 0 : PlayerPrefs.GetInt(video.Title, 0);
                bool reached = video.IsShowLimitReached();

                if (limit <= 0) unlimited++;
                if (reached) exhausted++;

                report.AppendLine($"  {video.Title} | {video.cap} | {video.MaxShowCount} | " +
                                  $"{(limit <= 0 ? "без лимита" : limit.ToString())} | {shown} | {(reached ? "да" : "нет")}");
            }

            report.AppendLine();
            report.AppendLine($"  Исчерпано: {exhausted}/{config.Videos.Count}");
            report.AppendLine($"  HasAvailableVideos(): {config.HasAvailableVideos()} " +
                              "(false → показы уходят в медиацию AppLovin)");

            if (unlimited > 0)
            {
                report.AppendLine();
                report.AppendLine($"  ВНИМАНИЕ: у {unlimited} креативов нет лимита (cap = 0). " +
                                  "Пул с ними НИКОГДА не исчерпается, и переход на медиацию не наступит. " +
                                  "Чтобы проверить переход, проставь \"cap\" в JSON-конфиге.");
            }

            Debug.Log(report.ToString());
        }

        [MenuItem(MenuRoot + "Burn All Caps (switch to mediation)", false, 211)]
        public static void BurnAllCaps()
        {
            var config = LoadConfig();
            if (config == null)
                return;

            int withLimit = 0;
            int withoutLimit = 0;

            foreach (var video in config.Videos)
            {
                if (video.EffectiveShowLimit > 0 && !string.IsNullOrWhiteSpace(video.Title))
                    withLimit++;
                else
                    withoutLimit++;
            }

            if (withLimit == 0)
            {
                EditorUtility.DisplayDialog(
                    "Cross-Promo Caps",
                    "Ни у одного креатива нет лимита показов (cap = 0 и MaxShowCount = 0).\n\n" +
                    "Сжигать нечего: такой пул не исчерпывается, и переход на медиацию не наступит. " +
                    "Проставь \"cap\" в JSON-конфиге и повтори.",
                    "OK");
                return;
            }

            string message = $"Счётчики показов будут выставлены в лимит у {withLimit} креативов — " +
                             "кросс-промо начнёт считать их исчерпанными и уведёт показы в медиацию AppLovin.\n\n";

            if (withoutLimit > 0)
                message += $"Без лимита останутся {withoutLimit} креативов — пока они в конфиге, " +
                           "пул не исчерпается и переход НЕ наступит.\n\n";

            message += "Правятся PlayerPrefs редактора; на устройство это не влияет. Продолжить?";

            if (!EditorUtility.DisplayDialog("Cross-Promo Caps", message, "Сжечь капы", "Отмена"))
                return;

            foreach (var video in config.Videos)
            {
                int limit = video.EffectiveShowLimit;
                if (limit <= 0 || string.IsNullOrWhiteSpace(video.Title))
                    continue;

                PlayerPrefs.SetInt(video.Title, limit);
                Debug.Log($"[CrossPromoCapDebug] '{video.Title}': счётчик = {limit} (лимит выбран)");
            }

            PlayerPrefs.Save();
            ShowStatus();
        }

        [MenuItem(MenuRoot + "Reset Counters", false, 212)]
        public static void ResetCounters()
        {
            var config = LoadConfig();
            if (config == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Cross-Promo Caps",
                    $"Счётчики показов и кулдауны будут удалены у {config.Videos.Count} креативов — " +
                    "кросс-промо снова станет источником показов.\n\n" +
                    "Правятся PlayerPrefs редактора. Продолжить?",
                    "Сбросить", "Отмена"))
                return;

            foreach (var video in config.Videos)
            {
                if (string.IsNullOrWhiteSpace(video.Title))
                    continue;

                PlayerPrefs.DeleteKey(video.Title);
                PlayerPrefs.DeleteKey("CrossPromo_Cooldown_" + video.Title);
            }

            PlayerPrefs.DeleteKey("CrossPromo_LastShown");
            PlayerPrefs.Save();

            Debug.Log($"[CrossPromoCapDebug] Счётчики и кулдауны сброшены у {config.Videos.Count} креативов.");
            ShowStatus();
        }

        /// <summary>
        /// Тянет тот же конфиг, что и рантайм, по адресу из настроек SDK.
        /// <para>
        /// Скачивается обычным WebClient, а не UnityWebRequest: последний продвигается
        /// апдейтом редактора, и синхронное ожидание из пункта меню его бы просто заклинило.
        /// </para>
        /// </summary>
        private static PromosConfigurationInfo LoadConfig()
        {
            var settings = SdkSettingsManager.LoadSettings();
            string url = settings?.CrossPromo?.ConfigUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                EditorUtility.DisplayDialog("Cross-Promo Caps",
                    "В настройках SDK не задан Config URL кросс-промо.", "OK");
                return null;
            }

            string json;
            try
            {
                EditorUtility.DisplayProgressBar("Cross-Promo Caps", $"Загрузка {url}", 0.5f);
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    json = client.DownloadString(url);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Cross-Promo Caps",
                    $"Не удалось скачать конфиг:\n{url}\n\n{ex.Message}", "OK");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            PromosConfigurationInfo config;
            try
            {
                config = JsonUtility.FromJson<PromosConfigurationInfo>(json);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Cross-Promo Caps", $"Конфиг не разобрался: {ex.Message}", "OK");
                return null;
            }

            if (config?.Videos == null || config.Videos.Count == 0)
            {
                EditorUtility.DisplayDialog("Cross-Promo Caps", "В конфиге нет ни одного креатива.", "OK");
                return null;
            }

            return config;
        }
    }
}
#endif
