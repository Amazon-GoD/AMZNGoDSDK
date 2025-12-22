using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    public sealed class SDKSettingsWindow : EditorWindow
    {
        private static Dictionary<string, bool> _dependenciesInfo = new();
        
        private Vector2 _scrollPosition;
        private SdkSettingsData _currentSettings;

        [MenuItem("AMZN GoD/Settings", false, 0)]
        public static async void ShowWindow()
        {
            _dependenciesInfo = 
                await SdkDependencyManager.GetSdkDependenciesInstallInfoAsync();
            
            var window = GetWindow<SDKSettingsWindow>("AMZN GoD SDK Settings");
            window.minSize = new Vector2(400, 600);
            window._currentSettings = SdkSettingsManager.LoadSettings();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            
            _currentSettings.Enabled = EditorGUILayout.Toggle("SDK Enabled:", _currentSettings.Enabled);
            
            GUILayout.Space(10);
            
            GUILayout.Label("SDK Module Settings:", EditorStyles.boldLabel);
            
            // Infatica Settings
            DrawInfaticaSettings();
            
            // Cross-promo Settings
            DrawCrossPromoSettings();
            
            // AppMetrica Settings
            DrawAppMetricaSettings();
            
            // Adjust Settings
            DrawAdjustSettings();

            // In-App Purchase Settings
            DrawInAppPurchaseSettings();

            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
            {
                SdkSettingsManager.SaveSettings(_currentSettings);
                EditorUtility.DisplayDialog("Success", "Settings saved successfully!", "OK");
            }
            
            GUILayout.Space(10);
            
            // Dependencies section
            GUILayout.Label("Required External Dependencies:", EditorStyles.boldLabel);
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
            
            foreach (var dependency in _dependenciesInfo)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                bool isInstalled = _dependenciesInfo[dependency.Key];
                EditorGUILayout.LabelField($"{dependency.Key}: {(isInstalled ? "installed" : "missing")}", EditorStyles.boldLabel);
                
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
            
            GUILayout.Space(20);
            
            EditorGUILayout.HelpBox(
                $"Total dependencies configured: {_dependenciesInfo.Count}\n\n" +
                "Dependencies will be automatically checked when Unity starts.\nIf any dependencies are missing, SDK will be install it again.", 
                MessageType.Info);

            if (_dependenciesInfo.Any(x => x.Value == false))
            {
                if (GUILayout.Button("Install Miss Dependencies", GUILayout.Height(15)))
                {
                    SdkDependencyManager.InstallMissingDependencies();
                }
            }
        }

        private void DrawInfaticaSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Infatica", EditorStyles.boldLabel);
            
            _currentSettings.Infatica.Enabled = EditorGUILayout.Toggle("Enabled", _currentSettings.Infatica.Enabled);
            
            if (_currentSettings.Infatica.Enabled)
            {
                _currentSettings.Infatica.Mode = (InfaticaSettingData.InfaticaMode)EditorGUILayout
                    .EnumPopup("Mode", _currentSettings.Infatica.Mode);
                _currentSettings.Infatica.BatteryOptimizationIgnoreAsking = EditorGUILayout
                    .Toggle("Battery Optimization Ignore Asking", _currentSettings.Infatica.BatteryOptimizationIgnoreAsking);
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
        
        private void DrawCrossPromoSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Cross Promo", EditorStyles.boldLabel);
            
            _currentSettings.CrossPromo.Enabled = EditorGUILayout.Toggle("Enabled", _currentSettings.CrossPromo.Enabled);
            
            if (_currentSettings.CrossPromo.Enabled)
            {
                _currentSettings.CrossPromo.ConfigUrl = EditorGUILayout
                    .TextField("Config URL", _currentSettings.CrossPromo.ConfigUrl);
                _currentSettings.CrossPromo.MaxSdkKey = EditorGUILayout
                    .TextField("Max SDK Key", _currentSettings.CrossPromo.MaxSdkKey);
                _currentSettings.CrossPromo.InterstitialId = EditorGUILayout
                    .TextField("Interstitial Id", _currentSettings.CrossPromo.InterstitialId);
                _currentSettings.CrossPromo.RewardedId = EditorGUILayout
                    .TextField("Rewarded Id", _currentSettings.CrossPromo.RewardedId);
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
        
        private void DrawAppMetricaSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("AppMetrica", EditorStyles.boldLabel);
            
            _currentSettings.AppMetrica.Enabled = EditorGUILayout.Toggle("Enabled", _currentSettings.AppMetrica.Enabled);
            
            if (_currentSettings.AppMetrica.Enabled)
            {
                _currentSettings.AppMetrica.Key = EditorGUILayout
                    .TextField("Key", _currentSettings.AppMetrica.Key);
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
        
        private void DrawAdjustSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Adjust", EditorStyles.boldLabel);
            
            _currentSettings.Adjust.Enabled = EditorGUILayout.Toggle("Enabled", _currentSettings.Adjust.Enabled);
            
            if (_currentSettings.Adjust.Enabled)
            {
                _currentSettings.Adjust.Key = EditorGUILayout
                    .TextField("Key", _currentSettings.Adjust.Key);
                _currentSettings.Adjust.Environment = (AdjustSettingData.AdjustEnvironment)EditorGUILayout
                    .EnumPopup("Environment", _currentSettings.Adjust.Environment);
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawInAppPurchaseSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label("In-App Purchase", EditorStyles.boldLabel);

            _currentSettings.InAppPurchase.Enabled = EditorGUILayout.Toggle("Enabled", _currentSettings.InAppPurchase.Enabled);

            if (_currentSettings.InAppPurchase.Enabled)
            {
                _currentSettings.InAppPurchase.AppStoreTarget = (AppStoreTarget)EditorGUILayout
                    .EnumPopup("App Store Target", _currentSettings.InAppPurchase.AppStoreTarget);
                _currentSettings.InAppPurchase.UseFakeStoreInEditor = EditorGUILayout
                    .Toggle("Use Fake Store in Editor", _currentSettings.InAppPurchase.UseFakeStoreInEditor);

                GUILayout.Space(10);

                // Subscription Products
                GUILayout.Label("Subscription Products:", EditorStyles.miniBoldLabel);

                for (int i = 0; i < _currentSettings.InAppPurchase.SubscriptionProducts.Count; i++)
                {
                    var product = _currentSettings.InAppPurchase.SubscriptionProducts[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    product.Enabled = EditorGUILayout.Toggle("Enabled", product.Enabled);

                    if (product.Enabled)
                    {
                        product.ProductId = EditorGUILayout.TextField("Product ID", product.ProductId);
                        product.DisplayName = EditorGUILayout.TextField("Display Name", product.DisplayName);
                        product.RewardAmount = EditorGUILayout.IntField("Reward Amount", product.RewardAmount);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                if (GUILayout.Button("Add Subscription Product"))
                {
                    _currentSettings.InAppPurchase.SubscriptionProducts.Add(new SubscriptionProduct());
                }

                // Consumable Products
                GUILayout.Space(10);
                GUILayout.Label("Consumable Products:", EditorStyles.miniBoldLabel);

                for (int i = 0; i < _currentSettings.InAppPurchase.ConsumableProducts.Count; i++)
                {
                    var product = _currentSettings.InAppPurchase.ConsumableProducts[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    product.Enabled = EditorGUILayout.Toggle("Enabled", product.Enabled);

                    if (product.Enabled)
                    {
                        product.ProductId = EditorGUILayout.TextField("Product ID", product.ProductId);
                        product.DisplayName = EditorGUILayout.TextField("Display Name", product.DisplayName);
                        product.RewardAmount = EditorGUILayout.IntField("Reward Amount", product.RewardAmount);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                if (GUILayout.Button("Add Consumable Product"))
                {
                    _currentSettings.InAppPurchase.ConsumableProducts.Add(new ConsumableProduct());
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
    }
}