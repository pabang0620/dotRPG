using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Career (전직) combat: the states the skills leave behind (shields, guard, blessing, combo charges), the passives
    /// that change damage, and the cast entry point. Each career's skills live in their own partial file.
    /// Only the authority (solo play or the party host) writes HP and states; a party member's PC plays the same
    /// presentation as a prediction. Designs and numbers: Docs/PLAN_CAREER_SKILLS.md.
    /// </summary>
    public sealed partial class CareerCombat : MonoBehaviour
    {
        PlayerController owner;
        CareerAura aura;
        readonly Dictionary<EnemyController, float> broken = new Dictionary<EnemyController, float>();
        float guardSlowEnd;
        float shieldEnd, guardEnd, blessEnd, rhythmEnd, retalEnd, retalReady, hotEnd, counterEnd, elementEnd, oathEnd;
        int shield, guard, bless, stacks, counterDamage, castVersion, frenzy;
        float frenzyEnd;
        string lastElement = "";
        PlayerController hotSource;

        public Career ShieldCareer { get; private set; } = Career.Guardian;
        public bool GuardVisible => Time.time < guardEnd && counterEnd <= 0f;
        public bool BlessVisible => Time.time < blessEnd;
        /// <summary>Seconds left of 검귀 해방 (0 = off).</summary>
        public float FrenzyLeft => Mathf.Max(0f, frenzyEnd - Time.time);
        public bool HotVisible => Time.time < hotEnd && hotSource != null && !hotSource.IsDead;
        public bool CounterVisible => counterEnd > Time.time;
        public float OathRadius { get; private set; }
        public bool Cursed { get; private set; }
        public float SlowUntil { get; private set; }
        public int Shield => Time.time < shieldEnd ? shield : 0;
        public int ComboStacks => Time.time < rhythmEnd ? stacks : 0;

        /// <summary>Set while a secondary hit (burn tick, counter blast) lands: passives and combo do not react to it.</summary>
        static bool secondaryDamage;
        /// <summary>The career skill whose hit is landing right now (empty for a basic attack).</summary>
        static string currentSkill;

        Progression Prog => owner.Data.Progression;
        Career Mine => Prog.Career;

        public static CareerCombat For(PlayerController p)
        {
            var c = p.GetComponent<CareerCombat>();
            if (c == null) c = p.gameObject.AddComponent<CareerCombat>();
            c.owner = p;
            c.aura = CareerAura.For(p, c);
            return c;
        }

        /// <summary>Retired attack-speed passive; kept for the stat sheet.</summary>
        public static int SpeedFor(CharacterData d) => 0;

        // ---------- States ----------

        public void AddShield(int amount, float duration, Career source)
        {
            ShieldCareer = source;
            shield = Mathf.Max(Shield, amount);
            shieldEnd = Time.time + duration;
            CareerTrials.Record(owner, "shield", amount);
        }

        /// <summary>Damage reduction for a while. A weaker guard never stretches a stronger one that is running.</summary>
        public void AddGuard(int percent, float duration)
        {
            bool running = Time.time < guardEnd;
            if (running && percent < guard) return;
            guardEnd = running && percent == guard ? Mathf.Max(guardEnd, Time.time + duration) : Time.time + duration;
            guard = percent;
        }

        public void AddCurse(float slow = 0) { Cursed = true; SlowUntil = Time.time + slow; }

        public bool Cleanse()
        {
            bool had = Cursed || Time.time < SlowUntil;
            Cursed = false;
            SlowUntil = 0;
            return had;
        }

        public void ResetState()
        {
            castVersion++;
            StopAllCoroutines();
            owner.GetComponent<CharacterAnimator>()?.EndCareerPose();
            shield = guard = bless = stacks = 0;
            shieldEnd = guardEnd = guardSlowEnd = blessEnd = rhythmEnd = retalEnd = retalReady = counterEnd = hotEnd = oathEnd = 0;
            counterDamage = 0;
            shieldBack.Clear();
            frenzy = 0; frenzyEnd = 0;
            stanceOn = false;
            OathRadius = 0;
            hotSource = null;
            Cleanse();
            broken.Clear();
            lastElement = "";
            aura?.Clear();
        }

        /// <summary>Curses slow; only the 강철의 보루 stance itself slows the guardian.</summary>
        public float MoveScale => Time.time < SlowUntil ? .6f : Time.time < guardSlowEnd ? .7f : 1f;

        /// <summary>Damage the owner takes, after guard, 강철의 심장 and shields. Also arms 수호의 반향 and 응보의 방진.</summary>
        public int Absorb(int damage)
        {
            if (owner.Health.IsInvulnerable) return damage;
            int steel = Prog.Rank("g_steel");
            int reduction = (Time.time < guardEnd ? guard : 0) + (steel > 0 ? 4 + steel * 2 : 0);
            damage = Mathf.RoundToInt(damage * (1 - Mathf.Min(65, reduction) / 100f));
            int used = Mathf.Min(Shield, damage);
            shield -= used;
            damage -= used;
            if (used > 0) { CareerTrials.Record(owner, "absorbed", used); aura?.ShieldHit(); }
            if (Prog.Rank("g_retal") > 0 && Time.time >= retalReady) { retalEnd = Time.time + 4; retalReady = Time.time + 2; }
            if (counterEnd > Time.time) { counterEnd = 0; StartCoroutine(CounterBlast(1f)); }
            return Mathf.Max(0, damage);
        }

        void Update()
        {
            if (counterEnd > 0 && Time.time >= counterEnd) { counterEnd = 0; StartCoroutine(CounterBlast(.7f)); }
            if (OathRadius > 0 && Time.time >= oathEnd) OathRadius = 0;
            StanceTick();
        }

        /// <summary>Outgoing damage of the owner on <paramref name="enemy"/> (all hits, basic attacks included).</summary>
        public int ModifyDamage(EnemyController enemy, int amount)
        {
            if (secondaryDamage) return amount;
            float mult = Time.time < blessEnd ? 1 + bless / 100f : 1;
            if (Time.time < frenzyEnd) mult *= 1 + frenzy / 100f;
            if (broken.TryGetValue(enemy, out var until) && Time.time < until) mult *= 1.15f;
            if (Time.time < retalEnd) { mult *= 1 + (20 + 5 * (Prog.Rank("g_retal") - 1)) / 100f; retalEnd = 0; }
            int edge = Prog.Rank("f_edge");
            if (edge > 0 && enemy.Health.Current >= enemy.Health.Max * .8f) mult *= 1 + (15 + 5 * (edge - 1)) / 100f;
            return Mathf.Max(1, Mathf.RoundToInt(amount * mult));
        }

        public void OnLanded(EnemyController enemy)
        {
            if (secondaryDamage) return;
            if (Prog.Rank("f_rhythm") > 0 && string.IsNullOrEmpty(currentSkill))
            {
                int before = ComboStacks;
                stacks = Mathf.Min(3, ComboStacks + 1);
                rhythmEnd = Time.time + 6;
                if (before < 3 && stacks == 3 && owner.IsLocal)
                {
                    SkillVisuals.Flash(owner.Center, CareerFx.Steel, 1.4f, 0.2f);
                    SkillVisuals.Sparks(owner.Center, CareerFx.Steel, 8, 4f, 0.25f);
                }
            }
            CareerTrials.Record(owner, "hit", 1);
        }

        /// <summary>검기 연성: three charges make the next Fighter skill stronger, and are spent by it.</summary>
        float SpendRhythm()
        {
            int rank = Prog.Rank("f_rhythm");
            if (rank <= 0 || ComboStacks < 3) return 1f;
            stacks = 0;
            rhythmEnd = 0;
            return 1.2f + .05f * (rank - 1);
        }

        // ---------- Cast ----------

        public IEnumerator Cast(CareerSkill s, SkillNumbers n)
        {
            if (!Prog.CareerUnlocked(s)) yield break;
            string map = Game.Session.MapId;
            int version = castVersion;
            Vector2 dir = owner.AutoAim(Mathf.Max(4f, Mathf.Max(s.range, s.radius) + 1.5f)); // [AIM] every skill finds its target
            bool authority = !PartyNet.IsMember;
            Windup(s, dir);
            if (s.cast > 0) yield return new WaitForSeconds(s.cast);
            if (version != castVersion || !Valid(map) || !Prog.CareerUnlocked(s)) yield break;
            if (s.kind == CareerSkillKind.Awakening) Awaken(s);
            if (s.career == Career.Fighter) n.damage = Mathf.RoundToInt(n.damage * SpendRhythm());
            var run = new Run { s = s, n = n, dir = dir, map = map, version = version, authority = authority };
            switch (s.career)
            {
                case Career.Fighter: yield return Fighter(run); break;
                case Career.Guardian: yield return Guardian(run); break;
                case Career.Arcanist: yield return Arcanist(run); break;
                case Career.Bishop: yield return Bishop(run); break;
            }
        }

        /// <summary>Everything one cast needs; <see cref="Live"/> stops it when the caster dies, leaves or resets.</summary>
        sealed class Run
        {
            public CareerSkill s;
            public SkillNumbers n;
            public Vector2 dir;
            public string map;
            public int version;
            public bool authority;
            public float Scale => n.careerPotency > 0 ? n.careerPotency : 1f;
        }

        bool Live(Run c) => c.version == castVersion && Valid(c.map);
        bool Valid(string map) => owner != null && !owner.IsDead && owner.gameObject.activeInHierarchy && Game.Session.MapId == map;

        Vector2 Aim()
        {
            var d = owner.AimDirection;
            return d.sqrMagnitude > .0001f ? d.normalized : owner.Facing.ToVector();
        }

        /// <summary>The cast moment: a stance for the warriors, a magic circle and a staff flash for the mages.</summary>
        void Windup(CareerSkill s, Vector2 dir)
        {
            var color = CareerFx.Main(s.career);
            owner.GetComponent<CharacterAnimator>()?.SetCareerFacing(FacingExtensions.FromVector(dir, owner.Facing));
            if (CareerCatalog.Base(s.career) == CharacterClass.Warrior)
            {
                owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(s.cast + .12f, s.effect == "execute" || s.effect == "swordrain" || s.effect == "aegis" ? 2 : 0, 0, WarriorAttackMotion.Contact);
                if (s.cast >= .15f) SkillVisuals.Flash(owner.Center + dir * .3f, color, 1f, s.cast);
            }
            else
            {
                SkillVisuals.CastCircle(owner.Position, color);
                SkillVisuals.StaffFlash(owner.Center + dir * .35f, color);
            }
            Sound(s.career == Career.Fighter ? "c_dash" : s.career == Career.Guardian ? "swing" : s.career == Career.Arcanist ? "c_arcane" : "magic", .55f);
        }

        void Awaken(CareerSkill s)
        {
            var color = CareerFx.Main(s.career);
            if (owner.IsLocal)
            {
                SkillVisuals.Awakening(owner, s.name, color);
                Game.Audio.PlaySfx("quest");
            }
            else SkillVisuals.Flash(owner.Center, color, 2.4f, .35f);
        }

        // ---------- Hit feel ----------

        float soundReady;

        /// <summary>A sound heard by everyone near the caster; impact sounds of one swing play once.</summary>
        void Sound(string key, float volume = 1f)
        {
            if (Game.Audio == null) return;
            Game.Audio.PlaySfx(key, owner.IsLocal ? volume : volume * .5f);
        }

        void ImpactSound(string key, float volume = 1f)
        {
            if (Time.unscaledTime < soundReady) return;
            soundReady = Time.unscaledTime + .06f;
            Sound(key, volume);
        }

        /// <summary>Camera feedback of the owner's own blows (light 0, medium 1, heavy 2).</summary>
        void Feel(int weight, Vector2 dir)
        {
            if (!owner.IsLocal) return;
            SkillCaster.TryHitStop(owner.Skills, weight == 2 ? .08f : weight == 1 ? .05f : .03f);
            if (weight >= 1) Game.Camera?.Shake(weight == 2 ? .14f : .07f, weight == 2 ? .18f : .1f);
            if (weight == 2) Game.Camera?.CareerImpulse(dir, .12f, .15f);
        }

        /// <summary>
        /// One career hit on a monster: damage with a real knockback, sparks, the impact sound and hit stop. Returns true
        /// when it landed (or, on a party member's PC, when the monster was there to be hit).
        /// </summary>
        bool Strike(Run c, EnemyController e, int damage, Vector2 from, float knockback, int weight, string sound = "hit")
        {
            if (e == null || e.IsDead) return false;
            Vector2 dir = e.Center - from;
            dir = dir.sqrMagnitude > .0001f ? dir.normalized : c.dir;
            bool landed = !c.authority;
            if (c.authority)
            {
                string previous = currentSkill;
                currentSkill = c.s.id;
                try { landed = e.TakeDamage(new DamageInfo(damage, from, knockback, Team.Player, owner.gameObject)); }
                finally { currentSkill = previous; }
            }
            if (!landed) return false;
            CareerFx.Hit(e.Center, dir, c.s.career, weight);
            if (weight == 2) e.HeavyHit(dir); // [FEEL] big skill hits read instantly
            ImpactSound(sound, weight == 2 ? 1f : .8f);
            Feel(weight, dir);
            return true;
        }

        /// <summary>A hit that does not count as a skill or basic attack (burn ticks).</summary>
        void SecondaryHit(EnemyController e, int damage)
        {
            if (e == null || e.IsDead) return;
            bool old = secondaryDamage;
            secondaryDamage = true;
            try { e.TakeDamage(new DamageInfo(damage, e.Position, 0f, Team.Player, owner.gameObject) { noHitInvulnerability = true }); }
            finally { secondaryDamage = old; }
        }

        void Stun(Run c, EnemyController e, float seconds)
        {
            if (!c.authority || e == null || e.IsDead) return;
            e.Stun(seconds);
            StunStars.Attach(e);
        }

        void Freeze(Run c, EnemyController e, float seconds)
        {
            if (!c.authority || e == null || e.IsDead) return;
            e.Freeze(seconds);
            IceEncase.Attach(e);
        }

        void Taunt(Run c, EnemyController e, float seconds)
        {
            if (!c.authority || e == null || e.IsDead) return;
            ThreatTable.For(e).Force(owner, e.IsBoss ? Mathf.Min(1.2f, seconds) : seconds);
            CareerTrials.Record(owner, "taunted", 1);
            SkillFx.Spawn("fx_alert", e.Center + Vector2.up * .9f, Color.white, .6f, SkillFx.TopOrder + 5).Pop().Fade(FxFade.Late);
        }

        // ---------- Allies ----------

        void Heal(Run c, PlayerController p, int value)
        {
            if (p == null || p.IsDead) return;
            if (c.authority)
            {
                int rank = Prog.Rank("b_mercy");
                if (rank > 0) value = Mathf.RoundToInt(value * (1 + (10 + 5 * (rank - 1)) / 100f));
                if (For(p).Cursed) value = Mathf.RoundToInt(value * .35f);
                int actual = p.Health.Heal(value);
                if (actual > 0) CareerTrials.Record(owner, "heal", actual);
            }
            CareerFx.Clip("b_heal", p.Position, Vector2.zero, 1f, 22f, VfxLayer.AtFeet, false);
        }

        /// <summary>A shield of <paramref name="fraction"/> of the target's max HP (축복의 그릇 adds to it).</summary>
        void GiveShield(Run c, PlayerController p, float fraction, float duration)
        {
            if (p == null || p.IsDead) return;
            if (c.authority)
            {
                int rank = Prog.Rank("b_grace");
                float bonus = rank > 0 ? 1 + (12 + 4 * (rank - 1)) / 100f : 1;
                For(p).AddShield(Mathf.RoundToInt(p.Health.Max * fraction * c.Scale * bonus), duration, c.s.career);
            }
            CareerFx.Bless(p.Center, CareerFx.Main(c.s.career), true);
        }

        // ---------- Queries ----------

        List<EnemyController> Enemies(Vector2 center, float radius)
        {
            var list = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && Vector2.Distance(e.Center, center) <= radius + .3f * e.Size) list.Add(e);
            list.Sort((a, b) => (a.Center - center).sqrMagnitude.CompareTo((b.Center - center).sqrMagnitude));
            return list;
        }

        /// <summary>Monsters inside a fan of <paramref name="degrees"/> in front of <paramref name="from"/>.</summary>
        List<EnemyController> Fan(Vector2 from, Vector2 dir, float range, float degrees)
        {
            var list = Enemies(from, range);
            float cos = Mathf.Cos(degrees * .5f * Mathf.Deg2Rad);
            list.RemoveAll(e => { var d = e.Center - from; return d.sqrMagnitude > .04f && Vector2.Dot(d.normalized, dir) < cos; });
            return list;
        }

        /// <summary>Monsters within <paramref name="halfWidth"/> of the segment a-b.</summary>
        List<EnemyController> Corridor(Vector2 a, Vector2 b, float halfWidth)
        {
            var list = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && DistanceToSegment(e.Center, a, b) <= halfWidth + .25f * e.Size) list.Add(e);
            list.Sort((x, y) => (x.Center - a).sqrMagnitude.CompareTo((y.Center - a).sqrMagnitude));
            return list;
        }

        /// <summary>The monster the cast is aimed at: nearest in front within range, else the point at full range.</summary>
        EnemyController Target(Vector2 dir, float range, out Vector2 point)
        {
            EnemyController best = null;
            float score = float.MaxValue;
            foreach (var e in Enemies(owner.Center, range))
            {
                var d = e.Center - owner.Center;
                float facing = d.sqrMagnitude > .01f ? Vector2.Dot(d.normalized, dir) : 1f;
                if (facing < .3f) continue;
                float sc = d.magnitude - facing * 1.5f;
                if (sc < score) { score = sc; best = e; }
            }
            point = best != null ? best.Center : owner.Center + dir * range;
            return best;
        }

        List<PlayerController> Allies(Vector2 at, float radius)
        {
            var list = new List<PlayerController>();
            void Add(PlayerController p) { if (p != null && !p.IsDead && !list.Contains(p) && Vector2.Distance(p.Center, at) <= radius) list.Add(p); }
            Add(owner);
            if (Game.Party != null) foreach (var p in Game.Party.Members) Add(p);
            Add(CareerTrials.Companion);
            return list;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a;
            float t = d.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + d * t);
        }

        static Vector2 Rotate(Vector2 d, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
        }

        /// <summary>Moves the owner like a dash (stops at walls). Only the PC that owns the body moves it.</summary>
        float Dash(Vector2 dir, float distance) => owner.NetPuppet ? owner.SkillClearance(dir, distance) : owner.SkillDash(dir, distance);

        float DashSeconds(float distance) => distance / (PlayerController.DashDistance / PlayerController.DashDuration);

        void Pose(float seconds, int stage) => owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(seconds, stage, 0, WarriorAttackMotion.Contact);
    }
}
