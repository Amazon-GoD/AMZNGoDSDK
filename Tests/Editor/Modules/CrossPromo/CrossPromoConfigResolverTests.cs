using NUnit.Framework;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// CrossPromoConfigResolver.TryParse: master JSON routing by package name, direct creative
    /// JSON, nested master rejection and malformed input. Fixtures mirror master.json,
    /// master.example.json and crosspromo-config.json (embedded, no file access).
    /// </summary>
    public class CrossPromoConfigResolverTests
    {
        private const string MasterJson =
            "{\"Packages\":[" +
            "{\"PackageName\":\"com.example.gameone\",\"ConfigUrl\":\"https://cdn.example.com/cross-promo/gameone.json\"}," +
            "{\"PackageName\":\"com.example.gametwo\",\"ConfigUrl\":\"https://cdn.example.com/cross-promo/gametwo.json?sig=A%2Fb&x=1\"}" +
            "]}";

        private const string CreativeJson =
            "{\"Weight\":1.0,\"Videos\":[" +
            "{\"Title\":\"Croko_V_01\",\"ButtonText\":\"Download\"," +
            "\"VideoUrl\":\"https://cdn.example.com/croco.mp4\",\"BannerUrl\":\"https://cdn.example.com/2.png\"," +
            "\"RedirectUrl\":\"amzn://apps/android?asin=B0BNW7XRJP\",\"OverlayShowDelayInSeconds\":4," +
            "\"Weight\":0.25,\"cap\":2,\"position\":1," +
            "\"adjust_click_url\":\"https://app.adjust.com/abc\",\"adjust_impression_url\":\"https://view.adjust.com/impression/abc\"," +
            "\"campaign\":\"camp\",\"adgroup\":\"group\",\"creative\":\"cr1\"," +
            "\"AppPackageName\":[\"com.angry.crocodile.attack\"]}," +
            "{\"Title\":\"Hunting_G_011\",\"VideoUrl\":\"https://cdn.example.com/hunt.mp4\",\"Weight\":0.75,\"cap\":1,\"position\":2}" +
            "]}";

        // ---------- master routing ----------

        [Test]
        public void TryParse_MasterJson_RoutesByPackageName()
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, "com.example.gameone", true,
                out var config, out var url, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(config);
            Assert.IsNull(error);
            Assert.AreEqual("https://cdn.example.com/cross-promo/gameone.json", url);
        }

        [Test]
        public void TryParse_MasterJson_SecondEntryAndUrlReturnedVerbatim()
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, "com.example.gametwo", true,
                out var config, out var url, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(config);
            Assert.AreEqual("https://cdn.example.com/cross-promo/gametwo.json?sig=A%2Fb&x=1", url);
        }

        [Test]
        public void TryParse_MasterJsonWithSurroundingWhitespace_StillParses()
        {
            bool ok = CrossPromoConfigResolver.TryParse("\n  " + MasterJson + "  \r\n", "com.example.gameone", true,
                out _, out var url, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNotNull(url);
        }

        [Test]
        public void TryParse_MasterJsonWhenMasterNotAllowed_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, "com.example.gameone", false,
                out var config, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(config);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("Nested master JSON is not supported"));
        }

        [Test]
        public void TryParse_UnknownPackage_FailsWithPackageNameInError()
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, "com.example.unknown", true,
                out var config, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(config);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("has no entry for package 'com.example.unknown'"));
        }

        [Test]
        public void TryParse_PackageNameMatchIsCaseSensitive()
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, "COM.EXAMPLE.GAMEONE", true,
                out _, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("has no entry for package"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void TryParse_MasterJsonWithEmptyPackageName_Fails(string packageName)
        {
            bool ok = CrossPromoConfigResolver.TryParse(MasterJson, packageName, true,
                out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("package name is empty"));
        }

        [Test]
        public void TryParse_DuplicatePackageEntries_Fails()
        {
            const string json =
                "{\"Packages\":[" +
                "{\"PackageName\":\"com.dup\",\"ConfigUrl\":\"https://a.example.com/1.json\"}," +
                "{\"PackageName\":\"com.dup\",\"ConfigUrl\":\"https://a.example.com/2.json\"}]}";

            bool ok = CrossPromoConfigResolver.TryParse(json, "com.dup", true, out _, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("duplicate entries for package 'com.dup'"));
        }

        [TestCase("ftp://cdn.example.com/a.json")]
        [TestCase("not a url")]
        [TestCase("/relative/path.json")]
        [TestCase("")]
        public void TryParse_NonHttpRouteUrl_Fails(string configUrl)
        {
            string json = "{\"Packages\":[{\"PackageName\":\"com.bad\",\"ConfigUrl\":\"" + configUrl + "\"}]}";

            bool ok = CrossPromoConfigResolver.TryParse(json, "com.bad", true, out _, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("must be an absolute HTTP(S) URL"));
        }

        [Test]
        public void TryParse_RouteWithoutConfigUrl_Fails()
        {
            const string json = "{\"Packages\":[{\"PackageName\":\"com.bad\"}]}";

            bool ok = CrossPromoConfigResolver.TryParse(json, "com.bad", true, out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("must be an absolute HTTP(S) URL"));
        }

        [Test]
        public void TryParse_PackagesAndVideosTogether_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Packages\":[],\"Videos\":[]}", "com.any", true,
                out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("cannot contain both Packages and Videos"));
        }

        // ---------- direct creative JSON ----------

        [Test]
        public void TryParse_CreativeJson_ReturnsConfigAndNoUrl()
        {
            bool ok = CrossPromoConfigResolver.TryParse(CreativeJson, "com.example.gameone", false,
                out var config, out var url, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(url);
            Assert.IsNull(error);
            Assert.IsNotNull(config);
            Assert.AreEqual(1f, config.Weight, 1e-6f);
            Assert.AreEqual(2, config.Videos.Count);

            var first = config.Videos[0];
            Assert.AreEqual("Croko_V_01", first.Title);
            Assert.AreEqual("Download", first.ButtonText);
            Assert.AreEqual("https://cdn.example.com/croco.mp4", first.VideoUrl);
            Assert.AreEqual("https://cdn.example.com/2.png", first.BannerUrl);
            Assert.AreEqual(4, first.OverlayShowDelayInSeconds);
            Assert.AreEqual(0.25f, first.Weight, 1e-6f);
            Assert.AreEqual(2, first.cap);
            Assert.AreEqual(1, first.position);
            Assert.AreEqual("https://app.adjust.com/abc", first.adjust_click_url);
            Assert.AreEqual("https://view.adjust.com/impression/abc", first.adjust_impression_url);
            Assert.AreEqual("camp", first.campaign);
            Assert.AreEqual("group", first.adgroup);
            Assert.AreEqual("cr1", first.creative);
            Assert.AreEqual(1, first.AppPackageName.Count);
            Assert.AreEqual("com.angry.crocodile.attack", first.AppPackageName[0]);

            Assert.AreEqual("Hunting_G_011", config.Videos[1].Title);
            Assert.AreEqual(2, config.Videos[1].position);
        }

        [Test]
        public void TryParse_CreativeJsonWhenMasterAllowed_StillReturnsConfig()
        {
            bool ok = CrossPromoConfigResolver.TryParse(CreativeJson, "com.example.gameone", true,
                out var config, out var url, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(url);
            Assert.AreEqual(2, config.Videos.Count);
        }

        [Test]
        public void TryParse_CreativeJsonIgnoresPackageName()
        {
            bool ok = CrossPromoConfigResolver.TryParse(CreativeJson, null, false,
                out var config, out _, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNotNull(config);
        }

        [Test]
        public void TryParse_EmptyVideosArray_ReturnsEmptyConfig()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Weight\":0.5,\"Videos\":[]}", "com.any", false,
                out var config, out _, out var error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(0, config.Videos.Count);
            Assert.AreEqual(0.5f, config.Weight, 1e-6f);
        }

        // ---------- invalid input ----------

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   \n ")]
        public void TryParse_EmptyJson_Fails(string json)
        {
            bool ok = CrossPromoConfigResolver.TryParse(json, "com.any", true, out var config, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(config);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("JSON is empty"));
        }

        [TestCase("[1,2,3]")]
        [TestCase("\"text\"")]
        [TestCase("42")]
        [TestCase("{\"Videos\":[]")]
        public void TryParse_RootIsNotObject_Fails(string json)
        {
            bool ok = CrossPromoConfigResolver.TryParse(json, "com.any", true, out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("JSON root must be an object"));
        }

        [Test]
        public void TryParse_ObjectWithoutPackagesOrVideos_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Foo\":1}", "com.any", true, out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("must contain a Packages array or a Videos array"));
        }

        [Test]
        public void TryParse_UnterminatedString_FailsWithError()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Videos\":[{\"Title\":\"abc}]}", "com.any", true,
                out var config, out var url, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(config);
            Assert.IsNull(url);
            Assert.That(error, Does.Contain("unterminated string"));
        }

        [Test]
        public void TryParse_DataAfterRootObject_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Videos\":[]} {\"x\":1}", "com.any", true,
                out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("after the root object"));
        }

        [Test]
        public void TryParse_DuplicateRootVideosField_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Videos\":[],\"Videos\":[]}", "com.any", true,
                out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("duplicate root 'Videos' field"));
        }

        [Test]
        public void TryParse_RootVideosNotAnArray_Fails()
        {
            bool ok = CrossPromoConfigResolver.TryParse("{\"Videos\":{}}", "com.any", true,
                out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("Root 'Videos' must be a JSON array"));
        }

        [Test]
        public void TryParse_NestedVideosKey_IsNotTreatedAsRootField()
        {
            // A "Videos" key inside a creative (depth 3) must not count as the root array.
            bool ok = CrossPromoConfigResolver.TryParse(
                "{\"Foo\":{\"Videos\":[]}}", "com.any", true, out _, out _, out var error);

            Assert.IsFalse(ok);
            Assert.That(error, Does.Contain("must contain a Packages array or a Videos array"));
        }

        [Test]
        public void TryParse_FailureLeavesOutputsNull()
        {
            CrossPromoConfigResolver.TryParse("{\"Foo\":1}", "com.any", true, out var config, out var url, out var error);

            Assert.IsNull(config);
            Assert.IsNull(url);
            Assert.IsNotNull(error);
        }
    }
}
