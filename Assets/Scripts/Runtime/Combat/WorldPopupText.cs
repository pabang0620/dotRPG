using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [FEEL] A short word over a character's head ("1초 무적"): follows them, rises a little and fades. Same TextMesh
    /// setup as the NPC quest marks (UIFont, dark shadow one pixel below).
    /// </summary>
    public sealed class WorldPopupText : MonoBehaviour
    {
        const float Height = 1.55f;
        Transform follow;
        TextMesh text, shadow;
        Color color;
        float age, life;

        public static void Show(Transform follow, string message, Color color, float seconds)
        {
            if (follow == null) return;
            var go = new GameObject("PopupText");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var p = go.AddComponent<WorldPopupText>();
            p.follow = follow;
            p.color = color;
            p.life = Mathf.Max(.6f, seconds);
            p.shadow = Make(go.transform, message, new Vector3(.02f, -.02f, 0f), 20019);
            p.text = Make(go.transform, message, Vector3.zero, 20020);
            p.Tick();
        }

        static TextMesh Make(Transform parent, string message, Vector3 local, int order)
        {
            var go = new GameObject("t");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var tm = go.AddComponent<TextMesh>();
            tm.text = message;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.07f;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            var font = UIFont.Get();
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) { tm.font = font; mr.sharedMaterial = font.material; }
            mr.sortingOrder = order;
            return tm;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= life || follow == null) { Destroy(gameObject); return; }
            Tick();
        }

        void Tick()
        {
            float rise = Mathf.Min(1f, age / .25f) * .25f;
            transform.position = (Vector2)follow.position + new Vector2(0f, Height + rise);
            float a = age < life - .3f ? 1f : Mathf.Clamp01((life - age) / .3f);
            text.color = new Color(color.r, color.g, color.b, a);
            shadow.color = new Color(0f, 0f, 0f, .85f * a);
        }
    }
}
