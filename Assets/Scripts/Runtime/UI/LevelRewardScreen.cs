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

        Text header;
        Button buyPass;
        sealed class Row { public Image bg; public Text level, free, pass; public Button freeBtn, passBtn; }
        readonly List<Row> rows = new List<Row>();
        readonly List<int> levels = new List<int>();

        public static LevelRewardScreen Create(Transform canvas)
        {
            var w = CreateWindow<LevelRewardScreen>(canvas, "LevelRewards", "레벨 보상", "menuicon_achievement");
            Instance = w;
            w.header = Label(w.content, "Header", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(860f, 44f), TextAnchor.MiddleLeft);
            w.buyPass = Button(w.content, "BuyPass", "", "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(330f, 50f), w.BuyPass, 18);
            Label(w.content, "ColLevel", "<b>달성 레벨</b>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -60f), new Vector2(140f, 30f), TextAnchor.MiddleLeft);
            Label(w.content, "ColFree", "<b>무료 보상</b> <color=#8c96a8>(별조각)</color>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -60f), new Vector2(380f, 30f), TextAnchor.MiddleLeft);
            Label(w.content, "ColPass", "<b><color=#ffd34a>성장 패스 보상</color></b>", 17, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, -60f), new Vector2(600f, 30f), TextAnchor.MiddleLeft);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var r = new Row();
                r.bg = Panel(w.content, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, Top - i * RowH), new Vector2(1200f, RowH - 4f), i % 2 == 0 ? RowA : RowB);
                r.level = Label(r.bg.transform, "Level", "", 20, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(12f, 0f), new Vector2(140f, 40f), TextAnchor.MiddleLeft);
                r.free = Label(r.bg.transform, "Free", "", 17, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(160f, 0f), new Vector2(260f, 40f), TextAnchor.MiddleLeft);
                r.freeBtn = Button(r.bg.transform, "FreeClaim", "받기", "ui_btn", new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(420f, 0f), new Vector2(110f, 40f), () => w.ClaimFree(idx), 16);
                r.pass = Label(r.bg.transform, "Pass", "", 16, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(560f, 0f), new Vector2(500f, 44f), TextAnchor.MiddleLeft);
                r.passBtn = Button(r.bg.transform, "PassClaim", "받기", "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-10f, 0f), new Vector2(110f, 40f), () => w.ClaimPass(idx), 16);
                w.rows.Add(r);
            }
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

        void ClaimFree(int i)
        {
            if (i >= levels.Count) return;
            var t = FreeAt(levels[i]);
            if (t == null || !t.claimable) return;
            LevelRewardClient.Claim(t.level, Answer);
        }

        void ClaimPass(int i)
        {
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
            if (levels.Count > rows.Count) Debug.LogWarning($"[dotRPG] level rewards: {levels.Count} levels but only {rows.Count} rows are shown");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
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
