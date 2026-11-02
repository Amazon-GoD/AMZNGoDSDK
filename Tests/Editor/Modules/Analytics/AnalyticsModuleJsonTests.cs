using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// AnalyticsModule static payload builders: the JSON must always be parseable (regression 1.0.9:
    /// NaN/Infinity revenue produced a bare literal and invalid JSON), optional fields appear only
    /// when set, strings are escaped, plus the small HTTP/placement predicates.
    /// </summary>
    public class AnalyticsModuleJsonTests
    {
        [Serializable]
        private class EventPayload
        {
            public string event_name;
            public string event_id;
            public string app_id;
            public string app_type;
            public string paid_app_id;
            public string donor_app_id;
            public string device_id_hash;
            public string console_id;
            public string network;
            public string ad_unit;
            public string placement;
            public double revenue;
            public string revenue_precision;
            public int n;
            public string sdk_version;
            public string app_version;
            public long ts;
        }

        private static EventPayload Parse(string json)
        {
            Assert.IsNotNull(json);
            Assert.IsTrue(json.StartsWith("{") && json.EndsWith("}"), json);
            var payload = JsonUtility.FromJson<EventPayload>(json);
            Assert.IsNotNull(payload, json);
            return payload;
        }

        // ---------- BuildMediationEventJson ----------

        [Test]
        public void BuildMediationEventJson_AllFields_RoundTrip()
        {
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", "AppLovin", "unit-1", "banner", 0.0123, "exact",
                "com.test.app", "hash1", 1700000000123L, "evt1");

            var p = Parse(json);

            Assert.AreEqual("mediation_impression", p.event_name);
            Assert.AreEqual("evt1", p.event_id);
            Assert.AreEqual("com.test.app", p.app_id);
            Assert.AreEqual("hash1", p.device_id_hash);
            Assert.AreEqual("AppLovin", p.network);
            Assert.AreEqual("unit-1", p.ad_unit);
            Assert.AreEqual("banner", p.placement);
            Assert.AreEqual(0.0123, p.revenue, 1e-9);
            Assert.AreEqual("exact", p.revenue_precision);
            Assert.AreEqual(SdkPackageVersion.Value, p.sdk_version);
            Assert.AreEqual(Application.version, p.app_version);
            Assert.AreEqual(1700000000123L, p.ts);
            Assert.AreEqual(0, p.n);
            Assert.That(json, Does.Contain("\"revenue\":0.0123,"));
        }

        // Regression 1.0.9: NaN/Infinity revenue serialized as a bare literal and broke the JSON.
        [Test]
        public void BuildMediationEventJson_NonFiniteRevenue_BecomesMinusOne()
        {
            double[] values = { double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            foreach (double revenue in values)
            {
                string json = AnalyticsModule.BuildMediationEventJson(
                    "mediation_impression", "net", "unit", "banner", revenue, "exact", "app", "hash", 1L, "evt");

                Assert.That(json, Does.Contain("\"revenue\":-1,"), json);
                Assert.That(json, Does.Not.Contain("NaN"), json);
                Assert.That(json, Does.Not.Contain("Infinity"), json);
                Assert.AreEqual(-1d, Parse(json).revenue, 0d);
            }
        }

        [Test]
        public void BuildMediationEventJson_UnknownRevenueMinusOne_IsPreserved()
        {
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", "net", "unit", "banner", -1d, "unknown", "app", "hash", 1L, "evt");
            Assert.AreEqual(-1d, Parse(json).revenue, 0d);
        }

        [Test]
        public void BuildMediationEventJson_CountPositive_AddsNField()
        {
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", "net", "unit", "banner", 0.5, "exact", "app", "hash", 5L, "evt", 7);

            Assert.That(json, Does.Contain("\"n\":7,"));
            Assert.AreEqual(7, Parse(json).n);
        }

        [Test]
        public void BuildMediationEventJson_CountZero_OmitsNField()
        {
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", "net", "unit", "banner", 0.5, "exact", "app", "hash", 5L, "evt");

            Assert.That(json, Does.Not.Contain("\"n\":"));
        }

        [Test]
        public void BuildMediationEventJson_NullStrings_AreEmptyAndStillValid()
        {
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", null, null, null, 0.5, null, "app", "hash", 5L, "evt");

            var p = Parse(json);
            Assert.AreEqual(string.Empty, p.network);
            Assert.AreEqual(string.Empty, p.ad_unit);
            Assert.AreEqual(string.Empty, p.placement);
            Assert.AreEqual(string.Empty, p.revenue_precision);
        }

        [Test]
        public void BuildMediationEventJson_SpecialCharacters_AreEscaped()
        {
            string network = "a\"b\\c\nd\te";
            string json = AnalyticsModule.BuildMediationEventJson(
                "mediation_impression", network, "unit", "banner", 0.5, "exact", "app", "hash", 5L, "evt");

            Assert.AreEqual(network, Parse(json).network);
        }

        [Test]
        public void BuildMediationEventJson_CommaDecimalCulture_StillUsesDot()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            CultureInfo comma;
            try
            {
                comma = new CultureInfo("de-DE");
            }
            catch (CultureNotFoundException)
            {
                Assert.Ignore("de-DE culture is not available on this machine.");
                return;
            }

            try
            {
                Thread.CurrentThread.CurrentCulture = comma;
                string json = AnalyticsModule.BuildMediationEventJson(
                    "mediation_impression", "net", "unit", "banner", 0.25, "exact", "app", "hash", 123456L, "evt", 3);

                Assert.That(json, Does.Contain("\"revenue\":0.25,"));
                Assert.That(json, Does.Contain("\"n\":3,"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ---------- BuildCrossPromoEventJson ----------

        [Test]
        public void BuildCrossPromoEventJson_AllFields_RoundTrip()
        {
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_click", "paid.app", "donor.app", "hash1", 1700000000999L, "evt2", "banner");

            var p = Parse(json);

            Assert.AreEqual("cp_click", p.event_name);
            Assert.AreEqual("evt2", p.event_id);
            Assert.AreEqual("paid.app", p.paid_app_id);
            Assert.AreEqual("donor.app", p.donor_app_id);
            Assert.AreEqual("hash1", p.device_id_hash);
            Assert.AreEqual("banner", p.placement);
            Assert.AreEqual(SdkPackageVersion.Value, p.sdk_version);
            Assert.AreEqual(Application.version, p.app_version);
            Assert.AreEqual(1700000000999L, p.ts);
            Assert.AreEqual(0, p.n);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void BuildCrossPromoEventJson_BlankPlacement_OmitsPlacementField(string placement)
        {
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_click", "paid.app", "donor.app", "hash1", 1L, "evt2", placement);

            Assert.That(json, Does.Not.Contain("\"placement\""));
            // JsonUtility leaves a missing field without initializer as null.
            Assert.That(Parse(json).placement, Is.Null.Or.Empty);
        }

        [Test]
        public void BuildCrossPromoEventJson_Count_AddsNField()
        {
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_impression", "paid.app", "donor.app", "hash1", 1L, "evt2", "banner", 12);

            Assert.That(json, Does.Contain("\"n\":12,"));
            Assert.AreEqual(12, Parse(json).n);
        }

        [Test]
        public void BuildCrossPromoEventJson_NegativeCount_OmitsNField()
        {
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_impression", "paid.app", "donor.app", "hash1", 1L, "evt2", "banner", -1);

            Assert.That(json, Does.Not.Contain("\"n\":"));
        }

        [Test]
        public void BuildCrossPromoEventJson_TimestampIsLastField()
        {
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_impression", "paid.app", "donor.app", "hash1", 42L, "evt2", "banner", 2);

            Assert.That(json, Does.EndWith("\"ts\":42}"));
        }

        [Test]
        public void BuildCrossPromoEventJson_SpecialCharacters_AreEscaped()
        {
            string paid = "paid\"app\\x";
            string json = AnalyticsModule.BuildCrossPromoEventJson(
                "cp_click", paid, "donor", "hash", 1L, "evt", "banner");

            Assert.AreEqual(paid, Parse(json).paid_app_id);
        }

        // ---------- BuildFirstOpenJson ----------

        [Test]
        public void BuildFirstOpenJson_WithClaimFields_IsValidJson()
        {
            string claim = "\"console_id\":\"console-1\",";
            string json = AnalyticsModule.BuildFirstOpenJson(
                "first_open", "com.test.app", "fire", "hash1", 1700000000000L, "evt3", claim);

            var p = Parse(json);

            Assert.AreEqual("first_open", p.event_name);
            Assert.AreEqual("evt3", p.event_id);
            Assert.AreEqual("com.test.app", p.app_id);
            Assert.AreEqual("fire", p.app_type);
            Assert.AreEqual("hash1", p.device_id_hash);
            Assert.AreEqual("console-1", p.console_id);
            Assert.AreEqual(SdkPackageVersion.Value, p.sdk_version);
            Assert.AreEqual(1700000000000L, p.ts);
        }

        [Test]
        public void BuildFirstOpenJson_EmptyClaimFields_IsStillValidJson()
        {
            string json = AnalyticsModule.BuildFirstOpenJson(
                "first_open", "com.test.app", "fire", "hash1", 9L, "evt3", string.Empty);

            var p = Parse(json);
            Assert.AreEqual("first_open", p.event_name);
            Assert.AreEqual(9L, p.ts);
            Assert.That(p.console_id, Is.Null.Or.Empty);
        }

        // ---------- IsTransientHttpError ----------

        [TestCase(401L)]
        [TestCase(403L)]
        [TestCase(408L)]
        [TestCase(429L)]
        public void IsTransientHttpError_RetryableCodes_ReturnTrue(long code)
        {
            Assert.IsTrue(AnalyticsModule.IsTransientHttpError(code));
        }

        [TestCase(0L)]
        [TestCase(200L)]
        [TestCase(400L)]
        [TestCase(404L)]
        [TestCase(409L)]
        [TestCase(500L)]
        [TestCase(503L)]
        public void IsTransientHttpError_OtherCodes_ReturnFalse(long code)
        {
            Assert.IsFalse(AnalyticsModule.IsTransientHttpError(code));
        }

        // ---------- IsBannerPlacement ----------

        [TestCase("banner")]
        [TestCase("BANNER")]
        [TestCase("Banner")]
        [TestCase("  banner  ")]
        public void IsBannerPlacement_BannerVariants_ReturnTrue(string placement)
        {
            Assert.IsTrue(AnalyticsModule.IsBannerPlacement(placement));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("interstitial")]
        [TestCase("banners")]
        [TestCase("ban ner")]
        public void IsBannerPlacement_Other_ReturnFalse(string placement)
        {
            Assert.IsFalse(AnalyticsModule.IsBannerPlacement(placement));
        }
    }
}
