using System.IO;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class DataLoader
    {
        private const string ConfigFileName = "amzn_god_sdk";

        public static SdkSettingsData LoadSettings()
        {
            TextAsset jsonFile = Resources.Load<TextAsset>(ConfigFileName);

            if (jsonFile == null)
                throw new FileNotFoundException("Could not find config file: " + ConfigFileName);

            var settings = JsonUtility.FromJson<SdkSettingsData>(jsonFile.text);
            MigrateLegacyAnalytics(settings);
            ApplyAnalyticsDefaults(settings);
            return settings;
        }

        // Backend и API-ключ аналитики зашиты в SDK и больше не редактируются в окне настроек.
        // Конфиг мог прийти из старой версии с пустыми полями — без этой подстановки
        // аналитика молча перестала бы слать события, а починить её из GUI уже нельзя.
        // Вызывается ПОСЛЕ миграции, чтобы значения из legacy-блоков имели приоритет.
        private static void ApplyAnalyticsDefaults(SdkSettingsData s)
        {
            if (s?.Analytics == null)
                return;

            if (string.IsNullOrWhiteSpace(s.Analytics.BaseUrl))
                s.Analytics.BaseUrl = AnalyticsSettingData.DefaultBaseUrl;

            if (string.IsNullOrWhiteSpace(s.Analytics.ApiKey))
                s.Analytics.ApiKey = AnalyticsSettingData.DefaultApiKey;
        }

        // Переносит значения из устаревших мест (FirstOpenTracking-блок хотфикса
        // и CrossPromo.TrackingBaseUrl/ApiKey/AppType до хотфикса) в новый Analytics-блок.
        // Срабатывает только если Analytics.BaseUrl пуст — после первого save из окна
        // настройки уже лежат правильно и миграция становится no-op.
        private static void MigrateLegacyAnalytics(SdkSettingsData s)
        {
            if (s == null) return;
            s.Analytics ??= new AnalyticsSettingData();
            s.FirstOpenTracking ??= new FirstOpenTrackingSettingData();

            if (!string.IsNullOrWhiteSpace(s.Analytics.BaseUrl))
                return;

            if (!string.IsNullOrWhiteSpace(s.FirstOpenTracking.BaseUrl))
            {
                s.Analytics.BaseUrl = s.FirstOpenTracking.BaseUrl;
                s.Analytics.ApiKey = s.FirstOpenTracking.ApiKey;
                // Явный маппинг, а не каст: у FirstOpenAppType своя нумерация (Free=0),
                // а в AnalyticsAppType 0 — это уже None.
                s.Analytics.AppType = s.FirstOpenTracking.AppType == FirstOpenAppType.Paid
                    ? AnalyticsAppType.Paid
                    : AnalyticsAppType.Free;
                Debug.Log("[DataLoader] Migrated Analytics settings from legacy FirstOpenTracking block.");
            }

            // Если значения не пришли из хотфикс-блока — пробуем восстановить из
            // ещё более старой версии, где трекинг сидел в CrossPromo.
            if (string.IsNullOrWhiteSpace(s.Analytics.BaseUrl))
            {
                // Поля удалены из CrossPromoSettingData — читаем напрямую из JSON
                // в JsonUtility их уже нет, но если пользователь обновляется с очень
                // старой версии, ему придётся выставить Analytics руками. В рамках
                // хотфикса этот путь уже не актуален — миграция CrossPromo→Analytics
                // оставлена символически в журнале изменений.
            }

            // Гладкий апгрейд: на хотфикс-версии Analytics модуля не было, поле
            // Enabled оставалось false. Если есть валидные BaseUrl+ApiKey и Adjust
            // включён — повторяем хотфикс-логику автовключения, чтобы first_open
            // продолжал уходить.
            if (!s.Analytics.Enabled
                && !string.IsNullOrWhiteSpace(s.Analytics.BaseUrl)
                && !string.IsNullOrWhiteSpace(s.Analytics.ApiKey)
                && s.Adjust != null && s.Adjust.Enabled)
            {
                s.Analytics.Enabled = true;
                Debug.Log("[DataLoader] Auto-enabled Analytics (Adjust on + valid Analytics URL/key).");
            }
        }
    }
}
