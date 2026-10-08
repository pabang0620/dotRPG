using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵: the draws (오라 / 스킨 / 무기 / 방어구 / 장신구) as banner cards on the left, the chosen banner large on
    /// the right with its buttons, and the results: ten cards plus a separate "+1 보너스" card. The rate table opens in
    /// its own window (확률 보기). A 유니크 / 레전더리 result glows and trembles before it opens, then a golden flash,
    /// a burst of sparks, a punch and a lasting halo; an 에픽 aura or skin does the same in purple, a little smaller;
    /// 희귀 (and 에픽 equipment) get a blue burst. The wardrobe (옷장) is separate.
    /// </summary>
    public partial class GachaScreen : OnlineWindow
    {
        public static GachaScreen Instance { get; private set; }
        const float ListW = 260f, CardH = 80f, BigH = 440f, BigW = 940f, CellW = 118f, CellH = 158f, BonusW = 150f, BonusH = 200f;
        const float Step = 0.13f, Anticipation = 0.8f;

        sealed class Card { public string id; public Image bg, frame; public Text name; }
        sealed class Cell { public Image bg, halo, icon, frame; public Text name, note; public RectTransform rt; }
        sealed class Spark { public Image img; public Vector2 vel; public float age, life; }

        static readonly (string id, string name, string sub)[] Banners =
        {
            ("skin", "스킨 뽑기", "에픽 스킨 1% · 게이지로 유니크 스킨"),
            ("aura", "오라 뽑기", "에픽 오라 1% · 공격력 보너스"),
            ("weapon", "무기 뽑기", "레전더리 무기 0.05%"),
            ("armor", "방어구 뽑기", "상의 · 하의"),
            ("accessory", "장신구 뽑기", "목걸이 · 반지"),
            ("sealed", "봉인된 상자", "강화권 · 10회마다 부스터"), // [CASH] Docs/PLAN_CASH_BOX_PASS.md
        };
        bool Sealed => banner == "sealed";

        readonly List<Card> cards = new List<Card>();
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Spark> sparks = new List<Spark>();
        Image big, flash;
        Text bigTitle, bigSub, wallet, bonusLabel, badge, gaugeText;
        Image badgeBg, gaugeFill, gaugeBg;
        Button chooseBtn;
        RectTransform choiceModal;
        readonly List<(Button btn, Image icon, Text label)> choiceRows = new List<(Button, Image, Text)>();
        const float GaugeW = 420f;
        Button one, ten, rateBtn, wardrobeBtn;
        RectTransform main;
        // Banner list + banner + button row (6 cards of 80 + 10 gaps on the left are the tallest part).
        const float MainW = ListW + 16f + BigW, MainH = 6 * (CardH + 10f);
        RectTransform rateModal, cellRoot, resultPage;
        // Gamepad moves between the banner, tier and draw buttons, never while a result page or a modal is up (A closes those).
        protected override bool PadNavigation => (resultPage == null || !resultPage.gameObject.activeSelf) && (rateModal == null || !rateModal.gameObject.activeSelf) && (choiceModal == null || !choiceModal.gameObject.activeSelf);
        Text resultTitle;
        Button skipBtn, againBtn, okBtn;
        int lastCount = 1;
        Text rateText;
        List<StarPullResult> shown = new List<StarPullResult>();
        string banner = "skin";
        float revealAt, flashAt = -10f, punchAt = -10f;
        Color flashColor = new Color(1f, .85f, .45f);
        int revealed, waiting = -1;
        bool busy;

        public static GachaScreen Create(Transform canvas)
        {
            var w = CreateWindow<GachaScreen>(canvas, "Gacha", "캐시샵", "menuicon_cashshop");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            // [UI] The shop is laid out at a fixed size and shrunk to fit the window (1280x720 or a bigger UI scale
            // used to push the right end of the button row off screen).
            w.main = UIFactory.Place(UIFactory.Rect(w.content, "Main"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(MainW, MainH));

            // Left: banner cards.
            for (int i = 0; i < Banners.Length; i++)
            {
                var b = Banners[i];
                var card = new Card { id = b.id };
                var bg = Panel(w.main, "Card_" + b.id, tl, tl, new Vector2(0f, -i * (CardH + 10f)), new Vector2(ListW, CardH), UiTheme.PanelDeep);
                bg.raycastTarget = true;
                bg.gameObject.AddComponent<RectMask2D>();
                var art = UIFactory.Image(bg.transform, "Art", Game.Art.Get("Banners/banner_gacha_" + b.id), Color.white);
                art.preserveAspect = false;
                art.raycastTarget = false;
                UIFactory.Place(art.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40f, 0f), new Vector2(ListW * 1.4f, ListW * 1.4f * 9f / 16f));
                var shade = UIFactory.Image(bg.transform, "Shade", Game.Art.Get("ui_white"), new Color(0f, 0f, 0f, .45f));
                shade.preserveAspect = false; shade.raycastTarget = false;
                UIFactory.Place(shade.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(ListW, 40f));
                card.name = Label(bg.transform, "Name", b.name, 21, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 6f), new Vector2(ListW - 20f, 30f), TextAnchor.MiddleLeft);
                card.frame = UIFactory.Image(bg.transform, "Frame", Game.Art.Get("ui_white"), Color.clear);
                card.frame.preserveAspect = false; card.frame.raycastTarget = false;
                UIFactory.Place(card.frame.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(6f, CardH));
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                string id = b.id;
                btn.onClick.AddListener(() => w.SelectBanner(id));
                card.bg = bg;
                w.cards.Add(card);
            }

            // Right: the chosen banner, large.
            var bigBox = Panel(w.main, "Banner", tl, tl, new Vector2(ListW + 16f, 0f), new Vector2(BigW, BigH), new Color32(10, 14, 24, 255));
            bigBox.gameObject.AddComponent<RectMask2D>();
            w.big = UIFactory.Image(bigBox.transform, "Art", null, Color.white);
            w.big.preserveAspect = false;
            UIFactory.Place(w.big.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 940f * 9f / 16f));
            w.bigTitle = Label(bigBox.transform, "Title", "", 40, tl, tl, new Vector2(28f, -28f), new Vector2(520f, 56f), TextAnchor.MiddleLeft);
            w.bigSub = Label(bigBox.transform, "Sub", "", 19, tl, tl, new Vector2(30f, -90f), new Vector2(460f, 150f), TextAnchor.UpperLeft);
            // "출시 스킨 N종" ribbon on the skin banner (top right).
            w.badgeBg = UIFactory.Image(bigBox.transform, "Badge", Game.Art.Get("ui_white"), new Color32(150, 30, 40, 235));
            w.badgeBg.preserveAspect = false; w.badgeBg.raycastTarget = false;
            UIFactory.Place(w.badgeBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(210f, 44f));
            w.badge = Label(w.badgeBg.transform, "Text", "", 20, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 40f), TextAnchor.MiddleCenter);
            // Selection gauge: fills one notch per draw; full = choose the top item yourself.
            w.gaugeBg = UIFactory.Image(bigBox.transform, "Gauge", Game.Art.Get("ui_white"), new Color32(10, 12, 20, 220));
            w.gaugeBg.preserveAspect = false; w.gaugeBg.raycastTarget = false;
            UIFactory.Place(w.gaugeBg.rectTransform, tl, tl, new Vector2(30f, -238f), new Vector2(GaugeW, 22f));
            w.gaugeFill = UIFactory.Image(w.gaugeBg.transform, "Fill", Game.Art.Get("ui_white"), UiTheme.Accent);
            w.gaugeFill.preserveAspect = false; w.gaugeFill.raycastTarget = false;
            UIFactory.Place(w.gaugeFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 16f));
            w.gaugeText = Label(bigBox.transform, "GaugeText", "", 17, tl, tl, new Vector2(30f, -264f), new Vector2(GaugeW, 26f), TextAnchor.MiddleLeft);
            w.BuildTierPicker(bigBox.transform);
            w.chooseBtn = Button(bigBox.transform, "Choose", "선택하기", "ui_btn", tl, tl, new Vector2(30f + GaugeW + 14f, -230f), new Vector2(150f, 40f), w.OpenChoices, 18);
            var shard = UIFactory.Image(bigBox.transform, "StarShard", Game.Art.Get("icon_star_shard"), Color.white);
            shard.preserveAspect = true; shard.raycastTarget = false;
            UIFactory.Place(shard.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 14f), new Vector2(38f, 38f));
            w.wallet = Label(bigBox.transform, "Wallet", "", 22, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(68f, 18f), new Vector2(440f, 30f), TextAnchor.MiddleLeft);

            float by = -(BigH + 14f), bx = ListW + 16f;
            // The five buttons share the banner's width (940 = 230 + 270 + 130 + 110 + 152 + 4 gaps of 12).
            w.one = Button(w.main, "One", "", "ui_btn", tl, tl, new Vector2(bx, by), new Vector2(230f, 58f), () => w.Ask(1), 17);
            w.ten = Button(w.main, "Ten", "", "ui_btn", tl, tl, new Vector2(bx + 242f, by), new Vector2(270f, 58f), () => w.Ask(10), 17);
            w.rateBtn = Button(w.main, "Rates", "확률 보기", "ui_btngray", tl, tl, new Vector2(bx + 524f, by), new Vector2(130f, 58f), w.OpenRates, 17);
            w.wardrobeBtn = Button(w.main, "Wardrobe", "옷장", "ui_btngray", tl, tl, new Vector2(bx + 666f, by), new Vector2(110f, 58f), w.OpenWardrobe, 17);
            // Spares (a cosmetic drawn again): synthesis, dismantling and collections.
            Button(w.main, "Synth", "합성 · 컬렉션", "ui_btngray", tl, tl, new Vector2(bx + 788f, by), new Vector2(152f, 58f),
                () => { if (CosmeticSynthScreen.Instance != null) Game.Flow.OpenWindow(CosmeticSynthScreen.Instance); }, 17);
            // keep the price text off the button edges
            foreach (var b in new[] { w.one, w.ten }) UIFactory.Stretch(TextOf(b).rectTransform, 10f, 0f, 10f, 0f);

            // Result page: opens over the shop after a purchase and reveals the cards there.
            var page = Panel(w.content, "ResultPage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1220f, 640f), new Color32(8, 10, 18, 248));
            page.raycastTarget = true;
            w.resultPage = page.rectTransform;
            UIFactory.Stretch(w.resultPage, -20f, -20f, -20f, -20f);
            w.resultTitle = Label(page.transform, "Title", "", 30, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(900f, 44f), TextAnchor.MiddleCenter);
            w.skipBtn = Button(page.transform, "Skip", "모두 열기", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 24f), new Vector2(220f, 56f), w.RevealAll, 19);
            w.againBtn = Button(page.transform, "Again", "한 번 더", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(240f, 56f), () => w.Ask(w.lastCount), 19);
            w.okBtn = Button(page.transform, "Ok", "확인", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(260f, 24f), new Vector2(220f, 56f), w.CloseResults, 19);

            // Results: ten cards, a gap, then the bonus card.
            w.cellRoot = UIFactory.Place(UIFactory.Rect(page.transform, "Results"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1000f, 420f));
            for (int i = 0; i < 11; i++)
            {
                var c = new Cell();
                var pos = Vector2.zero; // placed per draw size in DrawCells
                c.halo = UIFactory.Image(w.cellRoot, "Halo" + i, Game.Art.Get("fx_glow"), Color.clear);
                c.halo.raycastTarget = false;
                UIFactory.Place(c.halo.rectTransform, tl, new Vector2(0.5f, 0.5f), pos + new Vector2(CellW / 2f, -CellH / 2f), new Vector2(CellW * 2.2f, CellH * 1.9f));
                c.bg = Panel(w.cellRoot, "Cell" + i, tl, tl, pos, new Vector2(CellW, CellH), RowA);
                c.rt = c.bg.rectTransform;
                c.frame = UIFactory.Image(c.bg.transform, "Frame", Game.Art.Get("ui_white"), Color.clear);
                c.frame.preserveAspect = false; c.frame.raycastTarget = false;
                c.frame.rectTransform.anchorMin = new Vector2(0f, 1f); c.frame.rectTransform.anchorMax = new Vector2(1f, 1f);
                c.frame.rectTransform.pivot = new Vector2(0.5f, 1f); c.frame.rectTransform.anchoredPosition = Vector2.zero; c.frame.rectTransform.sizeDelta = new Vector2(0f, 5f);
                c.icon = UIFactory.Image(c.bg.transform, "Icon", CosmeticAura.Sprite, Color.white);
                c.icon.preserveAspect = true;
                c.icon.rectTransform.anchorMin = new Vector2(0.1f, 0.42f); c.icon.rectTransform.anchorMax = new Vector2(0.9f, 0.94f);
                c.icon.rectTransform.offsetMin = c.icon.rectTransform.offsetMax = Vector2.zero;
                int cellIndex = i;
                GearTooltip.Hook(c.icon, () => cellIndex < w.revealed && cellIndex < w.shown.Count && w.shown[cellIndex].gear ? w.shown[cellIndex].itemId : null);
                c.name = Label(c.bg.transform, "Name", "", 16, tl, tl, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
                c.name.rectTransform.anchorMin = new Vector2(0f, 0.16f); c.name.rectTransform.anchorMax = new Vector2(1f, 0.42f);
                c.name.rectTransform.offsetMin = new Vector2(3f, 0f); c.name.rectTransform.offsetMax = new Vector2(-3f, 0f);
                c.note = Label(c.bg.transform, "Note", "", 16, tl, tl, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
                c.note.rectTransform.anchorMin = new Vector2(0f, 0.02f); c.note.rectTransform.anchorMax = new Vector2(1f, 0.17f);
                c.note.rectTransform.offsetMin = c.note.rectTransform.offsetMax = Vector2.zero;
                w.cells.Add(c);
            }
            w.bonusLabel = Label(w.cellRoot, "BonusLabel", "<color=#ffd34a><b>+1 보너스</b></color>", 18, tl, tl, Vector2.zero, new Vector2(BonusW, 26f), TextAnchor.MiddleCenter);
            page.gameObject.SetActive(false);

            w.flash = UIFactory.Image(w.content, "Flash", Game.Art.Get("ui_white"), Color.clear);
            w.flash.preserveAspect = false;
            w.flash.raycastTarget = false;
            UIFactory.Stretch(w.flash.rectTransform);

            // Choice window: the gauge is full, pick one top item you don't own yet.
            var cm = Panel(w.content, "ChoiceModal", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 420f), UiTheme.Overlay);
            cm.raycastTarget = true;
            w.choiceModal = cm.rectTransform;
            Label(cm.transform, "Title", "<b>원하는 것을 하나 고르세요</b>", 26, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(600f, 40f), TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = Button(cm.transform, "Choice" + i, "", "ui_btngray", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f - i * 70f), new Vector2(520f, 62f), () => w.Choose(idx), 20);
                var ic = UIFactory.Image(b.transform, "Icon", null, Color.white);
                ic.preserveAspect = true; ic.raycastTarget = false;
                UIFactory.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(56f, 56f));
                var lb = TextOf(b);
                UIFactory.Stretch(lb.rectTransform, 80f, 0f, 12f, 0f);
                lb.alignment = TextAnchor.MiddleLeft;
                w.choiceRows.Add((b, ic, lb));
            }
            Button(cm.transform, "Cancel", "닫기", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(180f, 48f), () => w.choiceModal.gameObject.SetActive(false), 18);
            cm.gameObject.SetActive(false);

            // 확률 보기 window (over everything in the shop).
            var modal = Panel(w.content, "RateModal", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 560f), UiTheme.Overlay);
            modal.raycastTarget = true;
            w.rateModal = modal.rectTransform;
            var vp = UIFactory.Place(UIFactory.Rect(modal.transform, "Viewport"), tl, tl, new Vector2(24f, -20f), new Vector2(672f, 456f));
            vp.gameObject.AddComponent<Image>().color = Color.clear;
            vp.gameObject.AddComponent<RectMask2D>();
            w.rateText = Label(vp, "Text", "", 16, tl, tl, Vector2.zero, new Vector2(660f, 1200f), TextAnchor.UpperLeft);
            w.rateText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = vp.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vp; scroll.content = w.rateText.rectTransform; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
            Button(modal.transform, "Close", "닫기", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(200f, 50f), w.CloseRates, 18);
            modal.gameObject.SetActive(false);

            StarShopClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            CashClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); }; // [CASH]
            return w;
        }

        public override async void Show()
        {
            base.Show();
            rateModal.gameObject.SetActive(false);
            shown = new List<StarPullResult>();
            revealed = 0;
            waiting = -1;
            Refresh();
            CashClient.Refresh(); // [CASH] sealed box table and gauge
            if (!StarShopClient.Available) return;
            await StarShopClient.RefreshAsync();
            Refresh();
        }

        bool Revealing => revealed < shown.Count;

        void SelectBanner(string id)
        {
            if (busy || Revealing || banner == id) return;
            banner = id;
            shown = new List<StarPullResult>();
            revealed = 0;
            waiting = -1;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        (string id, string name, string sub) Current()
        {
            foreach (var b in Banners) if (b.id == banner) return b;
            return Banners[0];
        }

        List<CosmeticProduct> choices = new List<CosmeticProduct>();

        void OpenChoices()
        {
            if (busy || Revealing) return;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            choices = new List<CosmeticProduct>();
            foreach (var p in CosmeticCatalog.All)
            {
                if (banner == "skin" ? (p.IsSkin && p.Skin.cls == cls && p.Rarity == CosmeticRarity.Unique) : (!p.IsSkin && p.Rarity == CosmeticRarity.Epic)) choices.Add(p);
            }
            for (int i = 0; i < choiceRows.Count; i++)
            {
                var row = choiceRows[i];
                bool on = i < choices.Count;
                row.btn.gameObject.SetActive(on);
                if (!on) continue;
                var p = choices[i];
                bool owned = StarShopClient.Owned.Contains(p.Id);
                var look = p.IsSkin ? SkinCatalog.LookFor(p.Skin.cls, p.Id) : null;
                var card = CosmeticAura.Card(p);
                row.icon.sprite = card ?? (look != null ? Game.Art.GetCharacter(look, "down", "idle0") : CosmeticAura.ForProduct(p));
                row.icon.color = card != null || look != null ? Color.white : p.Color;
                row.label.text = owned ? $"<color=#8c96a8>{p.Name} · 보유 중</color>" : $"<color={CosmeticCatalog.RarityHex(p.Rarity)}>{p.Name}</color>  <size=15>공격력 +{p.DamagePercent}%</size>";
                row.btn.interactable = !owned;
            }
            choiceModal.gameObject.SetActive(true);
            choiceModal.SetAsLastSibling();
            Game.Audio.PlaySfx("select");
        }

        void Choose(int i)
        {
            if (i >= choices.Count) return;
            var p = choices[i];
            Game.UI.Confirm($"{p.Name}\n선택 게이지를 사용해 이것을 받습니다.", () =>
            {
                busy = true;
                StarShopClient.Claim(banner, p.Id, (ok, msg) =>
                {
                    busy = false;
                    choiceModal.gameObject.SetActive(false);
                    if (!ok) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(msg); Refresh(); return; }
                    shown = new List<StarPullResult> { new StarPullResult { itemId = p.Id, rarity = banner == "skin" ? "unique" : "epic", byPity = true } };
                    revealed = 0; waiting = -1; lastCount = 1;
                    revealAt = Time.unscaledTime + 0.3f;
                    OpenResults();
                    Refresh();
                });
            }, true);
        }

        void OpenWardrobe()
        {
            if (Game.UI.Cosmetics != null) Game.Flow.OpenWindow(Game.UI.Cosmetics);
        }

        void OpenRates()
        {
            rateText.text = RateText();
            rateModal.gameObject.SetActive(true);
            rateModal.SetAsLastSibling();
            Game.Audio.PlaySfx("select");
        }

        void CloseRates() => rateModal.gameObject.SetActive(false);

        void Ask(int count)
        {
            if (busy || !StarShopClient.Available || Revealing) return;
            if (Sealed) { AskSealed(count); return; }
            int price = count == 10 ? StarShopClient.PriceTen : StarShopClient.PriceOne;
            int times = count == 10 ? StarShopClient.TenCount : 1;
            if (StarShopClient.Balance < price)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            string what = count == 10 ? $"{times - 1}회 + 보너스 1회" : "1회";
            string tierNote = banner == "aura" || banner == "skin" ? "" : $" (Lv.{SelectedTierLevel()} 장비)";
            Game.UI.Confirm($"{Current().name}{tierNote}\n{StarShopClient.Stars(price)}로 {what} 뽑습니다.\n뽑으시겠습니까?", () => Pull(count), true);
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
                waiting = -1;
                lastCount = count;
                revealAt = Time.unscaledTime + 0.45f;
                OpenResults();
                Refresh();
            }, SelectedTier());
        }

        void OpenResults()
        {
            resultTitle.text = $"<b>{Current().name} 결과</b>";
            LayoutCells(shown.Count);
            resultPage.gameObject.SetActive(true);
            resultPage.SetAsLastSibling();
            flash.transform.SetAsLastSibling();
        }

        void CloseResults()
        {
            if (Revealing) { RevealAll(); return; }
            resultPage.gameObject.SetActive(false);
            shown = new List<StarPullResult>();
            Refresh();
        }

    }
}
