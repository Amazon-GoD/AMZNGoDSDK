using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            if (!EditorUtility.DisplayDialog(
                    "AppLovin MAX",
                    $"Будет прописан scoped registry {RegistryUrl} в {ManifestPath} " +
                    $"и установлен пакет {MaxPluginPackageId}.\n\nПродолжить?",
                    "Установить", "Отмена"))
                return;

            InstallMaxPluginAsync().ConfigureAwait(false);
        }

        [MenuItem("AMZN GoD/AppLovin/Install Allowed Adapters", false, 301)]
        public static void InstallAllowedAdaptersMenu()
        {
            var adapters = AllowedAdapterPackageIds();
            var blocked = BlockedNetworkNames();

            if (!EditorUtility.DisplayDialog(
                    "AppLovin MAX",
                    $"Будет установлено адаптеров: {adapters.Count}.\n\n" +
                    $"Исключены по стоп-листу: {(blocked.Count == 0 ? "—" : string.Join(", ", blocked))}\n\n" +
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

            Debug.Log($"[AppLovinInstaller] Установка {packageId}...");
            var request = Client.Add(packageId);

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
