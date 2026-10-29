using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Thumb-sized tabs and buttons on a phone (the 1280x720 window shows at 0.8 on the 1024x576 canvas). Run every
    /// frame because Refresh puts the difficulty buttons back to their PC size; growing is skipped once they are tall enough.
    /// </summary>
    public partial class DungeonSelectScreen
    {
        void FitTouch()
        {
            const float tap = MobileTouch.WindowTapHeight;
            // Tabs grow up into the gap under the header, so the list under them keeps its place.
            MobileTouch.GrowHeight((RectTransform)tabWeekday.transform, tap, MobileTouch.Grow.Up);
            MobileTouch.GrowHeight((RectTransform)tabNormal.transform, tap, MobileTouch.Grow.Up);
            MobileTouch.GrowHeight((RectTransform)tabRaid.transform, tap, MobileTouch.Grow.Up);
            MobileTouch.GrowHeight((RectTransform)partyButton.transform, tap, MobileTouch.Grow.Centre);
            MobileTouch.GrowHeight((RectTransform)resetButton.transform, tap, MobileTouch.Grow.Centre);
            // Normal / hard / hell buttons (the two small raid buttons share their line with the schedule text: left as is).
            foreach (var b in diffButtons)
            {
                if (b == null) continue;
                var rt = (RectTransform)b.transform;
                // [TIERS 2026-10-29] Weekday buttons are narrower with 7~8 shown: every button but the raid ones grows.
                if (!Mathf.Approximately(rt.sizeDelta.x, RaidDiffW)) MobileTouch.GrowHeight(rt, tap, MobileTouch.Grow.Centre);
            }
        }
    }
}
