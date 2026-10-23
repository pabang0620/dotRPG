using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DotRPG
{
    /// <summary>
    /// [UI] Skill range preview: while the mouse rests on a skill (HUD skill bar slot, skill window card) the skill's real
    /// area is drawn on the ground around / in front of the player (along the current aim), from the same numbers and
    /// shapes the skill code uses (SkillCaster.Numbers / CareerCombat). Circles are true world circles (1 unit = 1 m),
    /// as the hit tests are. Purely visual, pooled renderers, no gameplay effect.
    /// </summary>
    public sealed class SkillRangePreview : MonoBehaviour
    {
        enum Kind { SelfCircle, AheadCircle, Fan, Line }

        struct Shape
        {
            public Kind kind;
            public float a, b, c;   // SelfCircle: r / AheadCircle: dist, r / Fan: range, degrees / Line: start, length, half width
            public float turn;      // degrees off the aim (spread shots)
            public float alpha;     // 1 = main area, lower = secondary (charged size, pull reach)
        }

        const int Pool = 8, Tex = 128;
        const float Ppu = Tex / 2f; // a 128 px disc is 2 units wide at scale 1 -> scale = radius

        static SkillRangePreview inst;
        static Sprite disc, rect;
        static readonly Dictionary<int, Sprite> fans = new Dictionary<int, Sprite>();

        readonly List<Shape> shapes = new List<Shape>();
        readonly SpriteRenderer[] pool = new SpriteRenderer[Pool];
        Color color = Color.white;
        object owner;

        /// <summary>The short text for the hovered skill ("반경 7.5m", "사거리 7m · 폭 1.2m"), empty when nothing shows.</summary>
        public static string Label { get; private set; } = "";
        /// <summary>The HUD slot being previewed, or -1.</summary>
        public static int Slot { get; private set; } = -1;

        static SkillRangePreview Instance
        {
            get
            {
                if (inst != null) return inst;
                var go = new GameObject("SkillRangePreview");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<SkillRangePreview>();
                return inst;
            }
        }

        // ---------- API ----------

        /// <summary>Preview what is on HUD slot <paramref name="slot"/> (its final numbers, supports included).</summary>
        public static void ShowSlot(int slot, object from)
        {
            var p = Game.Player;
            if (p == null || p.Skills == null || Game.Session == null) return;
            var gem = Game.Session.Progression.Active(slot) ?? (slot == SkillGems.UltimateSlot && Game.Session.Progression.IsPromoted ? CareerCatalog.For(Game.Session.Progression.Career)[8].Gem : null);
            if (gem == null) { Hide(from); return; }
            Instance.Build(gem.id, p.Skills.Numbers(slot), from);
            Slot = slot;
        }

        /// <summary>Preview a skill by id (skill window cards), with its own numbers and no supports.</summary>
        public static void ShowSkill(string id, object from)
        {
            var p = Game.Player;
            var gem = SkillGems.Get(id);
            if (p == null || gem == null || p.Data?.Stats == null) return;
            Instance.Build(id, p.Data.Stats.Skill(p.Class, 1, gem, System.Array.Empty<SkillGem>()), from);
            Slot = -1;
        }

        /// <summary>Stops the preview if <paramref name="from"/> started it.</summary>
        public static void Hide(object from)
        {
            if (inst == null || (from != null && !ReferenceEquals(inst.owner, from))) return;
            inst.shapes.Clear();
            inst.owner = null;
            Label = "";
            Slot = -1;
            foreach (var r in inst.pool) if (r != null) r.enabled = false;
        }

        // ---------- Shapes from the skill code ----------

        void Build(string id, SkillNumbers n, object from)
        {
            owner = from;
            shapes.Clear();
            var career = CareerCatalog.Get(id);
            color = career != null ? CareerFx.Main(career.career) : Game.Player != null && Game.Player.Class == CharacterClass.Mage ? new Color(.55f, .75f, 1f) : new Color(1f, .8f, .4f);
            float r = n.radius, range = n.range;
            var label = new StringBuilder();
            switch (career != null ? career.effect : id)
            {
                // Fighter
                case "cross": Fan(range, 120f); label.Append(L.F("부채꼴 120° · 사거리 {0:0.#}m", range)); break;
                case "flurry": Fan(range + .3f, 150f); label.Append(L.F("부채꼴 150° · 사거리 {0:0.#}m", range + .3f)); break;
                case "rush": Line(0f, range, r); label.Append(LineText(range, r)); break;
                case "break": Line(.4f, range, r); label.Append(LineText(range, r)); break;
                case "iaido": Line(.3f, range, r); label.Append(LineText(range, r)); break;
                case "execute": Line(0f, range, .05f, .5f); Ahead(range, r); label.Append(L.F("사거리 {0:0.#}m · 착지 반경 {1:0.#}m", range, r)); break;
                case "xslash": Self(r); label.Append(L.F("반경 {0:0.#}m · 경직 3초 · 3초 뒤 피해", r)); break;
                case "swordrain": Self(range + 1f, .45f); Self(r); label.Append(L.F("낙검 반경 {0:0.#}m · 거대한 검 반경 {1:0.#}m", range + 1f, r)); break;
                case "stance": for (int k = -1; k <= 1; k++) Line(.4f, range, r, k == 0 ? 1f : .5f, k * 18f); label.Append(L.F("기본 공격마다 · {0} (3타 3줄)", LineText(range, r))); break;
                // Guardian
                case "guard": case "oath": case "taunt": case "counter": case "aegis": Self(r); label.Append(L.F("반경 {0:0.#}m", r)); break;
                case "shieldthrow": Line(0f, range, .75f); label.Append(LineText(range, .75f)); break;
                case "bash": Fan(range, 110f); label.Append(L.F("부채꼴 110° · 사거리 {0:0.#}m", range)); break;
                // Mage
                case "fire": Line(0f, range, .05f, .5f); Ahead(range, r); label.Append(L.F("사거리 {0:0.#}m · 폭발 반경 {1:0.#}m", range, r)); break;
                case "ice": for (int k = -1; k <= 1; k++) Line(0f, range, r, k == 0 ? 1f : .7f, k * 20f); label.Append(L.F("3갈래 20° · {0}", LineText(range, r))); break;
                case "storm": Line(0f, range, .05f, .5f); Ahead(range, r); label.Append(L.F("사거리 {0:0.#}m · 연쇄 반경 {1:0.#}m", range, r)); break;
                case "orbit": Fan(range, 150f); label.Append(L.F("부채꼴 150° · 사거리 {0:0.#}m", range)); break;
                case "nebula": Ahead(range, r); Ahead(range, r * 1.8f, .35f); label.Append(L.F("사거리 {0:0.#}m · 반경 {1:0.#}m (최대 모으면 {2:0.#}m)", range, r, r * 1.8f)); break;
                case "blink": Line(0f, range, .05f, .5f); Self(r); label.Append(L.F("순간이동 {0:0.#}m · 잔상 폭발 반경 {1:0.#}m", range, r)); break;
                case "rift": Ahead(range, r); Ahead(range, r * 1.6f, .35f); label.Append(L.F("사거리 {0:0.#}m · 반경 {1:0.#}m (끌어당김 {2:0.#}m)", range, r, r * 1.6f)); break;
                case "cataclysm": Self(r); label.Append(L.F("내 주변 반경 {0:0.#}m에 혜성 4초", r)); break;
                // Bishop
                case "heal": case "bloom": case "cleanse": case "sanctuary": case "bless": case "dawn": Self(r); label.Append(L.F("반경 {0:0.#}m", r)); break;
                case "light": Line(.4f, range, r); label.Append(LineText(range, r)); break;
                // Self buffs
                case "frenzy": case "barrier": Self(.7f); label.Append(L.T("자신에게 거는 버프")); break;
                // Base class skills (SkillCaster.Skills)
                case "crush": Ahead(Mathf.Min(1.1f, range), r); label.Append(L.F("앞 {0:0.#}m · 반경 {1:0.#}m", Mathf.Min(1.1f, range), r)); break;
                case "lance": Self(range, .5f); label.Append(L.F("사거리 {0:0.#}m 안 가장 가까운 적", range)); break;
                case "charge": Line(0f, range, .05f, .5f); Ahead(range, r); label.Append(L.F("돌진 {0:0.#}m · 반경 {1:0.#}m", range, r)); break;
                case "firefield": Ahead(Mathf.Min(4f, range), r); label.Append(L.F("앞 {0:0.#}m · 반경 {1:0.#}m", Mathf.Min(4f, range), r)); break;
                case "whirl": case "cry": case "nova": Self(r); label.Append(L.F("반경 {0:0.#}m", r)); break;
                case "frostorb": Line(0f, range, r); Ahead(range, r); label.Append(L.F("{0} · 폭발 반경 {1:0.#}m", LineText(range, r), r)); break;
                // [ROGUE] (SkillCaster.Rogue)
                case "stab": case "backstab":
                    if (NinjaReach(out float reach)) { Line(0f, reach, .3f); label.Append(LineText(reach, .3f)); } // [NINJA] thrown
                    else { Line(0f, range, r); label.Append(LineText(range, r)); }
                    break;
                case "leap": Self(r); label.Append(L.F("반경 {0:0.#}m", r)); break;
                case "raid": Self(range, .5f); label.Append(L.F("사거리 {0:0.#}m 안 가장 가까운 적", range)); break;
                case "dance": Self(range, .6f); label.Append(L.F("반경 {0:0.#}m", range)); break;
                case "scarecrow": Ahead(SkillCaster.ScarecrowFront, r); label.Append(L.F("앞 {0:0.#}m · 반경 {1:0.#}m", SkillCaster.ScarecrowFront, r)); break;
                case "blades": case "meteor": Self(range, .6f); label.Append(L.F("반경 {0:0.#}m 안의 적에게 낙하 · 한 발 반경 {1:0.#}m", range, r)); break;
                default: if (r > 0f) { Self(r); label.Append(L.F("반경 {0:0.#}m", r)); } break; // passives draw nothing
            }
            Label = label.ToString();
            if (shapes.Count == 0) Label = "";
        }

        /// <summary>[NINJA 2026-10-23] A promoted ninja throws 급소 투척 / 등 뒤 저격: the line is the throw's reach.</summary>
        static bool NinjaReach(out float reach)
        {
            var p = Game.Player;
            bool ninja = p != null && p.Class == CharacterClass.Rogue && CareerCombat.For(p).ThrowsKunai;
            reach = ninja ? CareerCombat.For(p).ThrowReach : 0f;
            return ninja;
        }

        static string LineText(float length, float halfWidth) => L.F("사거리 {0:0.#}m · 폭 {1:0.#}m", length, halfWidth * 2f);

        void Self(float r, float alpha = 1f) => shapes.Add(new Shape { kind = Kind.SelfCircle, a = r, alpha = alpha });
        void Ahead(float dist, float r, float alpha = 1f) => shapes.Add(new Shape { kind = Kind.AheadCircle, a = dist, b = r, alpha = alpha });
        void Fan(float range, float degrees) => shapes.Add(new Shape { kind = Kind.Fan, a = range, b = degrees, alpha = 1f });
        void Line(float start, float length, float halfWidth, float alpha = 1f, float turn = 0f) =>
            shapes.Add(new Shape { kind = Kind.Line, a = start, b = length, c = halfWidth, alpha = alpha, turn = turn });

        // ---------- Drawing ----------

        void LateUpdate()
        {
            var p = Game.Player;
            int used = 0;
            if (p != null && !p.IsDead && shapes.Count > 0)
            {
                Vector2 aim = p.AimDirection.sqrMagnitude > .0001f ? p.AimDirection.normalized : p.Facing.ToVector();
                Vector2 c = p.Center;
                foreach (var s in shapes)
                {
                    if (used >= Pool) break;
                    var r = Renderer(used++);
                    var t = r.transform;
                    Vector2 dir = Rotate(aim, s.turn);
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    r.color = new Color(color.r, color.g, color.b, .85f * s.alpha);
                    switch (s.kind)
                    {
                        case Kind.SelfCircle:
                            r.sprite = Disc; t.position = c; t.rotation = Quaternion.identity; t.localScale = new Vector3(s.a, s.a, 1f); break;
                        case Kind.AheadCircle:
                            r.sprite = Disc; t.position = c + dir * s.a; t.rotation = Quaternion.identity; t.localScale = new Vector3(s.b, s.b, 1f); break;
                        case Kind.Fan:
                            r.sprite = FanSprite(Mathf.RoundToInt(s.b)); t.position = c; t.rotation = Quaternion.Euler(0f, 0f, angle); t.localScale = new Vector3(s.a, s.a, 1f); break;
                        default:
                            r.sprite = Rect; t.position = c + dir * s.a; t.rotation = Quaternion.Euler(0f, 0f, angle);
                            t.localScale = new Vector3(Mathf.Max(.05f, s.b), Mathf.Max(.1f, s.c * 2f) / RectHeight, 1f); break; // starts s.a ahead, runs s.b
                    }
                    r.enabled = true;
                }
            }
            for (int i = used; i < Pool; i++) if (pool[i] != null && pool[i].enabled) pool[i].enabled = false;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            if (degrees == 0f) return v;
            float a = degrees * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }

        SpriteRenderer Renderer(int i)
        {
            if (pool[i] != null) return pool[i];
            var r = new GameObject("RangeShape" + i).AddComponent<SpriteRenderer>();
            r.transform.SetParent(transform, false);
            r.sortingOrder = SkillFx.GroundOrder + 30; // on the ground, under characters
            r.enabled = false;
            pool[i] = r;
            return r;
        }

        // ---------- Generated sprites: a soft fill with a bright 2 px rim ----------

        static Color32 Px(bool inside, bool rim) => rim ? new Color32(255, 255, 255, 255) : inside ? new Color32(255, 255, 255, 60) : new Color32(0, 0, 0, 0);

        static Sprite Disc
        {
            get
            {
                if (disc != null) return disc;
                var px = new Color32[Tex * Tex];
                float h = Tex / 2f;
                for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                {
                    float d = Mathf.Sqrt((x + .5f - h) * (x + .5f - h) + (y + .5f - h) * (y + .5f - h));
                    px[y * Tex + x] = Px(d <= h, d <= h && d > h - 2f);
                }
                disc = Make("RangeDisc", px, Tex, Tex, new Vector2(.5f, .5f));
                return disc;
            }
        }

        static Sprite FanSprite(int degrees)
        {
            if (fans.TryGetValue(degrees, out var s)) return s;
            var px = new Color32[Tex * Tex];
            float h = Tex / 2f, half = degrees * .5f;
            for (int y = 0; y < Tex; y++)
            for (int x = 0; x < Tex; x++)
            {
                float dx = x + .5f - h, dy = y + .5f - h, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Abs(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg); // 0 along +x (the aim)
                bool inside = d <= h && ang <= half;
                // Rim on the arc and on both straight edges (distance to the edge line in pixels).
                float edge = Mathf.Abs(Mathf.Sin((half - ang) * Mathf.Deg2Rad) * d);
                bool rim = inside && (d > h - 2f || edge < 1.5f);
                px[y * Tex + x] = Px(inside, rim);
            }
            s = Make("RangeFan" + degrees, px, Tex, Tex, new Vector2(.5f, .5f));
            fans[degrees] = s;
            return s;
        }

        const int RectW = 64, RectH = 16;
        const float RectHeight = RectH / Ppu; // world height of the rect sprite at scale 1

        static Sprite Rect
        {
            get
            {
                if (rect != null) return rect;
                var px = new Color32[RectW * RectH];
                for (int y = 0; y < RectH; y++)
                for (int x = 0; x < RectW; x++)
                    px[y * RectW + x] = Px(true, y < 1 || y >= RectH - 1 || x >= RectW - 1);
                // Pixels per unit = RectW so the length is 1 unit at scale 1 (x scale = metres).
                var t = new Texture2D(RectW, RectH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "RangeRect" };
                t.SetPixels32(px);
                t.Apply(false, true);
                rect = Sprite.Create(t, new UnityEngine.Rect(0, 0, RectW, RectH), new Vector2(0f, .5f), RectW);
                return rect;
            }
        }

        static Sprite Make(string name, Color32[] px, int w, int h, Vector2 pivot)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new UnityEngine.Rect(0, 0, w, h), pivot, Ppu);
        }
    }

    /// <summary>Hover handler: shows a HUD slot's or a skill's range while the pointer is over it.</summary>
    public sealed class SkillRangeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public int slot = -1;
        public string skillId;

        public void OnPointerEnter(PointerEventData e)
        {
            if (slot >= 0) SkillRangePreview.ShowSlot(slot, this);
            else if (!string.IsNullOrEmpty(skillId)) SkillRangePreview.ShowSkill(skillId, this);
        }

        public void OnPointerExit(PointerEventData e) => SkillRangePreview.Hide(this);
        void OnDisable() => SkillRangePreview.Hide(this);
    }
}
