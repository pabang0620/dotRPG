using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Casts the skills in the five slots (Q / W / E / R, T = awakening). Numbers come from
    /// the member's <see cref="CharacterStatsCalc.Skill"/> (skill × supports × passive tree); the look of each
    /// skill lives in <see cref="SkillVisuals"/>. Companions cast the same skills; only the local player's
    /// casts shake the screen, show toasts and the awakening banner.
    /// </summary>
    public partial class SkillCaster : MonoBehaviour
    {
        PlayerController owner;
        readonly float[] readyAt = new float[SkillGems.Slots];
        /// <summary>[DEMO] Career trial build: skills come back almost at once so every skill can be tried in a row.</summary>
        public static bool NoCooldown;
        float castEnd;
        readonly Dictionary<string,float> skillReady = new Dictionary<string,float>();
        float ReadyAt(int slot) {var g=Prog.Active(slot);return g!=null&&skillReady.TryGetValue(g.id,out var t)?t:0;}

        public bool IsCasting => Time.time < castEnd;

        public void Setup(PlayerController player) { owner = player; CareerCombat.For(player); }

        Progression Prog => owner.Data.Progression;

        /// <summary>True when the slot's skill is open, off cooldown and nothing else is being cast (AI companions).</summary>
        public bool IsReady(int slot) => Prog.Active(slot) != null && Time.time >= ReadyAt(slot) && !IsCasting;

        /// <summary>Screen shake for the local player's skills only.</summary>
        void Shake(float strength, float duration)
        {
            if (owner.IsLocal) Game.Camera?.Shake(strength, duration);
        }

        void Toast(string message)
        {
            if (owner.IsLocal) GameEvents.RaiseToast(message);
        }

        /// <summary>Clears every cooldown (new game, class change).</summary>
        public void ResetCooldowns()
        {
            skillReady.Clear();
            for (int i = 0; i < readyAt.Length; i++) readyAt[i] = 0f;
            castEnd = 0f;
        }

        /// <summary>0..1 cooldown progress for the HUD (1 = ready).</summary>
        public float CooldownProgress(int slot, out float remaining)
        {
            remaining = Mathf.Max(0f, ReadyAt(slot) - Time.time);
            var gem = Prog.Active(slot);
            if (gem == null || remaining <= 0f) return 1f;
            var n = Numbers(slot);
            return 1f - remaining / Mathf.Max(0.01f, n.cooldown);
        }

        /// <summary>Final numbers of a slot's skill (also for a locked slot, as a preview).</summary>
        public SkillNumbers Numbers(int slot)
        {
            var prog = Prog;
            var gem = prog.Active(slot) ?? SkillGems.ForSlot(owner.Class, slot);
            return gem != null ? owner.Data.Stats.Skill(owner.Class, slot, gem, prog.Supports(slot)) : default;
        }

        /// <summary>[PARTY NET] Any member started a skill (member, slot).</summary>
        public static event System.Action<PlayerController, int> Casted;

        /// <summary>[PARTY NET] A puppet replays a cast the host already decided: no cooldown wait.</summary>
        public void NetResetCooldown(int slot)
        {
            if (slot >= 0 && slot < readyAt.Length) { readyAt[slot] = 0f; var g=Prog.Active(slot); if(g!=null)skillReady.Remove(g.id); }
            castEnd = 0f;
        }

        public void TryCast(int slot)
        {
            if (slot<0 || slot>=SkillGems.Slots || owner.IsDead || owner.IsDashing) return;
            var gem = Prog.Active(slot);
            if (gem == null)
            {
                if (!owner.IsLocal) return;
                var locked = SkillGems.ForSlot(owner.Class, slot);
                GameEvents.RaiseToast(slot == SkillGems.UltimateSlot
                    ? "전직 후 자신의 각성 퀘스트를 완료해야 사용할 수 있습니다."
                    : $"아직 잠긴 스킬입니다: {locked?.name} (Lv.{Progression.SlotLevel(slot)}에 해금)");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            if (Time.time < ReadyAt(slot) || IsCasting) return;
            var careerSkill = CareerCatalog.Get(gem.id);
            if (careerSkill != null && !CareerCombat.For(owner).CanCast(careerSkill)) return; // e.g. all three shields still out
            var n = Numbers(slot);
            if (!owner.TrySpend(n.manaCost, n.usesLife))
            {
                if (!owner.IsLocal) return;
                GameEvents.RaiseToast(n.usesLife ? "HP가 부족합니다." : "MP가 부족합니다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            if (BeginCharge(slot, careerSkill, n)) return; // [CHARGE] fires on release, cooldown starts then
            readyAt[slot] = Time.time + (NoCooldown ? Mathf.Min(n.cooldown, 0.2f) : n.cooldown);
            skillReady[gem.id]=readyAt[slot];
            castEnd = Time.time + (gem.IsUltimate ? 0.6f : 0.25f);
            var career=CareerCatalog.Get(gem.id);
            if(career!=null) {
                castEnd=Time.time+career.cast+CareerMoves.Recovery(career)+.08f;
                // [BALANCE] 회귀의 방패: each node rank above 1 throws 0.04 s sooner (rank 3: about 4.5 throws a second).
                if (career.effect == "shieldthrow") castEnd -= .04f * (Mathf.Clamp(Prog.Rank(career.id), 1, 3) - 1);
                StartCoroutine(CareerCombat.For(owner).Cast(career,n));
            }
            else {owner.GetComponent<CharacterAnimator>()?.EndCareerPose();StartCoroutine(Cast(gem, n));}
            Casted?.Invoke(owner, slot); // [PARTY NET] the host replays it on member PCs
        }

        /// <summary>[AIM] The direction of the cast in progress: toward the nearest monster in reach, else the aim.</summary>
        Vector2 castAim = Vector2.right;

        IEnumerator Cast(SkillGem gem, SkillNumbers n)
        {
            castAim = owner.AutoAim(Mathf.Max(4f, Mathf.Max(n.range, n.radius) + 1.5f));
            if (!owner.NetPuppet) owner.FaceTowards(owner.Position + castAim); // the body turns to its target
            for (int i = 0; i <= n.repeats; i++)
            {
                if (owner.IsDead) yield break;
                switch (gem.id)
                {
                    // [SKILL v2]
                    case "crush": Crush(n); break;
                    case "lance": Lance(n); break;
                    case "charge": yield return StartCoroutine(Charge(n)); break;
                    case "firefield": StartCoroutine(FireField(n)); break;
                    // v1 + v2
                    case "whirl": Whirl(n); break;
                    case "cry": WarCry(n); break;
                    case "blades": yield return StartCoroutine(Blades(n, i == 0)); break;
                    case "nova": Nova(n); break;
                    case "frostorb": StartCoroutine(FrostOrb(n)); break;
                    case "meteor": yield return StartCoroutine(Meteor(n, i == 0)); break;
                }
                if (i < n.repeats) yield return new WaitForSeconds(0.28f);
            }
        }

        // ---------- Hits ----------

        void Hit(EnemyController enemy, SkillNumbers n, Vector2 from, float knockback)
        {
            if (enemy == null || enemy.IsDead) return;
            int damage = n.bossPct > 0 && enemy.IsBoss ? Mathf.RoundToInt(n.damage * (1f + n.bossPct / 100f)) : n.damage; // [SKILL v2] 보스 사냥
            // Hit feel: a harder shove, sparks and a tiny freeze-frame on the local player's hits.
            bool landed = enemy.TakeDamage(new DamageInfo(damage, from, knockback * 1.2f, Team.Player, owner.gameObject));
            if (landed) HitFeel(enemy, knockback >= 8f);
            if (landed && knockback >= 8f) enemy.HeavyHit((enemy.Center - from).normalized); // [FEEL]
            if (landed && n.leechPct > 0)
                owner.Heal(Mathf.Max(1, damage * n.leechPct / 100));
        }

        // ---------- Hit feel ----------

        static float hitStopUntil;

        /// <summary>Sparks and a white pop on the monster; the local player's hits also stop the world for a blink.</summary>
        void HitFeel(EnemyController e, bool heavy)
        {
            var color = owner.Class == CharacterClass.Mage ? SkillVisuals.ArcGlow : SkillVisuals.WhirlGold;
            SkillVisuals.Sparks(e.Center, color, heavy ? 9 : 5, heavy ? 4.6f : 3.2f);
            SkillVisuals.Flash(e.Center, new Color(1f, 1f, 1f, 0.6f), heavy ? 1.25f : 0.85f, 0.07f);
            if (owner.IsLocal) TryHitStop(this, heavy ? 0.055f : 0.03f);
        }

        /// <summary>A freeze-frame of <paramref name="seconds"/> (real time), at most one per 0.14 s.</summary>
        public static void TryHitStop(MonoBehaviour host, float seconds)
        {
            if (host == null || Time.unscaledTime < hitStopUntil || Time.timeScale < 0.99f) return;
            hitStopUntil = Time.unscaledTime + 0.14f; // one stop per burst of hits, not one per monster
            host.StartCoroutine(HitStop(seconds));
        }

        static IEnumerator HitStop(float seconds)
        {
            Time.timeScale = 0.06f;
            yield return new WaitForSecondsRealtime(seconds);
            if (Game.State != null) Game.State.RefreshTimeScale();
            else Time.timeScale = 1f;
        }

        static List<EnemyController> EnemiesInRadius(Vector2 center, float radius)
        {
            var list = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && Vector2.Distance(e.Center, center) <= radius) list.Add(e);
            return list;
        }

        List<EnemyController> EnemiesByDistance(float range)
        {
            var list = EnemiesInRadius(owner.Center, range);
            Vector2 me = owner.Position;
            list.Sort((a, b) => Vector2.Distance(a.Position, me).CompareTo(Vector2.Distance(b.Position, me)));
            return list;
        }

        static IEnumerator After(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        readonly List<Collider2D> probe = new List<Collider2D>();

        /// <summary>True when a projectile at <paramref name="p"/> runs into a wall, cliff or prop (not water, characters, trees).</summary>
        bool Blocked(Vector2 p)
        {
            probe.Clear();
            Physics2D.OverlapCircle(p, 0.08f, new ContactFilter2D { useTriggers = false }, probe);
            foreach (var col in probe)
            {
                if (col == null || col.attachedRigidbody != null || col.gameObject.name == "Water") continue;
                if (col.GetComponentInParent<IDamageable>() != null) continue;
                return true;
            }
            return false;
        }

        int strikeCursor;

        /// <summary>Where the next strike of an awakening skill lands: mostly on monsters, the rest around the player.</summary>
        Vector2 NextTarget(float range, int index, int total)
        {
            if (index == 0) strikeCursor = 0;
            var enemies = EnemiesByDistance(range);
            if (enemies.Count > 0 && index < Mathf.CeilToInt(total * 0.75f))
                return enemies[strikeCursor++ % enemies.Count].Position + Random.insideUnitCircle * 0.25f;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            return owner.Position + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1.2f, Mathf.Max(1.5f, range));
        }

        static EnemyController Nearest(Vector2 from, float range, HashSet<EnemyController> exclude)
        {
            EnemyController best = null;
            float bestDist = range;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled || (exclude != null && exclude.Contains(e))) continue;
                float d = Vector2.Distance(from, e.Center);
                if (d <= bestDist) { bestDist = d; best = e; }
            }
            return best;
        }
    }
}
