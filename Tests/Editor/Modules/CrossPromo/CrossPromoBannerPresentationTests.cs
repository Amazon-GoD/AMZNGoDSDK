using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// CrossPromoBannerPresentation: hiding the cross-promo banner through the single CanvasGroup
    /// it shares with the game. Regression: the banner used to add a second CanvasGroup for its
    /// own switching; Unity refuses a second one, the reference stayed null and the creative was
    /// never hidden when AppLovin MAX took the slot.
    /// </summary>
    public class CrossPromoBannerPresentationTests
    {
        private GameObject _go;
        private CanvasGroup _group;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("banner", typeof(RectTransform));
            _group = _go.AddComponent<CanvasGroup>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        // The Unity constraint the fix is built around. If a future Unity version lifts it, this
        // test goes red and the shared-group design can be revisited.
        [Test]
        public void Unity_RefusesSecondCanvasGroupOnSameGameObject()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var second = _go.AddComponent<CanvasGroup>();

                Assert.IsTrue(second == null);
                Assert.AreEqual(1, _go.GetComponents<CanvasGroup>().Length);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void Hide_ZeroesGroupAndBlocksInput()
        {
            var presentation = new CrossPromoBannerPresentation(_group);

            presentation.SetVisible(false);

            Assert.IsTrue(presentation.IsHidden);
            Assert.AreEqual(0f, _group.alpha);
            Assert.IsFalse(_group.interactable);
            Assert.IsFalse(_group.blocksRaycasts);
        }

        [Test]
        public void Show_RestoresValuesTheGameSet()
        {
            _group.alpha = 0.6f;
            _group.interactable = false;
            _group.blocksRaycasts = true;
            var presentation = new CrossPromoBannerPresentation(_group);

            presentation.SetVisible(false);
            presentation.SetVisible(true);

            Assert.IsFalse(presentation.IsHidden);
            Assert.AreEqual(0.6f, _group.alpha, 0.0001f);
            Assert.IsFalse(_group.interactable);
            Assert.IsTrue(_group.blocksRaycasts);
        }

        [Test]
        public void Show_WithoutPriorHide_DoesNotTouchGroup()
        {
            _group.alpha = 0.3f;
            var presentation = new CrossPromoBannerPresentation(_group);

            presentation.SetVisible(true);

            Assert.AreEqual(0.3f, _group.alpha, 0.0001f);
        }

        [Test]
        public void GameValues_ReadableWhileHidden()
        {
            _group.alpha = 0.6f;
            var presentation = new CrossPromoBannerPresentation(_group);

            presentation.SetVisible(false);

            // Eligibility must see the game's alpha, not the SDK's own zero.
            Assert.AreEqual(0.6f, presentation.GameAlpha, 0.0001f);
            Assert.IsTrue(presentation.GameInteractable);
            Assert.IsTrue(presentation.GameBlocksRaycasts);
        }

        [Test]
        public void GameHidesWithAlphaZero_StaysHiddenAfterSdkShow()
        {
            var presentation = new CrossPromoBannerPresentation(_group);
            _group.alpha = 0f;

            presentation.SetVisible(false);
            Assert.AreEqual(0f, presentation.GameAlpha);

            presentation.SetVisible(true);
            Assert.AreEqual(0f, _group.alpha);
        }

        [Test]
        public void GameWritesWhileHidden_AdoptedAndKeptHidden()
        {
            var presentation = new CrossPromoBannerPresentation(_group);
            presentation.SetVisible(false);

            // The game fades the banner in while MAX owns the slot.
            _group.alpha = 0.4f;
            presentation.SetVisible(false);

            Assert.AreEqual(0f, _group.alpha, "The SDK keeps the creative hidden under the MAX banner.");
            Assert.AreEqual(0.4f, presentation.GameAlpha, 0.0001f);

            presentation.SetVisible(true);
            Assert.AreEqual(0.4f, _group.alpha, 0.0001f);
        }

        [Test]
        public void RepeatedHide_KeepsOriginalGameValues()
        {
            _group.alpha = 0.8f;
            var presentation = new CrossPromoBannerPresentation(_group);

            // The banner calls SetVisible every frame.
            for (int i = 0; i < 5; i++)
                presentation.SetVisible(false);
            presentation.SetVisible(true);

            Assert.AreEqual(0.8f, _group.alpha, 0.0001f);
        }

        [Test]
        public void NullGroup_TracksStateWithoutThrowing()
        {
            var presentation = new CrossPromoBannerPresentation(null);

            Assert.DoesNotThrow(() => presentation.SetVisible(false));
            Assert.IsTrue(presentation.IsHidden);
            Assert.DoesNotThrow(() => presentation.SetVisible(true));
            Assert.IsFalse(presentation.IsHidden);
            Assert.AreEqual(1f, presentation.GameAlpha);
        }
    }
}
