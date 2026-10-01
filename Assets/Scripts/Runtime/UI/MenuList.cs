using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Vertical menu driven by <see cref="InputReader"/> (keyboard / gamepad) and the mouse.
    /// Items are buttons or left/right option selectors (volume, resolution...).
    /// Only visible menus read input, and never on the frame they were opened.
    /// </summary>
    public class MenuList : MonoBehaviour
    {
        public class Item
        {
            public string label;
            public Func<string> value;
            public Action submit;
            public Action<int> adjust;
            public Func<bool> enabled;
            public RectTransform root;
            public Text labelText;
            public Text valueText;
            public Image highlight;

            public bool IsEnabled => enabled == null || enabled();
        }

        readonly List<Item> items = new List<Item>();
        int selected;
        int openedFrame;
        float rowHeight;
        int fontSize;
        float width;
        bool dark;

        public Action OnCancel;
        /// <summary>Left-align button labels (lists of items rather than centred menus).</summary>
        public bool AlignLeft;

        public static MenuList Create(Transform parent, string name, float width, float rowHeight, int fontSize, bool darkTheme)
        {
            var rt = UIFactory.Rect(parent, name);
            var list = rt.gameObject.AddComponent<MenuList>();
            list.width = width;
            list.rowHeight = rowHeight;
            list.fontSize = fontSize;
            list.dark = darkTheme;
            return list;
        }

        public RectTransform RectTransform => (RectTransform)transform;
        public float Height => items.Count * rowHeight;
        public int Selected => selected;

        /// <summary>Removes every row (used by screens whose contents change, e.g. the bag).</summary>
        public void Clear()
        {
            foreach (var item in items)
                if (item.root != null) Destroy(item.root.gameObject);
            items.Clear();
            RectTransform.sizeDelta = new Vector2(width, 0f);
        }

        /// <summary>Keeps the cursor on row <paramref name="index"/> (clamped), without a sound.</summary>
        public void SetSelectedSilently(int index)
        {
            selected = Mathf.Clamp(index, 0, Mathf.Max(0, items.Count - 1));
            if (items.Count > 0 && !items[selected].IsEnabled) selected = FirstEnabled();
            Refresh();
        }

        public int Count => items.Count;

        public Item AddButton(string label, Action submit, Func<bool> enabled = null)
        {
            var item = new Item { label = label, submit = submit, enabled = enabled };
            AddRow(item);
            return item;
        }

        public Item AddOption(string label, Func<string> value, Action<int> adjust)
        {
            var item = new Item { label = label, value = value, adjust = adjust };
            AddRow(item);
            return item;
        }

        void AddRow(Item item)
        {
            int index = items.Count;
            var row = UIFactory.Rect(transform, "Row_" + item.label);
            UIFactory.Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -index * rowHeight), new Vector2(width, rowHeight - 4f));

            // Transparent graphic so the whole row receives pointer events.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            item.highlight = UIFactory.Image(row, "Highlight", Game.Art.Get("ui_select"), Color.white);
            item.highlight.preserveAspect = false;
            UIFactory.Stretch(item.highlight.rectTransform);

            var textColor = dark ? UIColors.Cream : UIColors.Ink;
            bool isOption = item.value != null;
            item.labelText = UIFactory.Text(row, "Label", item.label, fontSize, textColor,
                isOption || AlignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, dark);
            UIFactory.Stretch(item.labelText.rectTransform, 18f, 0f, isOption ? width * 0.45f : 18f, 0f);
            if (isOption)
            {
                item.valueText = UIFactory.Text(row, "Value", "", fontSize, textColor, TextAnchor.MiddleRight, dark);
                UIFactory.Stretch(item.valueText.rectTransform, width * 0.45f, 0f, 18f, 0f);
            }

            var pointer = row.gameObject.AddComponent<MenuItemPointer>();
            pointer.list = this;
            pointer.index = index;

            item.root = row;
            items.Add(item);
            RectTransform.sizeDelta = new Vector2(width, Height);
        }

        void OnEnable()
        {
            openedFrame = Time.frameCount;
            if (selected >= items.Count || selected < 0 || !items[selected].IsEnabled) selected = FirstEnabled();
            Refresh();
        }

        int FirstEnabled()
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].IsEnabled) return i;
            return 0;
        }

        public void ResetSelection()
        {
            selected = FirstEnabled();
            Refresh();
        }

        void Update()
        {
            if (items.Count == 0 || Time.frameCount == openedFrame || Game.State.ChangedThisFrame) return;
            var input = Game.Input;
            var nav = input.NavigateStep;

            if (nav.y != 0) Move(-nav.y);
            else if (nav.x != 0) Adjust(selected, nav.x);

            if (input.SubmitPressed) Activate(selected, 1);
            else if (input.CancelPressed && OnCancel != null)
            {
                Game.Audio.PlaySfx("cancel");
                OnCancel();
            }
            Refresh();
        }

        void Move(int delta)
        {
            for (int step = 1; step <= items.Count; step++)
            {
                int i = ((selected + delta * step) % items.Count + items.Count) % items.Count;
                if (!items[i].IsEnabled) continue;
                if (i != selected) Game.Audio.PlaySfx("select");
                selected = i;
                return;
            }
        }

        void Adjust(int index, int direction)
        {
            var item = items[index];
            if (item.adjust == null || !item.IsEnabled) return;
            item.adjust(direction);
            Game.Audio.PlaySfx("select");
        }

        public void Select(int index)
        {
            if (index < 0 || index >= items.Count || !items[index].IsEnabled || index == selected) return;
            selected = index;
            Game.Audio.PlaySfx("select", 0.6f);
            Refresh();
        }

        public void Activate(int index, int direction)
        {
            if (index < 0 || index >= items.Count) return;
            var item = items[index];
            if (!item.IsEnabled) return;
            selected = index;
            if (item.submit != null)
            {
                Game.Audio.PlaySfx("confirm");
                item.submit();
            }
            else if (item.adjust != null)
            {
                Adjust(index, direction);
            }
            Refresh();
        }

        public void Refresh()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                bool enabled = item.IsEnabled;
                bool isSelected = i == selected;
                item.highlight.enabled = isSelected;
                var baseColor = dark ? UIColors.Cream : UIColors.Ink;
                // [UI] Dark theme: dark ink on the gold selection bar (cream on gold was unreadable).
                var color = !enabled ? UIColors.Disabled : dark && isSelected ? UIColors.Ink : baseColor;
                // Unselected rows carry an invisible arrow of the same width, so text never jumps sideways.
                string prefix = isSelected ? "▶ " : "<color=#00000000>▶</color> ";
                item.labelText.text = prefix + item.label;
                item.labelText.color = color;
                // [UI] No drop shadow under dark ink on the gold bar (it smeared the glyphs).
                var shadow = item.labelText.GetComponent<Shadow>();
                if (shadow != null) shadow.enabled = !(dark && isSelected && enabled);
                if (item.valueText != null)
                {
                    item.valueText.text = isSelected ? $"◀ {item.value()} ▶" : item.value();
                    item.valueText.color = color;
                    var vShadow = item.valueText.GetComponent<Shadow>();
                    if (vShadow != null) vShadow.enabled = !(dark && isSelected && enabled);
                }
            }
        }
    }

    /// <summary>Mouse support for <see cref="MenuList"/> rows.</summary>
    public class MenuItemPointer : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public MenuList list;
        public int index;

        public void OnPointerEnter(PointerEventData eventData) => list.Select(index);

        public void OnPointerClick(PointerEventData eventData)
        {
            list.Activate(index, eventData.button == PointerEventData.InputButton.Right ? -1 : 1);
        }
    }
}
