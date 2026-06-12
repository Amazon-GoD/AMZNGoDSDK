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
        /// <summary>Имя модуля — должно совпадать с ключами ModuleFolderManager.</summary>
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
            new ModuleManifestFootprint
            {
                ModuleName = "Infatica",
                // Пакетный префикс ловит и сервис, и boot-receiver, и любые
                // компоненты, которые объявит сам AAR (residential proxy агент).
                ComponentNamePrefixes = new[] { "com.infatica." },
                // RECEIVE_BOOT_COMPLETED добавлен в глобальный манифест именно ради
                // Infatica boot-receiver. FOREGROUND_SERVICE_* живут в скрытой папке
                // WithJobs~ и в выключенную сборку не попадают, но перечислены для
                // полноты (защищены проверкой "не нужен включённому модулю").
                Permissions = new[]
                {
                    "android.permission.RECEIVE_BOOT_COMPLETED",
                    "android.permission.FOREGROUND_SERVICE_DATA_SYNC",
                },
            },
            // Добавляй новые модули по образцу выше.
        };

        /// <summary>
        /// Карта "имя модуля → включён ли" на основе текущих настроек SDK.
        /// Зеркалит NativeDependencyValidator / ModuleFolderManager.
        /// </summary>
        public static Dictionary<string, bool> GetModuleEnabledMap(SdkSettingsData settings)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "Cross-Promo",        settings.CrossPromo.Enabled },
                { "Adjust",             settings.Adjust.Enabled },
                { "AppMetrica",         settings.AppMetrica.Enabled },
                { "Firebase",           settings.Firebase.Enabled },
                { "Infatica",           settings.Infatica.Enabled },
                { "InAppPurchase",      settings.InAppPurchase.Enabled },
                { "InternetConnection", settings.InternetConnection.Enabled },
                { "InGameDebugConsole", settings.DebugConsole.Enabled },
                { "Analytics",          settings.Analytics.Enabled },
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
