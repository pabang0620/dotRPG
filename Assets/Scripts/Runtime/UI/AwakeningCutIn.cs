using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Awakening cut-in: when the local player fires an awakening skill, the class illustration (Art/cutin_warrior,
    /// Art/cutin_mage) slides in from the bottom-left over a streak in the skill's colour, holds for a moment and
    /// slides out. Runs on unscaled time (the hit freeze-frames must not stall it). No illustration file: nothing shows.
    /// </summary>
    public class AwakeningCutIn : MonoBehaviour
    {
        const float Size = 430f, In = 0.18f, Hold = 0.95f, Out = 0.25f;

        RectTransform portraitRt;
        Image portrait, streak, streakEdge;
        float startedAt = -10f;

        public static AwakeningCutIn Create(Transform parent)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(parent, "AwakeningCutIn"));
            var v = root.gameObject.AddComponent<AwakeningCutIn>();
            // Colour band across the bottom-left (behind the character).
            v.streak = UIFactory.Image(root, "Streak", Game.Art.Get("ui_white"), Color.clear);
            v.streak.preserveAspect = false;
            UIFactory.Place(v.streak.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 40f), new Vector2(720f, 170f));
            v.streak.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            v.streakEdge = UIFactory.Image(v.streak.transform, "Edge", Game.Art.Get("ui_white"), Color.clear);
            v.streakEdge.preserveAspect = false;
            UIFactory.Place(v.streakEdge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(720f, 5f));
            v.portrait = UIFactory.Image(root, "Portrait", null, Color.white);
            v.portrait.preserveAspect = true;
            v.portraitRt = v.portrait.rectTransform;
            UIFactory.Place(v.portraitRt, Vector2.zero, Vector2.zero, new Vector2(-Size, 0f), new Vector2(Size, Size));
            root.gameObject.SetActive(true);
            v.Hide();
            return v;
        }

        void OnEnable() => GameEvents.Awakening += Show;
        void OnDisable() => GameEvents.Awakening -= Show;

        void Show(string skillName, Color color)
        {
            var player = Game.Player;
            if (player == null) return;
            var sprite = Resources.Load<Sprite>(SpriteLibrary.OverrideFolder + (player.Class == CharacterClass.Mage ? "cutin_mage" : "cutin_warrior"));
            if (sprite == null) return;
            portrait.sprite = sprite;
            streak.color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 0.7f);
            streakEdge.color = new Color(color.r, color.g, color.b, 0.95f);
            startedAt = Time.unscaledTime;
            portrait.enabled = streak.enabled = streakEdge.enabled = true;
            transform.SetAsLastSibling();
        }

        void Hide() => portrait.enabled = streak.enabled = streakEdge.enabled = false;

        void Update()
        {
            float t = Time.unscaledTime - startedAt;
            if (t > In + Hold + Out) { if (portrait.enabled) Hide(); return; }
            float slide = t < In ? 1f - Mathf.Pow(1f - t / In, 3f) // ease out on the way in
                : t < In + Hold ? 1f
                : 1f - Mathf.Pow((t - In - Hold) / Out, 2f);
            portraitRt.anchoredPosition = new Vector2(Mathf.Lerp(-Size, 10f, slide) + (t < In + Hold ? (t - In) * 14f : 0f), 0f);
            float a = Mathf.Clamp01(slide);
            portrait.color = new Color(1f, 1f, 1f, a);
            streak.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-720f, -40f, slide), 40f);
            var c = streak.color; c.a = 0.7f * a; streak.color = c;
            var e = streakEdge.color; e.a = 0.95f * a; streakEdge.color = e;
        }
    }
}
