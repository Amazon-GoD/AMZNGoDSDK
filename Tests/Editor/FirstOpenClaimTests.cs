using System;
using NUnit.Framework;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Поля заявки в first_open (console_id, adjust_app_token, is_debug, installer): что уходит,
    /// что отбрасывается, и что фрагмент встаёт в событие валидным JSON.
    /// </summary>
    public class FirstOpenClaimTests
    {
        private const string Token = "abcdefghij12";

        [Serializable]
        private class FirstOpenPayload
        {
            public string event_name;
            public string console_id;
            public string adjust_app_token;
            public bool is_debug;
            public string installer;
            public string sdk_version;
            public long ts;
        }

        [TestCase("abcdefghij12")]
        [TestCase("0123456789ab")]
        public void AdjustAppToken_AcceptsTwelveLowercaseAlphanumerics(string token)
        {
            Assert.IsTrue(Runtime.FirstOpenClaim.IsValidAdjustAppToken(token), token);
        }

        [TestCase("")]
        [TestCase("abcdefghij1")]
        [TestCase("abcdefghij123")]
        [TestCase("ABCDEFGHIJ12")]
        [TestCase("abcdefghij1 ")]
        [TestCase(" abcdefghij1")]
        [TestCase("abcdefghij-1")]
        [TestCase("abcdefghіj12")] // кириллическая і
        public void AdjustAppToken_RejectsEverythingElse(string token)
        {
            Assert.IsFalse(Runtime.FirstOpenClaim.IsValidAdjustAppToken(token), token);
        }

        [Test]
        public void AdjustAppToken_RejectsNull()
        {
            Assert.IsFalse(Runtime.FirstOpenClaim.IsValidAdjustAppToken(null));
        }

        [TestCase("com.amazon.venezia")]
        [TestCase("com.android.vending")]
        [TestCase("adb")]
        [TestCase("a+b:c_d-e")]
        public void Installer_AcceptsShortPackageLikeNames(string installer)
        {
            Assert.IsTrue(Runtime.FirstOpenClaim.IsValidInstaller(installer), installer);
        }

        [TestCase("")]
        [TestCase("com amazon")]
        [TestCase("com\"amazon")]
        [TestCase("com/amazon")]
        [TestCase("com\\amazon")]
        [TestCase("установщик")]
        [TestCase("com.amazon.venezia\n")]
        public void Installer_RejectsEverythingElse(string installer)
        {
            Assert.IsFalse(Runtime.FirstOpenClaim.IsValidInstaller(installer), installer);
        }

        [Test]
        public void Installer_LengthLimitIs64()
        {
            Assert.IsTrue(Runtime.FirstOpenClaim.IsValidInstaller(new string('a', 64)));
            Assert.IsFalse(Runtime.FirstOpenClaim.IsValidInstaller(new string('a', 65)));
            Assert.IsFalse(Runtime.FirstOpenClaim.IsValidInstaller(null));
        }

        [Test]
        public void BuildJsonFields_AllValid_FixedOrderWithTrailingComma()
        {
            string fields = Runtime.FirstOpenClaim.BuildJsonFields("F000042-0", Token, false, "com.amazon.venezia");

            Assert.AreEqual(
                "\"console_id\":\"F000042-0\",\"adjust_app_token\":\"abcdefghij12\",\"is_debug\":false," +
                "\"installer\":\"com.amazon.venezia\",",
                fields);
        }

        [Test]
        public void BuildJsonFields_GameWithoutIdOrAdjust_SendsOnlyIsDebug()
        {
            // Старая игра: ID пустой, Adjust выключен (токен null), установщик неизвестен.
            Assert.AreEqual("\"is_debug\":true,", Runtime.FirstOpenClaim.BuildJsonFields("", null, true, null));
            Assert.AreEqual("\"is_debug\":false,", Runtime.FirstOpenClaim.BuildJsonFields(null, null, false, ""));
        }

        [Test]
        public void BuildJsonFields_DropsInvalidValues()
        {
            string fields = Runtime.FirstOpenClaim.BuildJsonFields(
                "F000042-0\",\"x\":\"", "ABCDEFGHIJ12", true, "bad installer\"");

            Assert.AreEqual("\"is_debug\":true,", fields);
        }

        [Test]
        public void BuildJsonFields_InsideFirstOpen_IsValidJson()
        {
            // Тот же порядок, что в AnalyticsModule.BuildFirstOpenJson: заявка встаёт перед sdk_version.
            string json = "{\"event_name\":\"free_first_open\"," +
                          Runtime.FirstOpenClaim.BuildJsonFields("P000042-1", Token, true, "com.amazon.venezia") +
                          "\"sdk_version\":\"1.0.10\",\"ts\":1791480000000}";

            var payload = JsonUtility.FromJson<FirstOpenPayload>(json);

            Assert.AreEqual("free_first_open", payload.event_name);
            Assert.AreEqual("P000042-1", payload.console_id);
            Assert.AreEqual(Token, payload.adjust_app_token);
            Assert.IsTrue(payload.is_debug);
            Assert.AreEqual("com.amazon.venezia", payload.installer);
            Assert.AreEqual("1.0.10", payload.sdk_version);
            Assert.AreEqual(1791480000000L, payload.ts);
        }
    }
}
