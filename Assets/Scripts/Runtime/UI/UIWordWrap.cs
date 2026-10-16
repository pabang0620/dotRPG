using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] Line breaks for legacy UI Text. Unity breaks Korean (and other CJK) between any two syllables, so a word like
    /// 레전더리 ends up as "레전더/리" whenever it meets the edge of its box. This component, attached to every text
    /// <see cref="UIFactory.Text"/> makes, re-breaks the text itself:
    /// - a one-line box (no newline in the text, box shorter than two lines) never wraps: the text overflows its box
    ///   instead of splitting a word (long labels are the caller's job: wider box or <see cref="UIFactory.OneLine"/>),
    /// - a taller box is wrapped at spaces only (어절 단위), measured with the text's own font and size, so a word moves to
    ///   the next line whole. A single word wider than the box still breaks inside (Unity's fallback) so it stays readable.
    /// Texts with best fit, Overflow or an InputField are left alone. Rewrapping happens only when the text or the box changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIWordWrap : MonoBehaviour
    {
        /// <summary>A box shorter than this many font sizes holds one line (two lines need about 2.2 x the size): never wrap inside it.</summary>
        const float OneLineHeight = 2.2f;
        /// <summary>A text up to this many box widths counts as a label that should overflow rather than wrap; wider is a paragraph.</summary>
        const float ShortLabelWidth = 1.6f;

        Text text;
        RectTransform rt;
        string raw = "", lastOut;
        float lastWidth = -1f, lastHeight = -1f;
        int lastFontSize = -1;
        bool input, inputChecked, dirty = true, forcedOverflow;

        public static UIWordWrap Attach(Text t)
        {
            var w = t.GetComponent<UIWordWrap>();
            if (w == null) w = t.gameObject.AddComponent<UIWordWrap>();
            return w;
        }

        void Awake()
        {
            text = GetComponent<Text>();
            rt = text.rectTransform;
        }

        void OnEnable() { dirty = true; }
        void OnRectTransformDimensionsChange() { dirty = true; }

        void LateUpdate()
        {
            if (text == null) return;
            // The InputField is added to the parent after its text is made, so look for it on the first frame, not in Awake.
            if (!inputChecked) { input = GetComponentInParent<InputField>() != null; inputChecked = true; }
            if (input || text.resizeTextForBestFit) return;
            string cur = text.text;
            var rect = rt.rect;
            bool boxChanged = dirty || rect.width != lastWidth || rect.height != lastHeight || text.fontSize != lastFontSize;
            if (cur == lastOut && !boxChanged) return;
            if (cur != lastOut)
            {
                // The caller wrote the same source text again (a per-frame refresh): put the wrapped form back, no remeasure.
                if (lastOut != null && cur == raw && !boxChanged) { text.text = lastOut; return; }
                raw = cur;
            }
            if (rect.width <= 1f) { lastOut = cur; dirty = true; return; } // not laid out yet: try again next frame
            dirty = false;
            lastWidth = rect.width; lastHeight = rect.height; lastFontSize = text.fontSize;
            string outText = Rewrap(raw, rect);
            if (outText != cur) text.text = outText;
            lastOut = outText;
        }

        string Rewrap(string s, Rect rect)
        {
            if (string.IsNullOrEmpty(s)) return s;
            float width = rect.width;
            var settings = text.GetGenerationSettings(Vector2.zero); // zero extents: the unwrapped width of a string
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var gen = text.cachedTextGeneratorForLayout;
            float units = text.pixelsPerUnit;
            float Width(string line) => gen.GetPreferredWidth(line, settings) / units;
            // A short label in a one-line box (grade name, number with unit, column cell) slightly wider than the box:
            // never split it, let it overflow sideways. A paragraph in a box that is still small (tooltips and quest text
            // size their box from preferredHeight after a layout pass) is not a label: it keeps wrapping so the box can grow.
            bool oneLine = s.IndexOf('\n') < 0 && rect.height < text.fontSize * OneLineHeight && Width(s) <= width * ShortLabelWidth;
            if (oneLine)
            {
                if (text.horizontalOverflow == HorizontalWrapMode.Wrap) { text.horizontalOverflow = HorizontalWrapMode.Overflow; forcedOverflow = true; }
                return s;
            }
            // Back to wrapping when the same text later shows several lines (the overflow above was ours, not the caller's).
            if (forcedOverflow) { text.horizontalOverflow = HorizontalWrapMode.Wrap; forcedOverflow = false; }
            if (text.horizontalOverflow != HorizontalWrapMode.Wrap) return s; // the caller chose overflow: nothing to break
            if (s.IndexOf(' ') < 0) return s; // nothing to break at
            var paragraphs = s.Split('\n');
            var sb = new System.Text.StringBuilder(s.Length + 16);
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0) sb.Append('\n');
                string para = paragraphs[p];
                if (para.IndexOf(' ') < 0 || Width(para) <= width) { sb.Append(para); continue; }
                var words = para.Split(' ');
                string line = null;
                foreach (var word in words)
                {
                    if (line == null) { line = word; continue; }
                    string candidate = line + " " + word;
                    if (Width(candidate) <= width) line = candidate;
                    else { sb.Append(line).Append('\n'); line = word; }
                }
                sb.Append(line);
            }
            return sb.ToString();
        }
    }
}
