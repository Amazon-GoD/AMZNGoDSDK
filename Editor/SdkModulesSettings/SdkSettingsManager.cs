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
                Infatica = ConvertInfaticaSettingsToEditor(runtimeSettings.Infatica),
                InAppPurchase = ConvertInAppPurchaseSettingsToEditor(runtimeSettings.InAppPurchase),
                Firebase = ConvertFirebaseSettingsToEditor(runtimeSettings.Firebase),
                InternetConnection = ConvertInternetConnectionSettingsToEditor(runtimeSettings.InternetConnection)
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
                AppodealSdkKey = runtimeSettings.AppodealSdkKey
            };
        }

        private static InfaticaSettingData ConvertInfaticaSettingsToEditor(Runtime.InfaticaSettingData runtimeSettings)
        {
            var mode = runtimeSettings.Mode == Runtime.InfaticaSettingData.InfaticaMode.Review
                ? InfaticaSettingData.InfaticaMode.Review
                : InfaticaSettingData.InfaticaMode.Production;

            return new InfaticaSettingData
            {
                Enabled = runtimeSettings.Enabled,
                Mode = mode,
                BatteryOptimizationIgnoreAsking = runtimeSettings.BatteryOptimizationIgnoreAsking
            };
        }

        private static InAppPurchaseSettingData ConvertInAppPurchaseSettingsToEditor(Runtime.InAppPurchaseSettingData runtimeSettings)
        {
            var editorSettings = new InAppPurchaseSettingData
            {
                Enabled = runtimeSettings.Enabled,
                AppStoreTarget = runtimeSettings.UseAmazonAppStore ? AppStoreTarget.AmazonAppStore : AppStoreTarget.GooglePlay,
                UseFakeStoreInEditor = runtimeSettings.UseFakeStoreInEditor
            };

            // Convert subscription products
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

            // Convert consumable products
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

        public static void SaveSettings(SdkSettingsData settings)
        {
            // Convert Editor settings to Runtime settings
            var runtimeSettings = ConvertToRuntimeSettings(settings);

            string json = JsonUtility.ToJson(runtimeSettings, true);

            string fullPath = Path.Combine(ResourcesPath, ConfigFileName);

            if (!Directory.Exists(ResourcesPath))
                Directory.CreateDirectory(ResourcesPath);

            File.WriteAllText(fullPath, json);

            AssetDatabase.Refresh();
            
            Debug.Log("[SdkSettingsManager] ===== SAVE SETTINGS STARTED =====");
            Debug.Log($"[SdkSettingsManager] Infatica.Enabled = {settings.Infatica.Enabled}");
            
            // Обновляем define symbols для условной компиляции
            Debug.Log("[SdkSettingsManager] Updating define symbols...");
            ModuleDefineManager.UpdateDefineSymbols(settings);
            
            // Обновляем папки модулей если включено авто-обновление
            Debug.Log("[SdkSettingsManager] Calling ModuleFolderManager.OnSettingsSaved...");
            ModuleFolderManager.OnSettingsSaved(settings);
            
            Debug.Log("[SdkSettingsManager] ===== SAVE SETTINGS COMPLETED =====");
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
                InternetConnection = ConvertInternetConnectionSettings(editorSettings.InternetConnection)
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
                AppodealSdkKey = editorSettings.AppodealSdkKey
            };
        }

        private static Runtime.InfaticaSettingData ConvertInfaticaSettings(InfaticaSettingData editorSettings)
        {
            var mode = editorSettings.Mode == InfaticaSettingData.InfaticaMode.Review
                ? Runtime.InfaticaSettingData.InfaticaMode.Review
                : Runtime.InfaticaSettingData.InfaticaMode.Production;

            return new Runtime.InfaticaSettingData
            {
                Enabled = editorSettings.Enabled,
                Mode = mode,
                BatteryOptimizationIgnoreAsking = editorSettings.BatteryOptimizationIgnoreAsking
            };
        }

        private static Runtime.InAppPurchaseSettingData ConvertInAppPurchaseSettings(InAppPurchaseSettingData editorSettings)
        {
            var runtimeSettings = new Runtime.InAppPurchaseSettingData
            {
                Enabled = editorSettings.Enabled,
                UseAmazonAppStore = editorSettings.AppStoreTarget == AppStoreTarget.AmazonAppStore,
                UseFakeStoreInEditor = editorSettings.UseFakeStoreInEditor
            };

            // Convert subscription products
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

            // Convert consumable products
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
            };
        }
    }
}