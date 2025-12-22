using System;
using System.IO;
using UnityEditor;
using UnityEngine;

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
                InAppPurchase = ConvertInAppPurchaseSettingsToEditor(runtimeSettings.InAppPurchase)
            };

            return editorSettings;
        }

        private static AdjustSettingData ConvertAdjustSettingsToEditor(Runtime.AdjustSettingData runtimeSettings)
        {
            var adjustEnvironment = runtimeSettings.Environment == AdjustSdk.AdjustEnvironment.Production
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
                MaxSdkKey = runtimeSettings.MaxSdkKey,
                AppodealSdkKey = runtimeSettings.AppodealSdkKey,
                InterstitialId = runtimeSettings.InterstitialId,
                RewardedId = runtimeSettings.RewardedId
            };
        }

        private static InfaticaSettingData ConvertInfaticaSettingsToEditor(Runtime.InfaticaSettingData runtimeSettings)
        {
            var mode = runtimeSettings.Mode == Runtime.InfaticaModule.Mode.Review
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
                    Enabled = runtimeProduct.Enabled
                });
            }

            return editorSettings;
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
                InAppPurchase = ConvertInAppPurchaseSettings(editorSettings.InAppPurchase)
            };

            return runtimeSettings;
        }

        private static Runtime.AdjustSettingData ConvertAdjustSettings(AdjustSettingData editorSettings)
        {
            var adjustEnvironment = editorSettings.Environment == AdjustSettingData.AdjustEnvironment.Production
                ? AdjustSdk.AdjustEnvironment.Production
                : AdjustSdk.AdjustEnvironment.Sandbox;

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
                MaxSdkKey = editorSettings.MaxSdkKey,
                AppodealSdkKey = editorSettings.AppodealSdkKey,
                InterstitialId = editorSettings.InterstitialId,
                RewardedId = editorSettings.RewardedId
            };
        }

        private static Runtime.InfaticaSettingData ConvertInfaticaSettings(InfaticaSettingData editorSettings)
        {
            var mode = editorSettings.Mode == InfaticaSettingData.InfaticaMode.Review
                ? Runtime.InfaticaModule.Mode.Review
                : Runtime.InfaticaModule.Mode.Production;

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
                    Enabled = editorProduct.Enabled
                });
            }

            return runtimeSettings;
        }
    }
}