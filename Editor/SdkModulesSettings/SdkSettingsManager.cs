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
            TextAsset jsonFile = Resources.Load<TextAsset>(ConfigFileName.Split('.')[0]);

            if (jsonFile == null)
                return new SdkSettingsData();

            // Load runtime settings and convert to editor settings
            var runtimeSettings = JsonUtility.FromJson<Runtime.SdkSettingsData>(jsonFile.text);
            return ConvertToEditorSettings(runtimeSettings);
        }

        private static SdkSettingsData ConvertToEditorSettings(Runtime.SdkSettingsData runtimeSettings)
        {
            var editorSettings = new SdkSettingsData
            {
                Enabled = runtimeSettings.Enabled,
                Adjust = ConvertAdjustSettingsToEditor(runtimeSettings.Adjust),
                AppMetrica = ConvertAppMetricaSettingsToEditor(runtimeSettings.AppMetrica),
                CrossPromo = ConvertCrossPromoSettingsToEditor(runtimeSettings.CrossPromo),
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
                DefaultPromotedAppId = runtimeSettings.DefaultPromotedAppId,
                VideoBackend = runtimeSettings.VideoBackend
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
                    RewardAmount = runtimeProduct.RewardAmount,
                    DurationDays = runtimeProduct.DurationDays,
                    ConsumableRewards = ConvertSubscriptionConsumableRewardsToEditor(runtimeProduct.ConsumableRewards),
                    Enabled = runtimeProduct.Enabled
                });
            }

            foreach (var runtimeProduct in runtimeSettings.ConsumableProducts)
            {
                editorSettings.ConsumableProducts.Add(new ConsumableProduct
                {
                    ProductId = runtimeProduct.ProductId,
                    DisplayName = runtimeProduct.DisplayName,
                    RewardAmount = runtimeProduct.RewardAmount,
                    RewardKey = runtimeProduct.RewardKey,
                    RewardType = runtimeProduct.RewardType,
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
                PauseGameWhenOffline = runtimeSettings.PauseGameWhenOffline,
                ShowBanner = runtimeSettings.ShowBanner,
                BannerMessage = runtimeSettings.BannerMessage,
                ShowRetryButton = runtimeSettings.ShowRetryButton,
                RetryButtonLabel = runtimeSettings.RetryButtonLabel,
                BannerSortingOrder = runtimeSettings.BannerSortingOrder,
            };
        }

        private static List<SubscriptionConsumableReward> ConvertSubscriptionConsumableRewardsToEditor(List<Runtime.SubscriptionConsumableReward> runtimeRewards)
        {
            var editorRewards = new List<SubscriptionConsumableReward>();

            if (runtimeRewards == null)
                return editorRewards;

            foreach (var runtimeReward in runtimeRewards)
            {
                editorRewards.Add(new SubscriptionConsumableReward
                {
                    ProductId = runtimeReward.ProductId,
                    RewardAmount = runtimeReward.RewardAmount,
                    RewardKey = runtimeReward.RewardKey,
                    RewardType = runtimeReward.RewardType
                });
            }

            return editorRewards;
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

            var ids = subscriptionIds
                .Concat(consumableIds)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (ids.Length == 0)
            {
                message = string.Empty;
                return true;
            }

            message = $"Невозможно сохранить: повторяются идентификаторы продуктов ({string.Join(", ", ids)}). Каждый ProductId должен быть уникальным.";
            return false;
        }

        public static bool SaveSettings(SdkSettingsData settings)
        {
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
                DefaultPromotedAppId = editorSettings.DefaultPromotedAppId,
                VideoBackend = editorSettings.VideoBackend
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
                    RewardAmount = editorProduct.RewardAmount,
                    DurationDays = editorProduct.DurationDays,
                    ConsumableRewards = ConvertSubscriptionConsumableRewards(editorProduct.ConsumableRewards),
                    Enabled = editorProduct.Enabled
                });
            }

            foreach (var editorProduct in editorSettings.ConsumableProducts)
            {
                runtimeSettings.ConsumableProducts.Add(new Runtime.ConsumableProduct
                {
                    ProductId = editorProduct.ProductId,
                    DisplayName = editorProduct.DisplayName,
                    RewardAmount = editorProduct.RewardAmount,
                    RewardKey = editorProduct.RewardKey,
                    RewardType = editorProduct.RewardType,
                    Enabled = editorProduct.Enabled
                });
            }

            return runtimeSettings;
        }

        private static List<Runtime.SubscriptionConsumableReward> ConvertSubscriptionConsumableRewards(List<SubscriptionConsumableReward> editorRewards)
        {
            var runtimeRewards = new List<Runtime.SubscriptionConsumableReward>();

            if (editorRewards == null)
                return runtimeRewards;

            foreach (var reward in editorRewards)
            {
                runtimeRewards.Add(new Runtime.SubscriptionConsumableReward
                {
                    ProductId = reward.ProductId,
                    RewardAmount = reward.RewardAmount,
                    RewardKey = reward.RewardKey,
                    RewardType = reward.RewardType
                });
            }

            return runtimeRewards;
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

            var appType = runtimeSettings.AppType == Runtime.AnalyticsAppType.Paid
                ? AnalyticsAppType.Paid
                : AnalyticsAppType.Free;

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

            var appType = editorSettings.AppType == AnalyticsAppType.Paid
                ? Runtime.AnalyticsAppType.Paid
                : Runtime.AnalyticsAppType.Free;

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