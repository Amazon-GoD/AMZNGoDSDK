using NUnit.Framework;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// CrossPromoAdjustTracking pure helpers: http(s) URL check and the no-URL paths of the
    /// URL builders. URL assembly with query parameters is not covered here because it reads the
    /// device id (PlayerPrefs via DeviceIdProvider) and the cached public IP.
    /// </summary>
    public class CrossPromoAdjustTrackingTests
    {
        [TestCase("http://app.adjust.com/abc")]
        [TestCase("https://app.adjust.com/abc?x=1")]
        [TestCase("HTTP://APP.ADJUST.COM/ABC")]
        [TestCase("HtTpS://app.adjust.com")]
        public void IsHttpUrl_HttpAndHttps_ReturnTrue(string url)
        {
            Assert.IsTrue(CrossPromoAdjustTracking.IsHttpUrl(url), url);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("ftp://app.adjust.com/abc")]
        [TestCase("amzn://apps/android?asin=B0BNW7XRJP")]
        [TestCase("app.adjust.com/abc")]
        [TestCase("//app.adjust.com/abc")]
        [TestCase(" https://app.adjust.com/abc")]
        [TestCase("httpx://app.adjust.com")]
        public void IsHttpUrl_Other_ReturnFalse(string url)
        {
            Assert.IsFalse(CrossPromoAdjustTracking.IsHttpUrl(url), url);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void BuildAdjustUrl_BlankBaseUrl_ReturnsNull(string baseUrl)
        {
            Assert.IsNull(CrossPromoAdjustTracking.BuildAdjustUrl(baseUrl, "camp", "group", "creative"));
        }

        [Test]
        public void BuildImpressionUrl_NullConfig_ReturnsNull()
        {
            Assert.IsNull(CrossPromoAdjustTracking.BuildImpressionUrl(null));
        }

        [Test]
        public void BuildClickUrl_NullConfig_ReturnsNull()
        {
            Assert.IsNull(CrossPromoAdjustTracking.BuildClickUrl(null));
        }

        [Test]
        public void BuildImpressionAndClickUrl_ConfigWithoutTrackerUrls_ReturnNull()
        {
            var config = new PromoConfiguration { Title = "T", campaign = "c", adgroup = "g", creative = "x" };

            Assert.IsNull(CrossPromoAdjustTracking.BuildImpressionUrl(config));
            Assert.IsNull(CrossPromoAdjustTracking.BuildClickUrl(config));
        }
    }
}
