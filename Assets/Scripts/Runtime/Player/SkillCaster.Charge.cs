using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CHARGE] Held career skills (아크메이지 성운 폭발): pressing the key pays the mana and starts gathering, letting go
    /// (or 2.6 s, the cap plus a short hold) fires it. A longer charge scales damage up to 3x and the radius up to
    /// 1.8x. Only the local player charges; companions and party copies fire at once with the base numbers.
    /// </summary>
    public partial class SkillCaster
    {
        public const float MaxCharge = 2f, ChargeHoldCap = .6f;
        const float ChargeMove = .45f;

        int chargeSlot = -1;
        float chargeStart, nextMarker;
        CareerSkill chargeSkill;
        string chargeGem;
        SkillNumbers chargeNumbers;
        /// <summary>[VFX] The generated orb gathering at the staff while charging (null without the art).</summary>
        VfxPlayer chargeOrb;

        void StopOrb() { if (chargeOrb != null) chargeOrb.Stop(); chargeOrb = null; }

        public bool Charging => chargeSlot >= 0;
        /// <summary>0..1 of the current charge (0 when not charging).</summary>
        public float ChargeLevel => Charging ? Mathf.Clamp01((Time.time - chargeStart) / MaxCharge) : 0f;
        /// <summary>Walking speed while gathering.</summary>
        public float ChargeMoveScale => Charging ? ChargeMove : 1f;

        static bool Charges(CareerSkill s) => s != null && s.effect == "nebula";

        /// <summary>True when this cast becomes a charge (mana already paid by the caller).</summary>
        bool BeginCharge(int slot, CareerSkill s, SkillNumbers n)
        {
            if (!Charges(s) || !owner.IsLocal || owner.NetPuppet || Game.Input == null) return false;
            chargeSlot = slot;
            chargeSkill = s;
            chargeGem = Prog.Active(slot)?.id;
            chargeNumbers = n;
            chargeStart = Time.time;
            nextMarker = 0f;
            castEnd = float.MaxValue; // nothing else is cast while gathering
            Game.Audio.PlaySfx("c_arcane");
            StopOrb();
            if (VfxLibrary.Has("m2_charge_orb"))
                chargeOrb = CareerFx.Gen("m2_charge_orb", null, owner.Center, Vector2.zero, .7f, 0f, 14f, VfxLayer.Top, false, null, MaxCharge + ChargeHoldCap + 1f, true);
            return true;
        }

        void Update()
        {
            if (!Charging) return;
            if (owner == null || owner.IsDead) { CancelCharge(); return; }
            float held = Time.time - chargeStart;
            float k = ChargeLevel;
            if (chargeOrb != null)
            {
                // Held in front of the staff, swelling from 0.7 to 1.4 units with the charge.
                chargeOrb.Place(owner.Center + owner.AimDirection.normalized * .45f + Vector2.up * .1f);
                chargeOrb.Resize(.7f + .7f * k);
            }
            // The marker on the ground grows with the charge.
            if (Time.time >= nextMarker)
            {
                nextMarker = Time.time + .1f;
                var cc = CareerCombat.For(owner);
                Vector2 at = cc.ChargePoint(chargeNumbers.range);
                float r = chargeNumbers.radius * (1f + .8f * k);
                var color = Color.Lerp(new Color(.6f, .45f, 1f, .55f), new Color(1f, .85f, 1f, .8f), k);
                SkillFx.Spawn("fx_ring", at, color, .16f, SkillFx.GroundOrder + 8).Scale(new Vector2(r * 1.2f, r * .45f), new Vector2(r * 1.25f, r * .47f)).Additive();
                if (k >= 1f) SkillFx.Spawn("fx_sparkle", owner.Center + Random.insideUnitCircle * .5f, color, .3f, SkillFx.GroundOrder + 30).Scale(.3f, .1f);
            }
            if (!Game.Input.SkillHeld(chargeSlot) || held >= MaxCharge + ChargeHoldCap) ReleaseCharge();
        }

        void ReleaseCharge()
        {
            int slot = chargeSlot;
            float k = ChargeLevel;
            var n = chargeNumbers;
            n.damage = Mathf.RoundToInt(n.damage * (1f + 2f * k));
            n.radius *= 1f + .8f * k;
            chargeSlot = -1;
            StopOrb();
            castEnd = Time.time + CareerMoves.Recovery(chargeSkill) + .08f;
            readyAt[slot] = Time.time + (NoCooldown ? Mathf.Min(n.cooldown, 0.2f) : n.cooldown);
            // The cooldown belongs to the skill that was charged, even if the key was changed meanwhile.
            if (!string.IsNullOrEmpty(chargeGem)) skillReady[chargeGem] = readyAt[slot];
            StartCoroutine(CareerCombat.For(owner).Cast(chargeSkill, n));
            Casted?.Invoke(owner, slot);
        }

        void CancelCharge()
        {
            StopOrb();
            chargeSlot = -1;
            castEnd = 0f;
        }

        void OnDisable() { if (Charging) CancelCharge(); }
    }
}
