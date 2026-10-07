using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [LEVEL 13] 레벨 보상 window (side menu), two tracks per level: the free star shards and (with the 성장 패스,
    /// Docs/PLAN_CASH_BOX_PASS.md) the pass rewards. Both are per account, so a second character does not take them again.
    /// </summary>
    public class LevelRewardScreen : OnlineWindow
    {
        public static LevelRewardScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        const float RowH = 56f, Top = -96f;
        const int Rows = 8;

        Text header, pageText;
        Button buyPass, pagePrev, pageNext;
        int page;
        sealed class Row { public Image bg; public Text level, free, pass; public Button freeBtn, passBtn; }
        readonly List<Row> rows = new List<Row>();
        readonly List<int> levels = new List<int>();

        public static LevelRewardScreen Create(Transform canvas)
        {
            var w = CreateWindow<LevelRewardScreen>(canvas, "LevelRewards", "레벨 보상", "menuicon_levelreward");
            Instance = w;
            w.header = Label(w.content, "Header", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(860f, 44f), TextAnchor.MiddleLeft);
            w.buyPass = Button(w.content, "BuyPass", "", "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(330f, 50f), w.BuyPass, 18);
            var passIcon = UIFactory.Image(w.buyPass.transform, "PassIcon", Game.Art.Get("menuicon_pass"), Color.white); // [ART] growth pass ticket
            passIcon.preserveAspect = true; passIcon.raycastTarget = false;
            UIFactory.Place(passIcon.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(8f, 0f), new Vector2(44f, 36f));
            UIFactory.Stretch(TextOf(w.buyPass).rectTransform, 50f, 0f, 8f, 0f);
            Label(w.content, "ColLevel", "<b>달성 레벨</b>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -60f), new Vector2(140f, 30f), TextAnchor.MiddleLeft);
            Label(w.content, "ColFree", "<b>무료 보상</b> <color=#8c96a8>(별조각)</color>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -60f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
            Label(w.content, "ColPass", "<b><color=#ffd34a>성장 패스 보상</color></b>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, -60f), new Vector2(600f, 30f), TextAnchor.MiddleLeft);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.bg = Panel(w.content, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, Top - i * RowH), new Vector2(1200f, RowH - 4f), i % 2 == 0 ? RowA : RowB);
                r.level = Label(r.bg.transform, "Level", "", 20, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(12f, 0f), new Vector2(140f, 40f), TextAnchor.MiddleLeft);
                r.free = Label(r.bg.transform, "Free", "", 17, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(160f, 0f), new Vector2(250f, 40f), TextAnchor.MiddleLeft);
                r.freeBtn = Button(r.bg.transform, "FreeClaim", "받기", "ui_btn", new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(410f, 0f), UiSizes.ClaimButton, () => w.ClaimFree(idx), UiSizes.ClaimFont);
                r.pass = Label(r.bg.transform, "Pass", "", 16, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(560f, 0f), new Vector2(480f, 44f), TextAnchor.MiddleLeft);
                r.passBtn = Button(r.bg.transform, "PassClaim", "받기", "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-10f, 0f), UiSizes.ClaimButton, () => w.ClaimPass(idx), UiSizes.ClaimFont);
                w.rows.Add(r);
            }
            // Paging under the rows: only shown when there are more levels than rows.
            var br = new Vector2(1f, 0f);
            w.pagePrev = Button(w.content, "Prev", "◀", "ui_btngray", br, br, new Vector2(-140f, 0f), UiSizes.PageButton, () => w.Turn(-1), UiSizes.PageFont);
            w.pageText = Label(w.content, "Page", "", 18, br, br, new Vector2(-54f, 0f), new Vector2(80f, 34f), TextAnchor.MiddleCenter);
            w.pageNext = Button(w.content, "Next", "▶", "ui_btngray", br, br, new Vector2(0f, 0f), UiSizes.PageButton, () => w.Turn(1), UiSizes.PageFont);
            LevelRewardClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Show()
        {
            base.Show();
            LevelRewardClient.Refresh();
            Refresh();
        }

        LevelRewardClient.Tier FreeAt(int level) { foreach (var t in LevelRewardClient.Tiers) if (t.level == level) return t; return null; }
        LevelRewardClient.PassTier PassAt(int level) { foreach (var t in LevelRewardClient.PassTiers) if (t.level == level) return t; return null; }

        int Pages => Mathf.Max(1, (levels.Count + Rows - 1) / Rows);

        void Turn(int delta)
        {
            int next = Mathf.Clamp(page + delta, 0, Pages - 1);
            if (next == page) return;
            page = next;
            Game.Audio.PlaySfx("select", 0.5f);
            Refresh();
        }

        void ClaimFree(int row)
        {
            int i = page * Rows + row;
            if (i >= levels.Count) return;
            var t = FreeAt(levels[i]);
            if (t == null || !t.claimable) return;
            LevelRewardClient.Claim(t.level, Answer);
        }

        void ClaimPass(int row)
        {
            int i = page * Rows + row;
            if (i >= levels.Count) return;
            var t = PassAt(levels[i]);
            if (t == null || !t.claimable) return;
            LevelRewardClient.ClaimPass(t.level, Answer);
        }

        void BuyPass()
        {
            if (LevelRewardClient.PassOwned || LevelRewardClient.Busy) return;
            int price = LevelRewardClient.PassPrice;
            if (StarShopClient.Balance < price) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})"); return; }
            Game.UI.Confirm($"<b>성장 패스</b>\n별조각 {price:N0}개 · 계정당 한 번 · 기간 없음\n레벨마다 강화권·봉인된 상자 등 패스 보상을 받습니다.\n구매하시겠습니까?",
                () => LevelRewardClient.BuyPass(Answer), true);
        }

        void Answer(bool ok, string msg)
        {
            Game.Audio.PlaySfx(ok ? "quest" : "cancel");
            GameEvents.RaiseToast(msg);
        }

        static string Rewards(LevelRewardClient.PassTier t)
        {
            var parts = new List<string>();
            foreach (var (key, count) in t.rewards) parts.Add(DungeonDatabase.ItemName(key) + (count > 1 ? $" x{count}" : ""));
            return string.Join(" · ", parts);
        }

        protected override void Refresh()
        {
            bool online = OnlineSession.Playing;
            header.text = !online ? "<color=#8c96a8>온라인으로 접속하면 받을 수 있습니다.</color>"
                : !LevelRewardClient.Loaded ? "<color=#8c96a8>불러오는 중입니다...</color>"
                : $"계정 최고 레벨 <color=#ffd34a>Lv.{LevelRewardClient.AccountLevel}</color>  ·  <color=#b8c4d8>무료·패스 보상 모두 계정당 한 번</color>";
            buyPass.gameObject.SetActive(online && LevelRewardClient.Loaded);
            TextOf(buyPass).text = LevelRewardClient.PassOwned ? "<color=#8fe28f>성장 패스 보유 중</color>" : $"성장 패스 구매 · 별조각 {LevelRewardClient.PassPrice:N0}";
            buyPass.interactable = !LevelRewardClient.PassOwned && !LevelRewardClient.Busy;

            levels.Clear();
            foreach (var t in LevelRewardClient.Tiers) if (!levels.Contains(t.level)) levels.Add(t.level);
            foreach (var t in LevelRewardClient.PassTiers) if (!levels.Contains(t.level)) levels.Add(t.level);
            levels.Sort();
            int pages = Pages;
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            pageText.gameObject.SetActive(pages > 1);
            pagePrev.gameObject.SetActive(pages > 1);
            pageNext.gameObject.SetActive(pages > 1);
            pagePrev.interactable = page > 0;
            pageNext.interactable = page < pages - 1;
            for (int row = 0; row < rows.Count; row++)
            {
                var r = rows[row];
                int i = page * Rows + row;
                bool used = i < levels.Count;
                r.bg.gameObject.SetActive(used);
                if (!used) continue;
                int lv = levels[i];
                bool max = i == levels.Count - 1;
                r.level.text = $"<b>Lv.{lv}</b>{(max ? " <size=14><color=#ffb347>만렙</color></size>" : "")}";
                var f = FreeAt(lv);
                r.free.text = f == null ? "<color=#5a6478>-</color>" : $"별조각 <color=#ffd34a>{f.stars:N0}</color>" + (f.claimed ? "  <color=#8fe28f>받음</color>" : "");
                r.freeBtn.gameObject.SetActive(f != null && !f.claimed);
                r.freeBtn.interactable = f != null && f.claimable && !LevelRewardClient.Busy;
                if (f != null) TextOf(r.freeBtn).text = f.claimable ? "받기" : "미달성";
                var p = PassAt(lv);
                r.pass.text = p == null ? "<color=#5a6478>-</color>" : (LevelRewardClient.PassOwned ? "" : "<color=#8c96a8>[패스] </color>") + Rewards(p) + (p.claimed ? "  <color=#8fe28f>받음</color>" : "");
                r.passBtn.gameObject.SetActive(p != null && !p.claimed);
                r.passBtn.interactable = p != null && p.claimable && !LevelRewardClient.Busy;
                if (p != null) TextOf(r.passBtn).text = !LevelRewardClient.PassOwned ? "패스 필요" : p.claimable ? "받기" : "미달성";
            }
        }
    }
}
