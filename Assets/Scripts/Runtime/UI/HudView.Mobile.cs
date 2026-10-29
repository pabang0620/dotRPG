using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Phone-only HUD pieces of <see cref="HudView"/>: the quest tracker folds to its title line (the auto buttons
    /// stay under it), and <see cref="MobileHud"/> folds the rest. Nothing here runs on PC.
    /// </summary>
    public partial class HudView
    {
        const float MobileQuestFoldedHeight = 44f, MobileFoldButtonW = 48f, MobileAutoButtonH = 48f;

        bool mobileQuestOpen;
        Text mobileQuestArrow;

        /// <summary>Gold / item counts as the HUD writes them (만 / 억 or M / B), for the phone menu sheet.</summary>
        public static string MobileCountText(string id, long count) => CountText(id, count);

        void BuildMobile(RectTransform root)
        {
            mobileQuestOpen = MobileTouch.GetFlag("quest", false);
            var fold = MobileTouch.HudButton(questPanel, "Fold", "", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero,
                new Vector2(MobileFoldButtonW, MobileQuestFoldedHeight), out mobileQuestArrow);
            fold.onClick.AddListener(() =>
            {
                mobileQuestOpen = !mobileQuestOpen;
                MobileTouch.SetFlag("quest", mobileQuestOpen);
                RefreshQuest();
            });
            // The title ends left of the fold button.
            questTitle.rectTransform.offsetMax = new Vector2(-(14f + MobileFoldButtonW), questTitle.rectTransform.offsetMax.y);
            // 자동 진행 / 자동 사냥 are the phone's main quest controls: thumb height, the status line under them.
            foreach (var name in new[] { "Auto", "AutoHunt" })
            {
                var b = questPanel.Find(name) as RectTransform;
                if (b != null) b.sizeDelta = new Vector2(b.sizeDelta.x, MobileAutoButtonH);
            }
            autoStatus.rectTransform.anchoredPosition = new Vector2(autoStatus.rectTransform.anchoredPosition.x, -(MobileAutoButtonH + 8f));
            MobileHud.Attach(root, questPanel);
            FitQuestMobile();
        }

        /// <summary>After <see cref="RefreshQuest"/>: folded, only the title line shows.</summary>
        void FitQuestMobile()
        {
            if (mobileQuestArrow == null) return; // RefreshQuest runs once before BuildMobile
            mobileQuestArrow.text = mobileQuestOpen ? "▲" : "▼";
            if (mobileQuestOpen) return;
            // [UI 2026-10-29] Folded: the title line plus the one objective line RefreshQuest left in the body.
            questTitle.verticalOverflow = VerticalWrapMode.Truncate; // one line in the folded panel
            questBody.verticalOverflow = VerticalWrapMode.Truncate;
            questPanel.sizeDelta = new Vector2(questPanel.sizeDelta.x, MobileQuestFoldedHeight + 22f);
        }
    }
}
