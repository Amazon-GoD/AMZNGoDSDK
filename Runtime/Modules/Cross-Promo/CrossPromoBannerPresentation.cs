#if AMZN_CROSSPROMO_ENABLED
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Hides and restores the cross-promo banner through the single CanvasGroup of its root.
    /// <para>
    /// Unity allows one CanvasGroup per GameObject, so the group is shared with the game. The
    /// SDK writes it only while it keeps the banner hidden and puts the game's values back
    /// when the banner returns. Values the game writes while the banner is hidden are adopted
    /// as the game's intent instead of being lost.
    /// </para>
    /// </summary>
    internal sealed class CrossPromoBannerPresentation
    {
        private readonly CanvasGroup _group;
        private float _gameAlpha = 1f;
        private bool _gameInteractable = true;
        private bool _gameBlocksRaycasts = true;

        internal CrossPromoBannerPresentation(CanvasGroup group)
        {
            _group = group;
        }

        internal CanvasGroup Group => _group;

        /// <summary>The SDK currently keeps the banner hidden.</summary>
        internal bool IsHidden { get; private set; }

        /// <summary>Alpha requested by the game, regardless of the SDK hiding the banner.</summary>
        internal float GameAlpha => IsHidden || _group == null ? _gameAlpha : _group.alpha;

        internal bool GameInteractable => IsHidden || _group == null ? _gameInteractable : _group.interactable;

        internal bool GameBlocksRaycasts => IsHidden || _group == null ? _gameBlocksRaycasts : _group.blocksRaycasts;

        /// <summary>Safe to call every frame: writes the group only when something changes.</summary>
        internal void SetVisible(bool visible)
        {
            if (_group == null)
            {
                IsHidden = !visible;
                return;
            }

            if (visible)
            {
                if (!IsHidden)
                    return;

                IsHidden = false;
                _group.alpha = _gameAlpha;
                _group.interactable = _gameInteractable;
                _group.blocksRaycasts = _gameBlocksRaycasts;
                return;
            }

            if (!IsHidden)
            {
                IsHidden = true;
                _gameAlpha = _group.alpha;
                _gameInteractable = _group.interactable;
                _gameBlocksRaycasts = _group.blocksRaycasts;
            }
            else
            {
                // The SDK holds zeros here, so any other value was written by the game.
                if (_group.alpha != 0f)
                    _gameAlpha = _group.alpha;
                if (_group.interactable)
                    _gameInteractable = true;
                if (_group.blocksRaycasts)
                    _gameBlocksRaycasts = true;
            }

            if (_group.alpha != 0f)
                _group.alpha = 0f;
            if (_group.interactable)
                _group.interactable = false;
            if (_group.blocksRaycasts)
                _group.blocksRaycasts = false;
        }
    }
}
#endif
