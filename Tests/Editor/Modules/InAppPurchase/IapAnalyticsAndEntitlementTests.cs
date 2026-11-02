using System;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// IapAnalytics.MapAmazonStatus (Amazon purchase status to funnel reason) and the PlayerPrefs-free part of
    /// IapEntitlementStore (fresh-store state, EvaluateGrace without a reconciliation anchor, ever-purchased flag).
    /// ApplySnapshot / ApplyLivePurchaseGrant / Load are not covered: they write PlayerPrefs through SaveIfDirty.
    /// </summary>
    public class IapAnalyticsAndEntitlementTests
    {
        [TestCase("FAILED", "store_failed")]
        [TestCase("INVALID_SKU", "invalid_sku")]
        [TestCase("NOT_SUPPORTED", "not_supported")]
        [TestCase("PENDING", "pending")]
        public void MapAmazonStatus_KnownStatus_MapsToFunnelReason(string status, string expected)
        {
            Assert.AreEqual(expected, IapAnalytics.MapAmazonStatus(status));
        }

        [TestCase("SUCCESSFUL")]
        [TestCase("ALREADY_PURCHASED")]
        [TestCase("failed")]
        [TestCase("")]
        [TestCase("SOMETHING_NEW")]
        public void MapAmazonStatus_OtherStatus_MapsToUnknown(string status)
        {
            Assert.AreEqual("unknown", IapAnalytics.MapAmazonStatus(status));
        }

        [Test]
        public void MapAmazonStatus_Null_MapsToUnknown()
        {
            Assert.AreEqual("unknown", IapAnalytics.MapAmazonStatus(null));
        }

        [Test]
        public void EntitlementStore_FreshStore_HasNoKnowledgeAndNoAccess()
        {
            var store = new IapEntitlementStore();

            Assert.IsFalse(store.ReconciledThisSession);
            Assert.AreEqual(DateTime.MinValue, store.ReconciledAtUtc);
            Assert.AreEqual(IapEntitlementState.Unknown, store.GetState("sub"));
            Assert.IsFalse(store.GetEffectiveAccess("sub"));
            Assert.IsFalse(store.GetEffectiveAccess(null));
        }

        [Test]
        public void EvaluateGrace_NoReconciliationAnchor_RevokesNothing()
        {
            var store = new IapEntitlementStore();

            var revoked = store.EvaluateGrace(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), sku => 7);

            Assert.IsEmpty(revoked);
        }

        [Test]
        public void EvaluateGrace_NoReconciliationAnchor_NullTermResolver_DoesNotThrow()
        {
            var store = new IapEntitlementStore();

            Assert.DoesNotThrow(() => store.EvaluateGrace(DateTime.MaxValue, null));
        }

        [Test]
        public void EntitlementStore_MarkEverPurchased_IsTrackedPerSku()
        {
            var store = new IapEntitlementStore();

            store.MarkEverPurchased("sub");

            Assert.IsTrue(store.HasEverPurchased("sub"));
            Assert.IsFalse(store.HasEverPurchased("other"));
        }

        [Test]
        public void EntitlementStore_MarkEverPurchased_NullOrEmpty_IsIgnored()
        {
            var store = new IapEntitlementStore();

            store.MarkEverPurchased(null);
            store.MarkEverPurchased("");

            Assert.IsFalse(store.HasEverPurchased(null));
            Assert.IsFalse(store.HasEverPurchased(""));
        }
    }
}
