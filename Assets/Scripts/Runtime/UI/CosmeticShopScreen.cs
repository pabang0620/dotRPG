using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵 · 옷장. Three tabs (오라 / 전사 스킨 / 마법사 스킨); the list on the left scrolls on its own and the preview
    /// on the right always stays in view: the character wearing the selected look (a skin turns through every
    /// direction), its grade, attack bonus, price and the buy / wear button.
    /// </summary>
    public sealed class CosmeticShopScreen : WindowScreen
    {
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        const float ListW = 430f, RowH = 72f, TabH = 46f;
        enum Tab { Aura, WarriorSkin, MageSkin }
        static readonly string[] TabNames = { "오라", "전사 스킨", "마법사 스킨" };

        sealed class Row { public CosmeticProduct product; public RectTransform rt; public Text label; public Image thumb; }

        readonly List<Row> rows = new List<Row>();
        readonly List<Button> tabs = new List<Button>();
        CosmeticStore store;
        RectTransform layout, listPanel, detailPanel, listContent;
        ScrollRect listScroll;
        Text footer, restoreLabel, wallet, title, description, status, actionLabel, bonus;
        Image previewAura, previewCharacter, previewGlow;
        Button action, restore, gacha;
        Tab tab;
        int selected;
        bool restoreFocused;
        string shownFacing;
        Vector2 lastSize;

        public static CosmeticShopScreen Create(Transform canvas)
        {
            var s = CreateWindow<CosmeticShopScreen>(canvas, "CosmeticShop", "옷장", "menuicon_cosmetics");
            s.store = Game.Cosmetics;
            var viewport = UIFactory.Stretch(UIFactory.Rect(s.content, "Viewport"));
            s.layout = UIFactory.Stretch(UIFactory.Rect(viewport, "Layout"));

            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                s.tabs.Add(StoreButton(s.layout, "Tab_" + t, TabNames[i], TopLeft, TopLeft, new Vector2(i * 146f, 0f), new Vector2(138f, TabH), () => s.SelectTab(t), 19));
            }
            s.gacha = StoreButton(s.layout, "Gacha", "캐시샵 (뽑기)", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(200f, TabH), s.OpenGacha, 19);
            s.wallet = Label(s.layout, "Wallet", "", 19, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-212f, 0f), new Vector2(300f, TabH), TextAnchor.MiddleRight);

            // Left: the list of the current tab, scrolling inside its panel.
            var list = Panel(s.layout, "Catalog", TopLeft, TopLeft, new Vector2(0f, -(TabH + 10f)), new Vector2(ListW, 480f), UiTheme.Panel);
            s.listPanel = list.rectTransform;
            var lv = UIFactory.Rect(list.transform, "List");
            UIFactory.Stretch(lv, 10f, 10f, 10f, 10f);
            lv.gameObject.AddComponent<Image>().color = Color.clear;
            lv.gameObject.AddComponent<RectMask2D>();
            s.listContent = UIFactory.Place(UIFactory.Rect(lv, "Items"), TopLeft, TopLeft, Vector2.zero, new Vector2(ListW - 20f, 100f));
            s.listScroll = lv.gameObject.AddComponent<ScrollRect>();
            s.listScroll.viewport = lv;
            s.listScroll.content = s.listContent;
            s.listScroll.horizontal = false;
            s.listScroll.movementType = ScrollRect.MovementType.Clamped;
            s.listScroll.scrollSensitivity = 36f;
            foreach (var product in CosmeticCatalog.All)
            {
                var p = product;
                var row = new Row { product = p };
                var b = StoreButton(s.listContent, "Product_" + p.Id, "", TopLeft, TopLeft, Vector2.zero, new Vector2(ListW - 20f, RowH - 6f), () => s.SelectProduct(p), 20);
                row.rt = b.GetComponent<RectTransform>();
                row.label = b.GetComponentInChildren<Text>();
                row.label.alignment = TextAnchor.MiddleLeft;
                UIFactory.Stretch(row.label.rectTransform, 92f, 0f, 10f, 0f);
                var look = p.IsSkin ? SkinCatalog.LookFor(p.Skin.cls, p.Id) : null;
                var card = CosmeticAura.Card(p);
                row.thumb = UIFactory.Image(b.transform, "Thumbnail", card ?? (look != null ? Game.Art.GetCharacter(look, "down", "idle0") : CosmeticAura.ForProduct(p)), card != null || look != null ? Color.white : p.Color);
                row.thumb.preserveAspect = true;
                UIFactory.Place(row.thumb.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(72f, card != null ? 64f : look != null ? 60f : 36f));
                s.rows.Add(row);
            }

            // Right: the preview, fixed in place.
            var detail = Panel(s.layout, "Preview", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(TabH + 10f)), new Vector2(760f, 480f), UiTheme.PanelDeep);
            s.detailPanel = detail.rectTransform;
            var stage = Panel(detail.transform, "Stage", TopLeft, TopLeft, new Vector2(20f, -20f), new Vector2(260f, 300f), new Color32(18, 26, 40, 255));
            s.previewGlow = UIFactory.Image(stage.transform, "Glow", Game.Art.Get("fx_glow"), Color.clear);
            s.previewGlow.raycastTarget = false;
            UIFactory.Place(s.previewGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(300f, 300f));
            s.previewAura = UIFactory.Image(stage.transform, "Aura", CosmeticAura.Sprite, Color.white);
            UIFactory.Place(s.previewAura.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(220f, 110f));
            s.previewCharacter = UIFactory.Image(stage.transform, "Character", null, Color.white);
            s.previewCharacter.preserveAspect = true;
            UIFactory.Place(s.previewCharacter.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(240f, 240f));

            s.title = Label(detail.transform, "Title", "", 30, TopLeft, TopLeft, new Vector2(300f, -20f), new Vector2(440f, 44f), TextAnchor.MiddleLeft);
            s.bonus = Label(detail.transform, "Bonus", "", 22, TopLeft, TopLeft, new Vector2(300f, -68f), new Vector2(440f, 34f), TextAnchor.MiddleLeft);
            s.description = Label(detail.transform, "Description", "", 18, TopLeft, TopLeft, new Vector2(300f, -108f), new Vector2(440f, 200f), TextAnchor.UpperLeft);
            s.action = StoreButton(detail.transform, "PurchaseOrEquip", "", TopLeft, TopLeft, new Vector2(300f, -320f), new Vector2(440f, 58f), s.ActivateSelected, 22);
            s.actionLabel = s.action.GetComponentInChildren<Text>();
            s.restore = StoreButton(detail.transform, "Restore", "보유 외형 다시 불러오기", TopLeft, TopLeft, new Vector2(300f, -386f), new Vector2(440f, 44f), s.Restore, 18);
            s.restoreLabel = s.restore.GetComponentInChildren<Text>();
            s.status = Label(detail.transform, "Status", "", 17, TopLeft, TopLeft, new Vector2(20f, -330f), new Vector2(260f, 120f), TextAnchor.UpperLeft);
            s.footer = Label(s.layout, "Footer", "↑↓ 고르기   → 다시 불러오기   Enter / A 실행   ESC / B 닫기", 16,
                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(900f, 26f), TextAnchor.MiddleLeft);
            s.store.Changed += s.Refresh;
            StarShopClient.Changed += s.Refresh;
            s.SelectTab(Tab.Aura);
            return s;
        }

        public override async void Show()
        {
            restoreFocused = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            base.Show();
            Reflow();
            // Open on the skins of my class when I'm wearing one, else on the auras.
            if (Game.Player != null && store.SkinFor(Game.Player.Class) != null) SelectTab(Game.Player.Class == CharacterClass.Mage ? Tab.MageSkin : Tab.WarriorSkin);
            await store.RefreshAsync();
        }

        static Tab TabOf(CosmeticProduct p) => !p.IsSkin ? Tab.Aura : p.Skin.cls == CharacterClass.Mage ? Tab.MageSkin : Tab.WarriorSkin;

        List<Row> Visible()
        {
            var v = new List<Row>();
            foreach (var r in rows) if (TabOf(r.product) == tab) v.Add(r);
            return v;
        }

        void SelectTab(Tab t)
        {
            tab = t;
            selected = 0;
            restoreFocused = false;
            float y = 0f;
            foreach (var r in rows)
            {
                bool on = TabOf(r.product) == tab;
                r.rt.gameObject.SetActive(on);
                if (!on) continue;
                UIFactory.Place(r.rt, TopLeft, TopLeft, new Vector2(0f, -y), new Vector2(listContent.sizeDelta.x, RowH - 6f));
                y += RowH;
            }
            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x, y);
            listScroll.verticalNormalizedPosition = 1f;
            if (gameObject.activeInHierarchy) Game.Audio.PlaySfx("select");
            Refresh();
        }

        void SelectProduct(CosmeticProduct p)
        {
            var v = Visible();
            int i = v.FindIndex(r => r.product == p);
            if (i < 0) return;
            restoreFocused = false;
            selected = i;
            shownFacing = null;
            Refresh();
        }

        CosmeticProduct Current()
        {
            var v = Visible();
            return v.Count == 0 ? CosmeticCatalog.All[0] : v[Mathf.Clamp(selected, 0, v.Count - 1)].product;
        }

        bool Wearing(CosmeticProduct p) => p.IsSkin ? store.SkinFor(p.Skin.cls) == p.Id : store.Equipped.Id == p.Id;

        protected override void Refresh()
        {
            if (store == null) return;
            for (int i = 0; i < tabs.Count; i++)
                TextOf(tabs[i]).text = (Tab)i == tab ? $"<color=#ffd34a>{TabNames[i]}</color>" : TabNames[i];
            wallet.text = store.IsAvailable ? $"보유 <color=#ffd34a>{StarShopClient.Stars(StarShopClient.Balance)}</color>" : "<color=#8c96a8>온라인 전용</color>";
            gacha.interactable = store.IsAvailable;
            var v = Visible();
            for (int i = 0; i < v.Count; i++)
            {
                var item = v[i].product;
                string state = Wearing(item) ? "<color=#8fe28f>착용 중</color>" : store.Owns(item.Id) ? "보유" : "<color=#8c96a8>미보유</color>";
                string grade = $"<color={CosmeticCatalog.RarityHex(item.Rarity)}>{CosmeticCatalog.RarityName(item.Rarity)}{(item.IsSkin ? " 스킨" : "")}</color>";
                string dmg = item.DamagePercent > 0 ? $" · <color=#ffb347>공격력 +{item.DamagePercent}%</color>" : "";
                v[i].label.text = $"<b>{item.Name}</b>\n<size=16>{grade}{dmg} · {state}</size>";
                v[i].label.color = i == selected && !restoreFocused ? UiTheme.Accent : UiTheme.TextPrimary;
            }

            var product = Current();
            var skin = product.Skin;
            var offer = store.OfferFor(product.Id);
            bool owned = store.Owns(product.Id);
            bool equipped = Wearing(product);
            var aura = skin != null ? store.Equipped : product;
            previewAura.sprite = CosmeticAura.ForProduct(aura);
            previewAura.color = aura.Color;
            previewGlow.color = product.Rarity == CosmeticRarity.Unique ? new Color(1f, .78f, .3f, .35f) : product.Rarity == CosmeticRarity.Epic ? new Color(.75f, .5f, 1f, .3f) : product.Rarity == CosmeticRarity.Rare ? new Color(.45f, .65f, 1f, .25f) : new Color(1f, 1f, 1f, .08f);
            if (skin == null)
            {
                var animator = Game.Player != null ? Game.Player.GetComponent<CharacterAnimator>() : null;
                previewCharacter.sprite = animator != null ? animator.Renderer.sprite : Game.Art.GetCharacter(CharacterLook.Player, "down", "idle0");
            }
            title.text = $"<color={CosmeticCatalog.RarityHex(product.Rarity)}>{product.Name}</color>";
            bonus.text = product.DamagePercent > 0 ? $"착용 시 <color=#ffb347>공격력 +{product.DamagePercent}%</color>" : "<color=#8c96a8>능력치 없음</color>";
            // The price is on the buy button; here only what it is (kept short so it never runs under the button).
            string have = product.IsFree ? "무료" : owned ? "<color=#8fe28f>보유 중</color>" : store.IsAvailable ? "" : "<color=#8c96a8>온라인 전용</color>";
            description.text = skin != null
                ? $"<color={CosmeticCatalog.RarityHex(product.Rarity)}>{CosmeticCatalog.RarityName(product.Rarity)} 스킨</color> · {CharacterClassInfo.Get(skin.cls).displayName} 전용 · 영구 소장\n{skin.blurb}\n<color=#b8c4d8>8방향 모든 동작 전용 그림 · {(skin.afterimage ? "걸으면 잔상과 입자" : "주위에 빛 입자")}</color>\n{have}"
                : $"{(product.IsFree ? "기본" : CosmeticCatalog.RarityName(product.Rarity))} 등급 발밑 오라 · 영구 소장\n모든 직업 공용\n{have}";
            actionLabel.text = equipped ? (skin != null ? "벗기" : "착용 중") : owned ? "착용하기" : offer != null ? $"{offer.LocalizedPrice}로 구매"
                : product.GaugeOnly && store.IsAvailable ? "선택 게이지로만 획득" : "온라인 전용";
            action.interactable = !store.IsBusy && (!equipped || skin != null) && (owned || (store.IsAvailable && !store.NeedsPurchaseRecovery && offer != null));
            restore.interactable = store.IsAvailable && !store.IsBusy;
            restoreLabel.text = (restoreFocused ? "▶ " : "") + "보유 외형 다시 불러오기";
            restoreLabel.color = restoreFocused ? UiTheme.Accent : UiTheme.TextPrimary;
            status.text = store.Status;
        }

        void ActivateSelected()
        {
            if (!IsTop || !action.interactable) return;
            var product = Current();
            if (product.IsSkin && store.SkinFor(product.Skin.cls) == product.Id) { store.RemoveSkin(product.Skin.cls); return; }
            if (store.Owns(product.Id)) { store.Equip(product.Id); return; }
            var offer = store.OfferFor(product.Id);
            if (offer == null) return;
            if (StarShopClient.ExchangePrice.TryGetValue(product.Id, out int cost) && StarShopClient.Balance < cost)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {cost:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            Game.UI.Confirm($"{product.Name}\n{offer.LocalizedPrice} · 영구 소장\n구매하시겠습니까?", async () => await store.PurchaseAsync(offer), true);
        }

        void OpenGacha()
        {
            if (!IsTop || GachaScreen.Instance == null) return;
            Game.Flow.OpenWindow(GachaScreen.Instance);
        }

        async void Restore()
        {
            if (IsTop && restore.interactable) await store.RestoreAsync();
        }

        static readonly string[] PreviewTurn = { "down", "downside", "side", "upside", "up", "upside", "side", "downside" };
        static string PreviewFacing() => PreviewTurn[(int)(Time.unscaledTime / 0.9f) % PreviewTurn.Length];

        void LateUpdate()
        {
            if (content.rect.size != lastSize) Reflow();
            float t = Time.unscaledTime;
            foreach (var r in rows)
                if (r.rt.gameObject.activeSelf && r.product.Effect != CosmeticEffect.None) r.thumb.color = r.product.ColorAt(t);
            var current = Current();
            var aura = current.IsSkin ? store.Equipped : current;
            if (aura.Effect != CosmeticEffect.None) previewAura.color = aura.ColorAt(t);
            if (current.IsSkin && PreviewFacing() != shownFacing)
            {
                shownFacing = PreviewFacing();
                previewCharacter.sprite = Game.Art.GetCharacter(SkinCatalog.LookFor(current.Skin.cls, current.Id), shownFacing, "idle0");
            }
            var g = previewGlow.color;
            if (g.a > 0.1f) { g.a = (current.Rarity >= CosmeticRarity.Epic ? .3f : .2f) + .1f * Mathf.Sin(t * 3f); previewGlow.color = g; }
        }

        void Reflow()
        {
            lastSize = content.rect.size;
            float width = Mathf.Max(800f, lastSize.x);
            float height = Mathf.Max(420f, lastSize.y - (TabH + 10f) - 30f);
            listPanel.sizeDelta = new Vector2(ListW, height);
            detailPanel.sizeDelta = new Vector2(width - ListW - 16f, height);
        }

        static Button StoreButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, System.Action onClick, int font)
        {
            var image = Img(parent, name, "ui_btngray", Color.white);
            image.raycastTarget = true;
            UIFactory.Place(image.rectTransform, anchor, pivot, position, size);
            var button = image.gameObject.AddComponent<CosmeticShopButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick());
            UiButton.Attach(button);
            var text = UIFactory.Text(image.transform, "Text", label, UiTheme.Size(font), Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(text.rectTransform);
            return button;
        }

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        public void Navigate(Vector2Int direction)
        {
            if (direction.x != 0) restoreFocused = direction.x > 0;
            if (direction.y != 0)
            {
                var v = Visible();
                restoreFocused = false;
                selected = Mathf.Clamp(selected - direction.y, 0, Mathf.Max(0, v.Count - 1));
                shownFacing = null;
                if (v.Count > 0)
                {
                    // keep the chosen row inside the scrolling list
                    float top = selected * RowH, bottom = top + RowH;
                    float view = listScroll.viewport.rect.height;
                    float offset = listContent.anchoredPosition.y;
                    if (top < offset) offset = top;
                    else if (bottom > offset + view) offset = bottom - view;
                    listScroll.StopMovement();
                    listContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset, 0f, Mathf.Max(0f, listContent.rect.height - view)));
                }
            }
            Refresh();
        }

        public void SubmitFocused()
        {
            if (restoreFocused) Restore();
            else ActivateSelected();
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf || !TakesInput) return;
            if (Game.Input.NavigateStep != Vector2Int.zero) Navigate(Game.Input.NavigateStep);
            if (Game.Input.SubmitPressed) SubmitFocused();
        }

        void OnDestroy()
        {
            if (store != null) store.Changed -= Refresh;
            StarShopClient.Changed -= Refresh;
        }
    }

    // Pointer clicks use Button; keyboard/gamepad commands have exactly one owner: the screen.
    public sealed class CosmeticShopButton : Button
    {
        public override void OnSubmit(BaseEventData eventData) { }
        public override void OnMove(AxisEventData eventData) { }
    }
}
