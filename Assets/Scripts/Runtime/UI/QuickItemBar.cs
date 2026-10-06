using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Three quick-use slots to the right of the skill bar: health potion (1), mana potion (2) and the
    /// town return scroll (3) by default (rebindable; each slot shows its current key), and how many are left. Empty slots are dimmed.
    /// </summary>
    public class QuickItemBar : MonoBehaviour
    {
        const float Size = 58f, Gap = 8f;

        static readonly string[] Ids = { ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll };
        static readonly GameAction[] Keys = { GameAction.UseItem, GameAction.UseMana, GameAction.TownScroll };

        readonly Image[] icons = new Image[3];
        readonly Text[] keys = new Text[3];
        readonly Text[] counts = new Text[3];

        public static QuickItemBar Create(Transform parent)
        {
            float width = Ids.Length * Size + (Ids.Length - 1) * Gap;
            // The skill bar is 404 wide and centred; this sits just right of it, bottoms aligned.
            var root = UIFactory.Place(UIFactory.Rect(parent, "QuickItems"), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(226f, 50f), new Vector2(width, Size + 20f));
            var bar = root.gameObject.AddComponent<QuickItemBar>();
            for (int i = 0; i < Ids.Length; i++)
            {
                var background = UIFactory.Image(root, "Quick" + i, Game.Art.Get("ui_slot"), Color.white);
                background.preserveAspect = false;
                UIFactory.Place(background.rectTransform, Vector2.zero, Vector2.zero, new Vector2(i * (Size + Gap), 0f), new Vector2(Size, Size));
                bar.icons[i] = UIFactory.Image(background.transform, "Icon", Game.Art.Get(Game.Config.GetItem(Ids[i]).iconKey), Color.white);
                UIFactory.Place(bar.icons[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size * 0.68f, Size * 0.68f));
                bar.keys[i] = UIFactory.Text(background.transform, "Key", "", UiTheme.FontMin, new Color32(255, 224, 102, 255), TextAnchor.UpperLeft, true);
                UIFactory.Stretch(bar.keys[i].rectTransform, 8f, 2f, 2f, 6f);
                bar.counts[i] = UIFactory.Text(background.transform, "Count", "", 17, Color.white, TextAnchor.LowerRight, true);
                UIFactory.Stretch(bar.counts[i].rectTransform, 2f, 6f, 8f, 2f);
            }
            var caption = UIFactory.Text(root, "Caption", "물약 · 귀환", UiTheme.FontMin, new Color32(200, 214, 236, 255), TextAnchor.LowerCenter, true);
            UIFactory.Place(caption.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(width * 0.5f, Size + 1f), new Vector2(width + 20f, 20f));
            return bar;
        }

        void Update()
        {
            if (Game.Session == null || Game.Input == null) return;
            var inventory = Game.Session.Inventory;
            for (int i = 0; i < Ids.Length; i++)
            {
                int itemCount = inventory.Count(Ids[i]);
                keys[i].text = Game.Input.GetBindingLabel(Keys[i]);
                counts[i].text = itemCount.ToString();
                icons[i].color = itemCount > 0 ? Color.white : new Color(0.4f, 0.4f, 0.46f, 0.8f);
                counts[i].color = itemCount > 0 ? Color.white : new Color32(255, 120, 120, 255);
            }
        }
    }
}
