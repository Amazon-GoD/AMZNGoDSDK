using System.Collections.Generic;
using NUnit.Framework;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// PromosConfigurationInfo / PromoConfiguration pure paths: availability with the show limit
    /// ignored, filtering self/installed promos, master pool bookkeeping and Copy. Creatives carry
    /// positions so ApplyCooldownFilter takes the ordered branch and never reads cooldown
    /// PlayerPrefs; the show-limit check itself (PlayerPrefs, SdkModuleRegistry) is never invoked.
    /// </summary>
    public class PromosConfigurationInfoTests
    {
        private static PromoConfiguration Video(string title, int position, params string[] packages)
        {
            var video = new PromoConfiguration
            {
                Title = title,
                VideoUrl = "https://cdn.example.com/" + title + ".mp4",
                position = position,
                Weight = 0.5f,
            };
            video.AppPackageName = new List<string>(packages);
            return video;
        }

        private static PromosConfigurationInfo Info(params PromoConfiguration[] videos)
        {
            return new PromosConfigurationInfo
            {
                Weight = 1f,
                Videos = new List<PromoConfiguration>(videos),
            };
        }

        // ---------- HasAvailableVideos ----------

        [Test]
        public void HasAvailableVideos_IgnoreShowLimit_TrueWhenVideosExist()
        {
            var info = Info(Video("A", 1));

            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));
        }

        [Test]
        public void HasAvailableVideos_IgnoreShowLimit_FalseWhenEmptyOrNull()
        {
            Assert.IsFalse(Info().HasAvailableVideos(ignoreShowLimit: true));

            var nullList = new PromosConfigurationInfo { Videos = null };
            Assert.IsFalse(nullList.HasAvailableVideos(ignoreShowLimit: true));
        }

        [Test]
        public void HasAvailableVideos_MasterPoolTakesPrecedenceOverVideos()
        {
            var info = Info(Video("A", 1), Video("B", 2));
            info.ApplyCooldownFilter(null, ignoreShowLimit: true);
            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));

            info.RemoveFromMaster("A");
            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));

            // Videos still holds creatives, but the master (full) pool is empty.
            info.RemoveFromMaster("B");
            Assert.IsFalse(info.HasAvailableVideos(ignoreShowLimit: true));
        }

        // ---------- RemoveFromMaster ----------

        [Test]
        public void RemoveFromMaster_WithoutMasterPool_DoesNotThrow()
        {
            var info = Info(Video("A", 1));

            Assert.DoesNotThrow(() => info.RemoveFromMaster("A"));
            Assert.AreEqual(1, info.Videos.Count);
        }

        [Test]
        public void RemoveFromMaster_UnknownTitle_KeepsMasterPool()
        {
            var info = Info(Video("A", 1));
            info.ApplyCooldownFilter(null, ignoreShowLimit: true);

            info.RemoveFromMaster("missing");

            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));
        }

        [Test]
        public void RemoveFromMaster_DoesNotTouchActiveVideos()
        {
            var info = Info(Video("A", 1), Video("B", 2));
            info.ApplyCooldownFilter(null, ignoreShowLimit: true);

            info.RemoveFromMaster("A");

            Assert.AreEqual(2, info.Videos.Count);
        }

        // ---------- ApplyCooldownFilter (ordered branch) ----------

        [Test]
        public void ApplyCooldownFilter_Ordered_RestoresFullPoolAsCopies()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var info = Info(a, b);

            info.ApplyCooldownFilter(null, ignoreShowLimit: true);

            Assert.AreEqual(2, info.Videos.Count);
            Assert.AreNotSame(a, info.Videos[0]);
            Assert.AreEqual("A", info.Videos[0].Title);
            Assert.AreEqual("B", info.Videos[1].Title);
            Assert.AreEqual(a.RotationId, info.Videos[0].RotationId);
        }

        // ---------- RemoveInstalledOrSelfPromo ----------

        [Test]
        public void RemoveInstalledOrSelfPromo_SelfPackage_IsRemovedCaseInsensitively()
        {
            var info = Info(
                Video("Self", 1, "com.Own.Game"),
                Video("Other", 2, "com.other.game"));

            info.RemoveInstalledOrSelfPromo("com.own.game");

            Assert.AreEqual(1, info.Videos.Count);
            Assert.AreEqual("Other", info.Videos[0].Title);
        }

        [Test]
        public void RemoveInstalledOrSelfPromo_MatchesAnyPackageInList()
        {
            var info = Info(
                Video("Multi", 1, "com.other.one", "com.own.game"),
                Video("Keep", 2, "com.other.two"));

            info.RemoveInstalledOrSelfPromo("com.own.game");

            Assert.AreEqual(1, info.Videos.Count);
            Assert.AreEqual("Keep", info.Videos[0].Title);
        }

        [Test]
        public void RemoveInstalledOrSelfPromo_NoMatchOrEmptyOwnId_KeepsEverything()
        {
            var info = Info(
                Video("A", 1, "com.a.game"),
                Video("B", 2, "com.b.game"));

            info.RemoveInstalledOrSelfPromo("com.own.game");
            info.RemoveInstalledOrSelfPromo(null);
            info.RemoveInstalledOrSelfPromo(string.Empty);

            Assert.AreEqual(2, info.Videos.Count);
        }

        [Test]
        public void RemoveInstalledOrSelfPromo_NullEntriesAndMissingPackageLists_AreKept()
        {
            var noPackages = Video("NoPackages", 1);
            noPackages.AppPackageName = null;
            var emptyEntry = Video("EmptyEntry", 2, string.Empty, null);
            var info = Info(noPackages, null, emptyEntry);

            Assert.DoesNotThrow(() => info.RemoveInstalledOrSelfPromo("com.own.game"));

            Assert.AreEqual(3, info.Videos.Count);
        }

        [Test]
        public void RemoveInstalledOrSelfPromo_AlsoRemovesFromMasterPool()
        {
            var info = Info(
                Video("Self", 1, "com.own.game"),
                Video("Other", 2, "com.other.game"));
            info.ApplyCooldownFilter(null, ignoreShowLimit: true);

            info.RemoveInstalledOrSelfPromo("com.own.game");
            Assert.AreEqual(1, info.Videos.Count);
            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));

            // Only "Other" is left in the master pool; removing it empties the pool.
            info.RemoveFromMaster("Other");
            Assert.IsFalse(info.HasAvailableVideos(ignoreShowLimit: true));
        }

        [Test]
        public void RemoveInstalledOrSelfPromo_IsSafeToCallRepeatedly()
        {
            var info = Info(Video("Self", 1, "com.own.game"), Video("Other", 2, "com.other.game"));

            info.RemoveInstalledOrSelfPromo("com.own.game");
            info.RemoveInstalledOrSelfPromo("com.own.game");

            Assert.AreEqual(1, info.Videos.Count);
        }

        // ---------- Copy ----------

        [Test]
        public void Copy_CopiesWeightAndVideoReferencesIntoIndependentList()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var info = Info(a, b);
            info.Weight = 0.7f;

            var copy = info.Copy();

            Assert.AreNotSame(info, copy);
            Assert.AreEqual(0.7f, copy.Weight, 1e-6f);
            Assert.AreEqual(2, copy.Videos.Count);
            Assert.AreSame(a, copy.Videos[0]);
            Assert.AreSame(b, copy.Videos[1]);

            copy.Videos.RemoveAt(0);
            Assert.AreEqual(2, info.Videos.Count);
        }

        [Test]
        public void Copy_KeepsPositionLayout()
        {
            var info = Info(Video("A", 1), Video("B", 2));

            var copy = info.Copy();

            Assert.IsTrue(copy.PositionRotation.IsOrdered);
        }

        [Test]
        public void Copy_WithoutPositions_IsNotOrdered()
        {
            var info = Info(Video("A", 0), Video("B", 0));

            Assert.IsFalse(info.Copy().PositionRotation.IsOrdered);
        }

        [Test]
        public void Copy_MasterPoolIsDeepCopied()
        {
            var info = Info(Video("A", 1));
            info.ApplyCooldownFilter(null, ignoreShowLimit: true);

            var copy = info.Copy();
            copy.RemoveFromMaster("A");

            Assert.IsFalse(copy.HasAvailableVideos(ignoreShowLimit: true));
            Assert.IsTrue(info.HasAvailableVideos(ignoreShowLimit: true));
        }

        // ---------- PromoConfiguration ----------

        [TestCase(5, 0, 5)]
        [TestCase(5, 9, 5)]
        [TestCase(0, 9, 9)]
        [TestCase(0, 0, 0)]
        [TestCase(-1, 3, 3)]
        public void PromoConfiguration_EffectiveShowLimit_PrefersCapOverMaxShowCount(int cap, int maxShowCount, int expected)
        {
            var config = new PromoConfiguration { cap = cap, MaxShowCount = maxShowCount };

            Assert.AreEqual(expected, config.EffectiveShowLimit);
        }

        [Test]
        public void PromoConfiguration_Copy_CopiesFieldsAndDetachesPackageList()
        {
            var original = new PromoConfiguration
            {
                Title = "T",
                ButtonText = "Go",
                FileName = "clip",
                VideoUrl = "https://cdn.example.com/t.mp4",
                BannerUrl = "https://cdn.example.com/t.png",
                TrackingUrl = "https://track.example.com",
                RedirectUrl = "amzn://apps/android?asin=X",
                adjust_click_url = "https://app.adjust.com/c",
                adjust_impression_url = "https://view.adjust.com/i",
                campaign = "camp",
                adgroup = "group",
                creative = "cr",
                OverlayShowDelayInSeconds = 4,
                CloseShowDelayInSeconds = 6,
                FileExtension = VideoExtension.webm,
                Weight = 0.3f,
                AppPackageName = new List<string> { "com.a" },
                MaxShowCount = 7,
                cap = 2,
                position = 3,
                RotationId = 5,
            };

            var copy = original.Copy();

            Assert.AreNotSame(original, copy);
            Assert.AreEqual("T", copy.Title);
            Assert.AreEqual("Go", copy.ButtonText);
            Assert.AreEqual("clip", copy.FileName);
            Assert.AreEqual(original.VideoUrl, copy.VideoUrl);
            Assert.AreEqual(original.BannerUrl, copy.BannerUrl);
            Assert.AreEqual(original.TrackingUrl, copy.TrackingUrl);
            Assert.AreEqual(original.RedirectUrl, copy.RedirectUrl);
            Assert.AreEqual(original.adjust_click_url, copy.adjust_click_url);
            Assert.AreEqual(original.adjust_impression_url, copy.adjust_impression_url);
            Assert.AreEqual("camp", copy.campaign);
            Assert.AreEqual("group", copy.adgroup);
            Assert.AreEqual("cr", copy.creative);
            Assert.AreEqual(4, copy.OverlayShowDelayInSeconds);
            Assert.AreEqual(6, copy.CloseShowDelayInSeconds);
            Assert.AreEqual(VideoExtension.webm, copy.FileExtension);
            Assert.AreEqual(0.3f, copy.Weight, 1e-6f);
            Assert.AreEqual(7, copy.MaxShowCount);
            Assert.AreEqual(2, copy.cap);
            Assert.AreEqual(3, copy.position);
            Assert.AreEqual(5, copy.RotationId);

            copy.AppPackageName.Add("com.b");
            Assert.AreEqual(1, original.AppPackageName.Count);
        }

        [Test]
        public void PromoConfiguration_Copy_NullPackageListBecomesEmptyList()
        {
            var original = new PromoConfiguration { AppPackageName = null };

            var copy = original.Copy();

            Assert.IsNotNull(copy.AppPackageName);
            Assert.AreEqual(0, copy.AppPackageName.Count);
        }
    }
}
