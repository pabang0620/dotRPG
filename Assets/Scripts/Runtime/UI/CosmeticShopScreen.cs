using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public sealed class CosmeticShopScreen : WindowScreen
    {
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        readonly Text[] rowLabels = new Text[CosmeticCatalog.All.Count];
        CosmeticStore store;
        RectTransform layout;
        Text description, status, actionLabel;
        Image previewAura, previewCharacter;
        Button action, restore;
        int selected;

        public static CosmeticShopScreen Create(Transform canvas)
        {
            var screen = CreateWindow<CosmeticShopScreen>(canvas, "CosmeticShop", "외형 상점 · 옷장", "menuicon_cosmetics");
            screen.store = Game.Cosmetics;
            screen.layout = UIFactory.Place(UIFactory.Rect(screen.content, "Layout"), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1220f, 600f));
            var list = Panel(screen.layout, "Catalog", TopLeft, TopLeft, Vector2.zero, new Vector2(400f, 520f), UiTheme.Panel);
            Label(list.transform, "Hint", "마음에 드는 빛을 골라 보세요", 22, TopLeft, TopLeft, new Vector2(20f, -20f), new Vector2(360f, 44f));
            for (int i = 0; i < CosmeticCatalog.All.Count; i++)
            {
                int index = i;
                var row = Button(list.transform, "Product_" + CosmeticCatalog.All[i].Id, "", "ui_btngray", TopLeft, TopLeft,
                    new Vector2(20f, -80f - i * 92f), new Vector2(360f, 78f), () => screen.Select(index), 22);
                screen.rowLabels[i] = row.GetComponentInChildren<Text>();
                UIFactory.Stretch(screen.rowLabels[i].rectTransform, 86f, 0f, 12f, 0f);
                var product = CosmeticCatalog.All[i];
                var thumbnail = UIFactory.Image(row.transform, "Thumbnail", CosmeticAura.ForProduct(product), product.Color);
                UIFactory.Place(thumbnail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(68f, 34f));
            }

            var detail = Panel(screen.layout, "Preview", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(780f, 520f), UiTheme.PanelDeep);
            screen.previewAura = UIFactory.Image(detail.transform, "Aura", CosmeticAura.Sprite, Color.white);
            UIFactory.Place(screen.previewAura.rectTransform, TopLeft, TopLeft, new Vector2(36f, -204f), new Vector2(220f, 110f));
            screen.previewCharacter = UIFactory.Image(detail.transform, "Character", null, Color.white);
            screen.previewCharacter.preserveAspect = true;
            UIFactory.Place(screen.previewCharacter.rectTransform, TopLeft, TopLeft, new Vector2(66f, -100f), new Vector2(160f, 160f));
            screen.description = Label(detail.transform, "Description", "", 22, TopLeft, TopLeft, new Vector2(290f, -40f), new Vector2(450f, 260f));
            screen.action = Button(detail.transform, "PurchaseOrEquip", "", "ui_btngray", TopLeft, TopLeft,
                new Vector2(290f, -312f), new Vector2(440f, 56f), screen.ActivateSelected, 22);
            screen.actionLabel = screen.action.GetComponentInChildren<Text>();
            screen.restore = Button(detail.transform, "Restore", "구매 내역 복원", "ui_btngray", TopLeft, TopLeft,
                new Vector2(290f, -388f), new Vector2(440f, 50f), screen.Restore, 20);
            screen.status = Label(detail.transform, "Status", "", 18, TopLeft, TopLeft, new Vector2(24f, -454f), new Vector2(732f, 56f));
            Label(screen.layout, "Footer", "외형 전용 · 능력치 변화 없음 · 확정 구성 · 보유 상품 재구매 없음\n↑↓ 선택   Enter / A 착용·구매   Esc / B 닫기", 18,
                TopLeft, TopLeft, new Vector2(0f, -536f), new Vector2(1180f, 60f));
            screen.store.Changed += screen.Refresh;
            return screen;
        }

        public override async void Show()
        {
            base.Show();
            await store.RefreshAsync();
        }

        void Select(int index)
        {
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
                rowLabels[i].color = i == selected ? UiTheme.Accent : UiTheme.TextPrimary;
            }
            previewAura.sprite = CosmeticAura.ForProduct(product);
            previewAura.color = product.Color;
            var animator = Game.Player != null ? Game.Player.GetComponent<CharacterAnimator>() : null;
            previewCharacter.sprite = animator != null ? animator.Renderer.sprite : Game.Art.GetCharacter(CharacterLook.Player, "down", "idle0");
            string price = product.IsFree ? "무료" : owned ? "보유 중" : offer?.LocalizedPrice ?? "판매 준비 중";
            description.text = $"<size=30>{product.Name}</size>\n\n발밑 오라 · 영구 소장\n모든 직업에서 사용 가능\n\n{price}\n선택한 외형을 미리 보는 중입니다.";
            actionLabel.text = equipped ? "착용 중" : owned ? "착용하기" : offer != null ? $"{offer.LocalizedPrice} · 구매 확인" : "판매 준비 중";
            action.interactable = !store.IsBusy && !equipped && (owned || (store.IsAvailable && offer != null));
            restore.interactable = store.IsAvailable && !store.IsBusy;
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

        async void Restore() => await store.RestoreAsync();

        void LateUpdate()
        {
            // Keep both panels inside the window at larger UI scale settings.
            float scale = Mathf.Min(1f, content.rect.width / 1220f, content.rect.height / 600f);
            layout.localScale = Vector3.one * Mathf.Max(0f, scale);
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf || !TakesInput) return;
            if (Game.Input.NavigateStep.y != 0) Select(selected - Game.Input.NavigateStep.y);
            if (Game.Input.SubmitPressed) ActivateSelected();
        }

        void OnDestroy()
        {
            if (store != null) store.Changed -= Refresh;
        }
    }
}
