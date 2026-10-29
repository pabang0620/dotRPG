using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Phone placement of the boss bar: at the top, centred in the gap between the top-left HUD (status bars, right
    /// edge 390) and the menu / pause buttons left of the minimap (W - 344), shrunk to fit that gap. Too narrow even at the
    /// smallest size (big UI sizes): the PC fallback, centred under the top-left HUD.
    /// </summary>
    public sealed partial class BossHpBarView
    {
        const float MobileLeftReach = 398f, MobileRightReach = 352f, MobileMinScale = 0.62f;

        /// <summary>[TOUCH] Bottom of the bar block from the top edge (the quest tracker moves under it).</summary>
        public float MobileBottom { get; private set; }

        float MobileBarTop(RectTransform canvas)
        {
            float width = canvas != null ? canvas.rect.width : UIFactory.CanvasReference.x;
            float full = Width + 24f;
            // [UI 2026-10-29] A top-right status card (dungeon clock, boss rush / mini dungeon card) reaches further left than
            // the menu buttons: the bar keeps clear of it too, or falls back under the top-left HUD (below the card).
            float rightReach = RightCardBottom() > 0f ? Mathf.Max(MobileRightReach, CardLeftFromRight + CardGap) : MobileRightReach;
            float room = width - MobileLeftReach - rightReach;
            bool fits = room >= full * MobileMinScale;
            float k = fits ? Mathf.Min(1f, room / full) : MobileMinScale;
            float x = fits ? (MobileLeftReach + width - rightReach) * 0.5f - width * 0.5f : 0f;
            float top = fits ? Top : HudLeftBottom;
            if (!Mathf.Approximately(root.localScale.x, k)) root.localScale = new Vector3(k, k, 1f);
            if (!Mathf.Approximately(root.anchoredPosition.x, x)) root.anchoredPosition = new Vector2(x, root.anchoredPosition.y);
            MobileBottom = top + BarBlockHeight * k;
            return top;
        }
    }
}
