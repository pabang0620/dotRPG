using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI 2026-10-30] Docs/UI_AUDIT.md P7: a locked difficulty keeps its name readable and gets the lock icon on its top edge;
    /// the first locked difficulty's condition is shown next to the 난이도 heading.
    /// </summary>
    public partial class DungeonSelectScreen
    {
        readonly Image[] diffLocks = new Image[(int)DungeonDifficulty.Lord + 1];

        /// <summary>Wraps and shrinks <paramref name="text"/> to its box (moved here from the main file to keep it shorter).</summary>
        static void FitDungeonText(Text text, int minimum, int maximum)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(minimum, UIFactory.ReadableFloor);
            text.resizeTextMaxSize = maximum;
        }

        void ApplyDiffLocks(DungeonDef def, int shownCount)
        {
            var progress = Game.Session.Dungeons;
            for (int i = 0; i < diffButtons.Length; i++)
            {
                if (diffButtons[i] == null) continue;
                bool locked = def != null && i < shownCount && !progress.IsUnlocked(def, (DungeonDifficulty)i);
                if (diffLocks[i] == null)
                {
                    if (!locked) continue;
                    var img = UIFactory.Image(diffButtons[i].transform, "Lock", Game.Art.Get("ui_lock"), Color.white);
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    UIFactory.Place(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
                    diffLocks[i] = img;
                }
                diffLocks[i].gameObject.SetActive(locked);
            }
        }

        /// <summary>"   (dim) 보통 난이도를 먼저 클리어해야 합니다." for the first locked weekday difficulty, else "".</summary>
        string DiffLockHint(DungeonDef def)
        {
            if (def == null || def.isRaid) return "";
            var progress = Game.Session.Dungeons;
            int shown = DungeonDatabase.ShownDifficultyCount(def);
            for (int i = 1; i < shown; i++)
                if (!progress.IsUnlocked(def, (DungeonDifficulty)i))
                    return "   <size=16><color=#9aa0aa>" + L.F("{0} 난이도를 먼저 클리어해야 합니다.", L.T(DungeonDatabase.Difficulty((DungeonDifficulty)(i - 1)).name)) + "</color></size>";
            return "";
        }
    }
}
