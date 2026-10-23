using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE 2026-10-23] 변신술, the assassin's awakening (user: "어쌔신은 각성기를 변신술로"). It replaces 천영참: I take a
    /// shadow form for 6 s (every hit +18%, attacks and recharges +20% faster), then the form bursts around me (480%).
    /// One cast on a boss adds what 천영참 did (19.2 x attack): Tools/balance/theory_combat.py --awaken, plan 2.3 / 3.4.
    /// The body keeps its own sprite, drawn darker with grey afterimages (no new art). Plain colours only.
    /// </summary>
    public sealed partial class CareerCombat
    {
        /// <summary>변신술: damage and attack speed bonus while transformed (the speed goes through Stats.AttackSpeed).</summary>
        public const int FormDamagePct = 18, FormSpeedPct = 20;
        /// <summary>[ROGUE SPEC] 찰나의 변신 A: 2 s at +70%. 그림자 동화 B: 12 s at +9% and +15% speed, half the blast.</summary>
        const int FormShortDamagePct = 70, FormLongDamagePct = 9, FormLongSpeedPct = 15;
        const float FormLongBurst = .5f;
        /// <summary>Afterimages left behind while transformed (seconds apart) and their tint.</summary>
        const float FormGhostGap = .09f;
        static readonly Color FormGhostTint = new Color(.3f, .31f, .34f, .55f);

        /// <summary>Attack speed of the transformed characters (CharacterStats asks by data, not by body).</summary>
        static readonly Dictionary<CharacterData, (float end, int pct)> formSpeeds = new Dictionary<CharacterData, (float, int)>();

        float formEnd, formGhost;
        int formPct, formVersion;
        ShadowFormLook formLook;

        public float FormLeft => Mathf.Max(0f, formEnd - Time.time);
        public int FormPct => FormLeft > 0f ? formPct : 0;
        public int FormSpeed => FormLeft > 0f && owner != null && formSpeeds.TryGetValue(owner.Data, out var v) ? v.pct : 0;

        static int FormSpeedFor(CharacterData d) => d != null && formSpeeds.TryGetValue(d, out var v) && Time.time < v.end ? v.pct : 0;

        float FormDamageMul() => Time.time < formEnd ? 1f + formPct / 100f : 1f;

        void FormTick()
        {
            if (formEnd <= 0f) return;
            if (Time.time >= formEnd || owner == null || owner.IsDead) return; // Shapeshift ends it (and bursts)
            formGhost -= Time.deltaTime;
            if (formGhost > 0f) return;
            formGhost = FormGhostGap;
            CareerFx.Ghost(owner, FormGhostTint, .3f);
        }

        void EndForm()
        {
            formEnd = 0f;
            if (owner != null) formSpeeds.Remove(owner.Data);
            if (formLook != null) formLook.End();
            formLook = null;
        }

        /// <summary>변신술: the shadow form for its time, then the closing burst on every monster around me.</summary>
        IEnumerator Shapeshift(Run c)
        {
            float seconds = c.n.spec != '\0' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration;
            formPct = c.n.spec == 'a' ? FormShortDamagePct : c.n.spec == 'b' ? FormLongDamagePct : FormDamagePct;
            int speed = c.n.spec == 'b' ? FormLongSpeedPct : FormSpeedPct;
            float burstMul = c.n.spec == 'b' ? FormLongBurst : 1f;
            int version = ++formVersion;
            formEnd = Time.time + seconds;
            formGhost = 0f;
            formSpeeds[owner.Data] = (formEnd, speed);
            if (formLook == null) formLook = ShadowFormLook.Begin(owner);
            Sound("c_heavy", .8f);
            CareerFx.Gen("a_smoke", null, owner.Center, Vector2.zero, 1.8f, 0f, 14f, VfxLayer.Top, false, SmokeGray);
            if (owner.IsLocal) GameEvents.RaiseToast(L.F("변신술 · {0:0.#}초 동안 피해 +{1}%, 공격 속도 +{2}%", seconds, formPct, speed));
            while (Time.time < formEnd)
            {
                if (!Live(c) || version != formVersion) { if (version == formVersion) EndForm(); yield break; }
                yield return null;
            }
            if (version != formVersion) yield break;
            EndForm();
            if (!Live(c)) yield break;
            Vector2 center = owner.Center;
            Sound("c_heavy");
            Feel(2, Vector2.up);
            CareerFx.Gen("a_smoke", null, center, Vector2.zero, c.n.radius * 1.6f, 0f, 14f, VfxLayer.Top, false, SmokeGray);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * burstMul));
            foreach (var e in Enemies(center, c.n.radius)) StrikeRapid(c, e, damage, center, 2);
            SkillVisuals.UltFinish(center, Tint(c, AssassinPink), c.n.radius);
        }
    }

    /// <summary>
    /// [ROGUE 2026-10-23] 변신술's look: a dark see-through copy of the body's current frame drawn over it (the body itself
    /// is not recoloured, so hit flashes and 은신 keep working). No new art.
    /// </summary>
    public sealed class ShadowFormLook : MonoBehaviour
    {
        static readonly Color Shade = new Color(.1f, .1f, .13f, .62f);
        SpriteRenderer body, sr;

        public static ShadowFormLook Begin(PlayerController owner)
        {
            var body = owner.GetComponent<CharacterAnimator>()?.Body;
            if (body == null) return null;
            var go = new GameObject("ShadowForm");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var look = go.AddComponent<ShadowFormLook>();
            look.body = body;
            look.sr = go.AddComponent<SpriteRenderer>();
            look.Follow();
            return look;
        }

        public void End()
        {
            if (this != null) Destroy(gameObject);
        }

        void LateUpdate()
        {
            if (body == null) { Destroy(gameObject); return; }
            Follow();
        }

        void Follow()
        {
            sr.sprite = body.sprite;
            sr.flipX = body.flipX;
            sr.enabled = body.enabled && body.gameObject.activeInHierarchy;
            transform.SetPositionAndRotation(body.transform.position, body.transform.rotation);
            transform.localScale = body.transform.lossyScale;
            sr.sortingLayerID = body.sortingLayerID;
            sr.sortingOrder = body.sortingOrder + 1;
            var c = Shade;
            c.a *= body.color.a;
            sr.color = c;
        }
    }
}
