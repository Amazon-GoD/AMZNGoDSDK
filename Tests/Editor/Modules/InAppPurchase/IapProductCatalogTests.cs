using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// IapProductCatalog.Configure / TryResolve / CanBuy: which SKUs are known, long-lived and requested from the
    /// store, how disabled products, duplicates, blank ids and whitespace are handled, and that Configure
    /// rebuilds state from scratch. Receipt matching and product-data cache are not covered (Amazon DTOs).
    /// </summary>
    public class IapProductCatalogTests
    {
        private IapProductCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new IapProductCatalog();
        }

        private static SubscriptionProduct Sub(string id, int termDays = 7, int testMinutes = 0, bool enabled = true)
        {
            return new SubscriptionProduct
            {
                ProductId = id,
                TermDays = termDays,
                TestTermMinutes = testMinutes,
                Enabled = enabled,
            };
        }

        private static ConsumableProduct Consumable(string id, bool enabled = true)
        {
            return new ConsumableProduct { ProductId = id, Enabled = enabled };
        }

        private static NonConsumableProduct NonConsumable(string id, bool enabled = true)
        {
            return new NonConsumableProduct { ProductId = id, Enabled = enabled };
        }

        private static InAppPurchaseSettingData Settings()
        {
            return new InAppPurchaseSettingData();
        }

        [Test]
        public void Configure_NullSettings_LeavesCatalogEmpty()
        {
            _catalog.Configure(null);

            Assert.IsEmpty(_catalog.CatalogRequestSkus);
            Assert.IsEmpty(_catalog.LongLivedSkus);
            Assert.IsFalse(_catalog.TryResolve("anything", out _));
        }

        [Test]
        public void Configure_NullSettings_ClearsPreviousConfiguration()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub"));
            _catalog.Configure(settings);

            _catalog.Configure(null);

            Assert.IsFalse(_catalog.TryResolve("sub", out _));
            Assert.IsEmpty(_catalog.CatalogRequestSkus);
            Assert.IsEmpty(_catalog.LongLivedSkus);
        }

        [Test]
        public void Configure_EmptySettings_LeavesCatalogEmpty()
        {
            _catalog.Configure(Settings());

            Assert.IsEmpty(_catalog.CatalogRequestSkus);
            Assert.IsEmpty(_catalog.LongLivedSkus);
        }

        [Test]
        public void Configure_NullLists_AreTolerated()
        {
            var settings = Settings();
            settings.SubscriptionProducts = null;
            settings.ConsumableProducts = null;
            settings.NonConsumableProducts = null;

            Assert.DoesNotThrow(() => _catalog.Configure(settings));
            Assert.IsEmpty(_catalog.CatalogRequestSkus);
        }

        [Test]
        public void Configure_NullEntries_AreSkipped()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(null);
            settings.ConsumableProducts.Add(null);
            settings.NonConsumableProducts.Add(null);
            settings.SubscriptionProducts.Add(Sub("sub"));

            _catalog.Configure(settings);

            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "sub" }));
        }

        [Test]
        public void Configure_AllKinds_ResolvesWithCorrectKind()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub"));
            settings.ConsumableProducts.Add(Consumable("coins"));
            settings.NonConsumableProducts.Add(NonConsumable("no_ads"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub", out var sub));
            Assert.AreEqual(IapProductKind.Subscription, sub.Kind);
            Assert.IsTrue(_catalog.TryResolve("coins", out var coins));
            Assert.AreEqual(IapProductKind.Consumable, coins.Kind);
            Assert.IsTrue(_catalog.TryResolve("no_ads", out var noAds));
            Assert.AreEqual(IapProductKind.NonConsumable, noAds.Kind);
        }

        [Test]
        public void Configure_LongLivedSkus_ContainsSubscriptionsAndNonConsumablesOnly()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub"));
            settings.ConsumableProducts.Add(Consumable("coins"));
            settings.NonConsumableProducts.Add(NonConsumable("no_ads"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.LongLivedSkus.Contains("sub"));
            Assert.IsTrue(_catalog.LongLivedSkus.Contains("no_ads"));
            Assert.IsFalse(_catalog.LongLivedSkus.Contains("coins"));
            Assert.AreEqual(2, _catalog.LongLivedSkus.Count);
        }

        [Test]
        public void Configure_CatalogRequestSkus_FollowsKindOrderThenListOrder()
        {
            var settings = Settings();
            settings.NonConsumableProducts.Add(NonConsumable("no_ads"));
            settings.ConsumableProducts.Add(Consumable("coins"));
            settings.SubscriptionProducts.Add(Sub("sub_b"));
            settings.SubscriptionProducts.Add(Sub("sub_a"));

            _catalog.Configure(settings);

            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "sub_b", "sub_a", "coins", "no_ads" }));
        }

        [Test]
        public void Configure_DisabledProduct_IsKnownAndLongLivedButNotRequestedFromCatalog()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub_off", enabled: false));
            settings.SubscriptionProducts.Add(Sub("sub_on"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub_off", out var product));
            Assert.IsFalse(product.Enabled);
            Assert.IsTrue(_catalog.LongLivedSkus.Contains("sub_off"));
            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "sub_on" }));
        }

        [Test]
        public void Configure_BlankProductIds_AreSkipped()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub(null));
            settings.SubscriptionProducts.Add(Sub(""));
            settings.ConsumableProducts.Add(Consumable("   "));
            settings.NonConsumableProducts.Add(NonConsumable("\t"));

            _catalog.Configure(settings);

            Assert.IsEmpty(_catalog.CatalogRequestSkus);
            Assert.IsEmpty(_catalog.LongLivedSkus);
        }

        [Test]
        public void Configure_ProductIdWithSurroundingWhitespace_IsTrimmed()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("  sub_weekly \t"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub_weekly", out var product));
            Assert.AreEqual("sub_weekly", product.ProductId);
            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "sub_weekly" }));
            Assert.IsTrue(_catalog.CanBuy("sub_weekly"));
        }

        [Test]
        public void Configure_DuplicateIdSameKind_KeepsFirstEntry()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub", termDays: 7));
            settings.SubscriptionProducts.Add(Sub("sub", termDays: 30, enabled: false));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub", out var product));
            Assert.AreEqual(7, product.TermDays);
            Assert.IsTrue(product.Enabled);
            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "sub" }));
        }

        [Test]
        public void Configure_DuplicateIdAcrossKinds_KeepsFirstKindInConfigureOrder()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("shared"));
            settings.ConsumableProducts.Add(Consumable("shared"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("shared", out var product));
            Assert.AreEqual(IapProductKind.Subscription, product.Kind);
            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "shared" }));
        }

        [Test]
        public void Configure_DuplicateIdWithWhitespaceDifference_IsStillDuplicate()
        {
            var settings = Settings();
            settings.ConsumableProducts.Add(Consumable("coins"));
            settings.ConsumableProducts.Add(Consumable(" coins "));

            _catalog.Configure(settings);

            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "coins" }));
        }

        [Test]
        public void Configure_CalledTwice_RebuildsStateFromScratch()
        {
            var first = Settings();
            first.SubscriptionProducts.Add(Sub("old_sub"));
            first.ConsumableProducts.Add(Consumable("old_coins"));
            _catalog.Configure(first);

            var second = Settings();
            second.SubscriptionProducts.Add(Sub("new_sub"));
            _catalog.Configure(second);

            Assert.IsFalse(_catalog.TryResolve("old_sub", out _));
            Assert.IsFalse(_catalog.TryResolve("old_coins", out _));
            Assert.IsTrue(_catalog.TryResolve("new_sub", out _));
            Assert.That(_catalog.CatalogRequestSkus, Is.EqualTo(new[] { "new_sub" }));
            Assert.That(_catalog.LongLivedSkus.Count, Is.EqualTo(1));
        }

        [Test]
        public void Configure_Subscription_CopiesTermAndTestTerm()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub", termDays: 7, testMinutes: 10));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub", out var product));
            Assert.AreEqual(7, product.TermDays);
            Assert.AreEqual(10, product.TestTermMinutes);
        }

        [Test]
        public void Configure_ConsumableAndNonConsumable_HaveZeroTerm()
        {
            var settings = Settings();
            settings.ConsumableProducts.Add(Consumable("coins"));
            settings.NonConsumableProducts.Add(NonConsumable("no_ads"));

            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("coins", out var coins));
            Assert.AreEqual(0, coins.TermDays);
            Assert.AreEqual(0, coins.TestTermMinutes);
            Assert.IsTrue(_catalog.TryResolve("no_ads", out var noAds));
            Assert.AreEqual(0, noAds.TermDays);
        }

        [Test]
        public void EffectiveTermDays_WithoutTestTerm_IsTermDays()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub", termDays: 7));
            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub", out var product));
            Assert.AreEqual(7.0, product.EffectiveTermDays, 1e-9);
        }

        [Test]
        public void EffectiveTermDays_WithTestTerm_IsFractionalDays()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub", termDays: 7, testMinutes: 10));
            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("sub", out var product));
            Assert.AreEqual(10 / 1440.0, product.EffectiveTermDays, 1e-9);
        }

        [Test]
        public void TryResolve_NullEmptyOrUnknown_ReturnsFalseAndNullProduct()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("sub"));
            _catalog.Configure(settings);

            Assert.IsFalse(_catalog.TryResolve(null, out var forNull));
            Assert.IsNull(forNull);
            Assert.IsFalse(_catalog.TryResolve("", out var forEmpty));
            Assert.IsNull(forEmpty);
            Assert.IsFalse(_catalog.TryResolve("unknown", out var forUnknown));
            Assert.IsNull(forUnknown);
        }

        [Test]
        public void TryResolve_IsCaseSensitive()
        {
            var settings = Settings();
            settings.SubscriptionProducts.Add(Sub("Sub"));
            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.TryResolve("Sub", out _));
            Assert.IsFalse(_catalog.TryResolve("sub", out _));
        }

        [Test]
        public void CanBuy_EnabledProduct_IsTrue()
        {
            var settings = Settings();
            settings.ConsumableProducts.Add(Consumable("coins"));
            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.CanBuy("coins"));
        }

        [Test]
        public void CanBuy_DisabledProduct_IsFalse()
        {
            var settings = Settings();
            settings.NonConsumableProducts.Add(NonConsumable("no_ads", enabled: false));
            _catalog.Configure(settings);

            Assert.IsFalse(_catalog.CanBuy("no_ads"));
        }

        [Test]
        public void CanBuy_UnknownNullOrEmpty_IsFalse()
        {
            var settings = Settings();
            settings.ConsumableProducts.Add(Consumable("coins"));
            _catalog.Configure(settings);

            Assert.IsFalse(_catalog.CanBuy("unknown"));
            Assert.IsFalse(_catalog.CanBuy(null));
            Assert.IsFalse(_catalog.CanBuy(""));
        }

        [Test]
        public void CanBuy_DuplicateWithDisabledSecond_UsesFirstEnabledEntry()
        {
            var settings = Settings();
            settings.ConsumableProducts.Add(Consumable("coins", enabled: true));
            settings.ConsumableProducts.Add(Consumable("coins", enabled: false));
            _catalog.Configure(settings);

            Assert.IsTrue(_catalog.CanBuy("coins"));
        }
    }
}
