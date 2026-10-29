using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Ассет картинок баннеров подписки (<see cref="SubscriptionBannerSettings"/>).
    ///
    /// Окно SDK Settings правит плейсменты в редакторской модели вместе с остальными
    /// настройками, а Save Settings пишет их сюда: в JSON-конфиге ссылкам на спрайты не место.
    /// Ассет один, меняется только его папка: пока модуль IAP включён — generated Resources
    /// (рантайм грузит его оттуда), выключен — папка вне Resources, чтобы спрайты не попали в
    /// билд (см. <see cref="DisabledModuleBuildGuard"/>), а настройки не потерялись.
    /// </summary>
    public static class SubscriptionBannerSettingsStore
    {
        public const string ResourcesAssetPath = "Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK/SubscriptionBanners.asset";
        public const string ParkedAssetPath = "Assets/AMZNGoDSDKGenerated/SubscriptionBanners/SubscriptionBanners.asset";

        private const string ParkedFolder = "Assets/AMZNGoDSDKGenerated/SubscriptionBanners";

        public static SubscriptionBannerSettings Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SubscriptionBannerSettings>(ResourcesAssetPath);
            if (asset != null)
                return asset;

            return AssetDatabase.LoadAssetAtPath<SubscriptionBannerSettings>(ParkedAssetPath);
        }

        /// <summary>Заполняет плейсменты подписок редакторской модели из ассета.</summary>
        public static void ReadInto(InAppPurchaseSettingData settings)
        {
            if (settings?.SubscriptionProducts == null)
                return;

            SubscriptionBannerSettings asset = Load();
            foreach (var product in settings.SubscriptionProducts)
            {
                SubscriptionBannerProduct entry = asset != null ? asset.Find(product.ProductId) : null;
                product.BannerPlacements = entry?.Placements == null
                    ? new List<SubscriptionBannerPlacement>()
                    : entry.Placements.Where(p => p != null).Select(p => p.Clone()).ToList();
                EnsureDefaultPlacement(product.BannerPlacements);
            }
        }

        /// <summary>Первый плейсмент всегда есть и всегда называется «default».</summary>
        public static void EnsureDefaultPlacement(List<SubscriptionBannerPlacement> placements)
        {
            if (placements.Count == 0)
                placements.Add(new SubscriptionBannerPlacement());

            placements[0].Name = SubscriptionBannerSettings.DefaultPlacementName;
        }

        /// <summary>Имена плейсментов у каждой подписки непустые и не повторяются.</summary>
        public static bool Validate(InAppPurchaseSettingData settings, out string message)
        {
            var problems = new List<string>();
            foreach (var product in settings.SubscriptionProducts)
            {
                if (string.IsNullOrWhiteSpace(product.ProductId) || product.BannerPlacements == null)
                    continue;

                string problem = DescribePlacementProblem(product.BannerPlacements);
                if (problem != null)
                    problems.Add($"{product.ProductId.Trim()}: {problem}");
            }

            if (problems.Count > 0)
            {
                message = "Невозможно сохранить: плейсменты баннера подписки — " + string.Join("; ", problems) + ".";
                return false;
            }

            message = string.Empty;
            return true;
        }

        /// <summary>Что не так с именами плейсментов одной подписки; null — всё в порядке.</summary>
        public static string DescribePlacementProblem(List<SubscriptionBannerPlacement> placements)
        {
            // Первый — «default», его имя проставляется само.
            if (placements.Skip(1).Any(p => string.IsNullOrWhiteSpace(p.Name)))
                return "у дополнительного плейсмента не задано имя";

            string[] duplicates = placements
                .Select((p, i) => i == 0 ? SubscriptionBannerSettings.DefaultPlacementName : p.Name.Trim())
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            return duplicates.Length > 0
                ? "повторяются имена плейсментов (" + string.Join(", ", duplicates) + ")"
                : null;
        }

        /// <summary>
        /// Пишет плейсменты всех подписок в ассет; записи удалённых подписок уходят вместе с ними.
        /// Ассета ещё нет, а картинок не задано — ничего не создаёт.
        /// </summary>
        public static void Save(InAppPurchaseSettingData settings, bool moduleEnabled)
        {
            var products = settings.SubscriptionProducts
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductId))
                .ToList();

            foreach (var product in products)
            {
                product.BannerPlacements ??= new List<SubscriptionBannerPlacement>();
                EnsureDefaultPlacement(product.BannerPlacements);
            }

            SubscriptionBannerSettings asset = Load();
            if (asset == null)
            {
                bool configured = products.Any(p => p.BannerPlacements.Count > 1
                                                    || p.BannerPlacements.Any(x => x.Horizontal != null || x.Vertical != null));
                if (!configured)
                    return;

                string path = moduleEnabled ? ResourcesAssetPath : ParkedAssetPath;
                ModuleResourceSynchronizer.EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                asset = ScriptableObject.CreateInstance<SubscriptionBannerSettings>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.Products = products
                .Select(p => new SubscriptionBannerProduct
                {
                    ProductId = p.ProductId.Trim(),
                    Placements = p.BannerPlacements.Select(x =>
                    {
                        var copy = x.Clone();
                        copy.Name = copy.Name?.Trim();
                        return copy;
                    }).ToList(),
                })
                .ToList();

            // Только этот ассет: SaveAssets заодно записал бы чужие несохранённые правки проекта.
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        /// <summary>
        /// Кладёт ассет в Resources при включённом модуле и уносит из Resources при выключенном.
        /// Перемещение сохраняет GUID, поэтому ссылки на спрайты не рвутся.
        /// </summary>
        public static void SyncLocation(bool moduleEnabled)
        {
            string from = moduleEnabled ? ParkedAssetPath : ResourcesAssetPath;
            string to = moduleEnabled ? ResourcesAssetPath : ParkedAssetPath;

            if (AssetDatabase.LoadMainAssetAtPath(from) == null)
                return;

            if (AssetDatabase.LoadMainAssetAtPath(to) != null)
            {
                Debug.LogWarning($"[AMZN GoD SDK] Есть два ассета баннеров подписки: {from} и {to}. " +
                                 $"Используется {to}; лишний удали вручную.");
                return;
            }

            ModuleResourceSynchronizer.EnsureFolder(to.Substring(0, to.LastIndexOf('/')));
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[AMZN GoD SDK] Не удалось перенести настройки баннеров подписки в {to}: {error}");
                return;
            }

            Debug.Log($"[AMZN GoD SDK] Настройки баннеров подписки перенесены: {to}");

            if (moduleEnabled)
                ModuleResourceSynchronizer.DeleteFolderIfEmpty(ParkedFolder);
        }

        /// <summary>
        /// Какие ориентации разрешены в Player Settings — для них окно показывает поля картинок.
        /// Какой префаб показать, рантайм решает по экрану устройства.
        /// </summary>
        public static void GetProjectOrientations(out bool horizontal, out bool vertical)
        {
            switch (PlayerSettings.defaultInterfaceOrientation)
            {
                case UIOrientation.Portrait:
                case UIOrientation.PortraitUpsideDown:
                    horizontal = false;
                    vertical = true;
                    return;

                case UIOrientation.LandscapeLeft:
                case UIOrientation.LandscapeRight:
                    horizontal = true;
                    vertical = false;
                    return;

                default:
                    horizontal = PlayerSettings.allowedAutorotateToLandscapeLeft
                                 || PlayerSettings.allowedAutorotateToLandscapeRight;
                    vertical = PlayerSettings.allowedAutorotateToPortrait
                               || PlayerSettings.allowedAutorotateToPortraitUpsideDown;

                    if (!horizontal && !vertical)
                        horizontal = vertical = true;
                    return;
            }
        }
    }
}
