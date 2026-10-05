using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵 > 별조각 뽑기: auras and equipment, grades 일반 / 희귀 / 유니크. The rate table (per item, for this
    /// character's class), the pity count and the duplicate rule sit next to the buttons. Results flip one by one
    /// exactly as returned; a 유니크 is announced: its card glows before it opens, then a golden flash and a halo.
    /// </summary>
    public class GachaScreen : OnlineWindow
    {
        public static GachaScreen Instance { get; private set; }
        const int Cells = 11, PerRow = 6;
        const float CellW = 118f, CellH = 150f, LeftW = 760f, RateW = 420f;
        const float Step = 0.14f, Anticipation = 0.75f;

        sealed class Cell { public Image bg, halo, icon; public Text name, note; public RectTransform rt; }

        Text header, rates, rules;
        Button one, ten, wardrobe;
        static readonly (string id, string label)[] Tabs = { ("aura", "오라 뽑기"), ("weapon", "무기 뽑기"), ("armor", "방어구 뽑기"), ("accessory", "장신구 뽑기") };
        readonly List<Button> tabButtons = new List<Button>();
        string banner = "aura";
        Image flash;
        readonly List<Cell> cells = new List<Cell>();
        List<StarPullResult> shown = new List<StarPullResult>();
        float revealAt, flashAt = -10f, punchAt = -10f;
        int revealed, punched = -1;
        bool busy;

        public static GachaScreen Create(Transform canvas)
        {
            var w = CreateWindow<GachaScreen>(canvas, "Gacha", "별조각 뽑기", "menuicon_cosmetics");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            for (int i = 0; i < Tabs.Length; i++)
            {
                string id = Tabs[i].id;
                w.tabButtons.Add(Button(w.content, "Tab_" + id, Tabs[i].label, "ui_btngray", tl, tl, new Vector2(i * 188f, 0f), new Vector2(180f, 44f), () => w.SelectBanner(id), 18));
            }
            w.header = Label(w.content, "Header", "", 20, tl, tl, new Vector2(0f, -50f), new Vector2(LeftW, 34f), TextAnchor.MiddleLeft);
            w.one = Button(w.content, "One", "", "ui_btn", tl, tl, new Vector2(0f, -88f), new Vector2(240f, 52f), () => w.Ask(1), 18);
            w.ten = Button(w.content, "Ten", "", "ui_btn", tl, tl, new Vector2(252f, -88f), new Vector2(290f, 52f), () => w.Ask(10), 18);
            w.wardrobe = Button(w.content, "Wardrobe", "옷장", "ui_btngray", tl, tl, new Vector2(554f, -88f), new Vector2(200f, 52f), w.OpenWardrobe, 17);
            for (int i = 0; i < Cells; i++)
            {
                int col = i % PerRow, row = i / PerRow;
                var c = new Cell();
                var pos = new Vector2(col * (CellW + 8f), -152f - row * (CellH + 10f));
                c.halo = UIFactory.Image(w.content, "Halo" + i, Game.Art.Get("fx_glow"), Color.clear);
                c.halo.raycastTarget = false;
                UIFactory.Place(c.halo.rectTransform, tl, new Vector2(0.5f, 0.5f), pos + new Vector2(CellW / 2f, -CellH / 2f), new Vector2(CellW * 1.9f, CellH * 1.7f));
                c.bg = Panel(w.content, "Cell" + i, tl, tl, pos, new Vector2(CellW, CellH), RowA);
                c.rt = c.bg.rectTransform;
                c.icon = UIFactory.Image(c.bg.transform, "Icon", CosmeticAura.Sprite, Color.white);
                c.icon.preserveAspect = true;
                UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(84f, 64f));
                c.name = Label(c.bg.transform, "Name", "", 15, tl, tl, new Vector2(4f, -80f), new Vector2(CellW - 8f, 40f), TextAnchor.UpperCenter);
                c.note = Label(c.bg.transform, "Note", "", 13, tl, tl, new Vector2(4f, -120f), new Vector2(CellW - 8f, 28f), TextAnchor.UpperCenter);
                w.cells.Add(c);
            }
            // Rate table on the right, scrollable (every item of every grade, for this character's class).
            var side = Panel(w.content, "Rates", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -50f), new Vector2(RateW, 600f), UiTheme.PanelDeep);
            var viewport = UIFactory.Place(UIFactory.Rect(side.transform, "Viewport"), tl, tl, new Vector2(12f, -10f), new Vector2(RateW - 24f, 400f));
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            w.rates = Label(viewport, "Table", "", 15, tl, tl, Vector2.zero, new Vector2(RateW - 30f, 900f), TextAnchor.UpperLeft);
            var fitter = w.rates.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = w.rates.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            w.rules = Label(side.transform, "Rules", "", 14, tl, tl, new Vector2(14f, -418f), new Vector2(RateW - 28f, 178f), TextAnchor.UpperLeft);
            // Full-window flash for a 유니크.
            w.flash = UIFactory.Image(w.content, "Flash", Game.Art.Get("ui_white"), Color.clear);
            w.flash.preserveAspect = false;
            w.flash.raycastTarget = false;
            UIFactory.Stretch(w.flash.rectTransform);
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

        void SelectBanner(string id)
        {
            if (busy || revealed < shown.Count || banner == id) return;
            banner = id;
            shown = new List<StarPullResult>();
            revealed = 0;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        string BannerName()
        {
            foreach (var t in Tabs) if (t.id == banner) return t.label;
            return "";
        }

        void OpenWardrobe()
        {
            if (Game.UI.Cosmetics != null) Game.Flow.OpenWindow(Game.UI.Cosmetics);
        }

        void Ask(int count)
        {
            if (busy || !StarShopClient.Available || revealed < shown.Count) return;
            int price = count == 10 ? StarShopClient.PriceTen : StarShopClient.PriceOne;
            int times = count == 10 ? StarShopClient.TenCount : 1;
            if (StarShopClient.Balance < price)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            Game.UI.Confirm($"{BannerName()}\n{StarShopClient.Stars(price)}로 {times}회 뽑습니다.\n뽑으시겠습니까?", () => Pull(count), true);
        }

        void Pull(int count)
        {
            busy = true;
            Refresh();
            StarShopClient.Pull(banner, count, (ok, msg, results) =>
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
                punched = -1;
                revealAt = Time.unscaledTime + 0.2f;
                Refresh();
            });
        }

        /// <summary>The announced results: a 유니크 aura, or Unique / Legendary equipment.</summary>
        static bool IsTop(StarPullResult r) => r.rarity == "unique" || r.rarity == "legendary";
        static bool IsGood(StarPullResult r) => r.rarity == "rare" || r.rarity == "epic";

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf) return;
            Animate();
            if (revealed >= shown.Count || Time.unscaledTime < revealAt) return;
            var r = shown[revealed];
            // A 유니크 card glows on its own for a moment before it opens.
            if (IsTop(r) && punched != revealed)
            {
                punched = revealed;
                revealAt = Time.unscaledTime + Anticipation;
                Game.Audio.PlaySfx("rank_reveal");
                return;
            }
            revealed++;
            revealAt = Time.unscaledTime + (IsTop(r) ? 0.5f : Step);
            if (IsTop(r))
            {
                flashAt = punchAt = Time.unscaledTime;
                Game.Audio.PlaySfx("quest");
                Game.Camera?.Shake(0.12f, 0.25f);
                string grade = r.rarity == "legendary" ? "레전더리" : "유니크";
                GameEvents.RaiseToast($"<color=#ffb347>{grade}!</color> {NameOf(r)}" + (r.duplicate ? "" : " 획득"));
            }
            else Game.Audio.PlaySfx(IsGood(r) ? "confirm" : "select");
            DrawCells();
        }

        /// <summary>Halo pulses, the anticipation glow, the flash and the card punch.</summary>
        void Animate()
        {
            float t = Time.unscaledTime;
            float f = Mathf.Clamp01(1f - (t - flashAt) / 0.7f);
            flash.color = new Color(1f, .85f, .45f, f * f * .75f);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                float scale = 1f;
                if (i < shown.Count && i >= revealed && i == punched)
                {
                    // waiting 유니크: golden pulse and a slight tremble
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 26f);
                    c.halo.color = new Color(1f, .8f, .3f, .55f + .45f * k);
                    c.bg.color = Color.Lerp(HeadRow, new Color32(140, 100, 30, 255), k);
                    scale = 1.06f + .04f * k;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 2.5f);
                }
                else
                {
                    c.rt.localRotation = Quaternion.identity;
                    if (i < revealed && i < shown.Count)
                    {
                        var r = shown[i];
                        float k = 0.5f + 0.5f * Mathf.Sin(t * 3f + i);
                        c.halo.color = IsTop(r) ? new Color(1f, .78f, .3f, .45f + .35f * k)
                            : IsGood(r) ? new Color(.45f, .65f, 1f, .18f + .14f * k) : Color.clear;
                        if (IsTop(r) && i == punched) scale = 1f + 0.35f * Mathf.Clamp01(1f - (t - punchAt) / 0.35f);
                        if (!r.gear)
                        {
                            var p = CosmeticCatalog.Find(r.itemId);
                            if (p != null && p.Effect != CosmeticEffect.None) c.icon.color = p.ColorAt(t);
                        }
                    }
                    else c.halo.color = Color.clear;
                }
                c.rt.localScale = new Vector3(scale, scale, 1f);
            }
        }

        static CosmeticRarity RarityOf(string r) => r == "unique" ? CosmeticRarity.Unique : r == "rare" ? CosmeticRarity.Rare : CosmeticRarity.Common;

        static string NameOf(StarPullResult r)
        {
            if (r.gear)
            {
                var g = EquipmentDatabase.Get(r.itemId);
                return g != null ? g.name : DungeonDatabase.ItemName(r.itemId);
            }
            return CosmeticCatalog.Find(r.itemId)?.Name ?? r.itemId;
        }

        void DrawCells()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool on = i < shown.Count && i < revealed;
                c.icon.enabled = on;
                if (!on)
                {
                    c.bg.color = i < shown.Count ? HeadRow : RowA;
                    c.name.text = i < shown.Count ? "<color=#8c96a8>?</color>" : "";
                    c.note.text = "";
                    continue;
                }
                var r = shown[i];
                var rarity = RarityOf(r.rarity);
                c.bg.color = IsTop(r) ? new Color32(110, 76, 22, 245) : IsGood(r) ? new Color32(36, 56, 104, 245) : RowB;
                if (r.gear)
                {
                    var g = EquipmentDatabase.Get(r.itemId);
                    c.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(r.itemId));
                    c.icon.color = Color.white;
                    string hex = g != null ? EquipmentDatabase.RarityColor(g.rarity) : "#ffffff";
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = g != null ? $"<color={hex}>{EquipmentDatabase.RarityName(g.rarity)}</color> · 가방으로" : "가방으로";
                }
                else
                {
                    var p = CosmeticCatalog.Find(r.itemId);
                    c.icon.sprite = CosmeticAura.ForProduct(p);
                    c.icon.color = p != null ? p.Color : Color.white;
                    c.name.text = $"<color={CosmeticCatalog.RarityHex(rarity)}>{NameOf(r)}</color>";
                    c.note.text = r.duplicate ? $"<color=#b8c4d8>중복 · 별조각 +{r.refund}</color>"
                        : r.byPity ? "<color=#ffd34a>천장 확정 · NEW</color>" : "<color=#8fe28f>NEW</color>";
                }
            }
        }

        protected override void Refresh()
        {
            bool online = StarShopClient.Available;
            int left = Mathf.Max(1, StarShopClient.PityMax - StarShopClient.Pity);
            header.text = !online
                ? "<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>"
                : !StarShopClient.Loaded ? "<color=#8c96a8>불러오는 중입니다...</color>"
                : $"보유 <color=#ffd34a>{StarShopClient.Stars(StarShopClient.Balance)}</color>" +
                  (banner == "aura" ? $"   ·   유니크 확정까지 <color=#ffb347>{left}회</color>" : "   ·   뽑은 장비는 바로 가방으로");
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool on = Tabs[i].id == banner;
                TextOf(tabButtons[i]).text = on ? $"<color=#ffd34a>▶ {Tabs[i].label}</color>" : Tabs[i].label;
            }
            TextOf(one).text = $"1회 뽑기 · 별조각 {StarShopClient.PriceOne:N0}";
            TextOf(ten).text = $"{StarShopClient.TenCount - 1}+1회 뽑기 · 별조각 {StarShopClient.PriceTen:N0}";
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            rates.text = RateText();
            rules.text = banner == "aura" ? RuleText()
                : "<color=#b8c4d8>· 등급을 먼저 정하고, 그 등급의 장비 중 하나가 같은 확률로 나옵니다.\n· 뽑은 장비는 +0으로 바로 가방에 들어갑니다.\n· 장비 뽑기에는 천장이 없습니다.</color>";
            DrawCells();
        }

        List<StarRate> CurrentRates()
        {
            if (banner == "aura") return StarShopClient.Rates;
            foreach (var b in StarShopClient.Banners) if (b.id == banner) return b.rates;
            return new List<StarRate>();
        }

        static string GradeHex(string r) =>
            r == "legendary" ? "#ff8c3a" : r == "unique" ? "#ffb347" : r == "epic" ? "#c58cff" : r == "rare" ? "#9fc4ff" : r == "uncommon" ? "#8fe28f" : "#cfd6e2";

        string RateText()
        {
            var list = CurrentRates();
            if (list.Count == 0) return "<color=#8c96a8>확률표를 불러오는 중입니다.</color>";
            var sb = new StringBuilder();
            sb.Append($"<b>확률 공개 · {BannerName()}</b>  <size=12><color=#8c96a8>표 {StarShopClient.RatesVersion}{(banner == "aura" ? "" : " · 내 직업 기준")}</color></size>\n");
            foreach (var r in list)
            {
                sb.Append($"<color={GradeHex(r.rarity)}><b>{r.name} {r.rate:0.##}%</b></color>\n");
                foreach (var i in r.items)
                {
                    if (i.gear)
                    {
                        var g = EquipmentDatabase.Get(i.id);
                        sb.Append($"   {(g != null ? g.name : i.id)}  {i.rate:0.###}%\n");
                        continue;
                    }
                    var p = CosmeticCatalog.Find(i.id);
                    string mark = StarShopClient.Owned.Contains(i.id) ? " <color=#8fe28f>보유</color>" : "";
                    string dmg = p != null ? $" <color=#ffb347>공격력 +{p.DamagePercent}%</color>" : "";
                    sb.Append($"   {i.name}  {i.rate:0.###}%{dmg}{mark}\n");
                }
            }
            return sb.ToString();
        }

        static string RuleText()
        {
            StarShopClient.Refund.TryGetValue("common", out int rc);
            StarShopClient.Refund.TryGetValue("rare", out int rr);
            StarShopClient.Refund.TryGetValue("unique", out int ru);
            return "<color=#b8c4d8>" +
                   $"· 천장: 유니크 없이 {StarShopClient.PityMax - 1}회를 뽑으면 {StarShopClient.PityMax}번째는 유니크 확정. 유니크가 나오면 0부터 다시 셉니다(기한 없음).\n" +
                   "· 오라 중복 방지: 같은 등급에서 아직 없는 오라만 같은 확률로 나옵니다. 위 개별 확률은 아무것도 없을 때 기준입니다.\n" +
                   $"· 같은 등급 오라를 모두 가지면 중복이 나오고 별조각을 돌려줍니다(일반 {rc}, 희귀 {rr}, 유니크 {ru}).\n" +
                   "· 원하는 오라·스킨은 옷장에서 바로 살 수 있습니다." +
                   "</color>";
        }
    }
}
