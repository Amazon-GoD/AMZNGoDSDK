using System;
using AMZNGoDSDK.Runtime.ABTesting;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// FeaturesTesting registry (testId -> group -> handler): registration validation, update in place,
    /// run/lookup semantics for unknown tests and groups, and auto-cleanup on removal.
    /// </summary>
    public class FeaturesTestingTests
    {
        private sealed class CountingFeature : ITestableFeature
        {
            public int Runs;
            public void Run() { Runs++; }
        }

        private sealed class ThrowingFeature : ITestableFeature
        {
            public void Run() { throw new InvalidOperationException("boom"); }
        }

        private FeaturesTesting _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new FeaturesTesting();
        }

        [Test]
        public void TryRunFeature_RegisteredGroup_RunsOnceAndReturnsTrue()
        {
            var feature = new CountingFeature();
            _registry.AddOrUpdateFeature("test", "control", feature);

            Assert.IsTrue(_registry.TryRunFeature("test", "control"));
            Assert.AreEqual(1, feature.Runs);
        }

        [Test]
        public void TryRunFeature_RunsOnlyRequestedGroup()
        {
            var control = new CountingFeature();
            var variant = new CountingFeature();
            _registry.AddOrUpdateFeature("test", "control", control);
            _registry.AddOrUpdateFeature("test", "variant", variant);

            Assert.IsTrue(_registry.TryRunFeature("test", "variant"));

            Assert.AreEqual(0, control.Runs);
            Assert.AreEqual(1, variant.Runs);
        }

        [Test]
        public void TryRunFeature_UnknownTest_ReturnsFalse()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsFalse(_registry.TryRunFeature("other", "control"));
        }

        [Test]
        public void TryRunFeature_UnknownGroup_ReturnsFalseAndRunsNothing()
        {
            var feature = new CountingFeature();
            _registry.AddOrUpdateFeature("test", "control", feature);

            Assert.IsFalse(_registry.TryRunFeature("test", "missing"));
            Assert.AreEqual(0, feature.Runs);
        }

        [Test]
        public void TryRunFeature_NullArguments_ReturnFalse()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsFalse(_registry.TryRunFeature(null, "control"));
            Assert.IsFalse(_registry.TryRunFeature("test", null));
        }

        [Test]
        public void TryRunFeature_FeatureThrows_ExceptionPropagatesToCaller()
        {
            _registry.AddOrUpdateFeature("test", "control", new ThrowingFeature());

            Assert.Throws<InvalidOperationException>(() => _registry.TryRunFeature("test", "control"));
        }

        [Test]
        public void RunFeature_RegisteredGroup_RunsFeature()
        {
            var feature = new CountingFeature();
            _registry.AddOrUpdateFeature("test", "control", feature);

            _registry.RunFeature("test", "control");

            Assert.AreEqual(1, feature.Runs);
        }

        [Test]
        public void RunFeature_UnknownTest_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _registry.RunFeature("missing", "control"));
        }

        [Test]
        public void AddOrUpdateFeature_ExistingGroup_ReplacesHandler()
        {
            var first = new CountingFeature();
            var second = new CountingFeature();
            _registry.AddOrUpdateFeature("test", "control", first);
            _registry.AddOrUpdateFeature("test", "control", second);

            _registry.TryRunFeature("test", "control");

            Assert.AreEqual(0, first.Runs);
            Assert.AreEqual(1, second.Runs);
        }

        [Test]
        public void AddOrUpdateFeature_NewGroupOfExistingTest_KeepsOtherGroups()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());
            _registry.AddOrUpdateFeature("test", "variant", new CountingFeature());

            Assert.IsTrue(_registry.HasFeature("test", "control"));
            Assert.IsTrue(_registry.HasFeature("test", "variant"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AddOrUpdateFeature_BlankRemoteId_ThrowsArgumentNull(string remoteId)
        {
            Assert.Throws<ArgumentNullException>(() =>
                _registry.AddOrUpdateFeature(remoteId, "control", new CountingFeature()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AddOrUpdateFeature_BlankGroupName_ThrowsArgumentNull(string groupName)
        {
            Assert.Throws<ArgumentNullException>(() =>
                _registry.AddOrUpdateFeature("test", groupName, new CountingFeature()));
        }

        [Test]
        public void AddOrUpdateFeature_NullFeature_ThrowsArgumentNullAndRegistersNothing()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.AddOrUpdateFeature("test", "control", null));
            Assert.IsFalse(_registry.HasTest("test"));
        }

        [Test]
        public void HasFeature_ChecksTestAndGroup()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsTrue(_registry.HasFeature("test", "control"));
            Assert.IsFalse(_registry.HasFeature("test", "variant"));
            Assert.IsFalse(_registry.HasFeature("other", "control"));
            Assert.IsFalse(_registry.HasFeature(null, "control"));
            Assert.IsFalse(_registry.HasFeature("test", null));
        }

        [Test]
        public void HasFeature_IsCaseSensitive()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsFalse(_registry.HasFeature("TEST", "control"));
            Assert.IsFalse(_registry.HasFeature("test", "Control"));
        }

        [Test]
        public void HasTest_ReflectsRegistrationAndNull()
        {
            Assert.IsFalse(_registry.HasTest("test"));
            Assert.IsFalse(_registry.HasTest(null));

            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsTrue(_registry.HasTest("test"));
            Assert.IsFalse(_registry.HasTest("other"));
        }

        [Test]
        public void GetFirstGroupForTest_UnknownOrNullTest_ReturnsNull()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.IsNull(_registry.GetFirstGroupForTest("other"));
            Assert.IsNull(_registry.GetFirstGroupForTest(null));
        }

        [Test]
        public void GetFirstGroupForTest_SingleGroup_ReturnsIt()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.AreEqual("control", _registry.GetFirstGroupForTest("test"));
        }

        [Test]
        public void GetFirstGroupForTest_MultipleGroups_ReturnsRegisteredGroup()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());
            _registry.AddOrUpdateFeature("test", "variant", new CountingFeature());

            string first = _registry.GetFirstGroupForTest("test");

            Assert.IsNotNull(first);
            Assert.IsTrue(_registry.HasFeature("test", first));
        }

        [Test]
        public void RemoveFeature_RemovesOnlyThatGroup()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());
            _registry.AddOrUpdateFeature("test", "variant", new CountingFeature());

            _registry.RemoveFeature("test", "variant");

            Assert.IsTrue(_registry.HasFeature("test", "control"));
            Assert.IsFalse(_registry.HasFeature("test", "variant"));
            Assert.IsTrue(_registry.HasTest("test"));
        }

        [Test]
        public void RemoveFeature_LastGroup_RemovesWholeTest()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            _registry.RemoveFeature("test", "control");

            Assert.IsFalse(_registry.HasTest("test"));
            Assert.IsNull(_registry.GetFirstGroupForTest("test"));
        }

        [Test]
        public void RemoveFeature_UnknownOrNull_IsNoOp()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.DoesNotThrow(() => _registry.RemoveFeature("other", "control"));
            Assert.DoesNotThrow(() => _registry.RemoveFeature("test", "missing"));
            Assert.DoesNotThrow(() => _registry.RemoveFeature(null, null));
            Assert.IsTrue(_registry.HasFeature("test", "control"));
        }

        [Test]
        public void RemoveRemoteId_RemovesAllGroups()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());
            _registry.AddOrUpdateFeature("test", "variant", new CountingFeature());

            _registry.RemoveRemoteId("test");

            Assert.IsFalse(_registry.HasTest("test"));
            Assert.IsFalse(_registry.HasFeature("test", "control"));
        }

        [Test]
        public void RemoveRemoteId_NullOrUnknown_IsNoOp()
        {
            _registry.AddOrUpdateFeature("test", "control", new CountingFeature());

            Assert.DoesNotThrow(() => _registry.RemoveRemoteId(null));
            Assert.DoesNotThrow(() => _registry.RemoveRemoteId("other"));
            Assert.IsTrue(_registry.HasTest("test"));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            _registry.AddOrUpdateFeature("a", "control", new CountingFeature());
            _registry.AddOrUpdateFeature("b", "control", new CountingFeature());

            _registry.Clear();

            Assert.IsFalse(_registry.HasTest("a"));
            Assert.IsFalse(_registry.HasTest("b"));
        }
    }
}
