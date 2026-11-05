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

        [Serializable]
        private class FirstOpenPayload
        {
            public string event_name;
            public string event_id;
            public string app_id;
            public string app_type;
            public string device_id_hash;
            public string console_id;
            public string adjust_app_token;
            public bool is_debug;
            public string installer;
            public string firebase_project_id;
            public string firebase_app_id;
            public string appmetrica_key_fp;
            public string applovin_google_app_id;
            public string applovin_inter_unit;
            public string applovin_reward_unit;
            public string applovin_banner_unit;
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

        // SDK 1.0.11: the game profile goes right after the 1.0.10 claim fields, before the
        // version fields, the way TrySendFirstOpen concatenates them. Fake values only (public repo).
        [Test]
        public void BuildFirstOpenJson_WithGameProfile_ParsesInWireOrder()
        {
            var profile = new GameProfile.Values
            {
                FirebaseProjectId = "example-project",
                FirebaseAppId = "1:000000000000:android:0000000000000000",
                AppMetricaKey = "00000000-0000-0000-0000-000000000000",
                GoogleAppId = "ca-app-pub-0000000000000000~0000000000",
                InterUnit = "0000000000000000",
                RewardUnit = "1111111111111111",
                BannerUnit = "2222222222222222",
            };
            string fields = FirstOpenClaim.BuildJsonFields("F000042-0", "abcd1234efgh", false, "com.amazon.venezia") +
                            GameProfile.BuildJsonFields(profile);

            string json = AnalyticsModule.BuildFirstOpenJson(
                "free_first_open", "com.example.game", "free", "unattributed", 1791480000000L, "evt1", fields);

            Assert.IsTrue(json.StartsWith("{") && json.EndsWith("}"), json);
            var p = JsonUtility.FromJson<FirstOpenPayload>(json);
            Assert.AreEqual("free_first_open", p.event_name);
            Assert.AreEqual("unattributed", p.device_id_hash);
            Assert.AreEqual("F000042-0", p.console_id);
            Assert.AreEqual("abcd1234efgh", p.adjust_app_token);
            Assert.IsFalse(p.is_debug);
            Assert.AreEqual("com.amazon.venezia", p.installer);
            Assert.AreEqual("example-project", p.firebase_project_id);
            Assert.AreEqual("1:000000000000:android:0000000000000000", p.firebase_app_id);
            Assert.AreEqual("12b9377cbe7e", p.appmetrica_key_fp);
            Assert.AreEqual("ca-app-pub-0000000000000000~0000000000", p.applovin_google_app_id);
            Assert.AreEqual("0000000000000000", p.applovin_inter_unit);
            Assert.AreEqual("1111111111111111", p.applovin_reward_unit);
            Assert.AreEqual("2222222222222222", p.applovin_banner_unit);
            Assert.AreEqual(SdkPackageVersion.Value, p.sdk_version);
            Assert.AreEqual(1791480000000L, p.ts);

            string[] order =
            {
                "event_name", "event_id", "app_id", "app_type", "device_id_hash",
                "console_id", "adjust_app_token", "is_debug", "installer",
                "firebase_project_id", "firebase_app_id", "appmetrica_key_fp", "applovin_google_app_id",
                "applovin_inter_unit", "applovin_reward_unit", "applovin_banner_unit",
                "sdk_version", "app_version", "ts",
            };
            int previous = -1;
            foreach (string key in order)
            {
                int index = json.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
                Assert.Greater(index, previous, key);
                previous = index;
            }

            // No other keys: every key is followed by '":', and no value here contains that.
            int keys = 0;
            for (int i = json.IndexOf("\":", StringComparison.Ordinal); i >= 0; i = json.IndexOf("\":", i + 2, StringComparison.Ordinal))
                keys++;
            Assert.AreEqual(order.Length, keys, json);
            Assert.That(json, Does.Not.Contain("00000000-0000-0000-0000-000000000000"), "the AppMetrica key itself is never sent");
        }

        // SDK 1.0.11 without a profile (no provider, a failed read, or every module off) sends
        // exactly the 1.0.10 first_open; only sdk_version differs.
        [Test]
        public void BuildFirstOpenJson_WithoutGameProfile_IsThe1010Payload()
        {
            string claim = FirstOpenClaim.BuildJsonFields("F000042-0", "abcd1234efgh", false, "com.amazon.venezia");
            string expected =
                "{\"event_name\":\"free_first_open\",\"event_id\":\"evt1\",\"app_id\":\"com.example.game\"," +
                "\"app_type\":\"free\",\"device_id_hash\":\"unattributed\",\"console_id\":\"F000042-0\"," +
                "\"adjust_app_token\":\"abcd1234efgh\",\"is_debug\":false,\"installer\":\"com.amazon.venezia\"," +
                "\"sdk_version\":\"" + SdkPackageVersion.Value + "\",\"app_version\":\"" + Application.version + "\"," +
                "\"ts\":1791480000000}";

            foreach (var profile in new[] { null, new GameProfile.Values() })
            {
                string json = AnalyticsModule.BuildFirstOpenJson(
                    "free_first_open", "com.example.game", "free", "unattributed", 1791480000000L, "evt1",
                    claim + GameProfile.BuildJsonFields(profile));
                Assert.AreEqual(expected, json);
            }
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
