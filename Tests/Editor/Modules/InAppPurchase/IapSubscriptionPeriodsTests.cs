using System;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// IapSubscriptionPeriods: paid-period math for subscriptions (anchor = purchase date, length = term days,
    /// "now" = trusted time). Covers first/multiple periods, cancel and deferred ceilings, journal seeding
    /// vs. catch-up, and fractional terms (TestTermMinutes). Test times avoid exact period boundaries so the
    /// results do not depend on floating point rounding of TimeSpan.TotalDays.
    /// </summary>
    public class IapSubscriptionPeriodsTests
    {
        private const double WeekTerm = 7.0;
        private const double TenMinutesTerm = 10 / 1440.0;

        private static readonly DateTime Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static long Ms(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeMilliseconds();

        private static long PurchaseMs() => Ms(Start);

        // ---------- CurrentPeriodIndex ----------

        [Test]
        public void CurrentPeriodIndex_AtPurchaseMoment_IsZero()
        {
            Assert.AreEqual(0, IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, 0, 0, Start));
        }

        [Test]
        public void CurrentPeriodIndex_InsideFirstPeriod_IsZero()
        {
            Assert.AreEqual(0, IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(6.5)));
        }

        [Test]
        public void CurrentPeriodIndex_BeforePurchase_IsMinusOne()
        {
            Assert.AreEqual(-1, IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, 0, 0, Start.AddHours(-1)));
        }

        [TestCase(7.5, 1)]
        [TestCase(15.0, 2)]
        [TestCase(29.0, 4)]
        [TestCase(365.5, 52)]
        public void CurrentPeriodIndex_MultipleElapsedPeriods_CountsFullTerms(double elapsedDays, int expected)
        {
            Assert.AreEqual(expected,
                IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(elapsedDays)));
        }

        [Test]
        public void CurrentPeriodIndex_CancelledInSecondPeriod_StopsAtLastPaidBoundary()
        {
            long cancel = Ms(Start.AddDays(10));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(30));

            Assert.AreEqual(1, index);
        }

        [Test]
        public void CurrentPeriodIndex_CancelledJustAfterBoundary_KeepsThatPeriod()
        {
            long cancel = Ms(Start.AddDays(14).AddHours(1));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(30));

            Assert.AreEqual(2, index);
        }

        [Test]
        public void CurrentPeriodIndex_CancelledJustBeforeBoundary_DropsThatPeriod()
        {
            long cancel = Ms(Start.AddDays(14).AddHours(-1));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(30));

            Assert.AreEqual(1, index);
        }

        [Test]
        public void CurrentPeriodIndex_CancelledAtOrBeforePurchase_IsMinusOne()
        {
            long cancel = Ms(Start.AddDays(-1));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(3));

            Assert.AreEqual(-1, index);
        }

        [Test]
        public void CurrentPeriodIndex_CancelledInFuture_DoesNotLimitElapsedPeriods()
        {
            long cancel = Ms(Start.AddDays(100));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(15));

            Assert.AreEqual(2, index);
        }

        [Test]
        public void CurrentPeriodIndex_DeferredDate_ActsAsCeiling()
        {
            long deferred = Ms(Start.AddDays(20));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, 0, deferred, Start.AddDays(30));

            Assert.AreEqual(2, index);
        }

        [Test]
        public void CurrentPeriodIndex_BothCeilings_UsesTheLowerOne()
        {
            long cancel = Ms(Start.AddDays(24));
            long deferred = Ms(Start.AddDays(10));

            int index = IapSubscriptionPeriods.CurrentPeriodIndex(PurchaseMs(), WeekTerm, cancel, deferred, Start.AddDays(30));

            Assert.AreEqual(1, index);
        }

        [Test]
        public void CurrentPeriodIndex_FractionalTerm_CountsMinuteTerms()
        {
            int index = IapSubscriptionPeriods.CurrentPeriodIndex(
                PurchaseMs(), TenMinutesTerm, 0, 0, Start.AddMinutes(35));

            Assert.AreEqual(3, index);
        }

        // ---------- PeriodStartUtc ----------

        [Test]
        public void PeriodStartUtc_IndexZero_IsPurchaseDate()
        {
            Assert.AreEqual(Start, IapSubscriptionPeriods.PeriodStartUtc(PurchaseMs(), WeekTerm, 0));
        }

        [Test]
        public void PeriodStartUtc_IndexTwo_IsTwoTermsAfterPurchase()
        {
            Assert.AreEqual(Start.AddDays(14), IapSubscriptionPeriods.PeriodStartUtc(PurchaseMs(), WeekTerm, 2));
        }

        [Test]
        public void PeriodStartUtc_FractionalTerm_AddsMinutes()
        {
            DateTime actual = IapSubscriptionPeriods.PeriodStartUtc(PurchaseMs(), TenMinutesTerm, 3);

            Assert.That((actual - Start.AddMinutes(30)).TotalSeconds, Is.EqualTo(0).Within(1));
        }

        // ---------- PeriodsToFire: invalid input ----------

        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void PeriodsToFire_NonPositiveTerm_ReturnsEmpty(double termDays)
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), termDays, 0, 0, Start.AddDays(15), true, -1);

            Assert.IsEmpty(result);
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        public void PeriodsToFire_NonPositivePurchaseDate_ReturnsEmpty(long purchaseMs)
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                purchaseMs, WeekTerm, 0, 0, Start.AddDays(15), true, -1);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_NowBeforePurchase_ReturnsEmptyEvenWithJournal()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddHours(-1), true, -1);

            Assert.IsEmpty(result);
        }

        // ---------- PeriodsToFire: journal has entry (catch-up) ----------

        [Test]
        public void PeriodsToFire_JournalKnown_NothingFiredYet_ReturnsAllElapsedPeriods()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), true, -1);

            Assert.That(result, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void PeriodsToFire_JournalKnown_ReturnsOnlyPeriodsAfterLastFired()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), true, 0);

            Assert.That(result, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void PeriodsToFire_JournalKnown_AllAlreadyFired_ReturnsEmpty()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), true, 2);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_JournalKnown_LastFiredAheadOfCurrent_ReturnsEmpty()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), true, 10);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_JournalKnown_FirstPeriodOnly_ReturnsZero()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(2), true, -1);

            Assert.That(result, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void PeriodsToFire_JournalKnown_CancelDateStopsPeriods()
        {
            long cancel = Ms(Start.AddDays(10));

            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(30), true, -1);

            Assert.That(result, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void PeriodsToFire_JournalKnown_DeferredDateStopsPeriods()
        {
            long deferred = Ms(Start.AddDays(20));

            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, deferred, Start.AddDays(30), true, -1);

            Assert.That(result, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void PeriodsToFire_JournalKnown_FractionalTerm_ReturnsMinutePeriods()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), TenMinutesTerm, 0, 0, Start.AddMinutes(35), true, 0);

            Assert.That(result, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        // ---------- PeriodsToFire: journal does not know the receipt (seeding) ----------

        [Test]
        public void PeriodsToFire_NoJournal_ReturnsOnlyCurrentPeriod()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), false, -1);

            Assert.That(result, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void PeriodsToFire_NoJournal_IgnoresLastFiredIndex()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(15), false, 5);

            Assert.That(result, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void PeriodsToFire_NoJournal_FirstPeriod_ReturnsZero()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, 0, Start.AddDays(1), false, -1);

            Assert.That(result, Is.EqualTo(new[] { 0 }));
        }

        // Regression (IAP-14 seeding, logs 2026-08-14): a clean install must not grant a period for every
        // cancelled receipt of the history; a receipt cancelled inside the current period is a refund.
        [Test]
        public void PeriodsToFire_NoJournal_CancelledInsideCurrentPeriod_ReturnsEmpty()
        {
            long cancel = Ms(Start.AddDays(16));

            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(17), false, -1);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_NoJournal_CancelledInPastPeriod_ReturnsEmpty()
        {
            long cancel = Ms(Start.AddDays(10));

            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, cancel, 0, Start.AddDays(30), false, -1);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_NoJournal_DeferredInPast_ReturnsEmpty()
        {
            long deferred = Ms(Start.AddDays(20));

            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), WeekTerm, 0, deferred, Start.AddDays(30), false, -1);

            Assert.IsEmpty(result);
        }

        [Test]
        public void PeriodsToFire_NoJournal_FractionalTerm_ReturnsCurrentMinutePeriod()
        {
            var result = IapSubscriptionPeriods.PeriodsToFire(
                PurchaseMs(), TenMinutesTerm, 0, 0, Start.AddMinutes(35), false, -1);

            Assert.That(result, Is.EqualTo(new[] { 3 }));
        }
    }
}
