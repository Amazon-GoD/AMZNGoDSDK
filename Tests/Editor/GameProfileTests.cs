using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AMZNGoDSDK.Runtime;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Профиль игры в first_open (1.0.11): правила значений на границах, отпечаток ключа AppMetrica
    /// (та же формула, что в админке и в SQL BI), фрагмент JSON и строка лога без значений.
    /// Репозиторий публичный: здесь только явно фальшивые значения.
    /// </summary>
    public class GameProfileTests
    {
        private const string ProjectId = "example-project";
        private const string FirebaseAppId = "1:000000000000:android:0000000000000000";
        private const string ZeroUuid = "00000000-0000-0000-0000-000000000000";
        private const string GoogleAppId = "ca-app-pub-0000000000000000~0000000000";
        private const string Unit = "0000000000000000";

        // Первые 12 hex sha256("00000000-0000-0000-0000-000000000000"); сверено с SQL BI
        // left(encode(sha256(convert_to(lower(btrim(k)), 'UTF8')), 'hex'), 12).
        private const string ZeroUuidFingerprint = "12b9377cbe7e";

        // То же для "abcdef01-2345-6789-abcd-ef0123456789", сверено тем же SQL.
        private const string HexUuid = "abcdef01-2345-6789-abcd-ef0123456789";
        private const string HexUuidFingerprint = "5927dbb3765a";

        private static GameProfile.Values Full() => new GameProfile.Values
        {
            FirebaseProjectId = ProjectId,
            FirebaseAppId = FirebaseAppId,
            AppMetricaKey = ZeroUuid,
            GoogleAppId = GoogleAppId,
            InterUnit = Unit,
            RewardUnit = "1111111111111111",
            BannerUnit = "2222222222222222",
        };

        private const string FullFields =
            "\"firebase_project_id\":\"example-project\"," +
            "\"firebase_app_id\":\"1:000000000000:android:0000000000000000\"," +
            "\"appmetrica_key_fp\":\"12b9377cbe7e\"," +
            "\"applovin_google_app_id\":\"ca-app-pub-0000000000000000~0000000000\"," +
            "\"applovin_inter_unit\":\"0000000000000000\"," +
            "\"applovin_reward_unit\":\"1111111111111111\"," +
            "\"applovin_banner_unit\":\"2222222222222222\",";

        [Serializable]
        private class ProfilePayload
        {
            public string firebase_project_id;
            public string firebase_app_id;
            public string appmetrica_key_fp;
            public string applovin_google_app_id;
            public string applovin_inter_unit;
            public string applovin_reward_unit;
            public string applovin_banner_unit;
            public string sdk_version;
        }

        // ---------- Правила значений ----------

        [TestCase("example-project")]
        [TestCase("abcdef")]
        [TestCase("a0000000000000000000000000000z")] // 30 символов
        [TestCase("a-b-c-d-1")]
        public void FirebaseProjectId_Accepts(string value)
        {
            Assert.IsTrue(GameProfile.IsValidFirebaseProjectId(value), value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abcde")]
        [TestCase("a0000000000000000000000000000zz")] // 31 символ
        [TestCase("1example")]
        [TestCase("-example")]
        [TestCase("example-")]
        [TestCase("Example-project")]
        [TestCase("example_project")]
        [TestCase("example project")]
        [TestCase(" example-project")]
        [TestCase("еxample-project")] // кириллическая е
        public void FirebaseProjectId_Rejects(string value)
        {
            Assert.IsFalse(GameProfile.IsValidFirebaseProjectId(value), value ?? "null");
        }

        [TestCase("1:000000000000:android:0000000000000000")]
        [TestCase("1:0:android:a")]
        [TestCase("1:00000000000000000000:android:0000000000000000000000000000000000000000")] // 20 цифр, 40 hex
        public void FirebaseAppId_Accepts(string value)
        {
            Assert.IsTrue(GameProfile.IsValidFirebaseAppId(value), value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1:000000000000000000000:android:00")] // 21 цифра
        [TestCase("1:0:android:00000000000000000000000000000000000000000")] // 41 hex
        [TestCase("2:000000000000:android:00")]
        [TestCase("1::android:00")]
        [TestCase("1:000000000000:android:")]
        [TestCase("1:000000000000:ios:00")]
        [TestCase("1:000000000000:android:ABCDEF")]
        [TestCase("1:000000000000:android:0g")]
        [TestCase("1:000000000000:android:00 ")]
        [TestCase("1:0a:android:00")]
        [TestCase("1:000000000000:android:00:android:00")]
        public void FirebaseAppId_Rejects(string value)
        {
            Assert.IsFalse(GameProfile.IsValidFirebaseAppId(value), value ?? "null");
        }

        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("abcdef01-2345-6789-abcd-ef0123456789")]
        public void AppMetricaKey_AcceptsLowercaseUuid(string value)
        {
            Assert.IsTrue(GameProfile.IsValidAppMetricaKey(value), value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ABCDEF01-2345-6789-ABCD-EF0123456789")]
        [TestCase("00000000000000000000000000000000")]
        [TestCase("00000000-0000-0000-0000-00000000000")]
        [TestCase("00000000-0000-0000-0000-0000000000000")]
        [TestCase("0000000-00000-0000-0000-000000000000")]
        [TestCase("g0000000-0000-0000-0000-000000000000")]
        [TestCase("{00000000-0000-0000-0000-000000000000}")]
        [TestCase(" 00000000-0000-0000-0000-000000000000")]
        public void AppMetricaKey_RejectsEverythingElse(string value)
        {
            Assert.IsFalse(GameProfile.IsValidAppMetricaKey(value), value ?? "null");
        }

        [TestCase("000000000000", true)]
        [TestCase("abcdef012345", true)]
        [TestCase("00000000000", false)]
        [TestCase("0000000000000", false)]
        [TestCase("ABCDEF012345", false)]
        [TestCase("00000000000g", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void KeyFingerprint_Format(string value, bool expected)
        {
            Assert.AreEqual(expected, GameProfile.IsValidKeyFingerprint(value), value ?? "null");
        }

        [Test]
        public void GoogleAppId_AcceptsAdMobAppId()
        {
            Assert.IsTrue(GameProfile.IsValidGoogleAppId(GoogleAppId));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ca-app-pub-000000000000000~0000000000")] // 15 цифр
        [TestCase("ca-app-pub-00000000000000000~0000000000")] // 17 цифр
        [TestCase("ca-app-pub-0000000000000000~000000000")] // 9 цифр
        [TestCase("ca-app-pub-0000000000000000~00000000000")] // 11 цифр
        [TestCase("ca-app-pub-0000000000000000/0000000000")] // это ad unit AdMob, не App ID
        [TestCase("CA-APP-PUB-0000000000000000~0000000000")]
        [TestCase(" ca-app-pub-0000000000000000~0000000000")]
        [TestCase("ca-app-pub-000000000000000a~0000000000")]
        [TestCase("1:000000000000:android:0000000000000000")] // App ID Firebase
        public void GoogleAppId_RejectsEverythingElse(string value)
        {
            Assert.IsFalse(GameProfile.IsValidGoogleAppId(value), value ?? "null");
        }

        [TestCase("0000000000000000")]
        [TestCase("abcdef0123456789")]
        public void AdUnit_AcceptsSixteenLowercaseHex(string value)
        {
            Assert.IsTrue(GameProfile.IsValidAdUnit(value), value);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("000000000000000")]
        [TestCase("00000000000000000")]
        [TestCase("ABCDEF0123456789")]
        [TestCase("000000000000000g")]
        [TestCase("0000000000000000 ")]
        public void AdUnit_RejectsEverythingElse(string value)
        {
            Assert.IsFalse(GameProfile.IsValidAdUnit(value), value ?? "null");
        }

        // ---------- Отпечаток ключа AppMetrica ----------

        [Test]
        public void KeyFingerprint_KnownVectors()
        {
            Assert.AreEqual(ZeroUuidFingerprint, GameProfile.KeyFingerprint(ZeroUuid));
            Assert.AreEqual(HexUuidFingerprint, GameProfile.KeyFingerprint(HexUuid));
        }

        [Test]
        public void KeyFingerprint_TrimsAndLowercasesFirst()
        {
            Assert.AreEqual(ZeroUuidFingerprint, GameProfile.KeyFingerprint("  " + ZeroUuid + "\t"));
            Assert.AreEqual(HexUuidFingerprint, GameProfile.KeyFingerprint("ABCDEF01-2345-6789-ABCD-EF0123456789"));
            Assert.AreEqual(HexUuidFingerprint, GameProfile.KeyFingerprint(" AbCdEf01-2345-6789-aBcD-eF0123456789 "));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not-a-key")]
        [TestCase("00000000000000000000000000000000")]
        [TestCase("{00000000-0000-0000-0000-000000000000}")]
        public void KeyFingerprint_NotUuid_IsNull(string key)
        {
            Assert.IsNull(GameProfile.KeyFingerprint(key), key ?? "null");
        }

        // ---------- BuildJsonFields ----------

        [Test]
        public void BuildJsonFields_AllValid_FixedOrderWithTrailingComma()
        {
            Assert.AreEqual(FullFields, GameProfile.BuildJsonFields(Full()));
        }

        [Test]
        public void BuildJsonFields_NullOrEmpty_IsEmptyString()
        {
            Assert.AreEqual(string.Empty, GameProfile.BuildJsonFields(null));
            Assert.AreEqual(string.Empty, GameProfile.BuildJsonFields(new GameProfile.Values()));
            Assert.AreEqual(string.Empty, GameProfile.BuildJsonFields(new GameProfile.Values
            {
                FirebaseProjectId = "", FirebaseAppId = " ", AppMetricaKey = "", GoogleAppId = "",
                InterUnit = "", RewardUnit = "  ", BannerUnit = "",
            }));
        }

        [Test]
        public void BuildJsonFields_SkipsInvalidValues()
        {
            var values = new GameProfile.Values
            {
                FirebaseProjectId = "Bad Project",
                FirebaseAppId = "1:000000000000:ios:00",
                AppMetricaKey = "not-a-key",
                GoogleAppId = "ca-app-pub-0000000000000000/0000000000",
                InterUnit = "123",
                RewardUnit = Unit,
                BannerUnit = "0000000000000000\",\"x\":\"",
            };

            Assert.AreEqual("\"applovin_reward_unit\":\"0000000000000000\",", GameProfile.BuildJsonFields(values));
        }

        [Test]
        public void BuildJsonFields_LowercasesUnitsAndFirebaseAppId_TrimsEverything()
        {
            var values = new GameProfile.Values
            {
                FirebaseProjectId = " example-project ",
                FirebaseAppId = " 1:000000000000:android:ABCDEF0000000000 ",
                AppMetricaKey = " " + ZeroUuid + " ",
                GoogleAppId = " " + GoogleAppId + " ",
                InterUnit = " ABCDEF0000000000",
                RewardUnit = "AbCdEf0000000000 ",
                BannerUnit = "\tabcdef0000000000\n",
            };

            Assert.AreEqual(
                "\"firebase_project_id\":\"example-project\"," +
                "\"firebase_app_id\":\"1:000000000000:android:abcdef0000000000\"," +
                "\"appmetrica_key_fp\":\"12b9377cbe7e\"," +
                "\"applovin_google_app_id\":\"ca-app-pub-0000000000000000~0000000000\"," +
                "\"applovin_inter_unit\":\"abcdef0000000000\"," +
                "\"applovin_reward_unit\":\"abcdef0000000000\"," +
                "\"applovin_banner_unit\":\"abcdef0000000000\",",
                GameProfile.BuildJsonFields(values));
        }

        [Test]
        public void BuildJsonFields_ProjectIdAndGoogleAppIdAreNotLowercased()
        {
            // Правило бэкенда для них без верхнего регистра: такое значение не отправляется вовсе.
            var values = new GameProfile.Values
            {
                FirebaseProjectId = "Example-Project",
                GoogleAppId = "CA-APP-PUB-0000000000000000~0000000000",
            };

            Assert.AreEqual(string.Empty, GameProfile.BuildJsonFields(values));
        }

        [Test]
        public void BuildJsonFields_NeverSendsTheAppMetricaKey()
        {
            string fields = GameProfile.BuildJsonFields(new GameProfile.Values { AppMetricaKey = HexUuid });

            Assert.AreEqual("\"appmetrica_key_fp\":\"5927dbb3765a\",", fields);
            Assert.That(fields, Does.Not.Contain(HexUuid));
        }

        [Test]
        public void BuildJsonFields_InsideFirstOpen_IsValidJson()
        {
            string json = "{\"event_name\":\"free_first_open\"," +
                          FirstOpenClaim.BuildJsonFields("F000042-0", null, false, null) +
                          GameProfile.BuildJsonFields(Full()) +
                          "\"sdk_version\":\"1.0.11\",\"ts\":1791480000000}";

            var payload = JsonUtility.FromJson<ProfilePayload>(json);

            Assert.AreEqual(ProjectId, payload.firebase_project_id);
            Assert.AreEqual(FirebaseAppId, payload.firebase_app_id);
            Assert.AreEqual(ZeroUuidFingerprint, payload.appmetrica_key_fp);
            Assert.AreEqual(GoogleAppId, payload.applovin_google_app_id);
            Assert.AreEqual(Unit, payload.applovin_inter_unit);
            Assert.AreEqual("1111111111111111", payload.applovin_reward_unit);
            Assert.AreEqual("2222222222222222", payload.applovin_banner_unit);
            Assert.AreEqual("1.0.11", payload.sdk_version);
        }

        // ---------- DescribePresent ----------

        [Test]
        public void DescribePresent_NamesOnly_InWireOrder()
        {
            string described = GameProfile.DescribePresent(Full());

            Assert.AreEqual(string.Join(",", GameProfile.AllKeys), described);
            foreach (string value in new[]
                     {
                         ProjectId, FirebaseAppId, ZeroUuid, ZeroUuidFingerprint, GoogleAppId,
                         Unit, "1111111111111111", "2222222222222222", "000000000000",
                     })
            {
                Assert.That(described, Does.Not.Contain(value));
            }
        }

        [Test]
        public void DescribePresent_OnlyWhatIsSent()
        {
            var values = new GameProfile.Values { AppMetricaKey = "not-a-key", InterUnit = Unit, BannerUnit = "bad" };

            Assert.AreEqual("applovin_inter_unit", GameProfile.DescribePresent(values));
            Assert.AreEqual("none", GameProfile.DescribePresent(null));
            Assert.AreEqual("none", GameProfile.DescribePresent(new GameProfile.Values()));
        }

        // ---------- SafeCollect ----------

        [Test]
        public void SafeCollect_NullProvider_IsNull()
        {
            Assert.IsNull(GameProfile.SafeCollect(null));
        }

        [Test]
        public void SafeCollect_ReturnsWhatTheProviderBuilt()
        {
            var values = Full();

            Assert.AreSame(values, GameProfile.SafeCollect(() => values));
        }

        [Test]
        public void SafeCollect_ThrowingProvider_IsNull_AndLogsOnlyTheExceptionType()
        {
            // Текст исключения может нести значение — в лог уходит только тип.
            LogAssert.Expect(LogType.Warning,
                "[GameProfile] Profile read failed (InvalidOperationException) — first_open goes without the profile");

            var values = GameProfile.SafeCollect(() => throw new InvalidOperationException(ZeroUuid));

            Assert.IsNull(values);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
