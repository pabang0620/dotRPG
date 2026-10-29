using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI 2026-10-29] Compact quest tracker: folded (the default) it shows the main quest title and one objective line with
    /// its progress; the small ▼ / ▲ in the corner shows every objective and the pinned side quest. Remembered per player
    /// in PlayerPrefs (phones keep their own fold flag, <see cref="HudView"/>.Mobile).
    /// </summary>
    public partial class HudView
    {
        const string QuestOpenPref = "hud_quest_open";
        bool pcQuestOpen;
        Text pcQuestArrow;

        bool QuestOpen => TouchUi.Enabled ? mobileQuestOpen : pcQuestOpen;

        void BuildQuestFold()
        {
            try { pcQuestOpen = PlayerPrefs.GetInt(QuestOpenPref, 0) != 0; } catch (System.Exception) { pcQuestOpen = false; }
            var img = UIFactory.Image(questPanel, "Fold", Game.Art.Get("ui_btngray"), Color.white);
            img.preserveAspect = false;
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(30f, 24f));
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                pcQuestOpen = !pcQuestOpen;
                try { PlayerPrefs.SetInt(QuestOpenPref, pcQuestOpen ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { }
                RefreshQuest();
            });
            UiButton.Attach(btn);
            pcQuestArrow = UIFactory.Text(img.transform, "Arrow", "▼", 14, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(pcQuestArrow.rectTransform);
            pcQuestArrow.raycastTarget = false;
            questTitle.rectTransform.offsetMax = new Vector2(-(12f + 34f), questTitle.rectTransform.offsetMax.y); // the title ends left of it
        }

        /// <summary>The one line of the folded tracker: the first objective still open (main quest first), or the first line.</summary>
        string CompactLine(QuestDef main, StringBuilder full)
        {
            if (pcQuestArrow != null) pcQuestArrow.text = pcQuestOpen ? "▲" : "▼";
            if (!growthShown && main != null)
            {
                // QuestObjective is a struct: "found" stands in for a null check.
                QuestObjective pick = default;
                bool found = false;
                foreach (var o in Game.Quest.ObjectivesOf(main)) { if (!found) { pick = o; found = true; } if (!o.done) { pick = o; break; } }
                if (found) { var one = new StringBuilder(); AppendObjective(one, pick); return one.ToString().TrimEnd('\n'); }
            }
            string text = full.ToString();
            int cut = text.IndexOf('\n');
            return cut < 0 ? text : text.Substring(0, cut);
        }
    }
}
