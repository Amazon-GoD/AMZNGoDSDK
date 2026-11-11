using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// Banner slot controls: aggregation of what several banner instances show
    /// (CrossPromoBannerSlot) and the runtime switches kept on CrossPromoModule.
    /// </summary>
    public class CrossPromoBannerSlotTests
    {
        // ---------- CrossPromoBannerSlot ----------

        [Test]
        public void Slot_StartsEmpty()
        {
            Assert.AreEqual(BannerProvider.None, new CrossPromoBannerSlot().Current);
        }

        [Test]
        public void Slot_ReportsChangeOnlyWhenAggregateChanges()
        {
            var slot = new CrossPromoBannerSlot();

            Assert.IsTrue(slot.Report(1, BannerProvider.CrossPromo));
            Assert.IsFalse(slot.Report(1, BannerProvider.CrossPromo));
            Assert.IsFalse(slot.Report(2, BannerProvider.CrossPromo));
            Assert.AreEqual(BannerProvider.CrossPromo, slot.Current);
        }

        [Test]
        public void Slot_AppLovinWinsOverCrossPromo()
        {
            var slot = new CrossPromoBannerSlot();
            slot.Report(1, BannerProvider.CrossPromo);

            Assert.IsTrue(slot.Report(2, BannerProvider.AppLovin));
            Assert.AreEqual(BannerProvider.AppLovin, slot.Current);

            // The instance that owned MAX goes away; the other one still shows its creative.
            Assert.IsTrue(slot.Report(2, BannerProvider.None));
            Assert.AreEqual(BannerProvider.CrossPromo, slot.Current);
        }

        [Test]
        public void Slot_NoneFromEveryInstance_EmptiesSlot()
        {
            var slot = new CrossPromoBannerSlot();
            slot.Report(1, BannerProvider.AppLovin);
            slot.Report(2, BannerProvider.CrossPromo);

            slot.Report(1, BannerProvider.None);
            Assert.IsTrue(slot.Report(2, BannerProvider.None));
            Assert.AreEqual(BannerProvider.None, slot.Current);
        }

        [Test]
        public void Slot_NoneFromUnknownInstance_IsNoChange()
        {
            var slot = new CrossPromoBannerSlot();

            Assert.IsFalse(slot.Report(7, BannerProvider.None));
        }

        // ---------- provider routing ----------

        // moduleEnabled, appLovinAllowed, creativeAllowed, configReturnedVideos, hasFill → expected
        [TestCase(true, true, true, true, false, true, TestName = "Routing_CapsExhausted_GoesToAppLovin")]
        [TestCase(true, true, true, true, true, false, TestName = "Routing_HasFill_StaysOnCreative")]
        [TestCase(true, true, true, false, false, false, TestName = "Routing_ConfigNotLoaded_StaysOnCreative")]
        [TestCase(true, false, true, true, false, false, TestName = "Routing_AppLovinOff_NeverGoesToAppLovin")]
        [TestCase(true, true, false, true, true, true, TestName = "Routing_CreativeOff_GoesToAppLovinDespiteFill")]
        [TestCase(true, true, false, false, false, true, TestName = "Routing_CreativeOff_DoesNotWaitForConfig")]
        [TestCase(true, false, false, true, false, false, TestName = "Routing_BothOff_NoAppLovin")]
        [TestCase(false, true, false, true, false, false, TestName = "Routing_ModuleDisabled_NoAppLovin")]
        public void ShouldRouteToAppLovin_Cases(bool moduleEnabled, bool appLovinAllowed, bool creativeAllowed,
            bool configReturnedVideos, bool hasFill, bool expected)
        {
            Assert.AreEqual(expected, CrossPromoBannerSlot.ShouldRouteToAppLovin(
                moduleEnabled, appLovinAllowed, creativeAllowed, configReturnedVideos, hasFill));
        }

        // creativeAllowed, appLovinRequested, appLovinHasAd → provider
        [TestCase(true, false, false, BannerProvider.CrossPromo, TestName = "Resolve_CreativeOnly")]
        [TestCase(true, true, false, BannerProvider.CrossPromo, TestName = "Resolve_AppLovinLoading_CreativeKeepsSlot")]
        [TestCase(true, true, true, BannerProvider.AppLovin, TestName = "Resolve_AppLovinLoaded")]
        [TestCase(false, false, false, BannerProvider.None, TestName = "Resolve_CreativeOff_NoAppLovin_Empty")]
        [TestCase(false, true, false, BannerProvider.None, TestName = "Resolve_CreativeOff_AppLovinLoading_Empty")]
        [TestCase(false, true, true, BannerProvider.AppLovin, TestName = "Resolve_CreativeOff_AppLovinLoaded")]
        [TestCase(true, false, true, BannerProvider.CrossPromo, TestName = "Resolve_StaleAdWithoutRequest_Creative")]
        public void Resolve_Cases(bool creativeAllowed, bool appLovinRequested, bool appLovinHasAd, BannerProvider expected)
        {
            Assert.AreEqual(expected, CrossPromoBannerSlot.Resolve(creativeAllowed, appLovinRequested, appLovinHasAd));
        }

        // ---------- CrossPromoModule switches ----------

        private GameObject _go;
        private CrossPromoModule _module;
        private readonly List<BannerProvider> _events = new List<BannerProvider>();
        private Action<BannerProvider> _listener;

        [SetUp]
        public void SetUp()
        {
            // EditMode does not run Awake on a plain MonoBehaviour: the static Instance and the
            // sceneLoaded subscription stay untouched.
            _go = new GameObject("module");
            _module = _go.AddComponent<CrossPromoModule>();
            _events.Clear();
            _listener = provider => _events.Add(provider);
            CrossPromoModule.OnBannerProviderChanged += _listener;
        }

        [TearDown]
        public void TearDown()
        {
            CrossPromoModule.OnBannerProviderChanged -= _listener;
            UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void Module_SwitchesDefaultToEnabled()
        {
            Assert.IsTrue(_module.BannerEnabled);
            Assert.IsTrue(_module.AppLovinBannerFallbackEnabled);
            Assert.AreEqual(BannerProvider.None, _module.CurrentBannerProvider);
        }

        [Test]
        public void Module_SetBannerEnabled_Toggles()
        {
            LogAssert.ignoreFailingMessages = true;
            _module.SetBannerEnabled(false);
            Assert.IsFalse(_module.BannerEnabled);

            _module.SetBannerEnabled(true);
            Assert.IsTrue(_module.BannerEnabled);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Module_FallbackDisabled_NeverRoutesBannerToAppLovin()
        {
            _module.SetAppLovinBannerFallbackEnabled(false);

            Assert.IsFalse(_module.AppLovinBannerFallbackEnabled);
            Assert.IsFalse(_module.ShouldUseAppLovinBanner);
        }

        [Test]
        public void Module_ProviderSwitches_AreIndependent()
        {
            Assert.IsTrue(_module.IsBannerProviderEnabled(BannerProvider.CrossPromo));
            Assert.IsTrue(_module.IsBannerProviderEnabled(BannerProvider.AppLovin));
            Assert.IsFalse(_module.IsBannerProviderEnabled(BannerProvider.None));

            _module.SetBannerProviderEnabled(BannerProvider.CrossPromo, false);

            Assert.IsFalse(_module.CrossPromoBannerCreativeEnabled);
            Assert.IsTrue(_module.AppLovinBannerFallbackEnabled, "Switching the creative off must not touch MAX.");
            Assert.IsTrue(_module.BannerEnabled, "Provider switches are separate from the slot master switch.");

            _module.SetBannerProviderEnabled(BannerProvider.CrossPromo, true);
            Assert.IsTrue(_module.CrossPromoBannerCreativeEnabled);
        }

        [Test]
        public void Module_AppLovinProviderSwitch_IsTheFallbackSwitch()
        {
            _module.SetBannerProviderEnabled(BannerProvider.AppLovin, false);

            Assert.IsFalse(_module.AppLovinBannerFallbackEnabled);
            Assert.IsFalse(_module.IsBannerProviderEnabled(BannerProvider.AppLovin));
            Assert.IsTrue(_module.CrossPromoBannerCreativeEnabled);

            _module.SetAppLovinBannerFallbackEnabled(true);
            Assert.IsTrue(_module.IsBannerProviderEnabled(BannerProvider.AppLovin));
        }

        [Test]
        public void Module_ProviderNone_IsIgnored()
        {
            _module.SetBannerProviderEnabled(BannerProvider.None, false);

            Assert.IsTrue(_module.CrossPromoBannerCreativeEnabled);
            Assert.IsTrue(_module.AppLovinBannerFallbackEnabled);
            Assert.IsTrue(_module.BannerEnabled);
        }

        [Test]
        public void Module_ReportBannerProvider_RaisesEventOncePerChange()
        {
            _module.ReportBannerProvider(1, BannerProvider.CrossPromo);
            _module.ReportBannerProvider(1, BannerProvider.CrossPromo);
            _module.ReportBannerProvider(1, BannerProvider.AppLovin);
            _module.ReportBannerProvider(1, BannerProvider.None);

            CollectionAssert.AreEqual(
                new[] { BannerProvider.CrossPromo, BannerProvider.AppLovin, BannerProvider.None }, _events);
            Assert.AreEqual(BannerProvider.None, _module.CurrentBannerProvider);
        }

        [Test]
        public void Module_ThrowingListener_DoesNotBreakReporting()
        {
            Action<BannerProvider> throwing = _ => throw new InvalidOperationException("game listener");
            CrossPromoModule.OnBannerProviderChanged += throwing;
            try
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("OnBannerProviderChanged listener"));

                Assert.DoesNotThrow(() => _module.ReportBannerProvider(1, BannerProvider.CrossPromo));
                Assert.AreEqual(BannerProvider.CrossPromo, _module.CurrentBannerProvider);
            }
            finally
            {
                CrossPromoModule.OnBannerProviderChanged -= throwing;
            }
        }
    }
}
