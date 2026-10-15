using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [GROWTH] The quest log's 성장 추천 page (Docs/PLAN_GROWTH_GUIDE.md): in place of the quest detail, every
    /// <see cref="GrowthGuide"/> recommendation with its button (open the dungeon window, hunt at the recommended ground,
    /// open the requests). Opens by itself while no quest can be worked on; the button at the bottom left switches.
    /// </summary>
    public partial class QuestScreen
    {
        const int GrowthMaxRows = 6;
        const float GrowthRowHeight = 58f, GrowthRowsTop = 112f;

        Button growthBtn;
        RectTransform growthRoot;
        Text growthTitle;
        bool growthMode;
        readonly List<GameObject> growthRows = new List<GameObject>();

        void BuildGrowth(Transform side)
        {
            growthBtn = Button(side, "Growth", L.T("성장 추천"), "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 52f), new Vector2(190f, 44f), ToggleGrowth, 18);
            growthRoot = UIFactory.Rect(side, "GrowthGuide");
            UIFactory.Stretch(growthRoot, 0f, 0f, 0f, 0f);
            growthTitle = Label(growthRoot, "Title", "", 21, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(704f, 84f));
            growthRoot.gameObject.SetActive(false);
        }

        public override void Show()
        {
            GrowthGuide.Invalidate();
            growthMode = GrowthGuide.Active;
            base.Show();
        }

        void ToggleGrowth()
        {
            growthMode = !growthMode;
            Game.Audio.PlaySfx("select", 0.5f);
            if (quests.Count == 0) ShowGrowth();
            else Select(selected);
        }

        /// <summary>Shows the 성장 추천 page over the detail, or puts the detail back.</summary>
        void ShowGrowth()
        {
            if (growthRoot == null) return;
            TextOf(growthBtn).text = growthMode ? L.T("퀘스트 보기") : L.T("성장 추천");
            growthRoot.gameObject.SetActive(growthMode);
            detail.gameObject.SetActive(!growthMode);
            foreach (var r in growthRows) Destroy(r);
            growthRows.Clear();
            if (!growthMode) return;
            autoBtn.gameObject.SetActive(false);
            pinBtn.gameObject.SetActive(false);

            GrowthGuide.Invalidate();
            string head = $"<size=30><b>{GrowthGuide.HudTitle()}</b></size>\n";
            head += GrowthGuide.Active
                ? L.T("<color=#b8c4d8>지금 받을 수 있는 퀘스트가 없습니다. 아래 순서대로 하면 다음 퀘스트 레벨까지 빨리 오릅니다.</color>")
                : L.T("<color=#b8c4d8>진행할 퀘스트가 있습니다. 퀘스트 사이사이에 아래 콘텐츠로 경험치를 더 얻을 수 있습니다.</color>");
            growthTitle.text = head;

            var entries = GrowthGuide.Build();
            for (int i = 0; i < entries.Count && i < GrowthMaxRows; i++)
            {
                var e = entries[i];
                var bg = Panel(growthRoot, "Rec_" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -GrowthRowsTop - i * GrowthRowHeight), new Vector2(720f, GrowthRowHeight - 6f), new Color(1f, 1f, 1f, 0.05f));
                var text = Label(bg.transform, "Text", $"<color=#ffe066>{i + 1}</color>  {e.text}", 19, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(e.action != null ? 470f : 696f, GrowthRowHeight - 10f), TextAnchor.MiddleLeft);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 14;
                text.resizeTextMaxSize = 19;
                if (e.action != null)
                {
                    var act = e.action;
                    Button(bg.transform, "Go", e.button ?? L.T("이동"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(226f, 44f), () => { Game.Audio.PlaySfx("confirm"); act(); }, 17);
                }
                growthRows.Add(bg.gameObject);
            }
            hint.text = L.T("자동 진행 버튼도 퀘스트가 없을 때는 추천 사냥터로 가서 사냥하고, 퀘스트를 받을 수 있게 되면 퀘스트로 돌아갑니다.");
        }
    }
}
