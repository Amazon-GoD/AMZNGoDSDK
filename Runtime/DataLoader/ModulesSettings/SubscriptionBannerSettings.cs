using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Картинки баннеров подписки по плейсментам (<c>AmznGoDSDKCore.ShowSubBanner</c>).
    ///
    /// Хранится отдельным ассетом, а не в JSON-конфиге: JsonUtility не держит ссылок на спрайты.
    /// Ассет заполняет окно SDK Settings (In-App Purchase → подписка → Subscription Banner) по
    /// Save Settings. Пока модуль IAP включён, ассет лежит в generated Resources и грузится по
    /// <see cref="ResourcesPath"/>; при выключенном модуле редактор уносит его из Resources,
    /// чтобы спрайты не попадали в билд, — настройки при этом не теряются.
    ///
    /// Тип живёт в общей сборке, а не в сборке модуля IAP: при выключенном модуле ассет не должен
    /// превращаться в Missing Script.
    /// </summary>
    public sealed class SubscriptionBannerSettings : ScriptableObject
    {
        public const string ResourcesPath = "AMZNGoDSDK/SubscriptionBanners";

        /// <summary>Имя плейсмента по умолчанию — он всегда первый в списке и не удаляется.</summary>
        public const string DefaultPlacementName = "default";

        public List<SubscriptionBannerProduct> Products = new();

        /// <summary>Настройки баннера подписки. SKU сравнивается как в каталоге модуля: без пробелов по краям, с учётом регистра.</summary>
        public SubscriptionBannerProduct Find(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId) || Products == null)
                return null;

            productId = productId.Trim();
            foreach (var product in Products)
            {
                if (product != null && string.Equals(product.ProductId?.Trim(), productId, StringComparison.Ordinal))
                    return product;
            }

            return null;
        }
    }

    [Serializable]
    public sealed class SubscriptionBannerProduct
    {
        public string ProductId;

        /// <summary>Плейсменты в порядке ротации. Первый — плейсмент по умолчанию.</summary>
        public List<SubscriptionBannerPlacement> Placements = new();

        /// <summary>Плейсмент по имени, без учёта регистра. null — такого нет.</summary>
        public SubscriptionBannerPlacement FindPlacement(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Placements == null)
                return null;

            name = name.Trim();
            foreach (var placement in Placements)
            {
                if (placement != null && string.Equals(placement.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return placement;
            }

            return null;
        }
    }

    [Serializable]
    public sealed class SubscriptionBannerPlacement
    {
        public string Name;

        [Tooltip("Задник для горизонтального экрана (префаб SubscriptionBanner (Horizontal)).")]
        public Sprite Horizontal;

        [Tooltip("Задник для вертикального экрана (префаб SubscriptionBanner (Vertical)).")]
        public Sprite Vertical;

        /// <summary>
        /// Задник под ориентацию экрана. Если картинка нужной ориентации не задана, берётся
        /// другая: растянутая картинка лучше пустого белого экрана.
        /// </summary>
        public Sprite GetBackground(bool horizontal)
        {
            Sprite preferred = horizontal ? Horizontal : Vertical;
            Sprite fallback = horizontal ? Vertical : Horizontal;
            return preferred != null ? preferred : fallback;
        }

        public SubscriptionBannerPlacement Clone() => new()
        {
            Name = Name,
            Horizontal = Horizontal,
            Vertical = Vertical,
        };
    }
}
