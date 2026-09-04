using System;
using System.Collections.Generic;
using System.Linq;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// "Отпечаток" модуля в Android-манифесте: нативные компоненты и permissions,
    /// которые модуль привносит — напрямую (свой AAR/манифест) или через
    /// глобальный Assets/Plugins/Android/AndroidManifest.xml.
    ///
    /// Используется автоочисткой (<see cref="DisabledModuleManifestCleaner"/>),
    /// чтобы вырезать эти записи из сборки, когда модуль выключен — даже если они
    /// "застряли" в глобальном файле, который не убирается переименованием папки.
    /// </summary>
    public class ModuleManifestFootprint
    {
        /// <summary>Имя модуля — должно совпадать с ключами NativeDependencyValidator.ModuleRoots.</summary>
        public string ModuleName;

        /// <summary>
        /// Префиксы android:name. Любой service/receiver/provider/activity,
        /// чьё имя начинается с одного из префиксов, считается принадлежащим модулю.
        /// Главный инструмент: пакетный префикс ловит ВСЕ компоненты модуля,
        /// в т.ч. те, что объявлены внутри AAR.
        /// </summary>
        public string[] ComponentNamePrefixes = Array.Empty<string>();

        /// <summary>Точные android:name компонентов (в дополнение к префиксам).</summary>
        public string[] ComponentNames = Array.Empty<string>();

        /// <summary>
        /// uses-permission, которые добавляются специально ради этого модуля.
        /// Вырезаются только если их не запрашивает ни один включённый модуль и
        /// они не входят в <see cref="ModuleManifestRegistry.SharedPermissions"/>.
        /// </summary>
        public string[] Permissions = Array.Empty<string>();
    }

    /// <summary>
    /// Централизованный реестр манифест-отпечатков модулей SDK.
    /// Добавляй сюда новый модуль — и автоочистка начнёт его поддерживать "везде".
    /// </summary>
    public static class ModuleManifestRegistry
    {
        /// <summary>
        /// Permissions общей инфраструктуры — НИКОГДА не удаляются автоматически,
        /// даже если их перечислил выключенный модуль (их могут использовать
        /// рекламные SDK, аналитика и т.п.).
        /// </summary>
        public static readonly HashSet<string> SharedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.WAKE_LOCK",
            "android.permission.FOREGROUND_SERVICE",
            "android.permission.POST_NOTIFICATIONS",
            "com.google.android.gms.permission.AD_ID",
            "com.google.android.finsky.permission.BIND_GET_INSTALL_REFERRER_SERVICE",
        };

        /// <summary>
        /// Отпечатки всех модулей, у которых есть нативный след в манифесте.
        /// Модули без нативных компонентов (чистый C#) сюда добавлять не нужно.
        /// </summary>
        public static readonly List<ModuleManifestFootprint> Footprints = new List<ModuleManifestFootprint>
        {
            // Добавляй новые модули по образцу: ModuleName + ComponentNamePrefixes/Permissions.
            new ModuleManifestFootprint
            {
                // ResponseReceiver'ы Amazon Appstore SDK лежат в глобальном
                // Assets/Plugins/Android/AndroidManifest.xml (переименование папки модуля
                // в "~" их не убирает), поэтому с выключенным IAP их режет автоочистка.
                ModuleName = "InAppPurchase",
                ComponentNamePrefixes = new[]
                {
                    "com.amazon.device.iap.",
                    "com.amazon.device.drm.",
                },
            },
            new ModuleManifestFootprint
            {
                ModuleName = "AppLovin",
                ComponentNamePrefixes = new[]
                {
                    "com.applovin.",
                },
            },
            new ModuleManifestFootprint
            {
                ModuleName = "Adjust",
                ComponentNamePrefixes = new[] { "com.adjust.sdk." },
            },
            new ModuleManifestFootprint
            {
                ModuleName = "Firebase",
                ComponentNamePrefixes = new[] { "com.google.firebase." },
            },
            new ModuleManifestFootprint
            {
                ModuleName = "Cross-Promo",
                ComponentNamePrefixes = new[]
                {
                    "com.amzngod.exoplayer.",
                    "com.onevcat.uniwebview.",
                },
            },
        };

        /// <summary>
        /// Карта "имя модуля → включён ли" на основе текущих настроек SDK.
        /// Зеркалит NativeDependencyValidator.ModuleRoots.
        /// </summary>
        public static Dictionary<string, bool> GetModuleEnabledMap(SdkSettingsData settings)
        {
            bool sdkEnabled = settings != null && settings.Enabled;

            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "Cross-Promo",        sdkEnabled && settings.CrossPromo != null && settings.CrossPromo.Enabled },
                { "Adjust",             sdkEnabled && settings.Adjust != null && settings.Adjust.Enabled },
                { "AppMetrica",         sdkEnabled && settings.AppMetrica != null && settings.AppMetrica.Enabled },
                { "Firebase",           sdkEnabled && settings.Firebase != null && settings.Firebase.Enabled },
                { "InAppPurchase",      sdkEnabled && settings.InAppPurchase != null && settings.InAppPurchase.Enabled },
                { "InternetConnection", sdkEnabled && settings.InternetConnection != null && settings.InternetConnection.Enabled },
                { "InGameDebugConsole", sdkEnabled && settings.DebugConsole != null && settings.DebugConsole.Enabled },
                { "Analytics",          sdkEnabled && settings.Analytics != null && settings.Analytics.Enabled },
                { "AppLovin",           sdkEnabled && settings.AppLovin != null && settings.AppLovin.Enabled },
            };
        }

        /// <summary>Отпечатки модулей, которые ВЫКЛЮЧЕНЫ в настройках.</summary>
        public static List<ModuleManifestFootprint> GetDisabledFootprints(SdkSettingsData settings)
        {
            var enabledMap = GetModuleEnabledMap(settings);

            return Footprints
                .Where(f => !enabledMap.TryGetValue(f.ModuleName, out bool enabled) || !enabled)
                .ToList();
        }

        /// <summary>
        /// Permissions, которые легитимно запрашивает хотя бы один ВКЛЮЧЁННЫЙ модуль —
        /// их нельзя вырезать, даже если их же перечислил выключенный модуль.
        /// </summary>
        public static HashSet<string> GetEnabledPermissions(SdkSettingsData settings)
        {
            var enabledMap = GetModuleEnabledMap(settings);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in Footprints)
            {
                if (enabledMap.TryGetValue(f.ModuleName, out bool enabled) && enabled)
                {
                    foreach (var p in f.Permissions)
                        result.Add(p);
                }
            }

            return result;
        }

        /// <summary>
        /// Проверяет, принадлежит ли компонент с данным android:name отпечатку модуля
        /// (по точному имени или по пакетному префиксу).
        /// </summary>
        public static bool MatchesComponent(ModuleManifestFootprint footprint, string androidName)
        {
            if (string.IsNullOrEmpty(androidName))
                return false;

            if (footprint.ComponentNames.Any(n => string.Equals(n, androidName, StringComparison.Ordinal)))
                return true;

            return footprint.ComponentNamePrefixes.Any(prefix =>
                androidName.StartsWith(prefix, StringComparison.Ordinal));
        }
    }
}
