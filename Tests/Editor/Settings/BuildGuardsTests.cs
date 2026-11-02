using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Pre-build guards on in-memory configs: <see cref="AnalyticsAppTypeBuildGuard.GetBuildError"/>,
    /// <see cref="IapSubscriptionTermBuildGuard.GetBuildError"/>, and the Save Settings validation of IAP
    /// products and subscription banner placements they mirror.
    /// </summary>
    public class BuildGuardsTests
    {
        private static Runtime.SdkSettingsData AnalyticsSettings(Runtime.AnalyticsAppType appType) => new Runtime.SdkSettingsData
        {
            Enabled = true,
            Analytics = new Runtime.AnalyticsSettingData { Enabled = true, AppType = appType }
        };

        private static Runtime.SdkSettingsData IapSettings(params Runtime.SubscriptionProduct[] subscriptions)
        {
            var settings = new Runtime.SdkSettingsData
            {
                Enabled = true,
                InAppPurchase = new Runtime.InAppPurchaseSettingData { Enabled = true }
            };
            settings.InAppPurchase.SubscriptionProducts.AddRange(subscriptions);
            return settings;
        }

        private static InAppPurchaseSettingData EditorIap() => new InAppPurchaseSettingData
        {
            Enabled = true,
            SubscriptionProducts = new List<SubscriptionProduct>
            {
                new SubscriptionProduct { ProductId = "sub.weekly", TermDays = 7 }
            },
            ConsumableProducts = new List<ConsumableProduct>
            {
                new ConsumableProduct { ProductId = "coins.100" }
            },
            NonConsumableProducts = new List<NonConsumableProduct>
            {
                new NonConsumableProduct { ProductId = "no.ads" }
            }
        };

        [Test]
        public void AppType_None_BlocksBuild()
        {
            string error = AnalyticsAppTypeBuildGuard.GetBuildError(AnalyticsSettings(Runtime.AnalyticsAppType.None));

            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("не выбран App Type"));
        }

        [TestCase(Runtime.AnalyticsAppType.Free)]
        [TestCase(Runtime.AnalyticsAppType.Paid)]
        public void AppType_Selected_Passes(Runtime.AnalyticsAppType appType)
        {
            Assert.IsNull(AnalyticsAppTypeBuildGuard.GetBuildError(AnalyticsSettings(appType)));
        }

        [Test]
        public void AppType_NotCheckedWithoutConfigOrWhenDisabled()
        {
            var sdkDisabled = AnalyticsSettings(Runtime.AnalyticsAppType.None);
            sdkDisabled.Enabled = false;
            var analyticsDisabled = AnalyticsSettings(Runtime.AnalyticsAppType.None);
            analyticsDisabled.Analytics.Enabled = false;
            var noBlock = AnalyticsSettings(Runtime.AnalyticsAppType.None);
            noBlock.Analytics = null;

            Assert.IsNull(AnalyticsAppTypeBuildGuard.GetBuildError(null));
            Assert.IsNull(AnalyticsAppTypeBuildGuard.GetBuildError(sdkDisabled));
            Assert.IsNull(AnalyticsAppTypeBuildGuard.GetBuildError(analyticsDisabled));
            Assert.IsNull(AnalyticsAppTypeBuildGuard.GetBuildError(noBlock));
        }

        // IAP-15: an enabled subscription without a term must not reach a build.
        [Test]
        public void SubscriptionTerm_MissingOnEnabledSubscriptions_ListsThemTrimmed()
        {
            var settings = IapSettings(
                new Runtime.SubscriptionProduct { ProductId = " sub.weekly ", TermDays = 0 },
                new Runtime.SubscriptionProduct { ProductId = "sub.monthly", TermDays = 30 },
                new Runtime.SubscriptionProduct { ProductId = "sub.yearly", TermDays = -1 });

            string error = IapSubscriptionTermBuildGuard.GetBuildError(settings);

            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("не задан срок периода"));
            Assert.That(error, Does.Contain("sub.weekly, sub.yearly"));
            Assert.That(error, Does.Not.Contain("sub.monthly"));
        }

        [Test]
        public void SubscriptionTerm_IgnoresDisabledEmptyAndNullProducts()
        {
            var settings = IapSettings(
                new Runtime.SubscriptionProduct { ProductId = "sub.off", TermDays = 0, Enabled = false },
                new Runtime.SubscriptionProduct { ProductId = "  ", TermDays = 0 },
                null,
                new Runtime.SubscriptionProduct { ProductId = "sub.weekly", TermDays = 7 });

            Assert.IsNull(IapSubscriptionTermBuildGuard.GetBuildError(settings));
        }

        [Test]
        public void SubscriptionTerm_NotCheckedWithoutConfigOrWhenDisabled()
        {
            var missing = new Runtime.SubscriptionProduct { ProductId = "sub.weekly", TermDays = 0 };
            var sdkDisabled = IapSettings(missing);
            sdkDisabled.Enabled = false;
            var moduleDisabled = IapSettings(missing);
            moduleDisabled.InAppPurchase.Enabled = false;

            Assert.IsNull(IapSubscriptionTermBuildGuard.GetBuildError(null));
            Assert.IsNull(IapSubscriptionTermBuildGuard.GetBuildError(sdkDisabled));
            Assert.IsNull(IapSubscriptionTermBuildGuard.GetBuildError(moduleDisabled));
        }

        [Test]
        public void ProductIds_ValidCatalog_Passes()
        {
            Assert.IsTrue(SdkSettingsManager.ValidateInAppPurchaseProductIds(EditorIap(), out string message), message);
        }

        [Test]
        public void ProductIds_DuplicateAcrossListsIgnoringCaseAndSpaces_Fails()
        {
            var settings = EditorIap();
            settings.NonConsumableProducts.Add(new NonConsumableProduct { ProductId = " COINS.100 " });

            Assert.IsFalse(SdkSettingsManager.ValidateInAppPurchaseProductIds(settings, out string message));
            Assert.That(message, Does.Contain("повторяются идентификаторы продуктов"));
            Assert.That(message, Does.Contain("coins.100").IgnoreCase);
        }

        [Test]
        public void ProductIds_EmptyIdsAreNotDuplicates()
        {
            var settings = EditorIap();
            settings.ConsumableProducts.Add(new ConsumableProduct { ProductId = "" });
            settings.ConsumableProducts.Add(new ConsumableProduct { ProductId = null });

            Assert.IsTrue(SdkSettingsManager.ValidateInAppPurchaseProductIds(settings, out string message), message);
        }

        [Test]
        public void ProductIds_EnabledSubscriptionWithoutTerm_Fails()
        {
            var settings = EditorIap();
            settings.SubscriptionProducts.Add(new SubscriptionProduct { ProductId = "sub.monthly", TermDays = 0 });

            Assert.IsFalse(SdkSettingsManager.ValidateInAppPurchaseProductIds(settings, out string message));
            Assert.That(message, Does.Contain("не задан срок периода"));
            Assert.That(message, Does.Contain("sub.monthly"));
        }

        [Test]
        public void ProductIds_DisabledSubscriptionWithoutTerm_Passes()
        {
            var settings = EditorIap();
            settings.SubscriptionProducts.Add(new SubscriptionProduct { ProductId = "sub.monthly", TermDays = 0, Enabled = false });

            Assert.IsTrue(SdkSettingsManager.ValidateInAppPurchaseProductIds(settings, out string message), message);
        }

        [Test]
        public void ProductIds_DuplicateBannerPlacements_Fails()
        {
            var settings = EditorIap();
            settings.SubscriptionProducts[0].BannerPlacements = new List<Runtime.SubscriptionBannerPlacement>
            {
                new Runtime.SubscriptionBannerPlacement { Name = "default" },
                new Runtime.SubscriptionBannerPlacement { Name = "shop" },
                new Runtime.SubscriptionBannerPlacement { Name = " Shop " }
            };

            Assert.IsFalse(SdkSettingsManager.ValidateInAppPurchaseProductIds(settings, out string message));
            Assert.That(message, Does.Contain("sub.weekly"));
            Assert.That(message, Does.Contain("повторяются имена плейсментов"));
        }

        [Test]
        public void Placements_UnnamedExtraPlacement_Reported()
        {
            var placements = new List<Runtime.SubscriptionBannerPlacement>
            {
                new Runtime.SubscriptionBannerPlacement { Name = "default" },
                new Runtime.SubscriptionBannerPlacement { Name = "  " }
            };

            Assert.That(SubscriptionBannerSettingsStore.DescribePlacementProblem(placements),
                Does.Contain("не задано имя"));
        }

        [Test]
        public void Placements_FirstIsAlwaysDefault_AndClashesWithExplicitDefault()
        {
            // The first placement is treated as "default" whatever its stored name.
            var placements = new List<Runtime.SubscriptionBannerPlacement>
            {
                new Runtime.SubscriptionBannerPlacement { Name = "renamed" },
                new Runtime.SubscriptionBannerPlacement { Name = "DEFAULT" }
            };

            Assert.That(SubscriptionBannerSettingsStore.DescribePlacementProblem(placements),
                Does.Contain("повторяются имена плейсментов (default)"));
        }

        [Test]
        public void Placements_UniqueNames_NoProblem()
        {
            var placements = new List<Runtime.SubscriptionBannerPlacement>
            {
                new Runtime.SubscriptionBannerPlacement { Name = null },
                new Runtime.SubscriptionBannerPlacement { Name = "shop" },
                new Runtime.SubscriptionBannerPlacement { Name = "level_end" }
            };

            Assert.IsNull(SubscriptionBannerSettingsStore.DescribePlacementProblem(placements));
            Assert.IsNull(SubscriptionBannerSettingsStore.DescribePlacementProblem(new List<Runtime.SubscriptionBannerPlacement>()));
        }

        [Test]
        public void EnsureDefaultPlacement_AddsOrRenamesFirst()
        {
            var empty = new List<Runtime.SubscriptionBannerPlacement>();
            SubscriptionBannerSettingsStore.EnsureDefaultPlacement(empty);

            Assert.That(empty, Has.Count.EqualTo(1));
            Assert.AreEqual(Runtime.SubscriptionBannerSettings.DefaultPlacementName, empty[0].Name);

            var named = new List<Runtime.SubscriptionBannerPlacement>
            {
                new Runtime.SubscriptionBannerPlacement { Name = "custom" },
                new Runtime.SubscriptionBannerPlacement { Name = "shop" }
            };
            SubscriptionBannerSettingsStore.EnsureDefaultPlacement(named);

            Assert.That(named, Has.Count.EqualTo(2));
            Assert.AreEqual(Runtime.SubscriptionBannerSettings.DefaultPlacementName, named[0].Name);
            Assert.AreEqual("shop", named[1].Name);
        }
    }
}
