using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] A small elemental mark left on the ground where a comet of 유성우 lands (Docs/PLAN_SKILL_VFX.md): a burning
    /// crater, a frost patch, a sparking scorch or an arcane pit (frames of m6_marks, else a coloured glow). It grows in,
    /// glows with its element and fades out at the end of its life. Purely visual: the damage is ticked by the skill.
    /// </summary>
    public class GroundMarkFx : MonoBehaviour
    {
        SpriteRenderer body, glow;
        Color color;
        float life, age, size, worldW = 1f;

        /// <param name="frame">0 fire, 1 ice, 2 lightning, 3 arcane (the m6_marks sheet order).</param>
        public static GroundMarkFx Spawn(Vector2 at, int frame, Color color, float size, float life)
        {
            var go = new GameObject("Mark");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = at;
            var fx = go.AddComponent<GroundMarkFx>();
            fx.color = color;
            fx.life = life;
            fx.size = size;
            var frames = VfxLibrary.Has("m6_marks") ? VfxLibrary.Get("m6_marks") : null;
            Sprite sprite = frames != null && frames.Length > frame ? frames[frame] : Game.Art.Get("fx_glow");
            fx.body = go.AddComponent<SpriteRenderer>();
            fx.body.sprite = sprite;
            if (FxMaterials.Alpha != null) fx.body.sharedMaterial = FxMaterials.Alpha;
            fx.body.sortingOrder = SkillFx.GroundOrder + 11;
            fx.glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            fx.glow.transform.SetParent(go.transform, false);
            fx.glow.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.glow.sharedMaterial = FxMaterials.Additive;
            fx.glow.sortingOrder = SkillFx.GroundOrder + 12;
            fx.worldW = Mathf.Max(.01f, sprite.bounds.size.x);
            fx.Refresh();
            return fx;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            Refresh();
        }

        void Refresh()
        {
            float grow = Mathf.Clamp01(age / .15f);
            float fade = age > life - .5f ? (life - age) / .5f : 1f;
            float s = size * (.7f + .3f * grow) / worldW;
            transform.localScale = new Vector3(s, s, 1f);
            body.color = new Color(1f, 1f, 1f, grow * fade);
            glow.transform.localScale = new Vector3(worldW * 1.1f, worldW * .6f, 1f);
            float pulse = .75f + .25f * Mathf.Sin(age * 7f + transform.position.x);
            glow.color = new Color(color.r, color.g, color.b, .3f * pulse * grow * fade);
        }
    }
}
