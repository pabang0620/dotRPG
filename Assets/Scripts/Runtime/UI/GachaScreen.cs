using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵 > 별조각 뽑기: outfit-only (auras, no stats). The rate table, the pity count and the duplicate rule are
    /// always on screen next to the buttons; every pull is rolled and recorded by the server. Results appear one by
    /// one exactly as the server returned them (no near-miss effects).
    /// </summary>
    public class GachaScreen : OnlineWindow
    {
        public static GachaScreen Instance { get; private set; }
        const int Cells = 11, PerRow = 6;
        const float CellW = 118f, CellH = 132f, LeftW = 760f;

        Text header, rates, rules, recent;
        Button one, ten, wardrobe;
        readonly List<(Image bg, Image aura, Text name, Text note)> cells = new List<(Image, Image, Text, Text)>();
        List<StarPullResult> shown = new List<StarPullResult>();
        float revealAt;
        int revealed;
        bool busy;

        public static GachaScreen Create(Transform canvas)
        {
            var w = CreateWindow<GachaScreen>(canvas, "Gacha", "별조각 뽑기", "menuicon_cosmetics");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            w.header = Label(w.content, "Header", "", 20, tl, tl, Vector2.zero, new Vector2(LeftW, 64f), TextAnchor.UpperLeft);
            w.one = Button(w.content, "One", "", "ui_btn", tl, tl, new Vector2(0f, -72f), new Vector2(240f, 50f), () => w.Ask(1), 18);
            w.ten = Button(w.content, "Ten", "", "ui_btn", tl, tl, new Vector2(252f, -72f), new Vector2(290f, 50f), () => w.Ask(10), 18);
            w.wardrobe = Button(w.content, "Wardrobe", "옷장에서 착용", "ui_btngray", tl, tl, new Vector2(554f, -72f), new Vector2(200f, 50f), w.OpenWardrobe, 17);
            for (int i = 0; i < Cells; i++)
            {
                int col = i % PerRow, row = i / PerRow;
                var bg = Panel(w.content, "Cell" + i, tl, tl, new Vector2(col * (CellW + 8f), -136f - row * (CellH + 8f)), new Vector2(CellW, CellH), RowA);
                var aura = UIFactory.Image(bg.transform, "Aura", CosmeticAura.Sprite, Color.white);
                UIFactory.Place(aura.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(96f, 48f));
                var name = Label(bg.transform, "Name", "", 15, tl, tl, new Vector2(4f, -64f), new Vector2(CellW - 8f, 36f), TextAnchor.UpperCenter);
                var note = Label(bg.transform, "Note", "", 13, tl, tl, new Vector2(4f, -100f), new Vector2(CellW - 8f, 30f), TextAnchor.UpperCenter);
                w.cells.Add((bg, aura, name, note));
            }
            w.recent = Label(w.content, "Recent", "", 15, tl, tl, new Vector2(0f, -424f), new Vector2(LeftW, 150f), TextAnchor.UpperLeft);
            var side = Panel(w.content, "Rates", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(420f, 580f), UiTheme.PanelDeep);
            w.rates = Label(side.transform, "Table", "", 16, tl, tl, new Vector2(14f, -10f), new Vector2(392f, 380f), TextAnchor.UpperLeft);
            w.rules = Label(side.transform, "Rules", "", 14, tl, tl, new Vector2(14f, -392f), new Vector2(392f, 184f), TextAnchor.UpperLeft);
            StarShopClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override async void Show()
        {
            base.Show();
            shown = new List<StarPullResult>();
            revealed = 0;
            Refresh();
            if (!StarShopClient.Available) return;
            await StarShopClient.RefreshAsync();
            Refresh();
        }

        void OpenWardrobe()
        {
            if (Game.UI.Cosmetics != null) Game.Flow.OpenWindow(Game.UI.Cosmetics);
        }

        void Ask(int count)
        {
            if (busy || !StarShopClient.Available) return;
            int price = count == 10 ? StarShopClient.PriceTen : StarShopClient.PriceOne;
            int times = count == 10 ? StarShopClient.TenCount : 1;
            if (StarShopClient.Balance < price)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            Game.UI.Confirm($"{StarShopClient.Stars(price)}를 사용해 {times}회 뽑습니다.\n외형 전용 · 능력치 없음 · 결과는 서버가 정합니다.\n뽑으시겠습니까?", () => Pull(count), true);
        }

        void Pull(int count)
        {
            busy = true;
            Refresh();
            StarShopClient.Pull(count, (ok, msg, results) =>
            {
                busy = false;
                if (!ok)
                {
                    Game.Audio.PlaySfx("cancel");
                    GameEvents.RaiseToast(msg);
                    Refresh();
                    return;
                }
                shown = results;
                revealed = 0;
                revealAt = Time.unscaledTime;
                Refresh();
            });
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf || revealed >= shown.Count || Time.unscaledTime < revealAt) return;
            var r = shown[revealed++];
            revealAt = Time.unscaledTime + 0.12f;
            Game.Audio.PlaySfx(r.rarity == "legend" ? "quest" : r.rarity == "rare" ? "confirm" : "select");
            if (r.rarity == "legend")
            {
                var p = CosmeticCatalog.Find(r.itemId);
                GameEvents.RaiseToast($"<color=#ffb347>전설!</color> {p?.Name ?? r.itemId}" + (r.duplicate ? "" : " 획득"));
            }
            DrawCells();
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < cells.Count && i < revealed && i < shown.Count; i++)
            {
                var p = CosmeticCatalog.Find(shown[i].itemId);
                if (p != null && p.Effect != CosmeticEffect.None) cells[i].aura.color = p.ColorAt(t);
            }
        }

        static CosmeticRarity RarityOf(string r) => r == "legend" ? CosmeticRarity.Legend : r == "rare" ? CosmeticRarity.Rare : CosmeticRarity.Common;

        void DrawCells()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool on = i < shown.Count && i < revealed;
                c.aura.enabled = on;
                if (!on)
                {
                    c.bg.color = i < shown.Count ? HeadRow : RowA;
                    c.name.text = i < shown.Count ? "<color=#8c96a8>...</color>" : "";
                    c.note.text = "";
                    continue;
                }
                var r = shown[i];
                var p = CosmeticCatalog.Find(r.itemId);
                var rarity = RarityOf(r.rarity);
                c.bg.color = rarity == CosmeticRarity.Legend ? new Color32(92, 64, 20, 240) : rarity == CosmeticRarity.Rare ? new Color32(36, 56, 96, 240) : RowB;
                c.aura.sprite = CosmeticAura.ForProduct(p);
                c.aura.color = p != null ? p.Color : Color.white;
                c.name.text = $"<color={CosmeticCatalog.RarityHex(rarity)}>{p?.Name ?? r.itemId}</color>";
                c.note.text = r.duplicate ? $"<color=#b8c4d8>중복 · 별조각 +{r.refund}</color>"
                    : r.byPity ? "<color=#ffd34a>천장 확정 · NEW</color>" : "<color=#8fe28f>NEW</color>";
            }
        }

        protected override void Refresh()
        {
            bool online = StarShopClient.Available;
            int left = Mathf.Max(1, StarShopClient.PityMax - StarShopClient.Pity);
            header.text = !online
                ? "<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>"
                : !StarShopClient.Loaded ? "<color=#8c96a8>불러오는 중입니다...</color>"
                : $"보유 <color=#ffd34a>{StarShopClient.Stars(StarShopClient.Balance)}</color>   ·   전설 확정까지 <color=#ffb347>{left}회</color> (천장 {StarShopClient.PityMax}회)\n" +
                  "<color=#b8c4d8>별조각은 계정 공용입니다. 뽑기와 교환 결과는 서버에 기록됩니다.</color>";
            TextOf(one).text = $"1회 뽑기 · 별조각 {StarShopClient.PriceOne:N0}";
            TextOf(ten).text = $"{StarShopClient.TenCount - 1}+1회 뽑기 · 별조각 {StarShopClient.PriceTen:N0}";
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            rates.text = RateText();
            rules.text = RuleText();
            recent.text = RecentText();
            DrawCells();
        }

        static string RateText()
        {
            if (StarShopClient.Rates.Count == 0) return "<color=#8c96a8>확률표를 불러오는 중입니다.</color>";
            var sb = new StringBuilder();
            sb.Append($"<b>확률 공개</b>  <size=13><color=#8c96a8>표 {StarShopClient.RatesVersion}</color></size>\n");
            foreach (var r in StarShopClient.Rates)
            {
                var rarity = RarityOf(r.rarity);
                sb.Append($"<color={CosmeticCatalog.RarityHex(rarity)}><b>{r.name} {r.rate:0.##}%</b></color>\n");
                foreach (var i in r.items)
                {
                    string mark = StarShopClient.Owned.Contains(i.id) ? " <color=#8fe28f>보유</color>" : "";
                    sb.Append($"   {i.name}  {i.rate:0.##}%{mark}\n");
                }
            }
            return sb.ToString();
        }

        static string RuleText()
        {
            StarShopClient.Refund.TryGetValue("common", out int rc);
            StarShopClient.Refund.TryGetValue("rare", out int rr);
            StarShopClient.Refund.TryGetValue("legend", out int rl);
            return "<color=#b8c4d8>" +
                   $"· 천장: 전설 없이 {StarShopClient.PityMax - 1}회를 뽑으면 {StarShopClient.PityMax}번째는 전설 확정. 전설이 나오면 0부터 다시 셉니다(기한 없음).\n" +
                   "· 중복 방지: 같은 등급에서 아직 없는 외형만 같은 확률로 나옵니다. 위 개별 확률은 아무것도 없을 때 기준입니다.\n" +
                   $"· 같은 등급을 모두 가지면 중복이 나오고 별조각을 돌려줍니다(일반 {rc}, 희귀 {rr}, 전설 {rl}).\n" +
                   "· 원하는 외형은 옷장에서 별조각으로 바로 교환할 수 있습니다." +
                   "</color>";
        }

        static string RecentText()
        {
            if (StarShopClient.Recent.Count == 0) return "";
            var sb = new StringBuilder("<color=#8c96a8>최근 결과</color>  ");
            int n = 0;
            foreach (var r in StarShopClient.Recent)
            {
                if (n++ >= 22) break;
                var p = CosmeticCatalog.Find(r.itemId);
                sb.Append($"<color={CosmeticCatalog.RarityHex(RarityOf(r.rarity))}>{p?.Name ?? r.itemId}</color>{(r.duplicate ? "(중복)" : "")}   ");
            }
            return sb.ToString();
        }
    }
}
