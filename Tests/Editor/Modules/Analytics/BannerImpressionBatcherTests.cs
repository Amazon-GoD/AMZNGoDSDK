using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// Banner impression batching (SDK 1.0.9): key merging, 5-minute window, 1000-per-batch limit,
    /// 64 pending batches cap, 10 USD revenue plausibility cap, UTC day boundary, clock going
    /// backwards, identity binding and restoring the persisted buffer. All time is explicit.
    /// </summary>
    public class BannerImpressionBatcherTests
    {
        private const long DayMs = 86400000L;
        private const long DayStart = 19000L * DayMs;
        private const long Base = DayStart + 3600000L; // 01:00 UTC, far from both day edges
        private const string Hash = "hash-a";

        private static BannerImpressionBatch MakeBatch(string eventName, string device, string paidAppId, long firstTs, int n)
        {
            return new BannerImpressionBatch
            {
                EventName = eventName,
                DeviceIdHash = device,
                PaidAppId = paidAppId,
                Network = string.Empty,
                AdUnit = string.Empty,
                RevenuePrecision = string.Empty,
                FirstTs = firstTs,
                N = n,
            };
        }

        // ---------- AddCrossPromo ----------

        [Test]
        public void AddCrossPromo_SameKey_MergesIntoOneBatch()
        {
            var batcher = new BannerImpressionBatcher();
            Assert.AreEqual(0, batcher.AddCrossPromo(Hash, "paid1", Base));
            Assert.AreEqual(0, batcher.AddCrossPromo(Hash, "paid1", Base + 8000));
            Assert.AreEqual(0, batcher.AddCrossPromo(Hash, "paid1", Base + 16000));

            Assert.AreEqual(1, batcher.Count);
            Assert.IsFalse(batcher.IsEmpty);
            var snap = batcher.Snapshot();
            Assert.AreEqual(3, snap[0].N);
            Assert.AreEqual(Base, snap[0].FirstTs);
            Assert.AreEqual(BannerImpressionBatcher.CrossPromoEventName, snap[0].EventName);
            Assert.AreEqual("paid1", snap[0].PaidAppId);
            Assert.AreEqual(Hash, snap[0].DeviceIdHash);
        }

        [Test]
        public void AddCrossPromo_DifferentPaidApp_CreatesSeparateBatches()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);
            batcher.AddCrossPromo(Hash, "paid2", Base);
            Assert.AreEqual(2, batcher.Count);
        }

        [Test]
        public void AddCrossPromo_DifferentDevice_CreatesSeparateBatches()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo("hash-a", "paid1", Base);
            batcher.AddCrossPromo("hash-b", "paid1", Base);
            Assert.AreEqual(2, batcher.Count);
        }

        [Test]
        public void AddCrossPromo_NullIdentityAndPaidApp_StoredAsEmptyStrings()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(null, null, Base);
            var snap = batcher.Snapshot();
            Assert.AreEqual(string.Empty, snap[0].DeviceIdHash);
            Assert.AreEqual(string.Empty, snap[0].PaidAppId);
            Assert.AreEqual(string.Empty, snap[0].Network);
            Assert.AreEqual(string.Empty, snap[0].AdUnit);
            Assert.AreEqual(string.Empty, snap[0].RevenuePrecision);
        }

        [Test]
        public void AddCrossPromo_EarlierTimestampSameDay_LowersFirstTs()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base + 5000);
            batcher.AddCrossPromo(Hash, "paid1", Base);
            Assert.AreEqual(1, batcher.Count);
            Assert.AreEqual(Base, batcher.Snapshot()[0].FirstTs);
        }

        [Test]
        public void AddCrossPromo_FullBatch_ClosesAndStartsNewOne()
        {
            var batcher = new BannerImpressionBatcher();
            for (int i = 0; i < BannerImpressionBatcher.MaxImpressionsPerBatch; i++)
                batcher.AddCrossPromo(Hash, "paid1", Base);

            Assert.AreEqual(1, batcher.Count);
            Assert.AreEqual(1000, batcher.Snapshot()[0].N);

            batcher.AddCrossPromo(Hash, "paid1", Base);
            Assert.AreEqual(2, batcher.Count);
            var snap = batcher.Snapshot();
            Assert.AreEqual(1000, snap[0].N);
            Assert.AreEqual(1, snap[1].N);
        }

        [Test]
        public void AddCrossPromo_UtcDayBoundary_NeverMergesAcrossDays()
        {
            var batcher = new BannerImpressionBatcher();
            long lastMsOfDay = DayStart + DayMs - 1;
            batcher.AddCrossPromo(Hash, "paid1", lastMsOfDay);
            batcher.AddCrossPromo(Hash, "paid1", lastMsOfDay + 1);
            Assert.AreEqual(2, batcher.Count);

            batcher.AddCrossPromo(Hash, "paid1", DayStart);
            Assert.AreEqual(2, batcher.Count);
            Assert.AreEqual(2, batcher.Snapshot()[0].N);
        }

        [Test]
        public void AddCrossPromo_BufferAtCap_DropsOldestAndReportsLostImpressions()
        {
            var batcher = new BannerImpressionBatcher();
            // The oldest batch carries 3 impressions.
            batcher.AddCrossPromo(Hash, "p0", Base);
            batcher.AddCrossPromo(Hash, "p0", Base);
            batcher.AddCrossPromo(Hash, "p0", Base);
            for (int i = 1; i < BannerImpressionBatcher.MaxPendingBatches; i++)
                batcher.AddCrossPromo(Hash, "p" + i, Base + i);

            Assert.AreEqual(BannerImpressionBatcher.MaxPendingBatches, batcher.Count);

            int lost = batcher.AddCrossPromo(Hash, "newest", Base + 1000);

            Assert.AreEqual(3, lost);
            Assert.AreEqual(BannerImpressionBatcher.MaxPendingBatches, batcher.Count);
            foreach (var batch in batcher.Snapshot())
                Assert.AreNotEqual("p0", batch.PaidAppId);
        }

        // ---------- AddMediation ----------

        [Test]
        public void AddMediation_KnownRevenue_SumsInPicoUsd()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(Hash, "net", "unit", "exact", 0.001, Base);
            batcher.AddMediation(Hash, "net", "unit", "exact", 0.002, Base + 1);

            Assert.AreEqual(1, batcher.Count);
            var batch = batcher.Snapshot()[0];
            Assert.AreEqual(BannerImpressionBatcher.MediationEventName, batch.EventName);
            Assert.AreEqual(2, batch.N);
            Assert.IsTrue(batch.RevenueKnown);
            Assert.AreEqual(3000000000L, batch.RevenuePicoUsd);
            Assert.AreEqual(0.003, BannerImpressionBatcher.ToUsd(batch.RevenuePicoUsd), 1e-12);
            Assert.AreEqual("net", batch.Network);
            Assert.AreEqual("unit", batch.AdUnit);
            Assert.AreEqual("exact", batch.RevenuePrecision);
        }

        [Test]
        public void AddMediation_UnknownRevenue_GoesToSeparateBatchWithZeroRevenue()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(Hash, "net", "unit", "exact", 0.5, Base);
            batcher.AddMediation(Hash, "net", "unit", "exact", -1d, Base);
            batcher.AddMediation(Hash, "net", "unit", "exact", -1d, Base);

            Assert.AreEqual(2, batcher.Count);
            var snap = batcher.Snapshot();
            Assert.IsTrue(snap[0].RevenueKnown);
            Assert.AreEqual(1, snap[0].N);
            Assert.IsFalse(snap[1].RevenueKnown);
            Assert.AreEqual(2, snap[1].N);
            Assert.AreEqual(0L, snap[1].RevenuePicoUsd);
        }

        [Test]
        public void AddMediation_DifferentNetworkAdUnitOrPrecision_SeparateBatches()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(Hash, "net", "unit", "exact", 0.1, Base);
            batcher.AddMediation(Hash, "net2", "unit", "exact", 0.1, Base);
            batcher.AddMediation(Hash, "net", "unit2", "exact", 0.1, Base);
            batcher.AddMediation(Hash, "net", "unit", "estimated", 0.1, Base);
            Assert.AreEqual(4, batcher.Count);
        }

        [Test]
        public void AddMediation_NullStrings_StoredAsEmptyAndMergeWithEmpty()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(null, null, null, null, 0.1, Base);
            batcher.AddMediation(string.Empty, string.Empty, string.Empty, string.Empty, 0.1, Base);
            Assert.AreEqual(1, batcher.Count);
            var batch = batcher.Snapshot()[0];
            Assert.AreEqual(2, batch.N);
            Assert.AreEqual(string.Empty, batch.DeviceIdHash);
            Assert.AreEqual(string.Empty, batch.Network);
        }

        [Test]
        public void AddMediation_BufferAtCap_DropsOldest()
        {
            var batcher = new BannerImpressionBatcher();
            for (int i = 0; i < BannerImpressionBatcher.MaxPendingBatches; i++)
                batcher.AddMediation(Hash, "net" + i, "unit", "exact", 0.1, Base + i);

            int lost = batcher.AddMediation(Hash, "overflow", "unit", "exact", 0.1, Base + 500);

            Assert.AreEqual(1, lost);
            Assert.AreEqual(BannerImpressionBatcher.MaxPendingBatches, batcher.Count);
            foreach (var batch in batcher.Snapshot())
                Assert.AreNotEqual("net0", batch.Network);
        }

        // ---------- IsKnownRevenue / ToUsd ----------

        [TestCase(0d, true)]
        [TestCase(0.0001, true)]
        [TestCase(10d, true)]
        [TestCase(10.0000001, false)]
        [TestCase(-0.0000001, false)]
        [TestCase(-1d, false)]
        [TestCase(1000d, false)]
        public void IsKnownRevenue_RespectsZeroToTenUsdRange(double revenue, bool expected)
        {
            Assert.AreEqual(expected, BannerImpressionBatcher.IsKnownRevenue(revenue));
        }

        [Test]
        public void IsKnownRevenue_NaNAndInfinities_AreUnknown()
        {
            Assert.IsFalse(BannerImpressionBatcher.IsKnownRevenue(double.NaN));
            Assert.IsFalse(BannerImpressionBatcher.IsKnownRevenue(double.PositiveInfinity));
            Assert.IsFalse(BannerImpressionBatcher.IsKnownRevenue(double.NegativeInfinity));
        }

        [Test]
        public void AddMediation_OutOfRangeRevenue_TreatedAsUnknown()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(Hash, "net", "unit", "exact", double.NaN, Base);
            batcher.AddMediation(Hash, "net", "unit", "exact", 10.5, Base);
            batcher.AddMediation(Hash, "net", "unit", "exact", double.PositiveInfinity, Base);

            Assert.AreEqual(1, batcher.Count);
            var batch = batcher.Snapshot()[0];
            Assert.IsFalse(batch.RevenueKnown);
            Assert.AreEqual(3, batch.N);
            Assert.AreEqual(0L, batch.RevenuePicoUsd);
        }

        [Test]
        public void ToUsd_ConvertsPicoUsd()
        {
            Assert.AreEqual(0d, BannerImpressionBatcher.ToUsd(0L), 0d);
            Assert.AreEqual(1.5, BannerImpressionBatcher.ToUsd(1500000000000L), 1e-12);
            Assert.AreEqual(10d, BannerImpressionBatcher.ToUsd(10000000000000L), 1e-12);
        }

        // ---------- BindIdentity ----------

        [Test]
        public void BindIdentity_NullOrEmpty_ReturnsFalseAndKeepsBatchUnbound()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(null, "paid1", Base);

            Assert.IsFalse(batcher.BindIdentity(null));
            Assert.IsFalse(batcher.BindIdentity(string.Empty));
            Assert.AreEqual(string.Empty, batcher.Snapshot()[0].DeviceIdHash);
        }

        [Test]
        public void BindIdentity_NothingUnbound_ReturnsFalse()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);
            Assert.IsFalse(batcher.BindIdentity("other"));
            Assert.AreEqual(Hash, batcher.Snapshot()[0].DeviceIdHash);
        }

        [Test]
        public void BindIdentity_UnboundBatch_BecomesTakeableAfterBinding()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(null, "paid1", Base);

            // Unbound batches are never handed out, even with the All mode.
            Assert.AreEqual(0, batcher.TakeDue(Base + DayMs, BannerBatchFlush.All).Count);
            Assert.AreEqual(1, batcher.Count);

            Assert.IsTrue(batcher.BindIdentity(Hash));
            Assert.AreEqual(Hash, batcher.Snapshot()[0].DeviceIdHash);
            Assert.AreEqual(1, batcher.TakeDue(Base, BannerBatchFlush.All).Count);
        }

        [Test]
        public void BindIdentity_MatchingBoundBatch_MergesCountsAndKeepsEarliestTs()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base + 10000);
            batcher.AddCrossPromo(null, "paid1", Base);
            batcher.AddCrossPromo(null, "paid1", Base + 1000);

            Assert.AreEqual(2, batcher.Count);
            Assert.IsTrue(batcher.BindIdentity(Hash));

            Assert.AreEqual(1, batcher.Count);
            var batch = batcher.Snapshot()[0];
            Assert.AreEqual(3, batch.N);
            Assert.AreEqual(Base, batch.FirstTs);
            Assert.AreEqual(Hash, batch.DeviceIdHash);
        }

        [Test]
        public void BindIdentity_MediationMerge_SumsRevenue()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddMediation(Hash, "net", "unit", "exact", 0.25, Base);
            batcher.AddMediation(null, "net", "unit", "exact", 0.75, Base);

            Assert.IsTrue(batcher.BindIdentity(Hash));

            Assert.AreEqual(1, batcher.Count);
            var batch = batcher.Snapshot()[0];
            Assert.AreEqual(2, batch.N);
            Assert.AreEqual(1.0, BannerImpressionBatcher.ToUsd(batch.RevenuePicoUsd), 1e-12);
        }

        [Test]
        public void BindIdentity_MergeWouldExceedBatchLimit_KeepsBothBatches()
        {
            var batcher = new BannerImpressionBatcher();
            for (int i = 0; i < 600; i++)
                batcher.AddCrossPromo(Hash, "paid1", Base);
            for (int i = 0; i < 500; i++)
                batcher.AddCrossPromo(null, "paid1", Base);

            Assert.IsTrue(batcher.BindIdentity(Hash));

            Assert.AreEqual(2, batcher.Count);
            var snap = batcher.Snapshot();
            Assert.AreEqual(Hash, snap[0].DeviceIdHash);
            Assert.AreEqual(Hash, snap[1].DeviceIdHash);
            Assert.AreEqual(600, snap[0].N);
            Assert.AreEqual(500, snap[1].N);
        }

        [Test]
        public void BindIdentity_DifferentUtcDay_DoesNotMerge()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);
            batcher.AddCrossPromo(null, "paid1", Base + DayMs);

            Assert.IsTrue(batcher.BindIdentity(Hash));

            Assert.AreEqual(2, batcher.Count);
        }

        [Test]
        public void BindIdentity_DifferentPaidApp_DoesNotMerge()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);
            batcher.AddCrossPromo(null, "paid2", Base);

            Assert.IsTrue(batcher.BindIdentity(Hash));

            Assert.AreEqual(2, batcher.Count);
        }

        // ---------- TakeDue / CountDue ----------

        [Test]
        public void TakeDue_Required_KeepsBatchInsideWindow()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);

            var taken = batcher.TakeDue(Base + BannerImpressionBatcher.BatchWindowMs + 1000, BannerBatchFlush.Required);

            Assert.AreEqual(0, taken.Count);
            Assert.AreEqual(1, batcher.Count);
        }

        [Test]
        public void TakeDue_Due_RespectsFiveMinuteWindow()
        {
            Assert.AreEqual(300000L, BannerImpressionBatcher.BatchWindowMs);

            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);

            Assert.AreEqual(0, batcher.TakeDue(Base + 299999L, BannerBatchFlush.Due).Count);
            Assert.AreEqual(1, batcher.Count);

            var taken = batcher.TakeDue(Base + 300000L, BannerBatchFlush.Due);
            Assert.AreEqual(1, taken.Count);
            Assert.AreEqual(0, batcher.Count);
            Assert.IsTrue(batcher.IsEmpty);
        }

        [Test]
        public void TakeDue_All_TakesBatchesInsideWindow()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);
            batcher.AddCrossPromo(Hash, "paid2", Base);

            var taken = batcher.TakeDue(Base, BannerBatchFlush.All);

            Assert.AreEqual(2, taken.Count);
            Assert.AreEqual(0, batcher.Count);
        }

        [Test]
        public void TakeDue_Required_TakesFullBatch()
        {
            var batcher = new BannerImpressionBatcher();
            for (int i = 0; i < BannerImpressionBatcher.MaxImpressionsPerBatch; i++)
                batcher.AddCrossPromo(Hash, "paid1", Base);

            var taken = batcher.TakeDue(Base, BannerBatchFlush.Required);

            Assert.AreEqual(1, taken.Count);
            Assert.AreEqual(1000, taken[0].N);
        }

        [Test]
        public void TakeDue_Required_TakesBatchFromPreviousUtcDay()
        {
            var batcher = new BannerImpressionBatcher();
            long lastMsOfDay = DayStart + DayMs - 1;
            batcher.AddCrossPromo(Hash, "paid1", lastMsOfDay - 1000);

            Assert.AreEqual(0, batcher.CountDue(lastMsOfDay, BannerBatchFlush.Required));
            Assert.AreEqual(1, batcher.CountDue(lastMsOfDay + 1, BannerBatchFlush.Required));
            Assert.AreEqual(1, batcher.TakeDue(lastMsOfDay + 1, BannerBatchFlush.Required).Count);
        }

        [Test]
        public void TakeDue_ClockWentBackwards_RequiredTakesBatch()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);

            var taken = batcher.TakeDue(Base - 1, BannerBatchFlush.Required);

            Assert.AreEqual(1, taken.Count);
        }

        [Test]
        public void TakeDue_UnboundBatches_AreNeverTaken()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(null, "paid1", Base);

            Assert.AreEqual(0, batcher.CountDue(Base + DayMs, BannerBatchFlush.Required));
            Assert.AreEqual(0, batcher.CountDue(Base + DayMs, BannerBatchFlush.Due));
            Assert.AreEqual(0, batcher.CountDue(Base + DayMs, BannerBatchFlush.All));
            Assert.AreEqual(0, batcher.TakeDue(Base + DayMs, BannerBatchFlush.All).Count);
            Assert.AreEqual(1, batcher.Count);
        }

        [Test]
        public void TakeDue_MaxCount_TakesOldestFirstAndKeepsTheRest()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "late", Base + 30);
            batcher.AddCrossPromo(Hash, "oldest", Base);
            batcher.AddCrossPromo(Hash, "middle", Base + 10);

            var taken = batcher.TakeDue(Base + 1000, BannerBatchFlush.All, 2);

            Assert.AreEqual(2, taken.Count);
            Assert.AreEqual("oldest", taken[0].PaidAppId);
            Assert.AreEqual("middle", taken[1].PaidAppId);
            Assert.AreEqual(1, batcher.Count);
            Assert.AreEqual("late", batcher.Snapshot()[0].PaidAppId);
        }

        [Test]
        public void TakeDue_MaxCountWithEqualTimestamps_UsesBufferOrder()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "first", Base);
            batcher.AddCrossPromo(Hash, "second", Base);

            var taken = batcher.TakeDue(Base, BannerBatchFlush.All, 1);

            Assert.AreEqual(1, taken.Count);
            Assert.AreEqual("first", taken[0].PaidAppId);
            Assert.AreEqual("second", batcher.Snapshot()[0].PaidAppId);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void TakeDue_NonPositiveMaxCount_TakesNothing(int maxCount)
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);

            var taken = batcher.TakeDue(Base, BannerBatchFlush.All, maxCount);

            Assert.AreEqual(0, taken.Count);
            Assert.AreEqual(1, batcher.Count);
        }

        [Test]
        public void TakeDue_EmptyBuffer_ReturnsEmptyList()
        {
            var batcher = new BannerImpressionBatcher();
            var taken = batcher.TakeDue(Base, BannerBatchFlush.All);
            Assert.IsNotNull(taken);
            Assert.AreEqual(0, taken.Count);
        }

        [Test]
        public void CountDue_MatchesTakeDueWithoutChangingBuffer()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "old", Base);
            batcher.AddCrossPromo(Hash, "fresh", Base + 290000L);

            long now = Base + 300000L;
            Assert.AreEqual(1, batcher.CountDue(now, BannerBatchFlush.Due));
            Assert.AreEqual(2, batcher.Count);
            Assert.AreEqual(1, batcher.TakeDue(now, BannerBatchFlush.Due).Count);
            Assert.AreEqual(1, batcher.Count);
        }

        // ---------- Snapshot / Restore ----------

        [Test]
        public void Snapshot_ReturnsIndependentCopies()
        {
            var batcher = new BannerImpressionBatcher();
            batcher.AddCrossPromo(Hash, "paid1", Base);

            var snap = batcher.Snapshot();
            snap[0].N = 999;
            snap.Clear();

            Assert.AreEqual(1, batcher.Count);
            Assert.AreEqual(1, batcher.Snapshot()[0].N);
        }

        [Test]
        public void Restore_Null_ReturnsZeroAndKeepsBufferEmpty()
        {
            var batcher = new BannerImpressionBatcher();
            Assert.AreEqual(0, batcher.Restore(null, "id", 0L));
            Assert.IsTrue(batcher.IsEmpty);
        }

        [Test]
        public void Restore_ValidBatches_AreRestoredAsIs()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>
            {
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid1", Base, 4),
                MakeBatch(BannerImpressionBatcher.MediationEventName, Hash, string.Empty, Base + 1, 2),
            };

            int dropped = batcher.Restore(saved, "session", 0L);

            Assert.AreEqual(0, dropped);
            Assert.AreEqual(2, batcher.Count);
            var snap = batcher.Snapshot();
            Assert.AreEqual(4, snap[0].N);
            Assert.AreEqual(Hash, snap[0].DeviceIdHash);
            Assert.AreEqual(2, snap[1].N);
        }

        [Test]
        public void Restore_InvalidEntries_AreDropped()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>
            {
                null,
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid1", Base, 0),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid1", Base, -3),
                MakeBatch("unknown_event", Hash, "paid1", Base, 1),
                MakeBatch(null, Hash, "paid1", Base, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, string.Empty, Base, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, null, Base, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid-ok", Base, 1),
            };

            int dropped = batcher.Restore(saved, "session", 0L);

            Assert.AreEqual(7, dropped);
            Assert.AreEqual(1, batcher.Count);
            Assert.AreEqual("paid-ok", batcher.Snapshot()[0].PaidAppId);
        }

        [Test]
        public void Restore_TooOldItems_AreDropped()
        {
            var batcher = new BannerImpressionBatcher();
            long minTs = Base;
            var saved = new List<BannerImpressionBatch>
            {
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "old", minTs - 1, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "edge", minTs, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "new", minTs + 1, 1),
            };

            int dropped = batcher.Restore(saved, "session", minTs);

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(2, batcher.Count);
            var snap = batcher.Snapshot();
            Assert.AreEqual("edge", snap[0].PaidAppId);
            Assert.AreEqual("new", snap[1].PaidAppId);
        }

        [Test]
        public void Restore_MissingIdentity_UsesUnboundIdentity()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>
            {
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, null, "paid1", Base, 1),
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid2", Base, 1),
            };

            batcher.Restore(saved, "session-id", 0L);

            var snap = batcher.Snapshot();
            Assert.AreEqual("session-id", snap[0].DeviceIdHash);
            Assert.AreEqual(Hash, snap[1].DeviceIdHash);
        }

        [Test]
        public void Restore_MissingIdentityAndNullUnbound_StaysEmptyAndUnbound()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>
            {
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, string.Empty, "paid1", Base, 1),
            };

            batcher.Restore(saved, null, 0L);

            Assert.AreEqual(string.Empty, batcher.Snapshot()[0].DeviceIdHash);
            Assert.AreEqual(0, batcher.CountDue(Base + DayMs, BannerBatchFlush.All));
        }

        [Test]
        public void Restore_NullStrings_AreNormalizedToEmpty()
        {
            var batcher = new BannerImpressionBatcher();
            var item = new BannerImpressionBatch
            {
                EventName = BannerImpressionBatcher.MediationEventName,
                DeviceIdHash = Hash,
                PaidAppId = null,
                Network = null,
                AdUnit = null,
                RevenuePrecision = null,
                FirstTs = Base,
                N = 1,
            };

            batcher.Restore(new List<BannerImpressionBatch> { item }, "id", 0L);

            var batch = batcher.Snapshot()[0];
            Assert.AreEqual(string.Empty, batch.PaidAppId);
            Assert.AreEqual(string.Empty, batch.Network);
            Assert.AreEqual(string.Empty, batch.AdUnit);
            Assert.AreEqual(string.Empty, batch.RevenuePrecision);
        }

        [Test]
        public void Restore_OversizedCount_IsClampedToBatchLimit()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>
            {
                MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid1", Base, 5000),
            };

            batcher.Restore(saved, "id", 0L);

            Assert.AreEqual(BannerImpressionBatcher.MaxImpressionsPerBatch, batcher.Snapshot()[0].N);
        }

        [Test]
        public void Restore_RevenueSanitizing_ZeroesUnknownAndNegativeRevenue()
        {
            var batcher = new BannerImpressionBatcher();

            var unknownWithRevenue = MakeBatch(BannerImpressionBatcher.MediationEventName, Hash, string.Empty, Base, 1);
            unknownWithRevenue.RevenueKnown = false;
            unknownWithRevenue.RevenuePicoUsd = 5000L;

            var knownNegative = MakeBatch(BannerImpressionBatcher.MediationEventName, Hash, string.Empty, Base + 1, 1);
            knownNegative.RevenueKnown = true;
            knownNegative.RevenuePicoUsd = -7L;

            var knownValid = MakeBatch(BannerImpressionBatcher.MediationEventName, Hash, string.Empty, Base + 2, 1);
            knownValid.RevenueKnown = true;
            knownValid.RevenuePicoUsd = 123456L;

            batcher.Restore(new List<BannerImpressionBatch> { unknownWithRevenue, knownNegative, knownValid }, "id", 0L);

            var snap = batcher.Snapshot();
            Assert.AreEqual(0L, snap[0].RevenuePicoUsd);
            Assert.AreEqual(0L, snap[1].RevenuePicoUsd);
            Assert.AreEqual(123456L, snap[2].RevenuePicoUsd);
        }

        [Test]
        public void Restore_MoreThanCap_DropsOldestBatches()
        {
            var batcher = new BannerImpressionBatcher();
            var saved = new List<BannerImpressionBatch>();
            const int total = BannerImpressionBatcher.MaxPendingBatches + 6;
            // Newest first, so the oldest ones sit at the end of the saved list.
            for (int i = 0; i < total; i++)
                saved.Add(MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "p" + i, Base + (total - i), 1));

            int dropped = batcher.Restore(saved, "id", 0L);

            Assert.AreEqual(6, dropped);
            Assert.AreEqual(BannerImpressionBatcher.MaxPendingBatches, batcher.Count);
            long minTs = long.MaxValue;
            foreach (var batch in batcher.Snapshot())
            {
                if (batch.FirstTs < minTs)
                    minTs = batch.FirstTs;
            }

            // The 6 oldest (Base + 1 .. Base + 6) are gone.
            Assert.AreEqual(Base + 7, minTs);
        }

        [Test]
        public void Restore_ClonesInput_SoLaterMutationDoesNotLeak()
        {
            var batcher = new BannerImpressionBatcher();
            var item = MakeBatch(BannerImpressionBatcher.CrossPromoEventName, Hash, "paid1", Base, 2);

            batcher.Restore(new List<BannerImpressionBatch> { item }, "id", 0L);
            item.N = 50;

            Assert.AreEqual(2, batcher.Snapshot()[0].N);
        }
    }
}
