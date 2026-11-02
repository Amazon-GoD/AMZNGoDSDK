using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// IapReconcileSession request ownership (stale-run protection) and page accounting. Receipt-bearing paths
    /// are not covered here: PurchaseReceipt is a vendored Amazon DTO and is deliberately not used in tests.
    /// </summary>
    public class IapReconcileSessionTests
    {
        private IapReconcileSession _session;

        [SetUp]
        public void SetUp()
        {
            _session = new IapReconcileSession();
        }

        [Test]
        public void OwnsResponse_NoRegisteredRequests_AcceptsAnyResponse()
        {
            Assert.IsTrue(_session.OwnsResponse("req-1"));
            Assert.IsTrue(_session.OwnsResponse(null));
            Assert.IsTrue(_session.OwnsResponse(""));
        }

        [Test]
        public void OwnsResponse_RegisteredRequest_AcceptsOwnAndRejectsForeign()
        {
            _session.RegisterRequest("req-1");

            Assert.IsTrue(_session.OwnsResponse("req-1"));
            Assert.IsFalse(_session.OwnsResponse("req-old"));
        }

        [Test]
        public void OwnsResponse_RegisteredRequest_EmptyOrNullResponseId_IsAccepted()
        {
            _session.RegisterRequest("req-1");

            Assert.IsTrue(_session.OwnsResponse(null));
            Assert.IsTrue(_session.OwnsResponse(""));
        }

        [Test]
        public void OwnsResponse_MultipleRegisteredRequests_AcceptsEach()
        {
            _session.RegisterRequest("req-1");
            _session.RegisterRequest("req-2");

            Assert.IsTrue(_session.OwnsResponse("req-1"));
            Assert.IsTrue(_session.OwnsResponse("req-2"));
            Assert.IsFalse(_session.OwnsResponse("req-3"));
        }

        [Test]
        public void RegisterRequest_NullOrEmpty_IsIgnoredAndKeepsSoftMatching()
        {
            _session.RegisterRequest(null);
            _session.RegisterRequest("");

            Assert.IsTrue(_session.OwnsResponse("anything"));
        }

        [Test]
        public void RegisterRequest_SameIdTwice_IsIdempotent()
        {
            _session.RegisterRequest("req-1");
            _session.RegisterRequest("req-1");

            Assert.IsTrue(_session.OwnsResponse("req-1"));
            Assert.IsFalse(_session.OwnsResponse("req-2"));
        }

        [Test]
        public void AddPage_Null_CountsPageWithoutThrowing()
        {
            Assert.DoesNotThrow(() => _session.AddPage(null));
            Assert.AreEqual(1, _session.PageCount);
        }

        [Test]
        public void AddPage_EmptyPages_IncrementPageCount()
        {
            _session.AddPage(null);
            _session.AddPage(null);
            _session.AddPage(null);

            Assert.AreEqual(3, _session.PageCount);
        }

        [Test]
        public void PageCount_NewSession_IsZero()
        {
            Assert.AreEqual(0, _session.PageCount);
        }

        [Test]
        public void Complete_NoReceipts_ReturnsEmptyResult()
        {
            var result = _session.Complete(new HashSet<string> { "sub_weekly" });

            Assert.IsEmpty(result.ActiveSkus);
            Assert.IsEmpty(result.ActiveAnySkus);
            Assert.AreEqual(0, result.Receipts.Count);
        }

        [Test]
        public void Complete_AfterEmptyPage_ReturnsEmptyResult()
        {
            _session.AddPage(null);

            var result = _session.Complete(new HashSet<string>());

            Assert.IsEmpty(result.ActiveSkus);
            Assert.IsEmpty(result.ActiveAnySkus);
            Assert.AreEqual(0, result.Receipts.Count);
        }
    }
}
