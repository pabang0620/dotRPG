using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [VFX] 천체 붕괴's terrain (Docs/PLAN_SKILL_VFX.md): what a meteor leaves where it lands, a magma pool (fire), a
    /// glacier of ice crystals (ice) or a cluster of charged crystals (lightning). Generated pictures m5_magma /
    /// m5_glacier / m5_storm when present, else a coloured glow. It grows out of the ground, pulses with its own light
    /// (magma breathes, lightning flickers), and is moved and shrunk from outside while the circle collapses
    /// (<see cref="Position"/>, <see cref="Size"/>, <see cref="Brightness"/>).
    /// </summary>
    public class ElementTerrainFx : MonoBehaviour
    {
        const float GrowTime = .22f;

        SpriteRenderer body, glow;
        string element;
        float age, worldW = 1f, pivotY;
        bool flat;
        Color color;

        public Vector2 Position;
        /// <summary>World width of the terrain piece.</summary>
        public float Size = 2.5f;
        public float Brightness = 1f;

        public static ElementTerrainFx Spawn(string element, Vector2 at, float size, Color color)
        {
            var go = new GameObject("Terrain_" + element);
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<ElementTerrainFx>();
            fx.element = element;
            fx.Position = at;
            fx.Size = size;
            fx.color = color;
            fx.flat = element == "fire"; // the magma pool lies on the ground; ice and crystals stand up
            string id = element == "fire" ? "m5_magma" : element == "ice" ? "m5_glacier" : "m5_storm";
            Sprite sprite = VfxLibrary.Has(id) && VfxLibrary.Get(id).Length > 0 ? VfxLibrary.Get(id)[0] : Game.Art.Get("fx_glow");
            fx.body = new GameObject("Body").AddComponent<SpriteRenderer>();
            fx.body.transform.SetParent(go.transform, false);
            fx.body.sprite = sprite;
            if (FxMaterials.Alpha != null) fx.body.sharedMaterial = FxMaterials.Alpha;
            fx.glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            fx.glow.transform.SetParent(go.transform, false);
            fx.glow.sprite = Game.Art.Get("fx_glow");
            if (FxMaterials.Additive != null) fx.glow.sharedMaterial = FxMaterials.Additive;
            fx.worldW = Mathf.Max(.01f, sprite.bounds.size.x);
            fx.pivotY = sprite.pivot.y / Mathf.Max(1f, sprite.rect.height);
            fx.Refresh();
            return fx;
        }

        public void Kill() => Destroy(gameObject);

        void Update()
        {
            age += Time.deltaTime;
            Refresh();
        }

        void Refresh()
        {
            float grow = Mathf.Clamp01(age / GrowTime);
            grow = 1f - (1f - grow) * (1f - grow);
            float s = Size * grow / worldW;
            var b = body.transform;
            // Standing pieces sit on their base, the pool is centred on its spot.
            float h = body.sprite.bounds.size.y * s;
            b.position = Position + (flat ? Vector2.zero : Vector2.up * h * pivotY);
            b.localScale = new Vector3(s, s, 1f);
            body.sortingOrder = flat ? SkillFx.GroundOrder + 12 : SkillFx.At(Position.y, 4);
            // Its own light: magma breathes slowly, lightning flickers, ice glints.
            float pulse = element == "fire" ? .75f + .25f * Mathf.Sin(age * 5f)
                : element == "storm" ? (Random.value < .25f ? 1.2f : .7f)
                : .8f + .2f * Mathf.Sin(age * 9f);
            float bright = Mathf.Max(0f, Brightness);
            body.color = new Color(Mathf.Min(1f, .9f + .1f * bright), Mathf.Min(1f, .9f + .1f * bright), Mathf.Min(1f, .9f + .1f * bright), grow);
            glow.transform.position = Position + Vector2.up * (flat ? 0f : h * .35f);
            glow.transform.localScale = new Vector3(Size * 1.1f * grow, Size * (flat ? .7f : .9f) * grow, 1f);
            glow.sortingOrder = body.sortingOrder + 1;
            // A soft halo only: the picture carries the look (a strong glow washed the ice into a grey cloud).
            float halo = flat ? .3f : .14f;
            glow.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(halo * pulse * bright) * grow);
        }
    }
}
