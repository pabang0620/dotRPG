using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Shown after "새 게임": pick the warrior or the mage. Two animated preview cards follow the
    /// menu selection (keyboard / gamepad / mouse); confirming starts the game with that class.
    /// </summary>
    public class CharacterSelectScreen : MenuScreen
    {
        static readonly CharacterClass[] Classes = { CharacterClass.Warrior, CharacterClass.Mage };

        readonly Image[] previews = new Image[2];
        readonly Image[] cards = new Image[2];
        readonly Text[] names = new Text[2];
        Text description;
        InputField nameField; // [STORY] the hero's name ({name} in every conversation)
        int shownIndex = -1;
        float animTimer;

        public static CharacterSelectScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "CharacterSelect", true);
            var screen = root.gameObject.AddComponent<CharacterSelectScreen>();
            // Ten empty body lines reserve room for the cards and the description.
            screen.BuildPanel(root, "캐릭터 선택", 760, new string('\n', 9), 20);
            var body = screen.panel.Find("Body").GetComponent<Text>();
            body.text = "";

            for (int i = 0; i < Classes.Length; i++)
            {
                var info = CharacterClassInfo.Get(Classes[i]);
                var card = UIFactory.Place(UIFactory.Rect(screen.panel, "Card_" + info.saveId), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(i == 0 ? -170f : 170f, -92f), new Vector2(280f, 200f));
                var bg = UIFactory.Panel(card, "Bg", true);
                UIFactory.Stretch(bg.rectTransform);
                bg.raycastTarget = true;
                screen.cards[i] = bg;

                var preview = UIFactory.Image(card, "Preview", Game.Art.GetCharacter(info.Look, "down", "idle0"), Color.white);
                UIFactory.Place(preview.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(96f, 122f));
                preview.preserveAspect = true;
                // 64 px warrior frames and 128 px generated frames (mage) both fill the same box.
                UIFactory.Place(preview.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0, -2), new Vector2(144, 144));
                screen.previews[i] = preview;

                var name = UIFactory.Text(card, "Name", info.displayName, 28, UIColors.Cream, TextAnchor.MiddleCenter, true);
                UIFactory.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(260f, 38f));
                screen.names[i] = name;
            }

            screen.description = UIFactory.Text(screen.panel, "Description", "", 19, UiTheme.TextSecondary, TextAnchor.UpperCenter, true); // [UI] dark panel
            screen.description.lineSpacing = 1.2f;
            UIFactory.Place(screen.description.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -346f), new Vector2(700f, 48f)); // [UI] between the name box (to -342) and the menu (from -394)
            screen.nameField = BuildNameField(screen.panel);

            foreach (var cls in Classes)
            {
                var info = CharacterClassInfo.Get(cls);
                screen.menu.AddButton($"{info.displayName}로 시작", () => Game.Flow.NewGame(info.id, screen.nameField.text));
            }
            screen.menu.AddButton("돌아가기", () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();

            // Clicking a card behaves like clicking its menu row.
            for (int i = 0; i < Classes.Length; i++)
            {
                var pointer = screen.cards[i].gameObject.AddComponent<MenuItemPointer>();
                pointer.list = screen.menu;
                pointer.index = i;
            }
            return screen;
        }

        /// <summary>Name box under the cards: click (or Tab) to type, Enter / Esc to leave it.</summary>
        static InputField BuildNameField(RectTransform panel)
        {
            var label = UIFactory.Text(panel, "NameLabel", "이름", 20, UIColors.Cream, TextAnchor.MiddleRight, true);
            UIFactory.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-170f, -302f), new Vector2(80f, 40f));
            var bg = UIFactory.Panel(panel, "NameBox", true);
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(20f, -302f), new Vector2(280f, 40f));
            bg.raycastTarget = true;
            var text = UIFactory.Text(bg.rectTransform, "Text", "", 22, Color.white, TextAnchor.MiddleCenter, false);
            UIFactory.Stretch(text.rectTransform, 10f, 10f, 2f, 2f);
            text.supportRichText = false;
            var placeholder = UIFactory.Text(bg.rectTransform, "Placeholder", QuestJournal.DefaultName, 22, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter, false);
            UIFactory.Stretch(placeholder.rectTransform, 10f, 10f, 2f, 2f);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.characterLimit = 8;
            field.lineType = InputField.LineType.SingleLine;
            field.targetGraphic = bg;
            return field;
        }

        public override void Show()
        {
            base.Show();
            if (nameField != null) nameField.text = "";
            shownIndex = -1;
            animTimer = 0f;
            Refresh(true);
        }

        void Update()
        {
            // While the name box has the keyboard, letters such as X / F / Space must not press menu buttons.
            if (menu != null) menu.enabled = nameField == null || !nameField.isFocused;
            if (nameField != null && !nameField.isFocused && UnityEngine.Input.GetKeyDown(KeyCode.Tab)) nameField.ActivateInputField();
            animTimer += Time.unscaledDeltaTime;
            Refresh(false);
        }

        void Refresh(bool force)
        {
            int selected = menu != null && menu.Selected < Classes.Length ? menu.Selected : (shownIndex < 0 ? 0 : shownIndex);
            if (force || selected != shownIndex)
            {
                shownIndex = selected;
                var info = CharacterClassInfo.Get(Classes[selected]);
                description.text = info.description;
                for (int i = 0; i < Classes.Length; i++)
                {
                    bool on = i == selected;
                    cards[i].color = on ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    names[i].color = on ? UIColors.Highlight : UIColors.Disabled;
                    previews[i].color = on ? Color.white : new Color(0.6f, 0.6f, 0.65f, 1f);
                }
            }

            // The chosen character walks in place, the other one idles.
            for (int i = 0; i < Classes.Length; i++)
            {
                var look = CharacterClassInfo.Get(Classes[i]).Look;
                string frame = i == shownIndex
                    ? CharacterPreviewCard.WalkFrame(look, animTimer)
                    : (Mathf.FloorToInt(animTimer * 1.8f) % 2 == 0 ? "idle0" : "idle1");
                previews[i].sprite = Game.Art.GetCharacter(look, "down", frame);
            }
        }
    }
}
