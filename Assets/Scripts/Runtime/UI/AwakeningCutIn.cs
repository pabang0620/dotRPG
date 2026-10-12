using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Awakening cut-in, anime style: when the local player fires an awakening skill the screen flashes, speed lines
    /// sweep across a band in the skill's colour, the career illustration (Art/cutin_&lt;career&gt;, else the class one
    /// Art/cutin_warrior / cutin_mage) slides in from the bottom-left and the skill name slams in beside it.
    /// Runs on unscaled time (the hit freeze-frames must not stall it). No illustration file: nothing shows.
    /// </summary>
    public class AwakeningCutIn : MonoBehaviour
    {
        const float Size = 560f, In = 0.16f, Hold = 1.0f, Out = 0.24f, BandW = 900f;
        const int Lines = 9;
        const float StageScale = .65f; // [UI] cut-in size on screen (2026-10-12: smaller)

        RectTransform portraitRt, nameRt;
        Image portrait, streak, streakEdge, flash;
        Text nameText;
        readonly Image[] lines = new Image[Lines];
        readonly float[] lineY = new float[Lines], lineSpeed = new float[Lines], lineLen = new float[Lines];
        float startedAt = -10f;

        public static AwakeningCutIn Create(Transform parent)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(parent, "AwakeningCutIn"));
            var v = root.gameObject.AddComponent<AwakeningCutIn>();
            // A short white flash over the whole screen.
            v.flash = UIFactory.Image(root, "Flash", Game.Art.Get("ui_white"), Color.clear);
            v.flash.preserveAspect = false;
            v.flash.raycastTarget = false;
            UIFactory.Stretch(v.flash.rectTransform);
            // Colour band across the bottom-left (behind the character), with speed lines running through it.
            // Everything but the flash sits on a stage scaled down around the bottom-left corner.
            var stage = UIFactory.Stretch(UIFactory.Rect(root, "Stage"));
            stage.pivot = Vector2.zero;
            stage.localScale = new Vector3(StageScale, StageScale, 1f);
            v.streak = UIFactory.Image(stage, "Streak", Game.Art.Get("ui_white"), Color.clear);
            v.streak.preserveAspect = false;
            v.streak.raycastTarget = false;
            UIFactory.Place(v.streak.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 60f), new Vector2(BandW, 220f));
            v.streak.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            v.streakEdge = UIFactory.Image(v.streak.transform, "Edge", Game.Art.Get("ui_white"), Color.clear);
            v.streakEdge.preserveAspect = false;
            UIFactory.Place(v.streakEdge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(BandW, 6f));
            for (int i = 0; i < Lines; i++)
            {
                var l = UIFactory.Image(v.streak.transform, "Line" + i, Game.Art.Get("ui_white"), Color.clear);
                l.preserveAspect = false;
                l.raycastTarget = false;
                v.lines[i] = l;
            }
            v.portrait = UIFactory.Image(stage, "Portrait", null, Color.white);
            v.portrait.preserveAspect = true;
            v.portrait.raycastTarget = false;
            v.portraitRt = v.portrait.rectTransform;
            UIFactory.Place(v.portraitRt, Vector2.zero, Vector2.zero, new Vector2(-Size, 0f), new Vector2(Size, Size));
            // The skill name, big, beside the character.
            v.nameText = UIFactory.Text(stage, "SkillName", "", 46, Color.white, TextAnchor.MiddleLeft, true);
            v.nameText.fontStyle = FontStyle.BoldAndItalic;
            v.nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.nameText.raycastTarget = false;
            var outline = v.nameText.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(3f, -3f);
            outline.effectColor = new Color(0f, 0f, 0f, .85f);
            v.nameRt = v.nameText.rectTransform;
            UIFactory.Place(v.nameRt, Vector2.zero, Vector2.zero, new Vector2(Size - 40f, 150f), new Vector2(520f, 70f));
            root.gameObject.SetActive(true);
            v.Hide();
            return v;
        }

        void OnEnable() => GameEvents.Awakening += Show;
        void OnDisable() => GameEvents.Awakening -= Show;

        static Sprite Illustration(PlayerController player)
        {
            var career = player.Data.Progression.Career;
            Sprite s = null;
            if (career != Career.None) s = Resources.Load<Sprite>(SpriteLibrary.OverrideFolder + "cutin_" + career.ToString().ToLowerInvariant());
            return s != null ? s : Resources.Load<Sprite>(SpriteLibrary.OverrideFolder + (player.Class == CharacterClass.Mage ? "cutin_mage" : "cutin_warrior"));
        }

        void Show(string skillName, Color color)
        {
            var player = Game.Player;
            if (player == null) return;
            var sprite = Illustration(player);
            if (sprite == null) return;
            portrait.sprite = sprite;
            streak.color = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.75f);
            streakEdge.color = new Color(color.r, color.g, color.b, 0.95f);
            nameText.text = skillName ?? "";
            nameText.color = Color.Lerp(color, Color.white, .55f);
            for (int i = 0; i < Lines; i++)
            {
                lineY[i] = Random.Range(12f, 208f);
                lineSpeed[i] = Random.Range(2600f, 4200f);
                lineLen[i] = Random.Range(120f, 320f);
                lines[i].rectTransform.anchorMin = lines[i].rectTransform.anchorMax = Vector2.zero;
                lines[i].rectTransform.pivot = new Vector2(0f, .5f);
                lines[i].rectTransform.sizeDelta = new Vector2(lineLen[i], Random.Range(2f, 5f));
            }
            startedAt = Time.unscaledTime;
            SetVisible(true);
            transform.SetAsLastSibling();
        }

        void SetVisible(bool on)
        {
            portrait.enabled = streak.enabled = streakEdge.enabled = flash.enabled = nameText.enabled = on;
            foreach (var l in lines) l.enabled = on;
        }

        void Hide() => SetVisible(false);

        void Update()
        {
            float t = Time.unscaledTime - startedAt;
            if (t > In + Hold + Out) { if (portrait.enabled) Hide(); return; }
            float slide = t < In ? 1f - Mathf.Pow(1f - t / In, 3f) // ease out on the way in
                : t < In + Hold ? 1f
                : 1f - Mathf.Pow((t - In - Hold) / Out, 2f);
            float a = Mathf.Clamp01(slide);
            // Flash: bright for an instant, gone in 0.15 s.
            flash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(.45f * (1f - t / .15f)));
            // Portrait: slides in, then drifts slowly forward while it holds (a slight zoom sells the impact).
            float drift = t < In + Hold ? (t - In) * 16f : 0f;
            portraitRt.anchoredPosition = new Vector2(Mathf.Lerp(-Size, 0f, slide) + drift, 0f);
            float zoom = 1.08f - .08f * Mathf.Clamp01(t / (In + .25f));
            portraitRt.localScale = new Vector3(zoom, zoom, 1f);
            portrait.color = new Color(1f, 1f, 1f, a);
            // Band and its speed lines.
            streak.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-BandW, -60f, slide), 60f);
            var c = streak.color; c.a = .75f * a; streak.color = c;
            var e = streakEdge.color; e.a = .95f * a; streakEdge.color = e;
            for (int i = 0; i < Lines; i++)
            {
                float x = Mathf.Repeat(t * lineSpeed[i] + i * 137f, BandW + lineLen[i]) - lineLen[i];
                lines[i].rectTransform.anchoredPosition = new Vector2(x, lineY[i]);
                lines[i].color = new Color(1f, 1f, 1f, .55f * a);
            }
            // Skill name: slams in from the right a beat after the portrait, with a little overshoot.
            float nt = Mathf.Clamp01((t - .08f) / .18f);
            float overshoot = nt < 1f ? 1f + Mathf.Sin(nt * Mathf.PI) * .12f : 1f;
            nameRt.anchoredPosition = new Vector2(Mathf.Lerp(Size + 260f, Size - 40f, 1f - Mathf.Pow(1f - nt, 3f)), 150f);
            nameRt.localScale = new Vector3(overshoot, overshoot, 1f);
            var nc = nameText.color; nc.a = a * nt; nameText.color = nc;
        }
    }
}
