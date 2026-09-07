using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
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
    /// Молча при загрузке редактора ничего не ставится — в отличие от EDM4U
    /// (<see cref="DependencyInstaller"/>), который весит один пакет. Здесь речь о плагине плюс
    /// два десятка адаптеров, тянущих нативные зависимости; такое делается по явной команде.
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

        private const string ManifestPath = "Packages/manifest.json";
        private const string BackupDirectory = "Library/AmznGoDSDK";
        private const string StatusKey = "AMZNGoDSDK.AppLovinInstaller.Status";
        private const string PendingKey = "AMZNGoDSDK.AppLovinInstaller.Pending";
        private static bool _busy;
        private static string _installedStatus;
        private static bool _hasInstalledPlugin;
        private static double _statusCheckedAt;

        public static bool IsBusy => _busy;
        public static string Status => SessionState.GetString(StatusKey, "");
        public static bool HasInstalledPlugin { get { ReadInstalledStatus(); return _hasInstalledPlugin; } }
        public static string InstalledStatus { get { ReadInstalledStatus(); return _installedStatus; } }

        static AppLovinPackageInstaller()
        {
            string pending = SessionState.GetString(PendingKey, "");
            if (pending.Length == 0) return;
            SessionState.EraseString(PendingKey);
            SetStatus("Установка AppLovin была прервана. Проверьте состояние пакетов. Резервная копия: " + pending);
        }

        private static void ReadInstalledStatus()
        {
            if (_installedStatus != null && EditorApplication.timeSinceStartup - _statusCheckedAt < 2) return;
            _statusCheckedAt = EditorApplication.timeSinceStartup;
            var package = PackageInfo.GetAllRegisteredPackages().FirstOrDefault(item => item.name == MaxPluginPackageId);
            try
            {
                string legacy = AppLovinLegacyInstallation.Description;
                _hasInstalledPlugin = package != null || legacy != null;
                _installedStatus = string.Join("; ", new[] { package == null ? null : "UPM " + package.version, legacy }.Where(value => value != null));
                if (!_hasInstalledPlugin) _installedStatus = "MAX не установлен";
            }
            catch (Exception ex)
            {
                _hasInstalledPlugin = package != null;
                _installedStatus = "Не удалось проверить legacy MAX: " + ex.Message;
            }
        }

        private static void SetStatus(string message)
        {
            SessionState.SetString(StatusKey, message);
            foreach (var window in Resources.FindObjectsOfTypeAll<SDKSettingsWindow>()) window.Repaint();
        }

        private static bool CanStart()
        {
            if (_busy || FirebasePackageInstaller.IsBusy)
            {
                SetStatus("Дождитесь завершения текущей установки SDK.");
                return false;
            }
            return !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Сетки, у которых в реестре AppLovin есть именно Android-адаптер (25 на 2026-09-07).
        /// CSJ, Pangle и Tencent GDT представлены только iOS-пакетами и сюда не входят.
        /// Список проверен через /-/v1/search и оставлен статическим: набор пакетов
        /// должен быть воспроизводимым и не зависеть от того, доступна ли сеть в момент сборки.
        /// Появилась новая сетка — дописать сюда.
        /// </summary>
        private static readonly string[] RegistryNetworks =
        {
            "bidmachine", "bigoads", "bytedance", "chartboost", "facebook", "fyber",
            "google", "googleadmanager", "hyprmx", "inmobi", "ironsource", "line", "maio",
            "mintegral", "mobilefuse", "moloco", "mytarget", "ogurypresage",
            "pubmatic", "smaato", "unityads", "verve", "vungle", "yandex",
            "ysonetwork",
        };

        // Сборка идёт под Amazon Appstore, то есть Android. iOS-адаптеры тянут CocoaPods
        // и в этом проекте только раздували бы зависимости, поэтому ставим только .android.
        private const string AdapterIdFormat = "com.applovin.mediation.adapters.{0}.android";

        /// <summary>
        /// Потолки версий для пакетов, чьи свежие релизы не собираются в этом проекте.
        /// Без пина Client.Add ставит latest, и одно нажатие кнопки установки молча
        /// возвращает сборку в нерабочее состояние.
        /// <para>
        /// Проект: minSdk 23, compileSdk = targetSdk = 34, AGP 7.4.2 (потолок Unity 2022.3 —
        /// bundled Gradle 7.5.1 + JDK 11; AGP 8.9 требует Gradle 8.11+ и JDK 17).
        /// Версии ниже — последние, которые в эти рамки укладываются (проверено по
        /// AndroidManifest.xml и aar-metadata.properties самих артефактов, 2026-09-03):
        /// </para>
        /// <list type="bullet">
        /// <item>ads 8.6.3 → applovin-sdk 13.6.2, minSdk 23. С 13.6.3 AppLovin поднял minSdk до 24.</item>
        /// <item>facebook 6210000.0.0 → facebook-adapter 6.21.0.0, minSdk 16. Следующий (6.22.0.0)
        /// сам объявляет minSdk 24 и тянет audience-network-sdk 6.22.0 → androidx.browser 1.9.0,
        /// которому нужны compileSdk 36 и AGP 8.9.1.</item>
        /// <item>line 300000010.0.0 → line-adapter 3000.0.1.0 → fivead 3.0.1 → androidx.activity 1.9.3.
        /// Следующий тянет fivead 3.1.1 → activity 1.10.1, а ей нужен compileSdk 35.</item>
        /// <item>ogurypresage 6020200.0.0 → ogury-presage-adapter 6.2.2.0 → ogury-sdk 6.2.2 без
        /// ограничения по compileSdk. У 6.3.1 в aar-metadata стоит minCompileSdk 35.</item>
        /// </list>
        /// <para>
        /// Снимать пин можно только вместе с поднятием compileSdk/minSdk — и только проверив
        /// сборку, а не по номеру версии.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> PinnedVersions = new Dictionary<string, string>
        {
            { "com.applovin.mediation.ads", "8.6.3" },
            { "com.applovin.mediation.adapters.facebook.android", "6210000.0.0" },
            { "com.applovin.mediation.adapters.line.android", "300000010.0.0" },
            { "com.applovin.mediation.adapters.ogurypresage.android", "6020200.0.0" },
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
                    "Версия закреплена намеренно: начиная с applovin-sdk 13.6.3 плагин требует " +
                    "minSdk 24, а проект собирается с 23 (см. PinnedVersions).\n\nПродолжить?",
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
                    "Состав сетей и версии адаптеров сохранятся; установленные Facebook, Line и Ogury Presage вернутся к закреплённым версиям.\n\n" +
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
                    $"Исключены по стоп-листу: {(blocked.Count == 0 ? "—" : string.Join(", ", blocked))}\n\n" +
                    (pinned.Count == 0
                        ? string.Empty
                        : "С закреплённой версией (свежие ломают minSdk 23 / compileSdk 34):\n" +
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

        private static async Task RunOperationAsync(bool replace, bool adaptersOnly)
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
                foreach (var adapter in adapters)
                {
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
                    if (allowed.Count > 0)
                    {
                        EditorUtility.DisplayProgressBar("AppLovin MAX", "Установка адаптеров…", 0.5f);
                        upmTouched = true;
                        // Один расчёт зависимостей для всего набора: ошибка пакета не оставляет
                        // цепочку ранее установленных адаптеров от последовательных Client.Add.
                        await WaitForRequest(Client.AddAndRemove(allowed.Select(PackageSpec).ToArray(), Array.Empty<string>()));
                    }
                    foreach (string id in allowed)
                        expected[id] = PinnedVersions.TryGetValue(id, out string pin) ? pin : null;
                }
                else if (!replace)
                {
                    upmTouched = true;
                    await InstallPackageAsync(MaxPluginPackageId);
                }
                else
                {
                    var specifications = new List<string> { PackageSpec(MaxPluginPackageId) };
                    foreach (var adapter in adapters)
                    {
                        string version = PinnedVersions.TryGetValue(adapter.name, out string pin) ? pin : adapter.version;
                        expected[adapter.name] = version;
                        if (adapter.source == PackageSource.Registry)
                            specifications.Add(adapter.name + "@" + version);
                    }
                    foreach (string adapter in legacy.AdapterPins)
                    {
                        expected[adapter] = PinnedVersions[adapter];
                        if (!specifications.Contains(PackageSpec(adapter))) specifications.Add(PackageSpec(adapter));
                    }
                    EditorUtility.DisplayProgressBar("AppLovin MAX", "Установка " + PackageSpec(MaxPluginPackageId), 0.5f);
                    upmTouched = true;
                    await WaitForRequest(Client.AddAndRemove(specifications.ToArray(), Array.Empty<string>()));
                }
                var check = Client.List(true, true);
                await WaitForRequest(check);
                foreach (var package in expected)
                    if (!check.Result.Any(item => item.name == package.Key && (package.Value == null || item.version == package.Value)))
                        throw new IOException("UPM не подтвердил пакет " + package.Key + (package.Value == null ? "" : "@" + package.Value));
                if (replace && check.Result.Any(package => package.name.StartsWith("com.applovin.mediation.adapters.", StringComparison.Ordinal) && !expected.ContainsKey(package.name)))
                    throw new IOException("UPM изменил состав адаптеров. Замена отменена.");
                SessionState.EraseString(PendingKey);
                SetStatus("AppLovin: " + (adaptersOnly ? "адаптеры установлены" : PackageSpec(MaxPluginPackageId) + " установлен") + ". Резервная копия: " + backup);
                Debug.Log("[AppLovinInstaller] " + Status);
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
                }
            }
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

        internal static string[] RollbackRemovalIds(string manifest, IEnumerable<string> previousPackageNames)
        {
            var serializer = new DataContractJsonSerializer(typeof(ManifestDependencies),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)))
            {
                var parsed = (ManifestDependencies)serializer.ReadObject(stream);
                if (parsed?.Dependencies == null) throw new IOException("Не удалось прочитать зависимости manifest для отката.");
                var previous = new HashSet<string>(previousPackageNames, StringComparer.Ordinal);
                return parsed.Dependencies.Keys.Where(id => id.StartsWith("com.applovin.", StringComparison.Ordinal) && !previous.Contains(id)).ToArray();
            }
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
