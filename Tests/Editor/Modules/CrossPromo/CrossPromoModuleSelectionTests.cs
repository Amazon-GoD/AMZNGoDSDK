using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// CrossPromoModule static helpers: resolving the playable URL of a creative and the weighted
    /// random pick. Only outcomes that do not depend on the random value are asserted.
    /// </summary>
    public class CrossPromoModuleSelectionTests
    {
        private static PromoConfiguration Video(string title, float weight)
        {
            return new PromoConfiguration { Title = title, Weight = weight, VideoUrl = "https://cdn.example.com/" + title + ".mp4" };
        }

        // ---------- ResolvePromoUrl ----------

        [Test]
        public void ResolvePromoUrl_VideoUrl_WinsOverFileName()
        {
            var config = new PromoConfiguration { VideoUrl = "https://cdn.example.com/a.mp4", FileName = "local" };

            Assert.AreEqual("https://cdn.example.com/a.mp4", CrossPromoModule.ResolvePromoUrl(config));
        }

        [Test]
        public void ResolvePromoUrl_FileNameWithoutExtension_AppendsConfiguredExtension()
        {
            var config = new PromoConfiguration { FileName = "clip", FileExtension = VideoExtension.mp4 };

            string expected = Path.Combine(Application.streamingAssetsPath, "clip.mp4");
            Assert.AreEqual(expected, CrossPromoModule.ResolvePromoUrl(config));
        }

        [Test]
        public void ResolvePromoUrl_FileNameAlreadyHasExtension_IsNotDoubled()
        {
            var config = new PromoConfiguration { FileName = "clip.MP4", FileExtension = VideoExtension.mp4 };

            string expected = Path.Combine(Application.streamingAssetsPath, "clip.MP4");
            Assert.AreEqual(expected, CrossPromoModule.ResolvePromoUrl(config));
        }

        [Test]
        public void ResolvePromoUrl_FileNameWithOtherExtension_AppendsConfiguredOne()
        {
            var config = new PromoConfiguration { FileName = "clip.mp4", FileExtension = VideoExtension.webm };

            string expected = Path.Combine(Application.streamingAssetsPath, "clip.mp4.webm");
            Assert.AreEqual(expected, CrossPromoModule.ResolvePromoUrl(config));
        }

        [Test]
        public void ResolvePromoUrl_BlankVideoUrlFallsBackToFileName()
        {
            var config = new PromoConfiguration { VideoUrl = "   ", FileName = "clip", FileExtension = VideoExtension.mov };

            string expected = Path.Combine(Application.streamingAssetsPath, "clip.mov");
            Assert.AreEqual(expected, CrossPromoModule.ResolvePromoUrl(config));
        }

        [Test]
        public void ResolvePromoUrl_NoSource_ReturnsNull()
        {
            Assert.IsNull(CrossPromoModule.ResolvePromoUrl(new PromoConfiguration()));
            Assert.IsNull(CrossPromoModule.ResolvePromoUrl(new PromoConfiguration { VideoUrl = " ", FileName = "" }));
        }

        // ---------- SelectWeightedRandom ----------

        [Test]
        public void SelectWeightedRandom_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(CrossPromoModule.SelectWeightedRandom(null));
            Assert.IsNull(CrossPromoModule.SelectWeightedRandom(new List<PromoConfiguration>()));
        }

        [Test]
        public void SelectWeightedRandom_SingleVideo_ReturnsIt()
        {
            var only = Video("A", 1f);

            Assert.AreSame(only, CrossPromoModule.SelectWeightedRandom(new List<PromoConfiguration> { only }));
        }

        [Test]
        public void SelectWeightedRandom_SingleVideoEqualToExclude_StillReturnsIt()
        {
            var only = Video("A", 1f);

            var picked = CrossPromoModule.SelectWeightedRandom(new List<PromoConfiguration> { only }, only);

            Assert.AreSame(only, picked);
        }

        [Test]
        public void SelectWeightedRandom_AllWeightsZero_FallsBackToFirst()
        {
            var first = Video("A", 0f);
            var second = Video("B", 0f);

            var picked = CrossPromoModule.SelectWeightedRandom(new List<PromoConfiguration> { first, second });

            Assert.AreSame(first, picked);
        }

        [Test]
        public void SelectWeightedRandom_ExcludeLastShown_NeverPicksIt()
        {
            var a = Video("A", 0.5f);
            var b = Video("B", 0.5f);
            var videos = new List<PromoConfiguration> { a, b };

            for (int i = 0; i < 30; i++)
                Assert.AreSame(b, CrossPromoModule.SelectWeightedRandom(videos, a));
        }

        [Test]
        public void SelectWeightedRandom_ExcludeIsMatchedByTitleNotReference()
        {
            var a = Video("A", 0.5f);
            var b = Video("B", 0.5f);
            var videos = new List<PromoConfiguration> { a, b };
            var excludeCopy = a.Copy();

            for (int i = 0; i < 30; i++)
                Assert.AreSame(b, CrossPromoModule.SelectWeightedRandom(videos, excludeCopy));
        }

        [Test]
        public void SelectWeightedRandom_ExcludeMatchesEveryVideo_FallsBackToFullList()
        {
            var first = Video("Same", 0.5f);
            var second = Video("Same", 0.5f);
            var videos = new List<PromoConfiguration> { first, second };

            var picked = CrossPromoModule.SelectWeightedRandom(videos, first);

            Assert.IsTrue(ReferenceEquals(picked, first) || ReferenceEquals(picked, second));
        }

        [Test]
        public void SelectWeightedRandom_ZeroWeightVideoIsNeverPreferredOverWeightedOne()
        {
            var zero = Video("Zero", 0f);
            var full = Video("Full", 1f);
            var videos = new List<PromoConfiguration> { zero, full };

            for (int i = 0; i < 50; i++)
                Assert.AreSame(full, CrossPromoModule.SelectWeightedRandom(videos));
        }

        [Test]
        public void SelectWeightedRandom_AlwaysReturnsMemberOfPool()
        {
            var videos = new List<PromoConfiguration> { Video("A", 0.2f), Video("B", 0.3f), Video("C", 0.5f) };

            for (int i = 0; i < 50; i++)
                Assert.IsTrue(videos.Contains(CrossPromoModule.SelectWeightedRandom(videos)));
        }
    }
}
