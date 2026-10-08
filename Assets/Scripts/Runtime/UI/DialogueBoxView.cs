using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Renders <see cref="DialogueManager"/> state: name plate, typewriter text, next indicator.</summary>
    public class DialogueBoxView : MonoBehaviour
    {
        RectTransform box;
        RectTransform namePlate;
        Text nameText;
        Text bodyText;
        Text nextIndicator;
        Image portrait;
        string portraitFor;
        const float PortraitSize = 128f;

        public static DialogueBoxView Create(Transform canvas)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(canvas, "Dialogue"));
            root.gameObject.AddComponent<SafeAreaFitter>(); // [TOUCH]
            var view = root.gameObject.AddComponent<DialogueBoxView>();
            view.Build(root);
            return view;
        }

        void Build(RectTransform root)
        {
            if (TouchUi.Enabled)
            {
                // [TOUCH] Tap anywhere to go to the next line (below the box and the skip plate, above the HUD).
                tapLayer = UIFactory.Overlay(root, "TapLayer", Color.clear);
                tapLayer.raycastTarget = true;
                var tap = tapLayer.gameObject.AddComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => { if (Game.Input != null) Game.Input.TouchTap(GameAction.Submit); });
                tapLayer.gameObject.SetActive(false);
            }
            box = UIFactory.Place(UIFactory.Rect(root, "Box"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(980, 170));
            var bg = UIFactory.Panel(box, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);

            bodyText = UIFactory.Text(box, "Text", "", 26, UIColors.Cream, TextAnchor.UpperLeft, true); // [UI] cream on the navy panel
            bodyText.lineSpacing = 1.2f;
            UIFactory.Stretch(bodyText.rectTransform, 36, 30, 36, 34);

            namePlate = UIFactory.Place(UIFactory.Rect(box, "Name"), new Vector2(0, 1), new Vector2(0, 0), new Vector2(26, -12), new Vector2(220, 44));
            var nbg = UIFactory.Panel(namePlate, "Bg", true);
            UIFactory.Stretch(nbg.rectTransform);
            nameText = UIFactory.Text(namePlate, "Text", "", 22, UIColors.Highlight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(nameText.rectTransform, 10, 0, 10, 0);

            // [UI] Speaker portrait at the left of the box (only when that NPC has one drawn).
            portrait = UIFactory.SharpIcon(box, "Portrait", Color.white);
            portrait.raycastTarget = false;
            UIFactory.Place(portrait.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(PortraitSize, PortraitSize));
            portrait.enabled = false;

            nextIndicator = UIFactory.Text(box, "Next", "▼", 22, UIColors.Highlight, TextAnchor.LowerRight);
            UIFactory.Stretch(nextIndicator.rectTransform, 0, 14, 28, 0);

            // Skip plate (screen top-right, clearly visible): Esc skips this conversation, or the whole scene in a
            // cutscene, also while a scene is walking or waiting with no line on screen.
            skipPlate = UIFactory.Place(UIFactory.Rect(root, "SkipPlate"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(250, 46));
            var sbg = UIFactory.Panel(skipPlate, "Bg", true);
            UIFactory.Stretch(sbg.rectTransform);
            skipHint = UIFactory.Text(skipPlate, "Text", "", 20, UIColors.Cream, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(skipHint.rectTransform, 8, 0, 8, 0);
            skipPlate.gameObject.SetActive(false);
            if (TouchUi.Enabled)
            {
                // [TOUCH] The skip plate is a button (the same as pressing Esc).
                sbg.raycastTarget = true;
                var skip = sbg.gameObject.AddComponent<Button>();
                skip.targetGraphic = sbg;
                skip.onClick.AddListener(() => { if (Game.Input != null) Game.Input.TouchTap(GameAction.Pause); });
            }
        }

        Image tapLayer;
        Text skipHint;
        RectTransform skipPlate;

        void Update()
        {
            var dialogue = Game.Dialogue;
            bool show = dialogue != null && dialogue.IsOpen;
            var scene = Game.Cutscenes;
            bool inScene = scene != null && scene.IsPlaying;
            bool skippable = inScene ? scene.CanSkip : show;
            if (skipPlate.gameObject.activeSelf != skippable)
            {
                skipPlate.gameObject.SetActive(skippable);
                if (skippable) transform.SetAsLastSibling(); // above the fader during black narration
            }
            if (skippable)
                skipHint.text = TouchUi.Enabled ? (inScene ? "장면 건너뛰기" : "대화 건너뛰기")
                    : $"<color=#ffd34a><b>ESC</b></color>  {(inScene ? "장면 건너뛰기" : "대화 건너뛰기")}";
            if (box.gameObject.activeSelf != show)
            {
                box.gameObject.SetActive(show);
                // Story narration plays over a faded-out screen: the box must sit above the fader (created after it),
                // or the lines are invisible and the scene looks stuck on black.
                if (show) transform.SetAsLastSibling();
            }
            if (tapLayer != null)
            {
                bool tappable = show && Game.State.Current == GameState.Dialogue;
                if (tapLayer.gameObject.activeSelf != tappable) tapLayer.gameObject.SetActive(tappable);
            }
            if (!show) return;

            bool hasName = !string.IsNullOrEmpty(dialogue.Speaker);
            if (portraitFor != dialogue.Speaker)
            {
                portraitFor = dialogue.Speaker;
                var face = hasName ? PortraitOf(dialogue.Speaker) : null;
                portrait.enabled = face != null;
                portrait.sprite = face;
                float left = face != null ? 36 + PortraitSize : 36;
                UIFactory.Stretch(bodyText.rectTransform, left, 30, 36, 34);
                namePlate.anchoredPosition = new Vector2(face != null ? 26 + PortraitSize : 26, -12);
            }
            namePlate.gameObject.SetActive(hasName);
            if (hasName)
            {
                nameText.text = dialogue.Speaker;
                namePlate.sizeDelta = new Vector2(Mathf.Max(140f, nameText.preferredWidth + 40f), 44f);
            }

            string full = dialogue.FullText;
            int visible = dialogue.VisibleCharacters;
            // Reserve the full text layout (invisible rest) so words don't jump between lines.
            bodyText.text = visible >= full.Length ? full : full.Substring(0, visible) + "<color=#00000000>" + full.Substring(visible) + "</color>";

            nextIndicator.enabled = dialogue.LineComplete && Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.6f;
            nextIndicator.text = dialogue.HasMoreLines ? "▼" : "■";
        }

        /// <summary>The drawn portrait (Art/Portraits/portrait_&lt;npcId&gt;) of the NPC with this name on the map or in the config.</summary>
        static Sprite PortraitOf(string speaker)
        {
            string id = null;
            foreach (var npc in NpcController.All)
                if (npc != null && npc.Definition != null && npc.Definition.displayName == speaker) { id = npc.Definition.npcId; break; }
            if (id == null && Game.Config != null)
                foreach (var def in Game.Config.npcs)
                    if (def.displayName == speaker) { id = def.npcId; break; }
            // Titles differ between the dialogue and the NPC ("기사단장 레오나" / "레오나"): match on the name part.
            if (id == null)
                foreach (var npc in NpcController.All)
                {
                    string n = npc != null && npc.Definition != null ? npc.Definition.displayName : null;
                    if (!string.IsNullOrEmpty(n) && (speaker.EndsWith(" " + n) || n.EndsWith(" " + speaker))) { id = npc.Definition.npcId; break; }
                }
            return id != null ? Game.Art.Optional("Portraits/portrait_" + id) : null;
        }
    }
}
