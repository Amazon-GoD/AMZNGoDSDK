using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Purchasing;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    public sealed class SDKSettingsWindow : EditorWindow
    {
        private static Dictionary<string, bool> _dependenciesInfo = new();
        
        private static GUIStyle _moduleDescriptionStyle;

        private Vector2 _settingsScrollPosition;
        private Vector2 _dependenciesScrollPosition;
        private SdkSettingsData _currentSettings;

        [MenuItem("AMZN GoD/SDK Settings", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<SDKSettingsWindow>("AMZN GoD SDK Settings");
            window.minSize = new Vector2(400, 600);
            window._currentSettings = SdkSettingsManager.LoadSettings();
            
            // Load dependencies info asynchronously
            LoadDependenciesAsync();
        }
        
        //[MenuItem("AMZN GoD/Settings/Open SDK Settings", false, 0)]
        public static void ShowWindowAlt()
        {
            ShowWindow();
        }
        
        private static async void LoadDependenciesAsync()
        {
            _dependenciesInfo = 
                await SdkDependencyManager.GetSdkDependenciesInstallInfoAsync();
        }

        private void OnGUI()
        {
            _settingsScrollPosition = EditorGUILayout.BeginScrollView(_settingsScrollPosition);
            GUILayout.Space(10);

            _currentSettings.Enabled = EditorGUILayout.Toggle("SDK Enabled:", _currentSettings.Enabled);

            GUILayout.Space(5);
            
            // Conditional Compilation Info
            EditorGUILayout.HelpBox(
                "💡 Conditional Compilation: Disabled modules are excluded from builds using define symbols. " +
                "This reduces build size and compilation time. Click 'Module Status' to see active modules.",
                MessageType.Info);

            GUILayout.Space(10);

            GUILayout.Label("SDK Module Settings:", EditorStyles.boldLabel);

            DrawInternetConnectionSettings();
            DrawInfaticaSettings();
            DrawCrossPromoSettings();
            DrawAppMetricaSettings();
            DrawFirebaseSettings();
            DrawAdjustSettings();
            DrawInAppPurchaseSettings();

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
            {
                if (!SdkSettingsManager.ValidateInAppPurchaseProductIds(_currentSettings.InAppPurchase, out var message))
                {
                    EditorUtility.DisplayDialog("Ошибка", message, "OK");
                }
                else
                {
                    SdkSettingsManager.SaveSettings(_currentSettings);
                    EditorUtility.DisplayDialog("Success", 
                        "Settings saved successfully!\n\n" +
                        "Define symbols have been updated for conditional compilation.\n" +
                        "Unity will recompile scripts automatically.", 
                        "OK");
                }
            }
            
            if (GUILayout.Button("Module Status", GUILayout.Height(30), GUILayout.Width(120)))
            {
                ModuleStatusWindow.ShowWindow();
            }
            
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Dependencies section
            GUILayout.Label("Required External Dependencies:", EditorStyles.boldLabel);

            _dependenciesScrollPosition = EditorGUILayout.BeginScrollView(_dependenciesScrollPosition, GUILayout.Height(200));

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

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        private void DrawInternetConnectionSettings()
        {
            _currentSettings.InternetConnection.Enabled = DrawModuleSection(
                "Internet Connection",
                "Контроль подключения: задержка запуска SDK, пауза игры и баннер при потере сети.",
                _currentSettings.InternetConnection.Enabled,
                () =>
                {
                    _currentSettings.InternetConnection.CheckIntervalSeconds = Mathf.Max(
                        1f, EditorGUILayout.FloatField("Check Interval (sec)", _currentSettings.InternetConnection.CheckIntervalSeconds));
                    _currentSettings.InternetConnection.PauseGameWhenOffline = EditorGUILayout
                        .Toggle("Pause game when offline", _currentSettings.InternetConnection.PauseGameWhenOffline);
                    _currentSettings.InternetConnection.ShowBanner = EditorGUILayout
                        .Toggle("Show built-in banner", _currentSettings.InternetConnection.ShowBanner);
                });
        }

        private void DrawInfaticaSettings()
        {
            _currentSettings.Infatica.Enabled = DrawModuleSection(
                "Infatica",
                "Контроль согласия пользователя, фоновые сервисы и работа с батарейной оптимизацией.",
                _currentSettings.Infatica.Enabled,
                () =>
                {
                    _currentSettings.Infatica.PartnerId = EditorGUILayout
                        .TextField("Partner ID", _currentSettings.Infatica.PartnerId);
                    _currentSettings.Infatica.Mode = (InfaticaSettingData.InfaticaMode)EditorGUILayout
                        .EnumPopup("Mode", _currentSettings.Infatica.Mode);
                    _currentSettings.Infatica.BatteryOptimizationIgnoreAsking = EditorGUILayout
                        .Toggle("Battery Optimization Ignore Asking", _currentSettings.Infatica.BatteryOptimizationIgnoreAsking);
                });
        }
        
        private void DrawCrossPromoSettings()
        {
            _currentSettings.CrossPromo.Enabled = DrawModuleSection(
                "Cross Promo",
                "Показ рекламы Appodeal с гибкой конфигурацией и динамическими баннерами.",
                _currentSettings.CrossPromo.Enabled,
                () =>
                {
                    _currentSettings.CrossPromo.ConfigUrl = EditorGUILayout
                        .TextField("Config URL", _currentSettings.CrossPromo.ConfigUrl);
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("Appodeal SDK", EditorStyles.miniBoldLabel);
                    _currentSettings.CrossPromo.AppodealSdkKey = EditorGUILayout
                        .TextField("Appodeal SDK Key", _currentSettings.CrossPromo.AppodealSdkKey);
                });
        }
        
        private void DrawAppMetricaSettings()
        {
            _currentSettings.AppMetrica.Enabled = DrawModuleSection(
                "AppMetrica",
                "Сбор аналитики и событиеохранилище через AppMetrica Analytics.",
                _currentSettings.AppMetrica.Enabled,
                () =>
                {
                    _currentSettings.AppMetrica.Key = EditorGUILayout
                        .TextField("Key", _currentSettings.AppMetrica.Key);
                });
        }
        
        private void DrawFirebaseSettings()
        {
            _currentSettings.Firebase.Enabled = DrawModuleSection(
                "Firebase",
                "Firebase Analytics и Crashlytics: события и отчёты об ошибках.",
                _currentSettings.Firebase.Enabled,
                () =>
                {
                    _currentSettings.Firebase.EnableAnalytics = EditorGUILayout
                        .Toggle("Enable Analytics", _currentSettings.Firebase.EnableAnalytics);
                    _currentSettings.Firebase.EnableCrashlytics = EditorGUILayout
                        .Toggle("Enable Crashlytics", _currentSettings.Firebase.EnableCrashlytics);
                });
        }
        
        private void DrawAdjustSettings()
        {
            _currentSettings.Adjust.Enabled = DrawModuleSection(
                "Adjust",
                "Отправка событий с параметрами в Adjust и выбор окружения.",
                _currentSettings.Adjust.Enabled,
                () =>
                {
                    _currentSettings.Adjust.Key = EditorGUILayout
                        .TextField("Key", _currentSettings.Adjust.Key);
                    _currentSettings.Adjust.Environment = (AdjustSettingData.AdjustEnvironment)EditorGUILayout
                        .EnumPopup("Environment", _currentSettings.Adjust.Environment);
                });
        }

        private void DrawInAppPurchaseSettings()
        {
            _currentSettings.InAppPurchase.Enabled = DrawModuleSection(
                "In-App Purchase",
                "Управление Unity IAP, подписками и consumable товарами с автоматическими наградами.",
                _currentSettings.InAppPurchase.Enabled,
                () =>
                {
                    _currentSettings.InAppPurchase.AppStoreTarget = (AppStoreTarget)EditorGUILayout
                        .EnumPopup("App Store Target", _currentSettings.InAppPurchase.AppStoreTarget);
                    _currentSettings.InAppPurchase.UseFakeStoreInEditor = EditorGUILayout
                        .Toggle("Use Fake Store in Editor", _currentSettings.InAppPurchase.UseFakeStoreInEditor);

                    GUILayout.Space(10);

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
                        }

                    product.DurationDays = EditorGUILayout.IntField("Duration (days)", Math.Max(1, product.DurationDays));

                    GUILayout.Space(5);
                    GUILayout.Label("Subscription Consumables:", EditorStyles.miniBoldLabel);

                    for (int rewardIndex = 0; rewardIndex < product.ConsumableRewards.Count; rewardIndex++)
                    {
                        var reward = product.ConsumableRewards[rewardIndex];
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        var consumableOptions = _currentSettings.InAppPurchase.ConsumableProducts
                            .Where(c => !string.IsNullOrWhiteSpace(c.ProductId))
                            .Select(c => c.ProductId)
                            .ToArray();

                        if (consumableOptions.Length > 0)
                        {
                            int currentIndex = Mathf.Max(0, Array.IndexOf(consumableOptions, reward.ProductId));
                            currentIndex = Mathf.Clamp(EditorGUILayout.Popup("Consumable ID", currentIndex, consumableOptions), 0, consumableOptions.Length - 1);
                            reward.ProductId = consumableOptions[currentIndex];

                            var mappedConsumable = _currentSettings.InAppPurchase.ConsumableProducts
                                .FirstOrDefault(c => c.ProductId == reward.ProductId);

                            if (mappedConsumable != null)
                            {
                                EditorGUILayout.LabelField("Reward Amount", mappedConsumable.RewardAmount.ToString());
                                EditorGUILayout.LabelField("Reward Key", string.IsNullOrEmpty(mappedConsumable.RewardKey) ? mappedConsumable.ProductId : mappedConsumable.RewardKey);
                                EditorGUILayout.LabelField("Reward Type", mappedConsumable.RewardType.ToString());
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Добавьте consumable товар выше, чтобы выбрать его здесь.");
                            reward.ProductId = string.Empty;
                        }

                        if (GUILayout.Button("Remove Consumable Reward", GUILayout.Height(20)))
                        {
                            product.ConsumableRewards.RemoveAt(rewardIndex);
                            rewardIndex--;
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(5);
                            continue;
                        }

                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                    }

                    if (GUILayout.Button("Add Consumable Reward"))
                    {
                        product.ConsumableRewards.Add(new SubscriptionConsumableReward());
                    }

                        if (GUILayout.Button("Remove Subscription", GUILayout.Height(20)))
                        {
                            _currentSettings.InAppPurchase.SubscriptionProducts.RemoveAt(i);
                            i--;
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(5);
                            continue;
                        }

                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                    }

                    if (GUILayout.Button("Add Subscription Product"))
                    {
                        _currentSettings.InAppPurchase.SubscriptionProducts.Add(new SubscriptionProduct());
                    }

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
                            product.RewardKey = EditorGUILayout.TextField("Reward Key", string.IsNullOrEmpty(product.RewardKey) ? product.ProductId : product.RewardKey);
                            product.RewardType = (ConsumableRewardType)EditorGUILayout.EnumPopup("Reward Type", product.RewardType);
                        }

                        if (GUILayout.Button("Remove Consumable", GUILayout.Height(20)))
                        {
                            _currentSettings.InAppPurchase.ConsumableProducts.RemoveAt(i);
                            i--;
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(5);
                            continue;
                        }

                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                    }

                    if (GUILayout.Button("Add Consumable Product"))
                    {
                        _currentSettings.InAppPurchase.ConsumableProducts.Add(new ConsumableProduct());
                    }

                    GUILayout.Space(10);
                    GUILayout.Label("Catalog Imports:", EditorStyles.miniBoldLabel);

                    if (GUILayout.Button("Refresh Catalog"))
                    {
                        InAppPurchaseCatalogHelper.RefreshCatalog(_currentSettings.InAppPurchase);
                    }

                    if (_currentSettings.InAppPurchase.CatalogImportedProducts.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No catalog products are imported yet. Refresh catalog to see available IDs.", MessageType.Info);
                    }
                    else
                    {
                        for (int i = 0; i < _currentSettings.InAppPurchase.CatalogImportedProducts.Count; i++)
                        {
                            var catalogProduct = _currentSettings.InAppPurchase.CatalogImportedProducts[i];
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField($"{catalogProduct.ProductId} ({catalogProduct.Type})");
                            if (GUILayout.Button("Remove", GUILayout.Width(70)))
                            {
                                RemoveCatalogProduct(catalogProduct.ProductId);
                                EditorGUILayout.EndHorizontal();
                                break;
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                });
        }

        private void RemoveCatalogProduct(string productId)
        {
            _currentSettings.InAppPurchase.SubscriptionProducts.RemoveAll(p => p.ProductId == productId);
            _currentSettings.InAppPurchase.ConsumableProducts.RemoveAll(p => p.ProductId == productId);
            _currentSettings.InAppPurchase.CatalogImportedProducts.RemoveAll(p => p.ProductId == productId);
        }

        private bool DrawModuleSection(string title, string description, bool enabled, Action content)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var newEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, ModuleDescriptionStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (newEnabled && content != null)
            {
                GUILayout.Space(6);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                content();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);

            return newEnabled;
        }

        private GUIStyle ModuleDescriptionStyle =>
            _moduleDescriptionStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
    }
}