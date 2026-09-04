using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Ставит AppLovin MAX и адаптеры сеток через Unity Package Manager.
    /// <para>
    /// Начиная с MAX 8.0 плагин раздаётся не только .unitypackage'ом, но и через собственный
    /// npm-совместимый scoped registry AppLovin. Это позволяет обойтись без ручного скачивания
    /// и без Integration Manager: регистрируем реестр в манифесте проекта и добавляем пакеты
    /// обычным Client.Add.
    /// </para>
    /// <para>
    /// Молча при загрузке редактора ничего не ставится — в отличие от EDM4U
    /// (<see cref="DependencyInstaller"/>), который весит один пакет. Здесь речь о плагине плюс
    /// два десятка адаптеров, тянущих нативные зависимости; такое делается по явной команде.
    /// </para>
    /// </summary>
    public static class AppLovinPackageInstaller
    {
        public const string RegistryName = "AppLovin MAX";
        public const string RegistryUrl = "https://unity.packages.applovin.com";
        public const string RegistryScope = "com.applovin";

        /// <summary>Основной пакет плагина (displayName: AppLovin MAX Mediation Plugin for Unity).</summary>
        public const string MaxPluginPackageId = "com.applovin.mediation.ads";

        private const string ManifestPath = "Packages/manifest.json";
        private const string BackupDirectory = "Library/AmznGoDSDK";
        private const string DisabledStatePath = "ProjectSettings/AMZNGoDSDK/AppLovinPackages.disabled.json";
        private const string AppLovinSettingsPath = "Assets/MaxSdk/Resources/AppLovinSettings.asset";
        private const string DisabledAppLovinSettingsPath =
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

        private static readonly Regex AppLovinDependencyLine = new Regex(
            "^[ \\t]*\\\"(?<id>com\\.applovin\\.[^\\\"]+)\\\"[ \\t]*:[ \\t]*\\\"(?<version>[^\\\"]+)\\\"[ \\t]*,?[ \\t]*(?:\\r?\\n|$)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Сетки, у которых в реестре AppLovin есть адаптер. Снимок реестра на 2026-09-01
        /// (28 штук, получены через /-/v1/search). Список намеренно статический: набор пакетов
        /// должен быть воспроизводимым и не зависеть от того, доступна ли сеть в момент сборки.
        /// Появилась новая сетка — дописать сюда.
        /// </summary>
        private static readonly string[] RegistryNetworks =
        {
            "bidmachine", "bigoads", "bytedance", "chartboost", "csj", "facebook", "fyber",
            "google", "googleadmanager", "hyprmx", "inmobi", "ironsource", "line", "maio",
            "mintegral", "mobilefuse", "moloco", "mytarget", "ogurypresage", "pangle",
            "pubmatic", "smaato", "tencentgdt", "unityads", "verve", "vungle", "yandex",
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
        /// Делает состояние внешнего MAX симметричным тогглу модуля. При выключении
        /// точные версии пакетов сохраняются в ProjectSettings и удаляются из UPM
        /// manifest; Resources-настройка переносится под Editor. При включении всё
        /// восстанавливается без потери конфигурации.
        /// </summary>
        public static void SynchronizeWithModule(bool enabled)
        {
            try
            {
                bool manifestChanged = enabled ? RestoreDisabledPackages() : StashAndRemovePackages();
                SynchronizeSettingsAsset(enabled);
                if (manifestChanged)
                    Client.Resolve();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppLovinInstaller] Не удалось синхронизировать MAX с тогглом: {ex.Message}");
            }
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
            if (!EditorUtility.DisplayDialog(
                    "AppLovin MAX",
                    $"Будет прописан scoped registry {RegistryUrl} в {ManifestPath} " +
                    $"и установлен пакет {PackageSpec(MaxPluginPackageId)}.\n\n" +
                    "Версия закреплена намеренно: начиная с applovin-sdk 13.6.3 плагин требует " +
                    "minSdk 24, а проект собирается с 23 (см. PinnedVersions).\n\nПродолжить?",
                    "Установить", "Отмена"))
                return;

            InstallMaxPluginAsync().ConfigureAwait(false);
        }

        [MenuItem("AMZN GoD/AppLovin/Install Allowed Adapters", false, 301)]
        public static void InstallAllowedAdaptersMenu()
        {
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

            InstallAllowedAdaptersAsync().ConfigureAwait(false);
        }

        public static async Task InstallMaxPluginAsync()
        {
            if (!EnsureScopedRegistry())
                return;

            await InstallPackageAsync(MaxPluginPackageId);
        }

        public static async Task InstallAllowedAdaptersAsync()
        {
            if (!EnsureScopedRegistry())
                return;

            var adapters = AllowedAdapterPackageIds();
            var blocked = BlockedNetworkNames();

            if (blocked.Count > 0)
                Debug.Log($"[AppLovinInstaller] Пропущены по стоп-листу: {string.Join(", ", blocked)}");

            try
            {
                for (int i = 0; i < adapters.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "AppLovin MAX",
                        $"Установка {adapters[i]} ({i + 1}/{adapters.Count})",
                        (float)i / adapters.Count);

                    await InstallPackageAsync(adapters[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[AppLovinInstaller] Готово: обработано адаптеров {adapters.Count}.");
        }

        #endregion

        #region Disable / restore

        private static bool StashAndRemovePackages()
        {
            if (!File.Exists(ManifestPath))
                return false;

            string manifest = File.ReadAllText(ManifestPath);
            MatchCollection matches = AppLovinDependencyLine.Matches(manifest);
            if (matches.Count == 0)
                return false;

            var state = LoadDisabledState();
            foreach (Match match in matches)
            {
                string id = match.Groups["id"].Value;
                string version = match.Groups["version"].Value;
                var existing = state.Packages.FirstOrDefault(entry =>
                    string.Equals(entry.Id, id, StringComparison.Ordinal));
                if (existing == null)
                    state.Packages.Add(new DisabledPackageEntry { Id = id, Version = version });
                else
                    existing.Version = version;
            }

            SaveDisabledState(state);
            if (!BackupManifest(manifest))
                throw new IOException("не удалось создать резервную копию Packages/manifest.json");

            string updated = AppLovinDependencyLine.Replace(manifest, string.Empty);
            // После удаления последней зависимости не оставляем trailing comma.
            updated = Regex.Replace(updated, @",(?=\s*})", string.Empty);
            File.WriteAllText(ManifestPath, updated);
            Debug.Log($"[AppLovinInstaller] AppLovin выключен: из manifest удалено пакетов {matches.Count}.");
            return true;
        }

        private static bool RestoreDisabledPackages()
        {
            var state = LoadDisabledState();
            if (state.Packages.Count == 0 || !File.Exists(ManifestPath))
                return false;

            string manifest = File.ReadAllText(ManifestPath);
            var missing = state.Packages
                .Where(entry => manifest.IndexOf($"\"{entry.Id}\"", StringComparison.Ordinal) < 0)
                .ToList();
            if (missing.Count == 0)
            {
                DeleteDisabledState();
                return false;
            }

            int dependenciesIndex = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            int objectStart = dependenciesIndex < 0 ? -1 : manifest.IndexOf('{', dependenciesIndex);
            int objectEnd = objectStart < 0 ? -1 : manifest.IndexOf('}', objectStart);
            if (objectStart < 0 || objectEnd < 0)
                throw new InvalidDataException("в Packages/manifest.json не найден объект dependencies");

            bool hasExistingDependencies = !string.IsNullOrWhiteSpace(
                manifest.Substring(objectStart + 1, objectEnd - objectStart - 1));
            var insertion = new StringBuilder();
            insertion.AppendLine();
            for (int i = 0; i < missing.Count; i++)
            {
                bool needsComma = hasExistingDependencies || i < missing.Count - 1;
                insertion.Append("        \"").Append(missing[i].Id).Append("\": \"")
                    .Append(missing[i].Version).Append('"');
                if (needsComma)
                    insertion.Append(',');
                insertion.AppendLine();
            }

            if (!BackupManifest(manifest))
                throw new IOException("не удалось создать резервную копию Packages/manifest.json");

            manifest = manifest.Insert(objectStart + 1, insertion.ToString());
            File.WriteAllText(ManifestPath, manifest);
            DeleteDisabledState();
            Debug.Log($"[AppLovinInstaller] AppLovin включён: восстановлено пакетов {missing.Count}.");
            return true;
        }

        private static DisabledPackageState LoadDisabledState()
        {
            if (!File.Exists(DisabledStatePath))
                return new DisabledPackageState();

            return JsonUtility.FromJson<DisabledPackageState>(File.ReadAllText(DisabledStatePath))
                   ?? new DisabledPackageState();
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
            Client.Resolve();
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

        private static async Task InstallPackageAsync(string packageId)
        {
            // Последняя защита: сюда не должен попадать запрещённый пакет ни при каких правках
            // вызывающего кода — иначе установщик тихо соберёт то, что потом не соберётся.
            var forbidden = ForbiddenAdNetworks.MatchByGroup(packageId);
            if (forbidden != null)
            {
                Debug.LogError($"[AppLovinInstaller] Отказ: «{forbidden.DisplayName}» в стоп-листе ({packageId}).");
                return;
            }

            // UPM понимает форму name@version; без неё Client.Add тянет latest.
            string requestSpec = PackageSpec(packageId);

            if (requestSpec != packageId)
                Debug.Log($"[AppLovinInstaller] {packageId}: версия закреплена ({requestSpec}) — см. PinnedVersions.");

            Debug.Log($"[AppLovinInstaller] Установка {requestSpec}...");
            var request = Client.Add(requestSpec);

            while (!request.IsCompleted)
                await Task.Delay(100);

            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[AppLovinInstaller] {request.Result.packageId} установлен.");
                return;
            }

            string error = request.Error != null ? request.Error.message : "неизвестная ошибка";
            Debug.LogError($"[AppLovinInstaller] Не удалось установить {packageId}: {error}");
        }

        #endregion
    }
}
