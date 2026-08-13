using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
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

        // _currentSettings не сериализуется, поэтому после domain reload (рекомпиляция,
        // вход в Play mode) окно осталось бы с null и падало в OnGUI.
        private void OnEnable()
        {
            _currentSettings ??= SdkSettingsManager.LoadSettings();
        }

        /// <summary>
        /// Перечитывает конфиг во всех открытых окнах настроек. Нужен, когда конфиг правится
        /// в обход окна — например при сбросе App Type на старте редактора
        /// (<see cref="AnalyticsAppTypeSessionReset"/>): иначе окно показывало бы старый тип
        /// и по Save Settings вернуло бы его обратно в конфиг.
        /// </summary>
        public static void ReloadOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<SDKSettingsWindow>())
            {
                window._currentSettings = SdkSettingsManager.LoadSettings();
                window.Repaint();
            }
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
            DrawAnalyticsSettings();
            DrawInAppPurchaseSettings();
            DrawDebugConsoleSettings();

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
            {
                if (!SdkSettingsManager.ValidateInAppPurchaseProductIds(_currentSettings.InAppPurchase, out var message))
                {
                    EditorUtility.DisplayDialog("Ошибка", message, "OK");
                }
                else
                {
                    if (SdkSettingsManager.SaveSettings(_currentSettings))
                    {
                        EditorUtility.DisplayDialog("Success", 
                            "Settings saved successfully!\n\n" +
                            "Define symbols have been updated for conditional compilation.\n" +
                            "Unity will recompile scripts automatically.", 
                            "OK");
                    }
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

                    if (_currentSettings.InternetConnection.ShowBanner)
                    {
                        EditorGUI.indentLevel++;
                        _currentSettings.InternetConnection.BannerMessage = EditorGUILayout
                            .TextField("Banner Message", _currentSettings.InternetConnection.BannerMessage);
                        _currentSettings.InternetConnection.ShowRetryButton = EditorGUILayout
                            .Toggle("Show retry button", _currentSettings.InternetConnection.ShowRetryButton);

                        if (_currentSettings.InternetConnection.ShowRetryButton)
                        {
                            _currentSettings.InternetConnection.RetryButtonLabel = EditorGUILayout
                                .TextField("Retry Button Label", _currentSettings.InternetConnection.RetryButtonLabel);
                        }

                        _currentSettings.InternetConnection.BannerSortingOrder = EditorGUILayout
                            .IntField("Banner Sorting Order", _currentSettings.InternetConnection.BannerSortingOrder);
                        EditorGUI.indentLevel--;
                    }
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
                    _currentSettings.Infatica.NotificationTitle = EditorGUILayout
                        .TextField(
                            new GUIContent("Notification Title", "Заголовок уведомления сервиса. Если пусто — \"Welcome to <Product Name>\"."),
                            _currentSettings.Infatica.NotificationTitle);
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
                "Видео кросс-промо с гибкой конфигурацией. Трекинг impression/click делегируется в Analytics-модуль.",
                _currentSettings.CrossPromo.Enabled,
                () =>
                {
                    _currentSettings.CrossPromo.ConfigUrl = EditorGUILayout
                        .TextField("Config URL", _currentSettings.CrossPromo.ConfigUrl);

                    // Video-бэкенд всегда ExoPlayer — выбор из настроек убран (UnityVideoPlayer
                    // больше не предлагается). Значение форсится при сохранении настроек.
                    _currentSettings.CrossPromo.VideoBackend = Runtime.VideoPlayerBackend.ExoPlayer;

                    GUILayout.Space(10);
                    EditorGUILayout.LabelField("Video Player", EditorStyles.miniBoldLabel);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.LabelField(
                            new GUIContent("Video Backend",
                                "Всегда ExoPlayer (Android-only, нативный оверлей на Google ExoPlayer 2.19.1). Выбор бэкенда убран из настроек."),
                            new GUIContent("ExoPlayer (Android)"));
                    }
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
                "Amazon IAP: подписки, расходуемые и разовые покупки. Права определяет чек Amazon; " +
                "награды начисляет игра по событиям SDK (см. README модуля).",
                _currentSettings.InAppPurchase.Enabled,
                () =>
                {
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

                            // Без Math.Max: 0 — это осознанный признак «не задано», по нему
                            // не сохранится конфиг и не соберётся билд (IAP-15).
                            product.TermDays = EditorGUILayout.IntField("Term (days)", product.TermDays);

                            if (product.TermDays <= 0)
                            {
                                EditorGUILayout.HelpBox(
                                    "Term (days) не задан. Укажи реальный период подписки из консоли Amazon " +
                                    "(например, 7 для недельной) — от него считаются оплаченные периоды. " +
                                    "Без него настройки не сохранятся, а билд не соберётся.",
                                    MessageType.Error);
                            }

                            product.TestTermMinutes = EditorGUILayout.IntField("Term minutes (TEST)", product.TestTermMinutes);

                            if (product.TestTermMinutes > 0)
                            {
                                EditorGUILayout.HelpBox(
                                    $"ТЕСТОВЫЙ режим: период подписки — {product.TestTermMinutes} мин вместо " +
                                    $"{product.TermDays} дн. Продления считаются локально, поэтому короткий срок " +
                                    "позволяет проверить их реальным временем за минуты. Перед продакшн-релизом " +
                                    "верни 0 — иначе игроки будут получать периоды каждые несколько минут.",
                                    MessageType.Warning);
                            }
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
                        // Свежая подписка с TermDays = 0 сразу «не задана» — это задумано.
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
                    GUILayout.Label("Non-Consumable Products (разовые покупки):", EditorStyles.miniBoldLabel);

                    for (int i = 0; i < _currentSettings.InAppPurchase.NonConsumableProducts.Count; i++)
                    {
                        var product = _currentSettings.InAppPurchase.NonConsumableProducts[i];
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        product.Enabled = EditorGUILayout.Toggle("Enabled", product.Enabled);

                        if (product.Enabled)
                        {
                            product.ProductId = EditorGUILayout.TextField("Product ID", product.ProductId);
                            product.DisplayName = EditorGUILayout.TextField("Display Name", product.DisplayName);
                        }

                        if (GUILayout.Button("Remove Non-Consumable", GUILayout.Height(20)))
                        {
                            _currentSettings.InAppPurchase.NonConsumableProducts.RemoveAt(i);
                            i--;
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(5);
                            continue;
                        }

                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                    }

                    if (GUILayout.Button("Add Non-Consumable Product"))
                    {
                        _currentSettings.InAppPurchase.NonConsumableProducts.Add(new NonConsumableProduct());
                    }
                });
        }

        private void DrawDebugConsoleSettings()
        {
            _currentSettings.DebugConsole.Enabled = DrawModuleSection(
                "In-Game Debug Console",
                "Встроенная консоль для отладки: просмотр логов, ошибок и выполнение команд прямо в игре.",
                _currentSettings.DebugConsole.Enabled,
                null);
        }

        private void DrawAnalyticsSettings()
        {
            _currentSettings.Analytics.Enabled = DrawModuleSection(
                "Analytics",
                "Кастомный HTTP-трекер /v1/events: paid/free_first_open, cp_impression, cp_click. Единый владелец device_id_hash и очереди ретраев.",
                _currentSettings.Analytics.Enabled,
                () =>
                {
                    // Backend и ключ зашиты в SDK — правятся только в AnalyticsSettingData.
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField("Backend URL (fixed)", _currentSettings.Analytics.BaseUrl);
                    EditorGUILayout.TextField("API Key (fixed)", _currentSettings.Analytics.ApiKey);
                    EditorGUILayout.TextField("App ID (auto)", UnityEditor.PlayerSettings.applicationIdentifier);
                    EditorGUI.EndDisabledGroup();
                    _currentSettings.Analytics.AppType = (AnalyticsAppType)EditorGUILayout
                        .EnumPopup("App Type", _currentSettings.Analytics.AppType);

                    // Тип сбрасывается при каждом запуске редактора — подсказываем, почему
                    // поле снова пустое и чем это грозит, пока его не выставили.
                    if (_currentSettings.Analytics.AppType == AnalyticsAppType.None)
                    {
                        EditorGUILayout.HelpBox(
                            "App Type не выбран. Значение сбрасывается при каждом запуске Unity Editor — " +
                            "выбери Free или Paid и нажми Save Settings, иначе билд не соберётся.",
                            MessageType.Error);
                    }
                });
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