using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DotRPG
{
    public sealed class CosmeticShopScreen : WindowScreen
    {
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        readonly Text[] rowLabels = new Text[CosmeticCatalog.All.Count];
        readonly RectTransform[] rows = new RectTransform[CosmeticCatalog.All.Count];
        CosmeticStore store;
        RectTransform layout;
        RectTransform listPanel, detailPanel;
        ScrollRect scroll;
        Text footer, restoreLabel;
        Vector2 lastSize;
        bool restoreFocused;
        Text description, status, actionLabel;
        Image previewAura, previewCharacter;
        Button action, restore;
        int selected;

        public static CosmeticShopScreen Create(Transform canvas)
        {
            var screen = CreateWindow<CosmeticShopScreen>(canvas, "CosmeticShop", "외형 상점 · 옷장", "menuicon_cosmetics");
            screen.store = Game.Cosmetics;
            var viewport = UIFactory.Stretch(UIFactory.Rect(screen.content, "Viewport"));
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            screen.layout = UIFactory.Place(UIFactory.Rect(viewport, "Layout"), TopLeft, TopLeft, Vector2.zero, new Vector2(1220f, 600f));
            screen.scroll = viewport.gameObject.AddComponent<ScrollRect>();
            screen.scroll.viewport = viewport;
            screen.scroll.content = screen.layout;
            screen.scroll.horizontal = false;
            screen.scroll.movementType = ScrollRect.MovementType.Clamped;
            screen.scroll.scrollSensitivity = 36f;
            var list = Panel(screen.layout, "Catalog", TopLeft, TopLeft, Vector2.zero, new Vector2(400f, 520f), UiTheme.Panel);
            screen.listPanel = list.rectTransform;
            Label(list.transform, "Hint", "마음에 드는 빛을 골라 보세요", 22, TopLeft, TopLeft, new Vector2(20f, -20f), new Vector2(360f, 44f));
            for (int i = 0; i < CosmeticCatalog.All.Count; i++)
            {
                int index = i;
                var row = StoreButton(list.transform, "Product_" + CosmeticCatalog.All[i].Id, "", TopLeft, TopLeft,
                    new Vector2(20f, -80f - i * 92f), new Vector2(360f, 78f), () => screen.Select(index), 22);
                screen.rowLabels[i] = row.GetComponentInChildren<Text>();
                screen.rows[i] = row.GetComponent<RectTransform>();
                UIFactory.Stretch(screen.rowLabels[i].rectTransform, 86f, 0f, 12f, 0f);
                var product = CosmeticCatalog.All[i];
                var thumbnail = UIFactory.Image(row.transform, "Thumbnail", CosmeticAura.ForProduct(product), product.Color);
                UIFactory.Place(thumbnail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(68f, 34f));
            }

            var detail = Panel(screen.layout, "Preview", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(780f, 520f), UiTheme.PanelDeep);
            screen.detailPanel = detail.rectTransform;
            screen.previewAura = UIFactory.Image(detail.transform, "Aura", CosmeticAura.Sprite, Color.white);
            UIFactory.Place(screen.previewAura.rectTransform, TopLeft, TopLeft, new Vector2(36f, -204f), new Vector2(220f, 110f));
            screen.previewCharacter = UIFactory.Image(detail.transform, "Character", null, Color.white);
            screen.previewCharacter.preserveAspect = true;
            UIFactory.Place(screen.previewCharacter.rectTransform, TopLeft, TopLeft, new Vector2(66f, -100f), new Vector2(160f, 160f));
            screen.description = Label(detail.transform, "Description", "", 22, TopLeft, TopLeft, new Vector2(290f, -40f), new Vector2(450f, 260f));
            screen.action = StoreButton(detail.transform, "PurchaseOrEquip", "", TopLeft, TopLeft,
                new Vector2(290f, -312f), new Vector2(440f, 56f), screen.ActivateSelected, 22);
            screen.actionLabel = screen.action.GetComponentInChildren<Text>();
            screen.restore = StoreButton(detail.transform, "Restore", "구매 내역 복원", TopLeft, TopLeft,
                new Vector2(290f, -388f), new Vector2(440f, 50f), screen.Restore, 20);
            screen.restoreLabel = screen.restore.GetComponentInChildren<Text>();
            screen.status = Label(detail.transform, "Status", "", 18, TopLeft, TopLeft, new Vector2(24f, -454f), new Vector2(732f, 56f));
            screen.footer = Label(screen.layout, "Footer", "외형 전용 · 능력치 변화 없음 · 확정 구성 · 보유 상품 재구매 없음\n↑↓ 상품 선택   → 구매 복원   ← 상품으로   Enter / A 실행   Esc / B 닫기", 18,
                TopLeft, TopLeft, new Vector2(0f, -536f), new Vector2(1180f, 60f));
            screen.store.Changed += screen.Refresh;
            return screen;
        }

        public override async void Show()
        {
            restoreFocused = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            base.Show();
            Reflow();
            scroll.verticalNormalizedPosition = 1f;
            await store.RefreshAsync();
        }

        void Select(int index)
        {
            restoreFocused = false;
            selected = Mathf.Clamp(index, 0, CosmeticCatalog.All.Count - 1);
            Refresh();
        }

        protected override void Refresh()
        {
            if (store == null) return;
            var product = CosmeticCatalog.All[selected];
            var offer = store.OfferFor(product.Id);
            bool owned = store.Owns(product.Id);
            bool equipped = store.Equipped.Id == product.Id;
            for (int i = 0; i < CosmeticCatalog.All.Count; i++)
            {
                var item = CosmeticCatalog.All[i];
                string state = store.Equipped.Id == item.Id ? "착용 중" : store.Owns(item.Id) ? "보유" : "미보유";
                rowLabels[i].text = $"{(i == selected ? "▶ " : "")}{item.Name}\n<size=17>{state}</size>";
                rowLabels[i].color = i == selected && !restoreFocused ? UiTheme.Accent : UiTheme.TextPrimary;
            }
            previewAura.sprite = CosmeticAura.ForProduct(product);
            previewAura.color = product.Color;
            var animator = Game.Player != null ? Game.Player.GetComponent<CharacterAnimator>() : null;
            previewCharacter.sprite = animator != null ? animator.Renderer.sprite : Game.Art.GetCharacter(CharacterLook.Player, "down", "idle0");
            string price = product.IsFree ? "무료" : owned ? "보유 중" : offer?.LocalizedPrice ?? "판매 준비 중";
            description.text = $"<size=30>{product.Name}</size>\n\n발밑 오라 · 영구 소장\n모든 직업에서 사용 가능\n\n{price}\n선택한 외형을 미리 보는 중입니다.";
            actionLabel.text = equipped ? "착용 중" : owned ? "착용하기" : offer != null ? $"{offer.LocalizedPrice} · 구매 확인" : "판매 준비 중";
            action.interactable = !store.IsBusy && !equipped && (owned || (store.IsAvailable && !store.NeedsPurchaseRecovery && offer != null));
            restore.interactable = store.IsAvailable && !store.IsBusy;
            restoreLabel.text = (restoreFocused ? "▶ " : "") + "구매 내역 복원";
            restoreLabel.color = restoreFocused ? UiTheme.Accent : UiTheme.TextPrimary;
            status.text = store.Status;
        }

        void ActivateSelected()
        {
            if (!IsTop || !action.interactable) return;
            var product = CosmeticCatalog.All[selected];
            if (store.Owns(product.Id)) { store.Equip(product.Id); return; }
            var offer = store.OfferFor(product.Id);
            if (offer == null) return;
            Game.UI.Confirm($"{product.Name}\n{offer.LocalizedPrice} · 1회 결제 · 영구 외형\n이 상품을 구매하시겠습니까?",
                async () => await store.PurchaseAsync(offer), true);
        }

        async void Restore()
        {
            if (IsTop && restore.interactable) await store.RestoreAsync();
        }

        void LateUpdate()
        {
            if (content.rect.size != lastSize) Reflow();
        }

        void Reflow()
        {
            lastSize = content.rect.size;
            float width = Mathf.Max(320f, lastSize.x);
            bool columns = width >= 1180f;
            float listWidth = columns ? 380f : width;
            float detailWidth = columns ? width - 400f : width;
            bool compact = detailWidth < 700f;
            float detailTop = columns ? 0f : 500f;
            float detailHeight = compact ? 820f : 520f;
            listPanel.sizeDelta = new Vector2(listWidth, 480f);
            for (int i = 0; i < rows.Length; i++) rows[i].sizeDelta = new Vector2(listWidth - 40f, 78f);
            UIFactory.Place(detailPanel, TopLeft, TopLeft, new Vector2(columns ? 400f : 0f, -detailTop), new Vector2(detailWidth, detailHeight));
            float textX = compact ? 24f : 290f;
            float textWidth = detailWidth - textX - 24f;
            Place(previewAura.rectTransform, compact ? (detailWidth - 220f) / 2f : 36f, 204f, 220f, 110f);
            Place(previewCharacter.rectTransform, compact ? (detailWidth - 160f) / 2f : 66f, 100f, 160f, 160f);
            Place(description.rectTransform, textX, compact ? 320f : 40f, textWidth, 260f);
            Place(action.GetComponent<RectTransform>(), textX, compact ? 594f : 312f, textWidth, 56f);
            Place(restore.GetComponent<RectTransform>(), textX, compact ? 670f : 388f, textWidth, 50f);
            Place(status.rectTransform, 24f, compact ? 738f : 454f, detailWidth - 48f, compact ? 76f : 56f);
            Place(footer.rectTransform, 0f, detailTop + detailHeight + 16f, width, 110f);
            layout.sizeDelta = new Vector2(width, detailTop + detailHeight + 130f);
        }

        static void Place(RectTransform target, float x, float y, float width, float height) =>
            UIFactory.Place(target, TopLeft, TopLeft, new Vector2(x, -y), new Vector2(width, height));

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

        public void Navigate(Vector2Int direction)
        {
            if (direction.x != 0) restoreFocused = direction.x > 0;
            if (direction.y != 0) Select(selected - direction.y);
            Refresh();
            var target = restoreFocused ? restore.GetComponent<RectTransform>() : rows[selected];
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(layout, target);
            float top = -bounds.max.y;
            float bottom = -bounds.min.y;
            float offset = layout.anchoredPosition.y;
            float height = scroll.viewport.rect.height;
            if (top < offset) offset = top;
            else if (bottom > offset + height) offset = bottom - height;
            scroll.StopMovement();
            layout.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset, 0f, Mathf.Max(0f, layout.rect.height - height)));
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
        }
    }

    // Pointer clicks use Button; keyboard/gamepad commands have exactly one owner: the screen.
    public sealed class CosmeticShopButton : Button
    {
        public override void OnSubmit(BaseEventData eventData) { }
        public override void OnMove(AxisEventData eventData) { }
    }
}
