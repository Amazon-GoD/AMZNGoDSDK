using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// AnalyticsEventQueue capacity rules (regression 1.0.8: banner impressions must not displace
    /// regular events): 50 events in total, 10 banner impressions, oldest banner evicted first.
    /// Only the pure helpers are used; PlayerPrefs is never touched.
    /// </summary>
    public class AnalyticsEventQueueCapacityTests
    {
        private static string Banner(int id) =>
            "{\"event_name\":\"cp_impression\",\"placement\":\"banner\",\"id\":" + id + "}";

        private static string MediationBanner(int id) =>
            "{\"event_name\":\"mediation_impression\",\"placement\":\"banner\",\"id\":" + id + "}";

        private static string Regular(int id) =>
            "{\"event_name\":\"cp_click\",\"placement\":\"banner\",\"id\":" + id + "}";

        [Test]
        public void AddWithCapacity_EmptyQueue_Adds()
        {
            var events = new List<string>();
            bool added = AnalyticsEventQueue.AddWithCapacity(events, Regular(1), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsFalse(evicted);
            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public void AddWithCapacity_ElevenBannerImpressions_EvictsOldestBanner()
        {
            var events = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                bool addedEarly = AnalyticsEventQueue.AddWithCapacity(events, Banner(i), out bool evictedEarly);
                Assert.IsTrue(addedEarly);
                Assert.IsFalse(evictedEarly);
            }

            bool added = AnalyticsEventQueue.AddWithCapacity(events, Banner(10), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsTrue(evicted);
            Assert.AreEqual(10, events.Count);
            Assert.IsFalse(events.Contains(Banner(0)));
            Assert.IsTrue(events.Contains(Banner(10)));
            Assert.AreEqual(Banner(1), events[0]);
        }

        [Test]
        public void AddWithCapacity_MediationBannerImpressions_CountTowardsBannerBudget()
        {
            var events = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                AnalyticsEventQueue.AddWithCapacity(events, Banner(i), out _);
                AnalyticsEventQueue.AddWithCapacity(events, MediationBanner(i), out _);
            }

            bool added = AnalyticsEventQueue.AddWithCapacity(events, MediationBanner(99), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsTrue(evicted);
            Assert.AreEqual(10, events.Count);
            Assert.IsFalse(events.Contains(Banner(0)));
        }

        // Regression 1.0.8: a click (even with placement=banner) is a regular event, not a banner impression.
        [Test]
        public void AddWithCapacity_ClickWithBannerPlacement_IsNotBannerBudgeted()
        {
            var events = new List<string>();
            for (int i = 0; i < 12; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Regular(i), out _);

            Assert.AreEqual(12, events.Count);
        }

        [Test]
        public void AddWithCapacity_FullOfRegularEvents_BannerImpressionIsRefused()
        {
            var events = new List<string>();
            for (int i = 0; i < 50; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Regular(i), out _);
            Assert.AreEqual(50, events.Count);

            bool added = AnalyticsEventQueue.AddWithCapacity(events, Banner(1), out bool evicted);

            Assert.IsFalse(added);
            Assert.IsFalse(evicted);
            Assert.AreEqual(50, events.Count);
            Assert.IsFalse(events.Contains(Banner(1)));
        }

        [Test]
        public void AddWithCapacity_FullOfRegularEvents_RegularEventEvictsOldestRegular()
        {
            var events = new List<string>();
            for (int i = 0; i < 50; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Regular(i), out _);

            bool added = AnalyticsEventQueue.AddWithCapacity(events, Regular(100), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsTrue(evicted);
            Assert.AreEqual(50, events.Count);
            Assert.IsFalse(events.Contains(Regular(0)));
            Assert.AreEqual(Regular(100), events[49]);
        }

        [Test]
        public void AddWithCapacity_FullQueueWithBanners_RegularEventEvictsOldestBannerFirst()
        {
            var events = new List<string>();
            for (int i = 0; i < 45; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Regular(i), out _);
            for (int i = 0; i < 5; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Banner(i), out _);
            Assert.AreEqual(50, events.Count);

            bool added = AnalyticsEventQueue.AddWithCapacity(events, Regular(100), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsTrue(evicted);
            Assert.AreEqual(50, events.Count);
            Assert.IsFalse(events.Contains(Banner(0)));
            Assert.IsTrue(events.Contains(Regular(0)));
        }

        [Test]
        public void AddWithCapacity_FullQueueWithBanners_BannerImpressionEvictsOldestBanner()
        {
            var events = new List<string>();
            for (int i = 0; i < 45; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Regular(i), out _);
            for (int i = 0; i < 5; i++)
                AnalyticsEventQueue.AddWithCapacity(events, Banner(i), out _);

            bool added = AnalyticsEventQueue.AddWithCapacity(events, Banner(50), out bool evicted);

            Assert.IsTrue(added);
            Assert.IsTrue(evicted);
            Assert.AreEqual(50, events.Count);
            Assert.IsFalse(events.Contains(Banner(0)));
            Assert.IsTrue(events.Contains(Banner(50)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json at all")]
        [TestCase("{\"event_name\":\"cp_impression\"}")]
        [TestCase("{\"event_name\":\"cp_impression\",\"placement\":\"interstitial\"}")]
        [TestCase("{\"event_name\":\"first_open\",\"placement\":\"banner\"}")]
        public void AddWithCapacity_NonBannerPayloads_UseRegularBudget(string json)
        {
            var events = new List<string>();
            for (int i = 0; i < 11; i++)
                AnalyticsEventQueue.AddWithCapacity(events, json, out _);

            Assert.AreEqual(11, events.Count);
        }

        [Test]
        public void ApplyCapacity_Null_ReturnsEmptyList()
        {
            var result = AnalyticsEventQueue.ApplyCapacity(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ApplyCapacity_WithinLimits_ReturnsCopyUnchanged()
        {
            var events = new List<string> { Regular(1), Banner(2), Regular(3) };

            var result = AnalyticsEventQueue.ApplyCapacity(events);

            Assert.AreNotSame(events, result);
            CollectionAssert.AreEqual(events, result);
        }

        [Test]
        public void ApplyCapacity_TooManyBannerImpressions_KeepsNewestTen()
        {
            var events = new List<string>();
            for (int i = 0; i < 15; i++)
                events.Add(Banner(i));

            var result = AnalyticsEventQueue.ApplyCapacity(events);

            Assert.AreEqual(10, result.Count);
            Assert.AreEqual(Banner(5), result[0]);
            Assert.AreEqual(Banner(14), result[9]);
        }

        [Test]
        public void ApplyCapacity_MoreThanFiftyRegularEvents_KeepsNewestFifty()
        {
            var events = new List<string>();
            for (int i = 0; i < 60; i++)
                events.Add(Regular(i));

            var result = AnalyticsEventQueue.ApplyCapacity(events);

            Assert.AreEqual(50, result.Count);
            Assert.AreEqual(Regular(10), result[0]);
            Assert.AreEqual(Regular(59), result[49]);
        }

        [Test]
        public void ApplyCapacity_BannerImpressionsNeverDisplaceRegularEvents()
        {
            var events = new List<string>();
            for (int i = 0; i < 40; i++)
                events.Add(Regular(i));
            for (int i = 0; i < 20; i++)
                events.Add(Banner(i));

            var result = AnalyticsEventQueue.ApplyCapacity(events);

            Assert.AreEqual(50, result.Count);
            for (int i = 0; i < 40; i++)
                Assert.IsTrue(result.Contains(Regular(i)), "regular event " + i + " was lost");
        }
    }
}
