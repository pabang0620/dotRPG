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
    /// a burst of sparks, a punch and a lasting halo; 희귀 / 에픽 get a blue burst. The wardrobe (옷장) is separate.
    /// </summary>
    public class GachaScreen : OnlineWindow
    {
        public static GachaScreen Instance { get; private set; }
        const float ListW = 260f, CardH = 98f, BigH = 440f, CellW = 118f, CellH = 158f, BonusW = 150f, BonusH = 200f;
        const float Step = 0.13f, Anticipation = 0.8f;

        sealed class Card { public string id; public Image bg, frame; public Text name; }
        sealed class Cell { public Image bg, halo, icon, frame; public Text name, note; public RectTransform rt; }
        sealed class Spark { public Image img; public Vector2 vel; public float age, life; }

        static readonly (string id, string name, string sub)[] Banners =
        {
            ("skin", "스킨 뽑기", "유니크: 내 직업 코스튬 스킨"),
            ("aura", "오라 뽑기", "발밑 오라 · 공격력 보너스"),
            ("weapon", "무기 뽑기", "레전더리 무기 0.1%"),
            ("armor", "방어구 뽑기", "상의 · 하의"),
            ("accessory", "장신구 뽑기", "목걸이 · 반지"),
        };

        readonly List<Card> cards = new List<Card>();
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Spark> sparks = new List<Spark>();
        Image big, flash;
        Text bigTitle, bigSub, wallet, bonusLabel, badge;
        Image badgeBg;
        Button one, ten, rateBtn, wardrobeBtn;
        RectTransform rateModal, cellRoot, resultPage;
        Text resultTitle;
        Button skipBtn, againBtn, okBtn;
        int lastCount = 1;
        Text rateText;
        List<StarPullResult> shown = new List<StarPullResult>();
        string banner = "skin";
        float revealAt, flashAt = -10f, punchAt = -10f;
        int revealed, waiting = -1;
        bool busy;

        public static GachaScreen Create(Transform canvas)
        {
            var w = CreateWindow<GachaScreen>(canvas, "Gacha", "캐시샵", "menuicon_cashshop");
            Instance = w;
            var tl = new Vector2(0f, 1f);

            // Left: banner cards.
            for (int i = 0; i < Banners.Length; i++)
            {
                var b = Banners[i];
                var card = new Card { id = b.id };
                var bg = Panel(w.content, "Card_" + b.id, tl, tl, new Vector2(0f, -i * (CardH + 10f)), new Vector2(ListW, CardH), new Color32(14, 20, 32, 255));
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
            var bigBox = Panel(w.content, "Banner", tl, tl, new Vector2(ListW + 16f, 0f), new Vector2(940f, BigH), new Color32(10, 14, 24, 255));
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
            var shard = UIFactory.Image(bigBox.transform, "StarShard", Game.Art.Get("icon_star_shard"), Color.white);
            shard.preserveAspect = true; shard.raycastTarget = false;
            UIFactory.Place(shard.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 14f), new Vector2(38f, 38f));
            w.wallet = Label(bigBox.transform, "Wallet", "", 22, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(68f, 18f), new Vector2(440f, 30f), TextAnchor.MiddleLeft);

            float by = -(BigH + 14f), bx = ListW + 16f;
            w.one = Button(w.content, "One", "", "ui_btn", tl, tl, new Vector2(bx, by), new Vector2(250f, 58f), () => w.Ask(1), 18);
            w.ten = Button(w.content, "Ten", "", "ui_btn", tl, tl, new Vector2(bx + 262f, by), new Vector2(320f, 58f), () => w.Ask(10), 18);
            w.rateBtn = Button(w.content, "Rates", "확률 보기", "ui_btngray", tl, tl, new Vector2(bx + 594f, by), new Vector2(170f, 58f), w.OpenRates, 18);
            w.wardrobeBtn = Button(w.content, "Wardrobe", "옷장", "ui_btngray", tl, tl, new Vector2(bx + 776f, by), new Vector2(164f, 58f), w.OpenWardrobe, 18);
            // keep the price text off the button edges
            foreach (var b in new[] { w.one, w.ten }) UIFactory.Stretch(TextOf(b).rectTransform, 16f, 0f, 16f, 0f);

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
                c.name = Label(c.bg.transform, "Name", "", 15, tl, tl, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
                c.name.rectTransform.anchorMin = new Vector2(0f, 0.16f); c.name.rectTransform.anchorMax = new Vector2(1f, 0.42f);
                c.name.rectTransform.offsetMin = new Vector2(3f, 0f); c.name.rectTransform.offsetMax = new Vector2(-3f, 0f);
                c.note = Label(c.bg.transform, "Note", "", 13, tl, tl, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
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

            // 확률 보기 window (over everything in the shop).
            var modal = Panel(w.content, "RateModal", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 560f), new Color32(16, 22, 36, 252));
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
            int price = count == 10 ? StarShopClient.PriceTen : StarShopClient.PriceOne;
            int times = count == 10 ? StarShopClient.TenCount : 1;
            if (StarShopClient.Balance < price)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            string what = count == 10 ? $"{times - 1}회 + 보너스 1회" : "1회";
            Game.UI.Confirm($"{Current().name}\n{StarShopClient.Stars(price)}로 {what} 뽑습니다.\n뽑으시겠습니까?", () => Pull(count), true);
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
            });
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

        /// <summary>Opens every card left (one burst for the best of them).</summary>
        void RevealAll()
        {
            if (!Revealing) return;
            bool top = false;
            for (int i = revealed; i < shown.Count; i++) if (IsTop(shown[i])) { top = true; if (waiting < revealed) waiting = i; }
            revealed = shown.Count;
            if (top)
            {
                flashAt = punchAt = Time.unscaledTime;
                Game.Audio.PlaySfx("quest");
                Game.Camera?.Shake(0.14f, 0.3f);
                for (int i = 0; i < shown.Count; i++) if (IsTop(shown[i])) Burst(i, new Color(1f, .82f, .35f), 20);
            }
            DrawCells();
        }

        /// <summary>One big card for a single draw; two rows of five plus the separate bonus card for 10+1.</summary>
        void LayoutCells(int count)
        {
            bool single = count <= 1;
            float rowsW = 5 * CellW + 4 * 14f, gap = 60f;
            float startX = single ? (1000f - 190f) / 2f : (1000f - (rowsW + gap + BonusW)) / 2f;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                Vector2 pos, size;
                if (single) { pos = new Vector2(startX, -60f); size = new Vector2(190f, 250f); }
                else if (i < 10) { pos = new Vector2(startX + (i % 5) * (CellW + 14f), -40f - (i / 5) * (CellH + 18f)); size = new Vector2(CellW, CellH); }
                else { pos = new Vector2(startX + rowsW + gap, -40f - (2 * CellH + 18f - BonusH) / 2f); size = new Vector2(BonusW, BonusH); }
                UIFactory.Place(c.rt, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), pos + new Vector2(size.x / 2f, -size.y / 2f), size);
                UIFactory.Place(c.halo.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), pos + new Vector2(size.x / 2f, -size.y / 2f), size * 2f);
                if (i == 10) UIFactory.Place(bonusLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), pos + new Vector2(0f, 6f), new Vector2(BonusW, 28f));
            }
        }

        /// <summary>The announced results: 유니크 (aura / skin) or Unique / Legendary equipment.</summary>
        static bool IsTop(StarPullResult r) => r.rarity == "unique" || r.rarity == "legendary";
        static bool IsGood(StarPullResult r) => r.rarity == "rare" || r.rarity == "epic";

        /// <summary>Result i sits in cell i; a single draw lands in the first cell, a 10+1 fills ten plus the bonus.</summary>
        int CellOf(int i) => i;

        protected override void Update()
        {
            if (rateModal != null && rateModal.gameObject.activeSelf && Game.Input.CancelPressed) { CloseRates(); return; }
            if (resultPage != null && resultPage.gameObject.activeSelf && (Game.Input.CancelPressed || Game.Input.SubmitPressed))
            { if (Revealing) RevealAll(); else CloseResults(); Animate(); return; }
            base.Update();
            if (!gameObject.activeSelf) return;
            Animate();
            if (!Revealing || Time.unscaledTime < revealAt) return;
            var r = shown[revealed];
            if (IsTop(r) && waiting != revealed)
            {
                waiting = revealed; // the card glows and trembles first
                revealAt = Time.unscaledTime + Anticipation;
                Game.Audio.PlaySfx("rank_reveal");
                return;
            }
            int cell = CellOf(revealed);
            revealed++;
            revealAt = Time.unscaledTime + (IsTop(r) ? 0.55f : Step);
            if (IsTop(r))
            {
                flashAt = punchAt = Time.unscaledTime;
                Burst(cell, new Color(1f, .82f, .35f), 26);
                Game.Audio.PlaySfx("quest");
                Game.Camera?.Shake(0.14f, 0.3f);
                string grade = r.rarity == "legendary" ? "레전더리" : "유니크";
                GameEvents.RaiseToast($"<color=#ffb347>{grade}!</color> {NameOf(r)}" + (r.duplicate ? "" : " 획득"));
            }
            else if (IsGood(r)) { Burst(cell, new Color(.5f, .7f, 1f), 12); Game.Audio.PlaySfx("confirm"); }
            else Game.Audio.PlaySfx("select");
            DrawCells();
        }

        /// <summary>Sparks flying out of a card.</summary>
        void Burst(int cell, Color color, int count)
        {
            var c = cells[cell];
            var center = (Vector2)c.rt.anchoredPosition;
            for (int i = 0; i < count; i++)
            {
                var img = UIFactory.Image(cellRoot, "Spark", Game.Art.Get("ui_white"), color);
                img.raycastTarget = false;
                float a = i * Mathf.PI * 2f / count + Random.Range(-.2f, .2f);
                float speed = Random.Range(160f, 360f);
                float size = Random.Range(4f, 9f);
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), center, new Vector2(size, size));
                sparks.Add(new Spark { img = img, vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed, life = Random.Range(.5f, .9f) });
            }
        }

        void Animate()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(1f - (t - flashAt) / 0.8f);
            flash.color = new Color(1f, .85f, .45f, f * f * .8f);
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.age += dt;
                if (s.age >= s.life || s.img == null) { if (s.img != null) Destroy(s.img.gameObject); sparks.RemoveAt(i); continue; }
                s.vel *= 1f - 2.2f * dt;
                s.img.rectTransform.anchoredPosition += s.vel * dt;
                var col = s.img.color; col.a = 1f - s.age / s.life; s.img.color = col;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                float scale = 1f;
                c.rt.localRotation = Quaternion.identity;
                if (i == waiting && i >= revealed && i < shown.Count)
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 26f);
                    c.halo.color = new Color(1f, .8f, .3f, .55f + .45f * k);
                    c.bg.color = Color.Lerp(HeadRow, new Color32(150, 108, 30, 255), k);
                    scale = 1.08f + .05f * k;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 3f);
                }
                else if (i < revealed && i < shown.Count)
                {
                    var r = shown[i];
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 3f + i);
                    c.halo.color = IsTop(r) ? new Color(1f, .78f, .3f, .5f + .35f * k) : IsGood(r) ? new Color(.45f, .65f, 1f, .2f + .15f * k) : Color.clear;
                    if (IsTop(r) && i == waiting) scale = 1f + 0.4f * Mathf.Clamp01(1f - (t - punchAt) / 0.35f);
                    if (!r.gear)
                    {
                        var p = CosmeticCatalog.Find(r.itemId);
                        if (p != null && p.Effect != CosmeticEffect.None) c.icon.color = p.ColorAt(t);
                    }
                }
                else c.halo.color = Color.clear;
                c.rt.localScale = new Vector3(scale, scale, 1f);
            }
        }

        static string NameOf(StarPullResult r)
        {
            if (r.gear)
            {
                var g = EquipmentDatabase.Get(r.itemId);
                return g != null ? g.name : DungeonDatabase.ItemName(r.itemId);
            }
            return CosmeticCatalog.Find(r.itemId)?.Name ?? r.itemId;
        }

        static string GradeHex(string r) =>
            r == "legendary" ? "#ff8c3a" : r == "unique" ? "#ffb347" : r == "epic" ? "#c58cff" : r == "rare" ? "#9fc4ff" : r == "uncommon" ? "#8fe28f" : "#cfd6e2";

        void DrawCells()
        {
            int count = shown.Count;
            bonusLabel.gameObject.SetActive(count > 1);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool used = i < count;
                c.bg.gameObject.SetActive(used);
                c.halo.gameObject.SetActive(c.bg.gameObject.activeSelf);
                bool on = used && i < revealed;
                c.icon.enabled = on;
                c.frame.color = Color.clear;
                if (!on)
                {
                    c.bg.color = used ? HeadRow : new Color32(20, 28, 42, 160);
                    c.name.text = used ? "<color=#8c96a8>?</color>" : "";
                    c.note.text = "";
                    continue;
                }
                var r = shown[i];
                string hex = GradeHex(r.rarity);
                c.bg.color = IsTop(r) ? new Color32(110, 76, 22, 245) : IsGood(r) ? new Color32(36, 56, 104, 245) : RowB;
                c.frame.color = PixelHex(hex);
                if (r.gear)
                {
                    var g = EquipmentDatabase.Get(r.itemId);
                    c.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(r.itemId));
                    c.icon.color = Color.white;
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = g != null ? $"<color={hex}>{EquipmentDatabase.RarityName(g.rarity)}</color>" : "";
                }
                else
                {
                    var p = CosmeticCatalog.Find(r.itemId);
                    var look = p != null && p.IsSkin ? SkinCatalog.LookFor(p.Skin.cls, p.Id) : null;
                    c.icon.sprite = look != null ? Game.Art.GetCharacter(look, "down", "idle0") : CosmeticAura.ForProduct(p);
                    c.icon.color = look != null ? Color.white : p != null ? p.Color : Color.white;
                    c.name.text = $"<color={hex}>{NameOf(r)}</color>";
                    c.note.text = r.duplicate ? $"<color=#b8c4d8>중복 +{r.refund}</color>"
                        : r.byPity ? "<color=#ffd34a>천장 · NEW</color>" : "<color=#8fe28f>NEW</color>";
                }
            }
        }

        static Color PixelHex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        protected override void Refresh()
        {
            bool online = StarShopClient.Available;
            var cur = Current();
            foreach (var c in cards)
            {
                bool on = c.id == banner;
                c.frame.color = on ? new Color(1f, .83f, .3f, 1f) : Color.clear;
                c.name.text = on ? $"<color=#ffd34a><b>{NameOf(c.id)}</b></color>" : NameOf(c.id);
                c.bg.color = on ? new Color32(40, 52, 74, 255) : new Color32(14, 20, 32, 255);
            }
            big.sprite = Game.Art.Get("Banners/banner_gacha_" + banner);
            bigTitle.text = $"<b>{cur.name}</b>";
            // Skins released so far, and the ones this class can draw.
            var mine = new List<string>();
            int released = 0;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            foreach (var sk in SkinCatalog.All) { released++; if (sk.cls == cls) mine.Add(sk.name); }
            badgeBg.gameObject.SetActive(banner == "skin");
            badge.text = $"<b>출시 스킨 {mine.Count}종</b>";
            int pity = banner == "skin" ? StarShopClient.SkinPity : StarShopClient.Pity;
            bool hasPity = banner == "skin" || banner == "aura";
            string top = banner == "skin" ? "스킨" : "유니크 오라";
            bigSub.text = $"{cur.sub}\n" +
                          (hasPity ? $"<color=#ffd34a>{top} 확정까지 {Mathf.Max(1, StarShopClient.PityMax - pity)}회</color> (천장 {StarShopClient.PityMax}회)\n" : "뽑은 장비는 바로 가방으로\n") +
                          (banner == "skin" ? $"<color=#ffb347>스킨 공격력 +5%</color>\n<color=#b8c4d8>{CharacterClassInfo.Get(cls).displayName} 스킨: {string.Join(" · ", mine)}</color>" : banner == "aura" ? "<color=#ffb347>오라 공격력 +1~3%</color>" : "");
            wallet.text = online
                ? (StarShopClient.Loaded ? $"보유 <color=#ffd34a>{StarShopClient.Stars(StarShopClient.Balance)}</color>" : "<color=#8c96a8>불러오는 중...</color>")
                : "<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>";
            TextOf(one).text = $"1회 · 별조각 {StarShopClient.PriceOne:N0}";
            TextOf(ten).text = $"{StarShopClient.TenCount - 1}+1회 · 별조각 {StarShopClient.PriceTen:N0}";
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            if (rateModal.gameObject.activeSelf) rateText.text = RateText();
            DrawCells();
        }

        static string NameOf(string bannerId)
        {
            foreach (var b in Banners) if (b.id == bannerId) return b.name;
            return bannerId;
        }

        List<StarRate> CurrentRates()
        {
            if (banner == "aura") return StarShopClient.Rates;
            if (banner == "skin") return StarShopClient.SkinRates;
            foreach (var b in StarShopClient.Banners) if (b.id == banner) return b.rates;
            return new List<StarRate>();
        }

        string RateText()
        {
            var list = CurrentRates();
            if (list.Count == 0) return "<color=#8c96a8>확률표를 불러오는 중입니다.</color>";
            var sb = new StringBuilder();
            sb.Append($"<size=24><b>확률 공개 · {Current().name}</b></size>\n<color=#8c96a8>확률표 {StarShopClient.RatesVersion}{(banner == "aura" ? "" : " · 내 직업 기준")}</color>\n\n");
            foreach (var r in list)
            {
                sb.Append($"<color={GradeHex(r.rarity)}><b>{r.name}  {r.rate:0.##}%</b></color>\n");
                foreach (var i in r.items)
                {
                    if (i.gear)
                    {
                        var g = EquipmentDatabase.Get(i.id);
                        sb.Append($"    {(g != null ? g.name : i.id)}   {i.rate:0.###}%\n");
                        continue;
                    }
                    var p = CosmeticCatalog.Find(i.id);
                    string mark = StarShopClient.Owned.Contains(i.id) ? "  <color=#8fe28f>보유</color>" : "";
                    string dmg = p != null && p.DamagePercent > 0 ? $"  <color=#ffb347>공격력 +{p.DamagePercent}%</color>" : "";
                    sb.Append($"    {(p != null ? p.Name : i.name)}   {i.rate:0.###}%{dmg}{mark}\n");
                }
                sb.Append('\n');
            }
            StarShopClient.Refund.TryGetValue("common", out int rc);
            StarShopClient.Refund.TryGetValue("rare", out int rr);
            StarShopClient.Refund.TryGetValue("unique", out int ru);
            sb.Append("<color=#b8c4d8>");
            if (banner == "aura" || banner == "skin")
            {
                string top = banner == "skin" ? "스킨" : "유니크";
                sb.Append($"· 천장: {top} 없이 {StarShopClient.PityMax - 1}회를 뽑으면 {StarShopClient.PityMax}번째는 {top} 확정. 나오면 0부터 다시 셉니다(기한 없음).\n");
                sb.Append("· 중복 방지: 같은 등급에서 아직 없는 것만 같은 확률로 나옵니다. 개별 확률은 아무것도 없을 때 기준입니다.\n");
                sb.Append($"· 같은 등급을 모두 가지면 중복이 나오고 별조각을 돌려줍니다(일반 {rc}, 희귀 {rr}, 유니크 {ru}{(banner == "skin" ? ", 스킨 600" : "")}).\n");
            }
            else
            {
                sb.Append("· 등급을 먼저 정하고, 그 등급의 장비 중 하나가 같은 확률로 나옵니다. 그 뽑기에 없는 등급의 몫은 가장 낮은 등급이 가집니다.\n");
                sb.Append("· 뽑은 장비는 +0으로 바로 가방에 들어갑니다. 천장은 없습니다.\n");
            }
            sb.Append($"· 10+1회는 별조각 {StarShopClient.PriceTen:N0}로 {StarShopClient.TenCount}회를 뽑습니다(마지막 1회가 보너스).</color>");
            return sb.ToString();
        }
    }
}
