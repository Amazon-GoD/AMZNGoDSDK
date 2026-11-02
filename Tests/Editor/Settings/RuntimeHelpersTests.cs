using System;
using NUnit.Framework;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Shared runtime helpers: <see cref="Runtime.SdkJson.Escape"/> (IAP-19: one unescaped newline in a product
    /// name made AppMetrica drop the event silently) and <see cref="Runtime.AppLovinSettingData.NormalizeAdPlacement"/>
    /// (1.0.8: empty placements fall back to the canonical defaults).
    /// </summary>
    public class RuntimeHelpersTests
    {
        [Serializable]
        private class Payload
        {
            public string v;
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("plain", "plain")]
        [TestCase("a\"b", "a\\\"b")]
        [TestCase("a\\b", "a\\\\b")]
        [TestCase("line1\nline2", "line1\\nline2")]
        [TestCase("\r\t\b\f", "\\r\\t\\b\\f")]
        [TestCase("\u0001\u001f", "\\u0001\\u001f")]
        [TestCase("Подписка «Премиум»", "Подписка «Премиум»")]
        public void Escape_ProducesJsonStringBody(string input, string expected)
        {
            Assert.AreEqual(expected, Runtime.SdkJson.Escape(input));
        }

        [TestCase("Name with \"quotes\"")]
        [TestCase("Back\\slash and / slash")]
        [TestCase("Multi\nline\r\nname\twith tab")]
        [TestCase("Control \u0002 char")]
        [TestCase("Юникод и эмодзи 🎮")]
        public void Escape_RoundTripsThroughJsonParser(string input)
        {
            string json = "{\"v\":\"" + Runtime.SdkJson.Escape(input) + "\"}";

            Payload parsed = JsonUtility.FromJson<Payload>(json);

            Assert.AreEqual(input, parsed.v);
        }

        [TestCase(null, "interstitial")]
        [TestCase("", "interstitial")]
        [TestCase("   ", "interstitial")]
        [TestCase(" level_end ", "level_end")]
        [TestCase("main_menu", "main_menu")]
        public void NormalizeAdPlacement_FallsBackOnlyWhenBlank(string placement, string expected)
        {
            Assert.AreEqual(expected,
                Runtime.AppLovinSettingData.NormalizeAdPlacement(placement, Runtime.AppLovinSettingData.DefaultInterstitialAdPlacement));
        }
    }
}
