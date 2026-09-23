using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Ставит AppLovin MAX и адаптеры сеток через Unity Package Manager.
    /// <para>
    /// Начиная с MAX 8.0 плагин раздаётся не только .unitypackage'ом, но и через собственный
    /// npm-совместимый scoped registry AppLovin. Это позволяет обойтись без ручного скачивания
    /// и без Integration Manager: регистрируем реестр в манифесте проекта и добавляем пакеты
    /// через Client.Add / Client.AddAndRemove.
    /// </para>
    /// <para>
    /// Для включённого в сохранённых настройках модуля SdkDependencyManager автоматически
    /// устанавливает проверенный комплект MAX и разрешённых адаптеров после EDM4U.
    /// Команды меню также позволяют отдельно установить или заменить пакеты.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class AppLovinPackageInstaller
    {
        public const string RegistryName = "AppLovin MAX";
        public const string RegistryUrl = "https://unity.packages.applovin.com";
        public const string RegistryScope = "com.applovin";

        /// <summary>Основной пакет плагина (displayName: AppLovin MAX Mediation Plugin for Unity).</summary>
        public const string MaxPluginPackageId = "com.applovin.mediation.ads";
        public const string ObsoleteAdaptersNotice = "MAX 8.6.6 больше не поддерживает HyprMX и Maio; их UPM-пакеты удаляются при включении или обновлении AppLovin.";

        // MAX 8.6.6 removes these packages itself. Never restore them from a disabled-module stash.
        private static readonly HashSet<string> ObsoleteAdapterPackageIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "com.applovin.mediation.adapters.hyprmx.android",
            "com.applovin.mediation.adapters.maio.android",
        };

        private const string ManifestPath = "Packages/manifest.json";
        private const string BackupDirectory = "Library/AmznGoDSDK";
        internal const string DisabledStatePath = "ProjectSettings/AMZNGoDSDK/AppLovinPackages.disabled.json";
        internal const string AppLovinSettingsPath = "Assets/MaxSdk/Resources/AppLovinSettings.asset";
        internal const string DisabledAppLovinSettingsPath =
            "Assets/MaxSdk/Editor/AMZNGoDSDKDisabled/AppLovinSettings.asset";

        [Serializable]
        private sealed class DisabledPackageState
        {
            public List<DisabledPackageEntry> Packages = new List<DisabledPackageEntry>();
        }

        [Serializable]
        private sealed class DisabledPackageEntry
        {
            public string Id;
            public string Version;
        }

        private const string StatusKey = "AMZNGoDSDK.AppLovinInstaller.Status";
        private const string PendingKey = "AMZNGoDSDK.AppLovinInstaller.Pending";
        private static bool _busy;
        private static bool _synchronizing;
        private static bool? _pendingModuleState;
        private static string _installedStatus;
        private static bool _hasInstalledPlugin;
        private static bool _isRequiredVersionInstalled;
        private static double _statusCheckedAt;

        public static bool IsBusy => _busy || _synchronizing || _pendingModuleState.HasValue;
        public static string Status => SessionState.GetString(StatusKey, "");
        public static bool HasInstalledPlugin { get { ReadInstalledStatus(); return _hasInstalledPlugin; } }
        public static string InstalledStatus { get { ReadInstalledStatus(); return _installedStatus; } }
        public static bool IsRequiredVersionInstalled { get { ReadInstalledStatus(); return _isRequiredVersionInstalled; } }
        public static bool IsModuleEnabledInSavedSettings
        {
            get
            {
                var settings = SdkSettingsManager.LoadRuntimeSettings();
                return settings != null && settings.Enabled && settings.AppLovin != null && settings.AppLovin.Enabled;
            }
        }

        static AppLovinPackageInstaller()
        {
            EditorApplication.update += DrainModuleSynchronization;
            string pending = SessionState.GetString(PendingKey, "");
            if (pending.Length == 0) return;
            SessionState.EraseString(PendingKey);
            SetStatus("Установка AppLovin была прервана. Проверьте состояние пакетов. Резервная копия: " + pending);
        }

        private static void ReadInstalledStatus()
        {
            if (_installedStatus != null && EditorApplication.timeSinceStartup - _statusCheckedAt < 2) return;
            _statusCheckedAt = EditorApplication.timeSinceStartup;
            _isRequiredVersionInstalled = false;
            var packages = PackageInfo.GetAllRegisteredPackages();
            var package = packages.FirstOrDefault(item => item.name == MaxPluginPackageId);
            try
            {
                string legacy = AppLovinLegacyInstallation.Description;
                var disabledPackages = LoadDisabledState().Packages;
                var stashed = disabledPackages.FirstOrDefault(entry => entry.Id == MaxPluginPackageId);
                _hasInstalledPlugin = package != null || legacy != null || stashed != null;
                string requiredVersion = PinnedVersions[MaxPluginPackageId];
                _isRequiredVersionInstalled = _hasInstalledPlugin &&
                    (package == null || package.version == requiredVersion) &&
                    (legacy == null || AppLovinLegacyInstallation.InstalledVersion == requiredVersion) &&
                    (stashed == null || stashed.Version == requiredVersion) &&
                    packages.All(item => !ObsoleteAdapterPackageIds.Contains(item.name) &&
                        (!PinnedVersions.TryGetValue(item.name, out string pin) || item.version == pin)) &&
                    disabledPackages.All(item => !ObsoleteAdapterPackageIds.Contains(item.Id) &&
                        (!PinnedVersions.TryGetValue(item.Id, out string pin) || item.Version == pin));
                _installedStatus = string.Join("; ", new[] { package == null ? null : "UPM " + package.version, legacy,
                    stashed == null ? null : "UPM " + stashed.Version + " (модуль отключён)" }.Where(value => value != null));
                if (!_hasInstalledPlugin) _installedStatus = "MAX не установлен";
                else if (!_isRequiredVersionInstalled) _installedStatus += "; доступно обновление MAX или установленных адаптеров";
            }
            catch (Exception ex)
            {
                _hasInstalledPlugin = package != null;
                _installedStatus = "Не удалось проверить MAX: " + ex.Message;
            }
        }

        private static void SetStatus(string message)
        {
            SessionState.SetString(StatusKey, message);
            foreach (var window in Resources.FindObjectsOfTypeAll<SDKSettingsWindow>()) window.Repaint();
        }

        private static bool CanStart()
        {
            if (IsBusy || FirebasePackageInstaller.IsBusy)
            {
                SetStatus("Дождитесь завершения установки SDK или применения настроек модулей.");
                return false;
            }
            if (!IsModuleEnabledInSavedSettings)
            {
                SetStatus("Включите SDK и модуль AppLovin и сохраните настройки перед установкой.");
                return false;
            }
            if (LoadDisabledState().Packages.Count > 0 || File.Exists(DisabledAppLovinSettingsPath))
            {
                SynchronizeWithModule(true);
                SetStatus("Дождитесь восстановления сохранённых пакетов и настроек AppLovin.");
                return false;
            }
            return !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Поддерживаемые MAX 8.6.6 Android-адаптеры в реестре AppLovin (23 на 2026-09-23).
        /// Устаревшие HyprMX и Maio исключены согласно AppLovinInitialize.ObsoleteNetworks.
        /// CSJ, Pangle и Tencent GDT представлены только iOS-пакетами и сюда не входят.
        /// Список проверен через /-/v1/search и оставлен статическим: набор пакетов
        /// должен быть воспроизводимым и не зависеть от того, доступна ли сеть в момент сборки.
        /// Появилась новая сетка — дописать сюда.
        /// </summary>
        private static readonly string[] RegistryNetworks =
        {
            "bidmachine", "bigoads", "bytedance", "chartboost", "facebook", "fyber",
            "google", "googleadmanager", "inmobi", "ironsource", "line",
            "mintegral", "mobilefuse", "moloco", "mytarget", "ogurypresage",
            "pubmatic", "smaato", "unityads", "verve", "vungle", "yandex",
            "ysonetwork",
        };

        // Сборка идёт под Amazon Appstore, то есть Android. iOS-адаптеры тянут CocoaPods
        // и в этом проекте только раздували бы зависимости, поэтому ставим только .android.
        private const string AdapterIdFormat = "com.applovin.mediation.adapters.{0}.android";

        /// <summary>
        /// Проверенный набор на 2026-09-23: MAX 13.6.4, minSdk 24, compileSdk 36.
        /// Все разрешённые адаптеры закреплены, чтобы новая установка не подняла
        /// требования Android незаметно. Инструменты сборки задаёт AndroidGradleToolchain.
        /// </summary>
        private static readonly Dictionary<string, string> PinnedVersions = new Dictionary<string, string>
        {
            { "com.applovin.mediation.ads", "8.6.6" },
            { "com.applovin.mediation.adapters.bigoads.android", "6010000.0.0" },
            { "com.applovin.mediation.adapters.bytedance.android", "803000401.0.0" },
            { "com.applovin.mediation.adapters.chartboost.android", "9140101.0.0" },
            { "com.applovin.mediation.adapters.facebook.android", "6220000.0.0" },
            { "com.applovin.mediation.adapters.inmobi.android", "11040103.0.0" },
            { "com.applovin.mediation.adapters.ironsource.android", "906000000.0.0" },
            { "com.applovin.mediation.adapters.line.android", "300001010.1.0" },
            { "com.applovin.mediation.adapters.mintegral.android", "17018100.0.0" },
            { "com.applovin.mediation.adapters.mobilefuse.android", "1120000.0.0" },
            { "com.applovin.mediation.adapters.moloco.android", "4120000.0.0" },
            { "com.applovin.mediation.adapters.ogurypresage.android", "6030100.0.0" },
            { "com.applovin.mediation.adapters.pubmatic.android", "5040000.0.0" },
            { "com.applovin.mediation.adapters.smaato.android", "23020200.0.0" },
            { "com.applovin.mediation.adapters.unityads.android", "4200100.0.0" },
            { "com.applovin.mediation.adapters.verve.android", "3090200.0.0" },
            { "com.applovin.mediation.adapters.vungle.android", "7070801.0.0" },
            { "com.applovin.mediation.adapters.ysonetwork.android", "1030900.0.0" },
        };

        /// <summary>
        /// Спецификация пакета для UPM: <c>id@version</c>, если версия закреплена, иначе голый id
        /// (тогда UPM ставит latest). Используется и при установке, и в UI — чтобы в диалоге
        /// было видно, какая именно версия поедет в проект.
        /// </summary>
        public static string PackageSpec(string packageId)
        {
            return PinnedVersions.TryGetValue(packageId, out string pinned)
                ? $"{packageId}@{pinned}"
                : packageId;
        }

        /// <summary>Закреплённые версии (id → version) — для отображения в окне настроек.</summary>
        public static IReadOnlyDictionary<string, string> Pins => PinnedVersions;

        /// <summary>Закреплённые пакеты из переданного списка, в виде <c>id@version</c>.</summary>
        public static List<string> PinnedSpecsIn(IEnumerable<string> packageIds)
        {
            var result = new List<string>();

            foreach (var id in packageIds)
            {
                if (PinnedVersions.ContainsKey(id))
                    result.Add(PackageSpec(id));
            }

            return result;
        }

        #region Public API

        /// <summary>
        /// Делает состояние внешнего MAX симметричным тогглу модуля. При выключении
        /// точные версии пакетов сохраняются в ProjectSettings и удаляются из UPM
        /// manifest; Resources-настройка переносится под Editor. При включении всё
        /// восстанавливается без потери конфигурации.
        /// </summary>
        public static void SynchronizeWithModule(bool enabled)
        {
            // Последнее сохранённое состояние имеет приоритет. UPM нельзя запускать
            // одновременно с установкой SDK или предыдущим применением тоггла.
            _pendingModuleState = enabled;
        }

        private static void DrainModuleSynchronization()
        {
            if (!_pendingModuleState.HasValue || _busy || _synchronizing || FirebasePackageInstaller.IsBusy ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            bool enabled = _pendingModuleState.Value;
            _pendingModuleState = null;
            _ = SynchronizeModuleAsync(enabled);
        }

        /// <summary>
        /// Id адаптеров, которые разрешено ставить: всё из реестра за вычетом запрещённых
        /// сеток. Фильтр идёт через <see cref="ForbiddenAdNetworks"/> — тот же список, по
        /// которому <see cref="AppLovinNetworkGuard"/> роняет билд. Разойтись они не могут:
        /// установщик физически не предложит то, что потом остановит сборку.
        /// </summary>
        public static List<string> AllowedAdapterPackageIds()
        {
            var result = new List<string>();

            foreach (var network in RegistryNetworks)
            {
                string packageId = string.Format(AdapterIdFormat, network);

                if (ForbiddenAdNetworks.MatchByGroup(packageId) != null)
                    continue;

                result.Add(packageId);
            }

            return result;
        }

        /// <summary>Сетки из реестра, которые отсеяны стоп-листом (для отчёта в UI и логах).</summary>
        public static List<string> BlockedNetworkNames()
        {
            var result = new List<string>();

            foreach (var network in RegistryNetworks)
            {
                var forbidden = ForbiddenAdNetworks.MatchByGroup(string.Format(AdapterIdFormat, network));

                if (forbidden != null && !result.Contains(forbidden.DisplayName))
                    result.Add(forbidden.DisplayName);
            }

            return result;
        }

        /// <summary>Реестр AppLovin уже прописан в манифесте проекта.</summary>
        public static bool IsRegistryConfigured()
        {
            if (!File.Exists(ManifestPath))
                return false;

            try
            {
                return File.ReadAllText(ManifestPath).Contains(RegistryUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppLovinInstaller] Не удалось прочитать {ManifestPath}: {ex.Message}");
                return false;
            }
        }

        [MenuItem("AMZN GoD/AppLovin/Install MAX Plugin", false, 300)]
        public static void InstallMaxPluginMenu()
        {
            if (!CanStart()) return;
            if (HasInstalledPlugin)
            {
                SetStatus("MAX уже установлен. Для смены версии нажмите Replace MAX Plugin.");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "AppLovin MAX",
                    $"Будет прописан scoped registry {RegistryUrl} в {ManifestPath} " +
                    $"и установлен пакет {PackageSpec(MaxPluginPackageId)}.\n\n" +
                    "Для сборки нужны minSdk 24, compileSdk 36 и настроенные Android Toolchain инструменты.\n\nПродолжить?",
                    "Установить", "Отмена"))
                return;

            _ = InstallMaxPluginAsync();
        }

        [MenuItem("AMZN GoD/AppLovin/Replace MAX Plugin", false, 302)]
        public static void ReplaceMaxPluginMenu()
        {
            if (!CanStart()) return;
            _installedStatus = null;
            if (!HasInstalledPlugin)
            {
                SetStatus("MAX не установлен. Используйте Install MAX Plugin.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Заменить AppLovin MAX",
                    InstalledStatus + " → " + PackageSpec(MaxPluginPackageId) + ".\n\n" +
                    "Поддерживаемые адаптеры обновятся до закреплённых версий. " + ObsoleteAdaptersNotice + "\n\n" +
                    "Legacy-файлы SDK будут заменены пакетом UPM. Resources, настройки и SDK keys сохраняются. " +
                    "Резервная копия: Library/AmznGoDSDK/AppLovin.", "Заменить", "Отмена")) return;
            _ = RunOperationAsync(true, false);
        }

        [MenuItem("AMZN GoD/AppLovin/Install Allowed Adapters", false, 301)]
        public static void InstallAllowedAdaptersMenu()
        {
            if (!CanStart()) return;
            if (!HasInstalledPlugin)
            {
                SetStatus("Сначала установите MAX кнопкой Install MAX Plugin.");
                return;
            }
            var adapters = AllowedAdapterPackageIds();
            var blocked = BlockedNetworkNames();
            var pinned = PinnedSpecsIn(adapters);

            if (!EditorUtility.DisplayDialog(
                    "AppLovin MAX",
                    $"Будет установлено адаптеров: {adapters.Count}.\n\n" +
                    ObsoleteAdaptersNotice + "\n\n" +
                    $"Исключены по стоп-листу: {(blocked.Count == 0 ? "—" : string.Join(", ", blocked))}\n\n" +
                    (pinned.Count == 0
                        ? string.Empty
                        : "Проверенные версии для minSdk 24 / compileSdk 36:\n" +
                          string.Join("\n", pinned) + "\n\n") +
                    "Установка нескольких пакетов занимает время, редактор будет подвисать. Продолжить?",
                    "Установить", "Отмена"))
                return;

            _ = InstallAllowedAdaptersAsync();
        }

        public static Task InstallMaxPluginAsync()
        {
            return RunOperationAsync(false, false);
        }

        public static Task InstallAllowedAdaptersAsync()
        {
            return RunOperationAsync(false, true);
        }

        /// <summary>Checks the entire pinned set, including adapters missing from the project.</summary>
        public static bool IsRequiredSetInstalled
        {
            get
            {
                if (AppLovinLegacyInstallation.HasCore) return false;
                var packages = PackageInfo.GetAllRegisteredPackages();
                if (packages.Any(package => ObsoleteAdapterPackageIds.Contains(package.name)) ||
                    File.Exists(ManifestPath) && ReadManifestDependencies(File.ReadAllText(ManifestPath)).Keys.Any(ObsoleteAdapterPackageIds.Contains))
                    return false;
                return AllowedAdapterPackageIds().Concat(new[] { MaxPluginPackageId }).All(id =>
                    packages.Any(package => package.name == id && package.version == PinnedVersions[id] &&
                        !string.IsNullOrEmpty(package.resolvedPath) && File.Exists(Path.Combine(package.resolvedPath, "package.json"))));
            }
        }

        /// <summary>Installs the enabled module's complete pinned set without confirmation dialogs.</summary>
        public static async Task EnsureRequiredAsync()
        {
            if (!IsModuleEnabledInSavedSettings) return;
            if (IsRequiredSetInstalled) return;
            if (!CanStart()) throw new InvalidOperationException(Status);
            await RunOperationAsync(HasInstalledPlugin, false, true, true);
            if (!IsRequiredSetInstalled)
                throw new IOException("Не подтверждён полный комплект AppLovin MAX и адаптеров. " + Status);
        }

        private static async Task RunOperationAsync(bool replace, bool adaptersOnly,
            bool throwOnError = false, bool includeAllAdapters = false)
        {
            if (!CanStart()) return;
            _busy = true;
            bool locked = false;
            bool backedUp = false;
            bool upmTouched = false;
            AppLovinLegacyInstallation legacy = null;
            PackageInfo[] installed = null;
            try
            {
                _installedStatus = null;
                if (!replace && !adaptersOnly && HasInstalledPlugin)
                    throw new IOException("MAX уже установлен. Для смены версии нажмите Replace MAX Plugin.");
                if ((replace || adaptersOnly) && !HasInstalledPlugin)
                    throw new IOException("Сначала установите MAX кнопкой Install MAX Plugin.");
                installed = PackageInfo.GetAllRegisteredPackages();
                var installedCore = installed.FirstOrDefault(package => package.name == MaxPluginPackageId);
                if (replace && installedCore != null && installedCore.source == PackageSource.Embedded)
                    throw new IOException("MAX установлен как embedded package. Сначала перенесите его из Packages в обычный UPM пакет.");
                var adapters = installed.Where(package => package.name.StartsWith("com.applovin.mediation.adapters.", StringComparison.Ordinal)).ToArray();
                var obsolete = ReadManifestDependencies(File.ReadAllText(ManifestPath)).Keys
                    .Where(ObsoleteAdapterPackageIds.Contains).ToArray();
                foreach (var adapter in adapters)
                {
                    if (ObsoleteAdapterPackageIds.Contains(adapter.name) && adapter.source == PackageSource.Embedded)
                        throw new IOException("Устаревший адаптер установлен как embedded package: " + adapter.name + ". Удалите его явно перед обновлением MAX.");
                    var forbidden = ForbiddenAdNetworks.MatchByGroup(adapter.name);
                    if (forbidden != null)
                        throw new IOException("Установлен запрещённый адаптер " + forbidden.DisplayName + ". Удалите его явно перед установкой.");
                    if (replace && PinnedVersions.ContainsKey(adapter.name) && adapter.source != PackageSource.Registry)
                        throw new IOException("Закреплённый адаптер имеет нестандартный источник: " + adapter.packageId + ". Переведите его в registry UPM перед заменой.");
                }
                if (adaptersOnly && AppLovinLegacyInstallation.HasCore)
                    throw new IOException("Сначала переведите legacy MAX в UPM кнопкой Replace MAX Plugin.");
                legacy = AppLovinLegacyInstallation.Inspect(replace, adapters.Select(package => package.name));
                string backup = BackupDirectory + "/AppLovin/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                EditorApplication.LockReloadAssemblies();
                locked = true;
                legacy.Backup(backup);
                backedUp = true;
                SessionState.SetString(PendingKey, backup);
                SetStatus(replace ? "Замена AppLovin MAX…" : "Установка AppLovin MAX…");
                AssetDatabase.DisallowAutoRefresh();
                try
                {
                    if (!EnsureScopedRegistry()) throw new IOException("Не удалось настроить scoped registry AppLovin.");
                    if (replace) legacy.RemoveLegacy();
                }
                finally { AssetDatabase.AllowAutoRefresh(); }

                var expected = new Dictionary<string, string>();
                expected[MaxPluginPackageId] = adaptersOnly ? installedCore.version : PinnedVersions[MaxPluginPackageId];
                if (adaptersOnly)
                {
                    var allowed = AllowedAdapterPackageIds();
                    if (allowed.Count > 0 || obsolete.Length > 0)
                    {
                        EditorUtility.DisplayProgressBar("AppLovin MAX", "Установка адаптеров…", 0.5f);
                        upmTouched = true;
                        // Один расчёт зависимостей для всего набора: ошибка пакета не оставляет
                        // цепочку ранее установленных адаптеров от последовательных Client.Add.
                        await WaitForRequest(Client.AddAndRemove(allowed.Select(PackageSpec).ToArray(), obsolete));
                    }
                    foreach (string id in allowed)
                        expected[id] = PinnedVersions.TryGetValue(id, out string pin) ? pin : null;
                }
                else if (!replace && !includeAllAdapters)
                {
                    upmTouched = true;
                    await WaitForRequest(Client.AddAndRemove(new[] { PackageSpec(MaxPluginPackageId) }, obsolete));
                }
                else
                {
                    var specifications = new List<string> { PackageSpec(MaxPluginPackageId) };
                    foreach (var adapter in adapters)
                    {
                        if (ObsoleteAdapterPackageIds.Contains(adapter.name)) continue;
                        string version = PinnedVersions.TryGetValue(adapter.name, out string pin) ? pin : adapter.version;
                        expected[adapter.name] = version;
                        if (adapter.source == PackageSource.Registry)
                            specifications.Add(adapter.name + "@" + version);
                    }
                    foreach (string adapter in legacy.AdapterPins)
                    {
                        if (!PinnedVersions.ContainsKey(adapter)) continue;
                        expected[adapter] = PinnedVersions[adapter];
                        if (!specifications.Contains(PackageSpec(adapter))) specifications.Add(PackageSpec(adapter));
                    }
                    if (includeAllAdapters)
                        foreach (string adapter in AllowedAdapterPackageIds())
                        {
                            expected[adapter] = PinnedVersions[adapter];
                            if (!specifications.Contains(PackageSpec(adapter))) specifications.Add(PackageSpec(adapter));
                        }
                    EditorUtility.DisplayProgressBar("AppLovin MAX", "Установка " + PackageSpec(MaxPluginPackageId), 0.5f);
                    upmTouched = true;
                    await WaitForRequest(Client.AddAndRemove(specifications.ToArray(), obsolete));
                }
                var check = Client.List(true, true);
                await WaitForRequest(check);
                if (check.Result.Any(package => ObsoleteAdapterPackageIds.Contains(package.name)))
                    throw new IOException("UPM не удалил устаревшие адаптеры HyprMX/Maio.");
                foreach (var package in expected)
                    if (!check.Result.Any(item => item.name == package.Key && (package.Value == null || item.version == package.Value)))
                        throw new IOException("UPM не подтвердил пакет " + package.Key + (package.Value == null ? "" : "@" + package.Value));
                if (replace && check.Result.Any(package => package.name.StartsWith("com.applovin.mediation.adapters.", StringComparison.Ordinal) && !expected.ContainsKey(package.name)))
                    throw new IOException("UPM изменил состав адаптеров. Замена отменена.");
                SessionState.EraseString(PendingKey);
                SetStatus("AppLovin: " + (adaptersOnly ? "адаптеры установлены" : PackageSpec(MaxPluginPackageId) + " установлен") + ". Резервная копия: " + backup);
                Debug.Log("[AppLovinInstaller] " + Status);
                if (obsolete.Length > 0) Debug.Log("[AppLovinInstaller] " + ObsoleteAdaptersNotice);
            }
            catch (Exception failure)
            {
                string recovery = "Файлы проекта не изменены.";
                if (backedUp)
                {
                    try
                    {
                        try
                        {
                            if (upmTouched)
                            {
                                var previousPackages = installed.Where(package => package.name.StartsWith("com.applovin.", StringComparison.Ordinal)).ToArray();
                                var restoreSpecs = previousPackages.Where(package => package.source != PackageSource.Embedded)
                                    .Select(package => package.source == PackageSource.Registry ? package.packageId :
                                        package.packageId.Substring(package.name.Length + 1)).ToArray();
                                var removeIds = RollbackRemovalIds(File.ReadAllText(ManifestPath), installed.Select(package => package.name));
                                // UPM меняем до восстановления manifest: удалять можно только
                                // реально записанные в него пакеты, а не все попытки установки.
                                if (restoreSpecs.Length != 0 || removeIds.Length != 0)
                                    await WaitForRequest(Client.AddAndRemove(restoreSpecs, removeIds));
                            }
                        }
                        finally
                        {
                            // Даже ошибка UPM не должна помешать восстановлению файлов на диске.
                            AssetDatabase.DisallowAutoRefresh();
                            try { legacy.Restore(); }
                            finally { AssetDatabase.AllowAutoRefresh(); }
                        }
                        if (upmTouched)
                        {
                            var check = Client.List(true, true);
                            try
                            {
                                await WaitForRequest(check);
                                if (!new HashSet<string>(installed.Where(package => package.name.StartsWith("com.applovin.", StringComparison.Ordinal)).Select(package => package.packageId)).SetEquals(
                                        check.Result.Where(package => package.name.StartsWith("com.applovin.", StringComparison.Ordinal)).Select(package => package.packageId)))
                                    throw new IOException("UPM не восстановил исходный состав и версии AppLovin.");
                            }
                            // Сохраняем исходную форму manifest/lock и при ошибке проверки.
                            finally { legacy.RestorePackageFiles(); }
                        }
                        recovery = "Исходные файлы и версии AppLovin восстановлены.";
                    }
                    catch (Exception restoreFailure)
                    {
                        recovery = "Автоматический откат неполон: " + restoreFailure.Message + ". Резервная копия: " + legacy.BackupPath;
                        Debug.LogError("[AppLovinInstaller] " + restoreFailure);
                    }
                }
                SessionState.EraseString(PendingKey);
                SetStatus("Ошибка AppLovin: " + failure.Message + " " + recovery);
                Debug.LogError("[AppLovinInstaller] " + Status);
                if (throwOnError) throw new IOException(Status, failure);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _installedStatus = null;
                try { if (backedUp) AssetDatabase.Refresh(); }
                finally
                {
                    _busy = false;
                    if (locked) EditorApplication.UnlockReloadAssemblies();
                    EditorApplication.delayCall += ModuleDefineManager.UpdateDefineSymbolsFromSettings;
                }
            }
        }

        #endregion

        #region Disable / restore

        private static async Task SynchronizeModuleAsync(bool enabled)
        {
            AppLovinLegacyInstallation snapshot = null;
            bool locked = false;
            bool backedUp = false;
            bool upmTouched = false;
            bool completed = false;
            PackageInfo[] installed = null;
            try
            {
                var dependencies = File.Exists(ManifestPath)
                    ? ReadManifestDependencies(File.ReadAllText(ManifestPath)) : new Dictionary<string, string>();
                var state = LoadDisabledState();
                bool hasPackageChanges = enabled ? state.Packages.Count > 0 || dependencies.Keys.Any(ObsoleteAdapterPackageIds.Contains) :
                    dependencies.Keys.Any(id => id.StartsWith("com.applovin.", StringComparison.Ordinal));
                bool hasSettingsChanges = File.Exists(enabled ? DisabledAppLovinSettingsPath : AppLovinSettingsPath);
                if (!hasPackageChanges && !hasSettingsChanges) return;

                _synchronizing = true;
                installed = PackageInfo.GetAllRegisteredPackages();
                snapshot = AppLovinLegacyInstallation.CaptureModuleState();
                string backup = BackupDirectory + "/AppLovin/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                EditorApplication.LockReloadAssemblies();
                locked = true;
                snapshot.Backup(backup);
                backedUp = true;
                SessionState.SetString(PendingKey, backup);
                SetStatus(enabled ? "Восстановление AppLovin после включения модуля…" : "Отключение AppLovin…");
                SynchronizeSettingsAsset(enabled);
                upmTouched = hasPackageChanges;
                if (enabled) await RestoreDisabledPackages();
                else await StashAndRemovePackages();
                completed = true;
                SetStatus(enabled ? "Настройки модуля AppLovin применены." : "AppLovin отключён; пакеты и настройки сохранены для восстановления.");
            }
            catch (Exception ex)
            {
                string recovery = "";
                if (backedUp)
                {
                    try
                    {
                        try
                        {
                            if (upmTouched)
                            {
                                var previous = installed.Where(package => package.name.StartsWith("com.applovin.", StringComparison.Ordinal)).ToArray();
                                var restore = previous.Where(package => package.source != PackageSource.Embedded)
                                    .Select(package => package.source == PackageSource.Registry ? package.packageId :
                                        package.packageId.Substring(package.name.Length + 1)).ToArray();
                                var remove = RollbackRemovalIds(File.ReadAllText(ManifestPath), installed.Select(package => package.name));
                                if (restore.Length > 0 || remove.Length > 0)
                                    await WaitForRequest(Client.AddAndRemove(restore, remove));
                            }
                        }
                        finally { snapshot.Restore(); }
                        recovery = " Предыдущее состояние AppLovin восстановлено.";
                    }
                    catch (Exception restoreFailure)
                    {
                        recovery = " Откат неполон: " + restoreFailure.Message + ". Резервная копия: " + snapshot.BackupPath;
                    }
                }
                SetStatus("Не удалось применить настройки AppLovin: " + ex.Message + recovery);
                Debug.LogError("[AppLovinInstaller] " + Status);
            }
            finally
            {
                if (backedUp) SessionState.EraseString(PendingKey);
                _installedStatus = null;
                try { if (backedUp) AssetDatabase.Refresh(); }
                finally
                {
                    _synchronizing = false;
                    if (locked) EditorApplication.UnlockReloadAssemblies();
                    if (completed) EditorApplication.delayCall += ModuleDefineManager.UpdateDefineSymbolsFromSettings;
                }
            }
        }

        private static async Task StashAndRemovePackages()
        {
            if (!File.Exists(ManifestPath))
                return;

            var packages = ReadManifestDependencies(File.ReadAllText(ManifestPath))
                .Where(entry => entry.Key.StartsWith("com.applovin.", StringComparison.Ordinal)).ToArray();
            if (packages.Length == 0)
                return;

            var state = LoadDisabledState();
            foreach (var package in packages)
            {
                string id = package.Key;
                string version = package.Value;
                var existing = state.Packages.FirstOrDefault(entry =>
                    string.Equals(entry.Id, id, StringComparison.Ordinal));
                if (existing == null)
                    state.Packages.Add(new DisabledPackageEntry { Id = id, Version = version });
                else
                    existing.Version = version;
            }

            SaveDisabledState(state);
            await WaitForRequest(Client.AddAndRemove(Array.Empty<string>(), packages.Select(package => package.Key).ToArray()));
            Debug.Log($"[AppLovinInstaller] AppLovin выключен: из manifest удалено пакетов {packages.Length}.");
        }

        private static async Task RestoreDisabledPackages()
        {
            var state = LoadDisabledState();
            if (!File.Exists(ManifestPath))
                throw new IOException("Не найден Packages/manifest.json для восстановления AppLovin.");

            var dependencies = ReadManifestDependencies(File.ReadAllText(ManifestPath));
            var obsolete = dependencies.Keys.Where(ObsoleteAdapterPackageIds.Contains).ToArray();
            bool skippedObsolete = state.Packages.Any(entry => ObsoleteAdapterPackageIds.Contains(entry.Id));
            var missing = state.Packages
                .Where(entry => !ObsoleteAdapterPackageIds.Contains(entry.Id) && !dependencies.ContainsKey(entry.Id))
                .ToList();
            if (missing.Count > 0 || obsolete.Length > 0)
            {
                var specifications = missing.Select(entry =>
                    Regex.IsMatch(entry.Version, @"^(file:|https?://|git[+:]|ssh://)")
                        ? entry.Version : entry.Id + "@" + entry.Version).ToArray();
                await WaitForRequest(Client.AddAndRemove(specifications, obsolete));
                var check = Client.List(true, true);
                await WaitForRequest(check);
                if (check.Result.Any(package => ObsoleteAdapterPackageIds.Contains(package.name)))
                    throw new IOException("UPM не удалил устаревшие адаптеры HyprMX/Maio при включении AppLovin.");
            }
            DeleteDisabledState();
            Debug.Log($"[AppLovinInstaller] AppLovin включён: восстановлено пакетов {missing.Count}.");
            if (obsolete.Length > 0 || skippedObsolete) Debug.Log("[AppLovinInstaller] " + ObsoleteAdaptersNotice);
        }

        private static DisabledPackageState LoadDisabledState()
        {
            if (!File.Exists(DisabledStatePath))
                return new DisabledPackageState();

            var state = JsonUtility.FromJson<DisabledPackageState>(File.ReadAllText(DisabledStatePath));
            if (state?.Packages == null || state.Packages.Any(entry => entry == null || string.IsNullOrEmpty(entry.Id) ||
                    !entry.Id.StartsWith("com.applovin.", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Version)) ||
                state.Packages.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != state.Packages.Count)
                throw new IOException("Некорректная сохранённая конфигурация AppLovin: " + DisabledStatePath);
            return state;
        }

        private static void SaveDisabledState(DisabledPackageState state)
        {
            string directory = Path.GetDirectoryName(DisabledStatePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(DisabledStatePath, JsonUtility.ToJson(state, true));
        }

        private static void DeleteDisabledState()
        {
            if (File.Exists(DisabledStatePath))
                File.Delete(DisabledStatePath);
        }

        private static void SynchronizeSettingsAsset(bool enabled)
        {
            string source = enabled ? DisabledAppLovinSettingsPath : AppLovinSettingsPath;
            string destination = enabled ? AppLovinSettingsPath : DisabledAppLovinSettingsPath;
            // Когда MAX уже удалён, тип ScriptableObject недоступен и
            // LoadMainAssetAtPath возвращает null даже для существующего файла.
            if (!File.Exists(source))
                return;
            if (File.Exists(destination))
                throw new IOException($"целевой asset уже существует: {destination}");

            string directory = Path.GetDirectoryName(destination)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error))
                throw new IOException(error);
            Debug.Log($"[AppLovinInstaller] {source} -> {destination}");
        }

        #endregion

        #region Registry

        /// <summary>
        /// Дописывает scoped registry AppLovin в манифест проекта. Идемпотентно.
        /// <para>
        /// Манифест правится текстом, а не через сериализацию: JsonUtility не умеет
        /// произвольные структуры и на round-trip выбросил бы все незнакомые ему поля,
        /// то есть половину чужого манифеста. Точечная вставка сохраняет файл байт-в-байт,
        /// кроме добавленного блока. Оригинал перед записью копируется в Library/.
        /// </para>
        /// </summary>
        public static bool EnsureScopedRegistry()
        {
            if (IsRegistryConfigured())
                return true;

            if (!File.Exists(ManifestPath))
            {
                Debug.LogError($"[AppLovinInstaller] Не найден {ManifestPath} — реестр не прописан.");
                return false;
            }

            string manifest;
            try
            {
                manifest = File.ReadAllText(ManifestPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppLovinInstaller] Не удалось прочитать {ManifestPath}: {ex.Message}");
                return false;
            }

            string updated = InsertRegistry(manifest, out string error);
            if (updated == null)
            {
                Debug.LogError($"[AppLovinInstaller] {error} Пропиши реестр вручную:\n{RegistryEntryJson("  ")}");
                return false;
            }

            if (!BackupManifest(manifest))
                return false;

            try
            {
                File.WriteAllText(ManifestPath, updated);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppLovinInstaller] Не удалось записать {ManifestPath}: {ex.Message}");
                return false;
            }

            Debug.Log($"[AppLovinInstaller] Scoped registry «{RegistryName}» ({RegistryUrl}) добавлен в {ManifestPath}.");
            return true;
        }

        /// <summary>
        /// Возвращает манифест с добавленным реестром либо null с причиной в
        /// <paramref name="error"/>. Публичный ради тестируемости: разбирать текстовую
        /// вставку в JSON без прогонов на реальных манифестах — плохая идея.
        /// </summary>
        public static string InsertRegistry(string manifest, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(manifest))
            {
                error = "Манифест пуст.";
                return null;
            }

            if (manifest.Contains(RegistryUrl))
                return manifest;

            int scopedIndex = manifest.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);

            if (scopedIndex >= 0)
            {
                // Реестры уже есть — вставляем свой первым элементом массива.
                int arrayStart = manifest.IndexOf('[', scopedIndex);
                if (arrayStart < 0)
                {
                    error = "В манифесте есть \"scopedRegistries\", но не найдена открывающая скобка массива.";
                    return null;
                }

                string entry = RegistryEntryJson("    ");
                return manifest.Insert(arrayStart + 1, Environment.NewLine + entry + ",");
            }

            int braceIndex = manifest.IndexOf('{');
            if (braceIndex < 0)
            {
                error = "Манифест не похож на JSON-объект: нет открывающей скобки.";
                return null;
            }

            var block = new StringBuilder();
            block.Append(Environment.NewLine);
            block.Append("  \"scopedRegistries\": [").Append(Environment.NewLine);
            block.Append(RegistryEntryJson("    ")).Append(Environment.NewLine);
            block.Append("  ],");

            return manifest.Insert(braceIndex + 1, block.ToString());
        }

        private static string RegistryEntryJson(string indent)
        {
            var builder = new StringBuilder();
            builder.Append(indent).Append("{").Append(Environment.NewLine);
            builder.Append(indent).Append("  \"name\": \"").Append(RegistryName).Append("\",").Append(Environment.NewLine);
            builder.Append(indent).Append("  \"url\": \"").Append(RegistryUrl).Append("\",").Append(Environment.NewLine);
            builder.Append(indent).Append("  \"scopes\": [").Append(Environment.NewLine);
            builder.Append(indent).Append("    \"").Append(RegistryScope).Append("\"").Append(Environment.NewLine);
            builder.Append(indent).Append("  ]").Append(Environment.NewLine);
            builder.Append(indent).Append("}");
            return builder.ToString();
        }

        private static bool BackupManifest(string content)
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                string path = Path.Combine(
                    BackupDirectory,
                    $"manifest.json.backup-{DateTime.Now:yyyyMMdd-HHmmss}");

                File.WriteAllText(path, content);
                Debug.Log($"[AppLovinInstaller] Резервная копия манифеста: {path}");
                return true;
            }
            catch (Exception ex)
            {
                // Без бэкапа не пишем: манифест — единственное описание зависимостей проекта.
                Debug.LogError($"[AppLovinInstaller] Не удалось сохранить резервную копию манифеста: {ex.Message}. Установка отменена.");
                return false;
            }
        }

        #endregion

        #region Packages

        [DataContract]
        private sealed class ManifestDependencies
        {
            [DataMember(Name = "dependencies", IsRequired = true)]
            public Dictionary<string, string> Dependencies;
        }

        private static Dictionary<string, string> ReadManifestDependencies(string manifest)
        {
            var serializer = new DataContractJsonSerializer(typeof(ManifestDependencies),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)))
            {
                var parsed = (ManifestDependencies)serializer.ReadObject(stream);
                if (parsed?.Dependencies == null) throw new IOException("Не удалось прочитать зависимости manifest.");
                return parsed.Dependencies;
            }
        }

        internal static string[] RollbackRemovalIds(string manifest, IEnumerable<string> previousPackageNames)
        {
            var previous = new HashSet<string>(previousPackageNames, StringComparer.Ordinal);
            return ReadManifestDependencies(manifest).Keys.Where(id => id.StartsWith("com.applovin.", StringComparison.Ordinal) && !previous.Contains(id)).ToArray();
        }

        private static async Task InstallPackageAsync(string packageId)
        {
            // Последняя защита: сюда не должен попадать запрещённый пакет ни при каких правках
            // вызывающего кода — иначе установщик тихо соберёт то, что потом не соберётся.
            var forbidden = ForbiddenAdNetworks.MatchByGroup(packageId);
            if (forbidden != null)
            {
                throw new IOException($"Отказ: «{forbidden.DisplayName}» в стоп-листе ({packageId}).");
            }

            // UPM понимает форму name@version; без неё Client.Add тянет latest.
            string requestSpec = PackageSpec(packageId);

            if (requestSpec != packageId)
                Debug.Log($"[AppLovinInstaller] {packageId}: версия закреплена ({requestSpec}) — см. PinnedVersions.");

            Debug.Log($"[AppLovinInstaller] Установка {requestSpec}...");
            var request = Client.Add(requestSpec);

            await WaitForRequest(request);
            if (PinnedVersions.TryGetValue(packageId, out string version) && request.Result.version != version)
                throw new IOException("UPM не установил требуемую версию " + requestSpec);
        }

        private static async Task WaitForRequest(Request request)
        {
            while (!request.IsCompleted) await Task.Delay(100);
            if (request.Status != StatusCode.Success)
                throw new IOException(request.Error?.message ?? "Неизвестная ошибка Unity Package Manager.");
        }

        #endregion
    }
}
