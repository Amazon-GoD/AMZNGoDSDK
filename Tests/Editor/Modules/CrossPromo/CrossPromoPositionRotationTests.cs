using System;
using System.Collections.Generic;
using NUnit.Framework;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// CrossPromoPositionRotation: pinned slots (regression 1.0.3: fixed positions), gap skipping,
    /// Peek/Commit semantics with an injected selector. Every Peek uses ignoreShowLimit = true, and
    /// the filler tests use creatives with a null Title so the cooldown registry never reaches
    /// PlayerPrefs.
    /// </summary>
    public class CrossPromoPositionRotationTests
    {
        private static PromoConfiguration Video(string title, int position, string url = "https://cdn.example.com/v.mp4")
        {
            return new PromoConfiguration
            {
                Title = title,
                VideoUrl = url,
                position = position,
                Weight = 0.5f,
            };
        }

        private static PromoConfiguration NoSelector(List<PromoConfiguration> pool)
        {
            Assert.Fail("The weighted selector must not be called when a slot is pinned.");
            return null;
        }

        // ---------- construction ----------

        [Test]
        public void Constructor_NoPositions_IsNotOrderedAndPeekReturnsNull()
        {
            var videos = new List<PromoConfiguration> { Video("A", 0), Video("B", -2) };
            var rotation = new CrossPromoPositionRotation(videos);

            Assert.IsFalse(rotation.IsOrdered);
            Assert.IsNull(rotation.Peek(videos, null, NoSelector, true));
        }

        [Test]
        public void Constructor_NullList_IsNotOrdered()
        {
            var rotation = new CrossPromoPositionRotation((List<PromoConfiguration>)null);

            Assert.IsFalse(rotation.IsOrdered);
        }

        [Test]
        public void Constructor_AssignsRotationIdsFromOriginalIndex()
        {
            var a = Video("A", 1);
            var b = Video("B", 0);
            var videos = new List<PromoConfiguration> { a, null, b };

            var rotation = new CrossPromoPositionRotation(videos);

            Assert.IsTrue(rotation.IsOrdered);
            Assert.AreEqual(0, a.RotationId);
            Assert.AreEqual(2, b.RotationId);
        }

        [Test]
        public void Peek_NullVideos_ReturnsNull()
        {
            var videos = new List<PromoConfiguration> { Video("A", 1) };
            var rotation = new CrossPromoPositionRotation(videos);

            Assert.IsNull(rotation.Peek(null, null, NoSelector, true));
        }

        // ---------- fixed positions ----------

        [Test]
        public void Peek_PinnedVideos_ReturnedInSlotOrderAndCycle()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var c = Video("C", 3);
            var videos = new List<PromoConfiguration> { a, b, c };
            var rotation = new CrossPromoPositionRotation(videos);

            var first = rotation.Peek(videos, null, NoSelector, true);
            Assert.AreSame(a, first.Video);
            Assert.AreEqual(1, first.Slot);
            rotation.Commit(first);

            var second = rotation.Peek(videos, "A", NoSelector, true);
            Assert.AreSame(b, second.Video);
            Assert.AreEqual(2, second.Slot);
            rotation.Commit(second);

            var third = rotation.Peek(videos, "B", NoSelector, true);
            Assert.AreSame(c, third.Video);
            Assert.AreEqual(3, third.Slot);
            rotation.Commit(third);

            var wrapped = rotation.Peek(videos, "C", NoSelector, true);
            Assert.AreSame(a, wrapped.Video);
            Assert.AreEqual(1, wrapped.Slot);
        }

        [Test]
        public void Peek_PinnedVideosListedOutOfOrder_StillFollowSlotNumbers()
        {
            var a = Video("A", 2);
            var b = Video("B", 1);
            var videos = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(videos);

            var first = rotation.Peek(videos, null, NoSelector, true);
            Assert.AreSame(b, first.Video);
            rotation.Commit(first);

            var second = rotation.Peek(videos, null, NoSelector, true);
            Assert.AreSame(a, second.Video);
        }

        [Test]
        public void Peek_WithoutCommit_DoesNotConsumeSlot()
        {
            var videos = new List<PromoConfiguration> { Video("A", 1), Video("B", 2) };
            var rotation = new CrossPromoPositionRotation(videos);

            var first = rotation.Peek(videos, null, NoSelector, true);
            var again = rotation.Peek(videos, null, NoSelector, true);

            Assert.AreSame(first, again);
            Assert.AreEqual(1, again.Slot);
        }

        [Test]
        public void Commit_ForeignOrNullSelection_IsIgnored()
        {
            var a = Video("A", 1);
            var videos = new List<PromoConfiguration> { a, Video("B", 2) };
            var rotation = new CrossPromoPositionRotation(videos);
            var pending = rotation.Peek(videos, null, NoSelector, true);

            rotation.Commit(null);
            rotation.Commit(new CrossPromoPositionRotation.Selection { Video = a, Slot = 1 });

            var after = rotation.Peek(videos, null, NoSelector, true);
            Assert.AreSame(pending, after);
            Assert.AreEqual(1, after.Slot);
        }

        [Test]
        public void Commit_SameSelectionTwice_AdvancesOnlyOnce()
        {
            var videos = new List<PromoConfiguration> { Video("A", 1), Video("B", 2), Video("C", 3) };
            var rotation = new CrossPromoPositionRotation(videos);
            var first = rotation.Peek(videos, null, NoSelector, true);

            rotation.Commit(first);
            rotation.Commit(first);

            Assert.AreEqual(2, rotation.Peek(videos, null, NoSelector, true).Slot);
        }

        // Regression 1.0.3: empty positions must be skipped without scanning every slot number.
        [Test]
        public void Peek_GapBetweenPositions_JumpsToNextOccupiedSlot()
        {
            var a = Video("A", 1);
            var b = Video("B", 4);
            var videos = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(videos);

            var first = rotation.Peek(videos, null, NoSelector, true);
            Assert.AreSame(a, first.Video);
            rotation.Commit(first);

            var second = rotation.Peek(videos, "A", NoSelector, true);
            Assert.AreSame(b, second.Video);
            Assert.AreEqual(4, second.Slot);
            rotation.Commit(second);

            var wrapped = rotation.Peek(videos, "B", NoSelector, true);
            Assert.AreSame(a, wrapped.Video);
            Assert.AreEqual(1, wrapped.Slot);
        }

        [Test]
        public void Peek_HugePosition_DoesNotOverflowOrLoop()
        {
            var a = Video("A", 1);
            var b = Video("B", int.MaxValue);
            var videos = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(videos);

            rotation.Commit(rotation.Peek(videos, null, NoSelector, true));

            var second = rotation.Peek(videos, "A", NoSelector, true);
            Assert.AreSame(b, second.Video);
            Assert.AreEqual(int.MaxValue, second.Slot);
            rotation.Commit(second);

            Assert.AreEqual(1, rotation.Peek(videos, "B", NoSelector, true).Slot);
        }

        [Test]
        public void Peek_DuplicatePosition_FirstCreativeInJsonOwnsTheSlot()
        {
            var first = Video("First", 1);
            var second = Video("Second", 1);
            var videos = new List<PromoConfiguration> { first, second };
            var rotation = new CrossPromoPositionRotation(videos);

            var selection = rotation.Peek(videos, null, NoSelector, true);

            Assert.IsTrue(rotation.IsOrdered);
            Assert.AreSame(first, selection.Video);
            Assert.AreEqual(1, selection.Slot);
        }

        [Test]
        public void Peek_PinnedCreativeWithoutPlayableSource_IsSkipped()
        {
            var a = Video("A", 1);
            var broken = Video("Broken", 2, null);
            var videos = new List<PromoConfiguration> { a, broken };
            var rotation = new CrossPromoPositionRotation(videos);

            rotation.Commit(rotation.Peek(videos, null, NoSelector, true));
            var next = rotation.Peek(videos, "A", NoSelector, true);

            Assert.AreSame(a, next.Video);
            Assert.AreEqual(1, next.Slot);
        }

        [Test]
        public void Peek_FileNameInsteadOfUrl_CountsAsPlayable()
        {
            var local = new PromoConfiguration { Title = "Local", FileName = "clip", position = 1 };
            var videos = new List<PromoConfiguration> { local };
            var rotation = new CrossPromoPositionRotation(videos);

            var selection = rotation.Peek(videos, null, NoSelector, true);

            Assert.AreSame(local, selection.Video);
        }

        [Test]
        public void Peek_NothingPlayable_ReturnsNull()
        {
            var videos = new List<PromoConfiguration> { Video("A", 1, null), Video("B", 2, "  ") };
            var rotation = new CrossPromoPositionRotation(videos);

            Assert.IsNull(rotation.Peek(videos, null, NoSelector, true));
        }

        [Test]
        public void Peek_RebuiltListWithSameRotationIds_KeepsPendingSelection()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var original = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(original);
            var pending = rotation.Peek(original, null, NoSelector, true);

            var aCopy = a.Copy();
            var rebuilt = new List<PromoConfiguration> { aCopy, b.Copy() };
            var again = rotation.Peek(rebuilt, null, NoSelector, true);

            Assert.AreSame(pending, again);
            Assert.AreSame(aCopy, again.Video);
        }

        [Test]
        public void Peek_PendingCreativeDisappeared_MovesToNextAvailableSlot()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var videos = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(videos);
            rotation.Peek(videos, null, NoSelector, true);

            var withoutA = new List<PromoConfiguration> { b };
            var selection = rotation.Peek(withoutA, null, NoSelector, true);

            Assert.AreSame(b, selection.Video);
            Assert.AreEqual(2, selection.Slot);
        }

        // ---------- fillers and the injected selector ----------

        [Test]
        public void Peek_FreeSlot_UsesInjectedSelectorOverFillerPool()
        {
            var filler0 = Video(null, 0, "https://cdn.example.com/f0.mp4");
            var pinned = Video("Pinned", 2);
            var filler1 = Video(null, 0, "https://cdn.example.com/f1.mp4");
            var videos = new List<PromoConfiguration> { filler0, pinned, filler1 };
            var rotation = new CrossPromoPositionRotation(videos);

            int calls = 0;
            List<PromoConfiguration> receivedPool = null;
            Func<List<PromoConfiguration>, PromoConfiguration> pickLast = pool =>
            {
                calls++;
                receivedPool = pool;
                return pool[pool.Count - 1];
            };

            var first = rotation.Peek(videos, "last-title", pickLast, true);

            Assert.AreEqual(1, calls);
            Assert.AreEqual(2, receivedPool.Count);
            Assert.AreSame(filler0, receivedPool[0]);
            Assert.AreSame(filler1, receivedPool[1]);
            Assert.AreSame(filler1, first.Video);
            Assert.AreEqual(1, first.Slot);
            Assert.AreEqual("last-title", first.LastShownTitle);
            Assert.IsNull(first.ResetCooldownTitles);

            // Same slot, pending creative still in the pool: the selection is retained, selector not re-run.
            var again = rotation.Peek(videos, "last-title", pickLast, true);
            Assert.AreSame(first, again);
            Assert.AreEqual(1, calls);

            // The pinned slot never consults the selector.
            rotation.Commit(first);
            var pinnedSelection = rotation.Peek(videos, null, pickLast, true);
            Assert.AreSame(pinned, pinnedSelection.Video);
            Assert.AreEqual(2, pinnedSelection.Slot);
            Assert.AreEqual(1, calls);
            rotation.Commit(pinnedSelection);

            // Slot 3 is a filler slot again, the selector decides.
            Func<List<PromoConfiguration>, PromoConfiguration> pickFirst = pool => pool[0];
            var third = rotation.Peek(videos, null, pickFirst, true);
            Assert.AreSame(filler0, third.Video);
            Assert.AreEqual(3, third.Slot);
            rotation.Commit(third);

            // Cycle length is 3, so the next slot wraps back to 1.
            Assert.AreEqual(1, rotation.Peek(videos, null, pickFirst, true).Slot);
        }

        [Test]
        public void PeekCommit_SameInputsAndSelector_ProduceSameSequence()
        {
            string RunSequence()
            {
                var f0 = Video(null, 0, "https://cdn.example.com/f0.mp4");
                var p = Video("P", 2);
                var f1 = Video(null, 0, "https://cdn.example.com/f1.mp4");
                var videos = new List<PromoConfiguration> { f0, p, f1 };
                var rotation = new CrossPromoPositionRotation(videos);
                Func<List<PromoConfiguration>, PromoConfiguration> pickFirst = pool => pool[0];

                var parts = new List<string>();
                for (int i = 0; i < 7; i++)
                {
                    var selection = rotation.Peek(videos, null, pickFirst, true);
                    parts.Add(selection.Slot + ":" + selection.Video.VideoUrl);
                    rotation.Commit(selection);
                }

                return string.Join("|", parts);
            }

            Assert.AreEqual(RunSequence(), RunSequence());
        }

        // ---------- CopyLayout ----------

        [Test]
        public void CopyLayout_KeepsLayoutButStartsFromFirstSlot()
        {
            var a = Video("A", 1);
            var b = Video("B", 2);
            var videos = new List<PromoConfiguration> { a, b };
            var rotation = new CrossPromoPositionRotation(videos);
            rotation.Commit(rotation.Peek(videos, null, NoSelector, true));

            var copy = rotation.CopyLayout();

            Assert.IsTrue(copy.IsOrdered);
            Assert.AreSame(a, copy.Peek(videos, null, NoSelector, true).Video);
            Assert.AreSame(b, rotation.Peek(videos, null, NoSelector, true).Video);
        }
    }
}
