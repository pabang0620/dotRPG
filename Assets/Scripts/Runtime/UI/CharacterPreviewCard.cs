using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] Character card of the online list and creation screens: the sprite walks in place while the card is
    /// chosen and idles (dimmed) otherwise, with a name line and a small line under it.
    /// </summary>
    public sealed class CharacterPreviewCard
    {
        static readonly string[] WalkFrames = { "walk0", "walk1", "walk2", "walk3" };

        readonly Image bg, preview;
        readonly Text title, sub;
        CharacterLook look;
        bool selected;

        public RectTransform Rect { get; }

        CharacterPreviewCard(RectTransform rect, Image bg, Image preview, Text title, Text sub)
        {
            Rect = rect; this.bg = bg; this.preview = preview; this.title = title; this.sub = sub;
        }

        /// <param name="pos">Top-centre of the card, relative to the parent's top-centre.</param>
        public static CharacterPreviewCard Create(RectTransform parent, string name, Vector2 pos, Vector2 size, float spriteSize, int titleSize)
        {
            var rect = UIFactory.Place(UIFactory.Rect(parent, name), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, size);
            var bg = UIFactory.Panel(rect, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            var preview = UIFactory.Image(rect, "Preview", null, Color.white);
            // 64 px warrior frames and 128 px generated frames (mage) both fill the same box.
            UIFactory.Place(preview.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0, -2), new Vector2(spriteSize, spriteSize));
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            var title = UIFactory.Text(rect, "Name", "", titleSize, UIColors.Cream, TextAnchor.MiddleCenter, true);
            var sub = UIFactory.Text(rect, "Sub", "", 17, UiTheme.TextSecondary, TextAnchor.MiddleCenter, true);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(size.x - 10f, 24f));
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(size.x - 10f, titleSize + 8f));
            title.raycastTarget = false;
            sub.raycastTarget = false;
            return new CharacterPreviewCard(rect, bg, preview, title, sub);
        }

        /// <summary>The look this class shows in the world: the worn costume skin, else the class's own body.</summary>
        public static CharacterLook LookFor(CharacterClass cls) =>
            SkinCatalog.LookFor(cls, Game.Cosmetics?.SkinFor(cls)) ?? CharacterClassInfo.Get(cls).Look;

        public void Set(CharacterClass cls, string name, string line)
        {
            look = LookFor(cls);
            title.text = name;
            sub.text = line ?? "";
        }

        public void SetSelected(bool on)
        {
            selected = on;
            bg.color = on ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            title.color = on ? UIColors.Highlight : UIColors.Disabled;
            preview.color = on ? Color.white : new Color(0.6f, 0.6f, 0.65f, 1f);
        }

        /// <summary>Chosen: walks in place facing the camera. Otherwise: the slow idle.</summary>
        public void Animate(float t)
        {
            if (look == null) return;
            string frame = selected
                ? WalkFrames[Mathf.FloorToInt(t * 7f) % WalkFrames.Length]
                : (Mathf.FloorToInt(t * 1.8f) % 2 == 0 ? "idle0" : "idle1");
            preview.sprite = Game.Art.GetCharacter(look, "down", frame);
        }

        /// <summary>Hover selects the menu row, a click presses it (like the offline class cards).</summary>
        public void BindToMenu(MenuList list, int index)
        {
            var pointer = bg.gameObject.AddComponent<MenuItemPointer>();
            pointer.list = list;
            pointer.index = index;
        }

        /// <summary>A click runs this instead of a menu row.</summary>
        public void OnClick(Action action) => bg.gameObject.AddComponent<CardClick>().action = action;

        sealed class CardClick : MonoBehaviour, IPointerClickHandler
        {
            public Action action;
            public void OnPointerClick(PointerEventData eventData) => action?.Invoke();
        }
    }
}
