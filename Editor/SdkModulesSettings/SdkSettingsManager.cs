using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    public static class SdkSettingsManager
    {
        private const string ConfigFileName = "amzn_god_sdk.json";
        private const string ResourcesPath = "Assets/Resources/";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            
        }

        private static bool ConfigFileIsAlreadyExist()
        {
            try
            {
                string fullPath = Path.Combine(ResourcesPath, ConfigFileName);
                
                return File.ReadAllText(fullPath).Length > 0;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public static SdkSettingsData LoadSettings()
        {
            var runtimeSettings = LoadRuntimeSettings();

            if (runtimeSettings == null)
                return new SdkSettingsData();

            // Load runtime settings and convert to editor settings
            return ConvertToEditorSettings(runtimeSettings);
        }

        /// <summary>Путь к конфигу в проекте (asset path, а не абсолютный).</summary>
        internal static string ConfigAssetPath => Path.Combine(ResourcesPath, ConfigFileName).Replace('\\', '/');

        /// <summary>
        /// Читает конфиг «как есть», без конвертации в editor-представление.
        /// Возвращает null, если конфига ещё нет — вызывающий сам решает, ошибка это или нет.
        ///
        /// Читаем с диска, а не через Resources.Load: TextAsset кэшируется, и сразу после
        /// WriteRuntimeSettings (сброс App Type на старте редактора) Resources отдал бы
        /// старое значение, пока Unity не доимпортирует ассет.
        /// </summary>
        internal static Runtime.SdkSettingsData LoadRuntimeSettings()
        {
            string fullPath = Path.Combine(ResourcesPath, ConfigFileName);

            if (!File.Exists(fullPath))
                return null;

            return JsonUtility.FromJson<Runtime.SdkSettingsData>(File.ReadAllText(fullPath));
        }

        /// <summary>
        /// Пишет runtime-конфиг напрямую, без валидации нативных зависимостей, обновления
        /// define-символов и перекладывания папок модулей. Для служебных правок отдельных
        /// полей (см. AnalyticsAppTypeSessionReset): полный SaveSettings на старте редактора
        /// дёргал бы диалоги и рекомпиляцию.
        /// </summary>
        internal static void WriteRuntimeSettings(Runtime.SdkSettingsData runtimeSettings)
        {
            if (runtimeSettings == null)
                return;

            if (!Directory.Exists(ResourcesPath))
                Directory.CreateDirectory(ResourcesPath);

            File.WriteAllText(Path.Combine(ResourcesPath, ConfigFileName), JsonUtility.ToJson(runtimeSettings, true));
            AssetDatabase.ImportAsset(ConfigAssetPath, ImportAssetOptions.ForceUpdate);
        }

        private static SdkSettingsData ConvertToEditorSettings(Runtime.SdkSettingsData runtimeSettings)
        {
            var editorSettings = new SdkSettingsData
            {
                Enabled = runtimeSettings.Enabled,
                Adjust = ConvertAdjustSettingsToEditor(runtimeSettings.Adjust),
                AppMetrica = ConvertAppMetricaSettingsToEditor(runtimeSettings.AppMetrica),
                CrossPromo = ConvertCrossPromoSettingsToEditor(runtimeSettings.CrossPromo),
                Infatica = ConvertInfaticaSettingsToEditor(runtimeSettings.Infatica),
                InAppPurchase = ConvertInAppPurchaseSettingsToEditor(runtimeSettings.InAppPurchase),
                Firebase = ConvertFirebaseSettingsToEditor(runtimeSettings.Firebase),
                InternetConnection = ConvertInternetConnectionSettingsToEditor(runtimeSettings.InternetConnection),
                DebugConsole = ConvertDebugConsoleSettingsToEditor(runtimeSettings.DebugConsole),
                Analytics = ConvertAnalyticsSettingsToEditor(runtimeSettings.Analytics)
            };

            return editorSettings;
        }

        private static AdjustSettingData ConvertAdjustSettingsToEditor(Runtime.AdjustSettingData runtimeSettings)
        {
            var adjustEnvironment = runtimeSettings.Environment == Runtime.AdjustSettingData.AdjustEnvironment.Production
                ? AdjustSettingData.AdjustEnvironment.Production
                : AdjustSettingData.AdjustEnvironment.Sandbox;

            return new AdjustSettingData
            {
                Enabled = runtimeSettings.Enabled,
                Key = runtimeSettings.Key,
                Environment = adjustEnvironment
            };
        }

        private static AppMetricaSettingData ConvertAppMetricaSettingsToEditor(Runtime.AppMetricaSettingData runtimeSettings)
        {
            return new AppMetricaSettingData
            {
                Enabled = runtimeSettings.Enabled,
                Key = runtimeSettings.Key
            };
        }

        private static CrossPromoSettingData ConvertCrossPromoSettingsToEditor(Runtime.CrossPromoSettingData runtimeSettings)
        {
            return new CrossPromoSettingData
            {
                Enabled = runtimeSettings.Enabled,
                ConfigUrl = runtimeSettings.ConfigUrl,
                // DefaultPromotedAppId убран из настроек; VideoBackend всегда ExoPlayer.
                VideoBackend = Runtime.VideoPlayerBackend.ExoPlayer
            };
        }

        private static InfaticaSettingData ConvertInfaticaSettingsToEditor(Runtime.InfaticaSettingData runtimeSettings)
        {
            var mode = runtimeSettings.Mode == Runtime.InfaticaSettingData.InfaticaMode.Review
                ? InfaticaSettingData.InfaticaMode.Review
                : InfaticaSettingData.InfaticaMode.Production;

            var sdkVersion = runtimeSettings.SdkVersion == Runtime.InfaticaSettingData.InfaticaSdkVersion.WithoutJobs
                ? InfaticaSettingData.InfaticaSdkVersion.WithoutJobs
                : InfaticaSettingData.InfaticaSdkVersion.WithJobs;

            return new InfaticaSettingData
            {
                Enabled = runtimeSettings.Enabled,
                PartnerId = runtimeSettings.PartnerId,
                NotificationTitle = runtimeSettings.NotificationTitle,
                Mode = mode,
                SdkVersion = sdkVersion,
                BatteryOptimizationIgnoreAsking = runtimeSettings.BatteryOptimizationIgnoreAsking
            };
        }

        private static InAppPurchaseSettingData ConvertInAppPurchaseSettingsToEditor(Runtime.InAppPurchaseSettingData runtimeSettings)
        {
            var editorSettings = new InAppPurchaseSettingData
            {
                Enabled = runtimeSettings.Enabled
            };

            foreach (var runtimeProduct in runtimeSettings.SubscriptionProducts)
            {
                editorSettings.SubscriptionProducts.Add(new SubscriptionProduct
                {
                    ProductId = runtimeProduct.ProductId,
                    DisplayName = runtimeProduct.DisplayName,
                    // TermDays намеренно НЕ подменяется из устаревшего DurationDays: во всех
                    // старых конфигах там лежит дефолтная 30, а подписки портфеля недельные.
                    // Нулевой TermDays заставит один раз проставить настоящий срок (IAP-15).
                    TermDays = runtimeProduct.TermDays,
                    TestTermMinutes = runtimeProduct.TestTermMinutes,
                    Enabled = runtimeProduct.Enabled
                });
            }

            foreach (var runtimeProduct in runtimeSettings.ConsumableProducts)
            {
                editorSettings.ConsumableProducts.Add(new ConsumableProduct
                {
                    ProductId = runtimeProduct.ProductId,
                    DisplayName = runtimeProduct.DisplayName,
                    Enabled = runtimeProduct.Enabled
                });
            }

            foreach (var runtimeProduct in runtimeSettings.NonConsumableProducts)
            {
                editorSettings.NonConsumableProducts.Add(new NonConsumableProduct
                {
                    ProductId = runtimeProduct.ProductId,
                    DisplayName = runtimeProduct.DisplayName,
                    Enabled = runtimeProduct.Enabled
                });
            }

            return editorSettings;
        }

        private static FirebaseSettingData ConvertFirebaseSettingsToEditor(Runtime.FirebaseSettingData runtimeSettings)
        {
            runtimeSettings ??= new Runtime.FirebaseSettingData();

            return new FirebaseSettingData
            {
                Enabled = runtimeSettings.Enabled,
                EnableAnalytics = runtimeSettings.EnableAnalytics,
                EnableCrashlytics = runtimeSettings.EnableCrashlytics
            };
        }

        private static InternetConnectionSettingData ConvertInternetConnectionSettingsToEditor(Runtime.InternetConnectionSettingData runtimeSettings)
        {
            return new InternetConnectionSettingData
            {
                Enabled = runtimeSettings.Enabled,
                CheckIntervalSeconds = runtimeSettings.CheckIntervalSeconds,
                UseHttpProbe = runtimeSettings.UseHttpProbe,
                ProbeUrl = runtimeSettings.ProbeUrl,
                ProbeTimeoutSeconds = runtimeSettings.ProbeTimeoutSeconds,
                PauseGameWhenOffline = runtimeSettings.PauseGameWhenOffline,
                ShowBanner = runtimeSettings.ShowBanner,
                BannerMessage = runtimeSettings.BannerMessage,
                ShowRetryButton = runtimeSettings.ShowRetryButton,
                RetryButtonLabel = runtimeSettings.RetryButtonLabel,
                BannerSortingOrder = runtimeSettings.BannerSortingOrder,
            };
        }

        public static bool ValidateInAppPurchaseProductIds(InAppPurchaseSettingData settings, out string message)
        {
            var subscriptionIds = settings.SubscriptionProducts
                .Select(x => x.ProductId?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();

            var consumableIds = settings.ConsumableProducts
                .Select(x => x.ProductId?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();

            var nonConsumableIds = settings.NonConsumableProducts
                .Select(x => x.ProductId?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();

            var ids = subscriptionIds
                .Concat(consumableIds)
                .Concat(nonConsumableIds)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (ids.Length > 0)
            {
                message = $"Невозможно сохранить: повторяются идентификаторы продуктов ({string.Join(", ", ids)}). Каждый ProductId должен быть уникальным.";
                return false;
            }

            // IAP-15: у включённой подписки обязателен срок периода — от него считаются
            // оплаченные периоды. Выключенный продукт сохранение не блокирует.
            var missingTerm = settings.SubscriptionProducts
                .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ProductId) && x.TermDays <= 0)
                .Select(x => x.ProductId.Trim())
                .ToArray();

            if (missingTerm.Length > 0)
            {
                message = $"Невозможно сохранить: у подписок не задан срок периода Term (days): {string.Join(", ", missingTerm)}. " +
                          "Укажи реальный период подписки из консоли Amazon (например, 7 для недельной).";
                return false;
            }

            message = string.Empty;
            return true;
        }

        public static bool SaveSettings(SdkSettingsData settings)
        {
            // Барьер IAP-15 живёт здесь, а не только в кнопке окна: SaveSettings публичный,
            // и обходной вызов (визард, тулинг модулей) не должен сохранять подписку без
            // срока. Окно валидирует то же самое раньше — ради диалога с текстом.
            if (settings?.InAppPurchase != null
                && !ValidateInAppPurchaseProductIds(settings.InAppPurchase, out var iapError))
            {
                Debug.LogError($"[SdkSettingsManager] Save rejected: {iapError}");
                return false;
            }

            if (!NativeDependencyValidator.ValidateAndPrompt(settings))
            {
                Debug.Log("[SdkSettingsManager] Save cancelled by user due to native dependency conflicts.");
                return false;
            }

            // Convert Editor settings to Runtime settings
            var runtimeSettings = ConvertToRuntimeSettings(settings);

            string json = JsonUtility.ToJson(runtimeSettings, true);

            string fullPath = Path.Combine(ResourcesPath, ConfigFileName);

            if (!Directory.Exists(ResourcesPath))
                Directory.CreateDirectory(ResourcesPath);

            File.WriteAllText(fullPath, json);

            AssetDatabase.Refresh();
            
            Debug.Log("[SdkSettingsManager] ===== SAVE SETTINGS STARTED =====");
            Debug.Log($"[SdkSettingsManager] Infatica.Enabled = {settings.Infatica.Enabled}, SdkVersion = {settings.Infatica.SdkVersion}");
            
            // Обновляем define symbols для условной компиляции
            Debug.Log("[SdkSettingsManager] Updating define symbols...");
            ModuleDefineManager.UpdateDefineSymbols(settings);
            
            // Обновляем папки модулей если включено авто-обновление
            Debug.Log("[SdkSettingsManager] Calling ModuleFolderManager.OnSettingsSaved...");
            ModuleFolderManager.OnSettingsSaved(settings);
            
            Debug.Log("[SdkSettingsManager] ===== SAVE SETTINGS COMPLETED =====");
            return true;
        }

        private static Runtime.SdkSettingsData ConvertToRuntimeSettings(SdkSettingsData editorSettings)
        {
            var runtimeSettings = new Runtime.SdkSettingsData
            {
                Enabled = editorSettings.Enabled,
                Adjust = ConvertAdjustSettings(editorSettings.Adjust),
                AppMetrica = ConvertAppMetricaSettings(editorSettings.AppMetrica),
                CrossPromo = ConvertCrossPromoSettings(editorSettings.CrossPromo),
                Infatica = ConvertInfaticaSettings(editorSettings.Infatica),
                InAppPurchase = ConvertInAppPurchaseSettings(editorSettings.InAppPurchase),
                Firebase = ConvertFirebaseSettings(editorSettings.Firebase),
                InternetConnection = ConvertInternetConnectionSettings(editorSettings.InternetConnection),
                DebugConsole = ConvertDebugConsoleSettings(editorSettings.DebugConsole),
                Analytics = ConvertAnalyticsSettings(editorSettings.Analytics)
            };

            return runtimeSettings;
        }

        private static Runtime.AdjustSettingData ConvertAdjustSettings(AdjustSettingData editorSettings)
        {
            var adjustEnvironment = editorSettings.Environment == AdjustSettingData.AdjustEnvironment.Production
                ? Runtime.AdjustSettingData.AdjustEnvironment.Production
                : Runtime.AdjustSettingData.AdjustEnvironment.Sandbox;

            return new Runtime.AdjustSettingData
            {
                Enabled = editorSettings.Enabled,
                Key = editorSettings.Key,
                Environment = adjustEnvironment
            };
        }

        private static Runtime.AppMetricaSettingData ConvertAppMetricaSettings(AppMetricaSettingData editorSettings)
        {
            return new Runtime.AppMetricaSettingData
            {
                Enabled = editorSettings.Enabled,
                Key = editorSettings.Key
            };
        }

        private static Runtime.CrossPromoSettingData ConvertCrossPromoSettings(CrossPromoSettingData editorSettings)
        {
            return new Runtime.CrossPromoSettingData
            {
                Enabled = editorSettings.Enabled,
                ConfigUrl = editorSettings.ConfigUrl,
                // DefaultPromotedAppId всегда дефолтный (пустой) — из настроек убран.
                DefaultPromotedAppId = string.Empty,
                // Video-бэкенд всегда ExoPlayer — выбор из настроек убран.
                VideoBackend = Runtime.VideoPlayerBackend.ExoPlayer
            };
        }

        private static Runtime.InfaticaSettingData ConvertInfaticaSettings(InfaticaSettingData editorSettings)
        {
            var mode = editorSettings.Mode == InfaticaSettingData.InfaticaMode.Review
                ? Runtime.InfaticaSettingData.InfaticaMode.Review
                : Runtime.InfaticaSettingData.InfaticaMode.Production;

            var sdkVersion = editorSettings.SdkVersion == InfaticaSettingData.InfaticaSdkVersion.WithoutJobs
                ? Runtime.InfaticaSettingData.InfaticaSdkVersion.WithoutJobs
                : Runtime.InfaticaSettingData.InfaticaSdkVersion.WithJobs;

            return new Runtime.InfaticaSettingData
            {
                Enabled = editorSettings.Enabled,
                PartnerId = editorSettings.PartnerId,
                NotificationTitle = editorSettings.NotificationTitle,
                Mode = mode,
                SdkVersion = sdkVersion,
                BatteryOptimizationIgnoreAsking = editorSettings.BatteryOptimizationIgnoreAsking
            };
        }

        private static Runtime.InAppPurchaseSettingData ConvertInAppPurchaseSettings(InAppPurchaseSettingData editorSettings)
        {
            var runtimeSettings = new Runtime.InAppPurchaseSettingData
            {
                Enabled = editorSettings.Enabled
            };

            foreach (var editorProduct in editorSettings.SubscriptionProducts)
            {
                runtimeSettings.SubscriptionProducts.Add(new Runtime.SubscriptionProduct
                {
                    ProductId = editorProduct.ProductId,
                    DisplayName = editorProduct.DisplayName,
                    TermDays = editorProduct.TermDays,
                    TestTermMinutes = editorProduct.TestTermMinutes,
                    Enabled = editorProduct.Enabled
                });
            }

            foreach (var editorProduct in editorSettings.ConsumableProducts)
            {
                runtimeSettings.ConsumableProducts.Add(new Runtime.ConsumableProduct
                {
                    ProductId = editorProduct.ProductId,
                    DisplayName = editorProduct.DisplayName,
                    Enabled = editorProduct.Enabled
                });
            }

            foreach (var editorProduct in editorSettings.NonConsumableProducts)
            {
                runtimeSettings.NonConsumableProducts.Add(new Runtime.NonConsumableProduct
                {
                    ProductId = editorProduct.ProductId,
                    DisplayName = editorProduct.DisplayName,
                    Enabled = editorProduct.Enabled
                });
            }

            return runtimeSettings;
        }

        private static Runtime.FirebaseSettingData ConvertFirebaseSettings(FirebaseSettingData editorSettings)
        {
            return new Runtime.FirebaseSettingData
            {
                Enabled = editorSettings.Enabled,
                EnableAnalytics = editorSettings.EnableAnalytics,
                EnableCrashlytics = editorSettings.EnableCrashlytics
            };
        }

        private static Runtime.InternetConnectionSettingData ConvertInternetConnectionSettings(InternetConnectionSettingData editorSettings)
        {
            return new Runtime.InternetConnectionSettingData
            {
                Enabled = editorSettings.Enabled,
                CheckIntervalSeconds = editorSettings.CheckIntervalSeconds,
                UseHttpProbe = editorSettings.UseHttpProbe,
                ProbeUrl = editorSettings.ProbeUrl,
                ProbeTimeoutSeconds = editorSettings.ProbeTimeoutSeconds,
                PauseGameWhenOffline = editorSettings.PauseGameWhenOffline,
                ShowBanner = editorSettings.ShowBanner,
                BannerMessage = editorSettings.BannerMessage,
                ShowRetryButton = editorSettings.ShowRetryButton,
                RetryButtonLabel = editorSettings.RetryButtonLabel,
                BannerSortingOrder = editorSettings.BannerSortingOrder,
            };
        }

        private static DebugConsoleSettingData ConvertDebugConsoleSettingsToEditor(Runtime.DebugConsoleSettingData runtimeSettings)
        {
            runtimeSettings ??= new Runtime.DebugConsoleSettingData();
            return new DebugConsoleSettingData { Enabled = runtimeSettings.Enabled };
        }

        private static Runtime.DebugConsoleSettingData ConvertDebugConsoleSettings(DebugConsoleSettingData editorSettings)
        {
            return new Runtime.DebugConsoleSettingData { Enabled = editorSettings.Enabled };
        }

        private static AnalyticsSettingData ConvertAnalyticsSettingsToEditor(Runtime.AnalyticsSettingData runtimeSettings)
        {
            runtimeSettings ??= new Runtime.AnalyticsSettingData();

            // None обязан доезжать до окна как None: подстановка Free тут вернула бы
            // «молчаливый дефолт», который вся эта механика и убирает.
            var appType = runtimeSettings.AppType switch
            {
                Runtime.AnalyticsAppType.Free => AnalyticsAppType.Free,
                Runtime.AnalyticsAppType.Paid => AnalyticsAppType.Paid,
                _ => AnalyticsAppType.None
            };

            // Backend/ключ зашиты в SDK: если конфиг пришёл из старой версии с пустыми
            // полями, подставляем дефолт — иначе окно сохранило бы пустоту обратно в JSON.
            return new AnalyticsSettingData
            {
                Enabled = runtimeSettings.Enabled,
                BaseUrl = string.IsNullOrWhiteSpace(runtimeSettings.BaseUrl)
                    ? Runtime.AnalyticsSettingData.DefaultBaseUrl
                    : runtimeSettings.BaseUrl,
                ApiKey = string.IsNullOrWhiteSpace(runtimeSettings.ApiKey)
                    ? Runtime.AnalyticsSettingData.DefaultApiKey
                    : runtimeSettings.ApiKey,
                AppType = appType
            };
        }

        private static Runtime.AnalyticsSettingData ConvertAnalyticsSettings(AnalyticsSettingData editorSettings)
        {
            editorSettings ??= new AnalyticsSettingData();

            var appType = editorSettings.AppType switch
            {
                AnalyticsAppType.Free => Runtime.AnalyticsAppType.Free,
                AnalyticsAppType.Paid => Runtime.AnalyticsAppType.Paid,
                _ => Runtime.AnalyticsAppType.None
            };

            return new Runtime.AnalyticsSettingData
            {
                Enabled = editorSettings.Enabled,
                BaseUrl = editorSettings.BaseUrl,
                ApiKey = editorSettings.ApiKey,
                AppType = appType
            };
        }
    }
}