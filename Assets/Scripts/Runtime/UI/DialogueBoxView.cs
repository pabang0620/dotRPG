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

        public static DialogueBoxView Create(Transform canvas)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(canvas, "Dialogue"));
            var view = root.gameObject.AddComponent<DialogueBoxView>();
            view.Build(root);
            return view;
        }

        void Build(RectTransform root)
        {
            box = UIFactory.Place(UIFactory.Rect(root, "Box"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(980, 170));
            var bg = UIFactory.Panel(box, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);

            bodyText = UIFactory.Text(box, "Text", "", 26, UIColors.Ink, TextAnchor.UpperLeft);
            bodyText.lineSpacing = 1.2f;
            UIFactory.Stretch(bodyText.rectTransform, 36, 30, 36, 34);

            namePlate = UIFactory.Place(UIFactory.Rect(box, "Name"), new Vector2(0, 1), new Vector2(0, 0), new Vector2(26, -12), new Vector2(220, 44));
            var nbg = UIFactory.Panel(namePlate, "Bg", true);
            UIFactory.Stretch(nbg.rectTransform);
            nameText = UIFactory.Text(namePlate, "Text", "", 22, UIColors.Highlight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(nameText.rectTransform, 10, 0, 10, 0);

            nextIndicator = UIFactory.Text(box, "Next", "▼", 22, UIColors.InkSoft, TextAnchor.LowerRight);
            UIFactory.Stretch(nextIndicator.rectTransform, 0, 14, 28, 0);
        }

        void Update()
        {
            var dialogue = Game.Dialogue;
            bool show = dialogue != null && dialogue.IsOpen;
            if (box.gameObject.activeSelf != show) box.gameObject.SetActive(show);
            if (!show) return;

            bool hasName = !string.IsNullOrEmpty(dialogue.Speaker);
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
    }
}
