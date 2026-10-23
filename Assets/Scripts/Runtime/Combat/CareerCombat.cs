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
        public int FrenzyPct => frenzy;
        public bool HotVisible => Time.time < hotEnd && hotSource != null && !hotSource.IsDead;
        public bool CounterVisible => counterEnd > Time.time;
        public float OathRadius { get; private set; }
        public bool Cursed { get; private set; }
        public float SlowUntil { get; private set; }
        public int Shield => Time.time < shieldEnd ? shield : 0;
        public int ComboStacks => Time.time < rhythmEnd ? stacks : 0;

        // [UI] Seconds left of each timed state, for the buff row over the skill bar (0 = not running).
        public float ShieldLeft => Shield > 0 ? Mathf.Max(0f, shieldEnd - Time.time) : 0f;
        public float GuardLeft => GuardVisible ? Mathf.Max(0f, guardEnd - Time.time) : 0f;
        public float CounterLeft => Mathf.Max(0f, counterEnd - Time.time);
        /// <summary>[GUARDIAN] Blows 응보의 방진 can still block (0 when the stance is down).</summary>
        public int CounterCharges => CounterVisible ? counterCharges : 0;
        public const int CounterBlocks = 3;
        int counterCharges;
        float blockedShownAt = -1f;

        /// <summary>[UX] "방어!" over the character when a hit was stopped completely (at most ~4 a second).</summary>
        public void ShowBlocked()
        {
            if (owner == null || Time.time - blockedShownAt < .25f) return;
            blockedShownAt = Time.time;
            WorldPopupText.Show(owner.transform, L.T("방어!"), new Color(.6f, 1f, .95f), .7f);
        }
        public float BlessLeft => Mathf.Max(0f, blessEnd - Time.time);
        public float HotLeft => HotVisible ? Mathf.Max(0f, hotEnd - Time.time) : 0f;
        public float OathLeft => OathRadius > 0 ? Mathf.Max(0f, oathEnd - Time.time) : 0f;
        public float RetaliationLeft => Mathf.Max(0f, retalEnd - Time.time);
        public float ComboLeft => ComboStacks > 0 ? Mathf.Max(0f, rhythmEnd - Time.time) : 0f;
        public float InvulnerableLeft => Mathf.Max(0f, invulnerableEnd - Time.time);
        /// <summary>[UI] 축복의 연결's damage bonus (%) while it runs.</summary>
        public int BlessPercent => Time.time < blessEnd ? bless : 0;

        /// <summary>[GUARDIAN] Every hit is nullified until then (강철의 보루, 응보의 방진's blocked blow).</summary>
        float invulnerableEnd;
        /// <summary>[BISHOP] Standing in the 천상의 행진 sanctuary: my hits land 50% harder.</summary>
        float sanctumEnd;
        public float SanctumLeft => Mathf.Max(0f, sanctumEnd - Time.time);
        public void AddInvulnerable(float seconds) => invulnerableEnd = Mathf.Max(invulnerableEnd, Time.time + seconds);

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

        /// <summary>[BISHOP 2026-10-12] Standing in the bishop's sanctum: immune to every status effect (stun, curse, slow).</summary>
        public bool StatusImmune => Time.time < sanctumEnd;

        public void AddCurse(float slow = 0) { if (StatusImmune) return; Cursed = true; SlowUntil = Time.time + slow; }

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
            invulnerableEnd = 0;
            counterCharges = 0;
            sanctumEnd = 0;
            OathRadius = 0;
            hotSource = null;
            Cleanse();
            broken.Clear();
            lastElement = "";
            aura?.Clear();
            ResetSpecWarrior(); // [CH3 SPEC]
            RogueReset(); // [ROGUE] marks, venom, stealth, kunai, clones
        }

        /// <summary>Curses slow; only the 강철의 보루 stance itself slows the guardian.</summary>
        public float MoveScale => (Time.time < SlowUntil ? .6f : Time.time < guardSlowEnd ? .7f : Stealthed ? StealthSpeed : 1f) * SpecMoveBonus; // [CH3 SPEC] 전장의 함성 B haste, [ROGUE] 은신 +30%

        /// <summary>Damage the owner takes, after guard, 강철의 심장 and shields. Also arms 수호의 반향 and 응보의 방진.</summary>
        public int Absorb(int damage)
        {
            if (owner.Health.IsInvulnerable) return damage;
            // [GUARDIAN] Invulnerable: nothing gets through (the counter stance still answers the blow).
            if (Time.time < invulnerableEnd) { aura?.ShieldHit(); SkillVisuals.Sparks(owner.Center, new Color(.6f, 1f, .95f, 1f), 6, 3.5f); ShowBlocked(); return 0; }
            // 응보의 방진: each of its blocks stops a blow completely and answers it (full power first, then 60%).
            if (counterEnd > Time.time && counterCharges > 0)
            {
                counterCharges--;
                if (counterCharges == 0) counterEnd = 0;
                AddInvulnerable(.5f);
                StartCoroutine(CounterBlast(CounterPower())); // [CH3 SPEC] A one heavy answer, B five light ones
                ShowBlocked();
                return 0;
            }
            int reduction = (Time.time < guardEnd ? guard : 0) + SteelReduction(); // [CH3 SPEC] 철벽 A near bosses
            int before = damage;
            damage = Mathf.RoundToInt(damage * (1 - Mathf.Min(65, reduction) / 100f));
            SpecOnHurt(before - damage); // [CH3 SPEC] 강철 파동 B, 수호의 벽 B
            int used = Mathf.Min(Shield, damage);
            shield -= used;
            damage -= used;
            if (used > 0) { CareerTrials.Record(owner, "absorbed", used); aura?.ShieldHit(); if (damage <= 0) ShowBlocked(); }
            if (Prog.Rank("g_retal") > 0 && Time.time >= retalReady) { retalEnd = Time.time + 4; retalReady = Time.time + 2; }
            return Mathf.Max(0, damage);
        }

        void Update()
        {
            if (counterEnd > 0 && Time.time >= counterEnd) { counterEnd = 0; StartCoroutine(CounterBlast(.7f)); }
            if (OathRadius > 0 && Time.time >= oathEnd) OathRadius = 0;
            StanceTick();
            RogueTick();
        }

        /// <summary>Outgoing damage of the owner on <paramref name="enemy"/> (all hits, basic attacks included).</summary>
        public int ModifyDamage(EnemyController enemy, int amount)
        {
            if (secondaryDamage) return amount;
            echoRaw = amount; // [NINJA] 분신술 copies 60% of this hit (CloneEcho)
            float mult = Time.time < blessEnd ? 1 + bless / 100f : 1;
            if (Time.time < frenzyEnd && (!frenzyBossOnly || enemy.IsBoss)) mult *= 1 + frenzy / 100f; // [CH3 SPEC] 귀검 집중 A: bosses only
            if (Time.time < sanctumEnd) mult *= 1.5f;
            if (broken.TryGetValue(enemy, out var until) && Time.time < until) mult *= 1.15f;
            if (Time.time < retalEnd) mult *= RetalMul(); // [CH3 SPEC] A/B rules in CareerCombat.SpecWarrior
            mult *= EdgeMul(enemy) * SpecTakenMul(enemy);
            if (CareerCatalog.Base(Mine) == CharacterClass.Rogue && Prog.IsPromoted) mult *= RogueDamageMul(enemy, Mathf.RoundToInt(amount * mult)); // [ROGUE]
            return Mathf.Max(1, Mathf.RoundToInt(amount * mult));
        }

        public void OnLanded(EnemyController enemy)
        {
            if (secondaryDamage) return;
            SpecLanded(enemy); // [CH3 SPEC]
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
            if (CareerCatalog.Base(Mine) == CharacterClass.Rogue && Prog.IsPromoted) RogueLanded(enemy); // [ROGUE]
            CloneEcho(enemy); // [NINJA] 분신술: the clones do the same blow at 60%
        }


        // ---------- Cast ----------

        public IEnumerator Cast(CareerSkill s, SkillNumbers n) => Cast(s, n, false);

        /// <param name="aimed">True: go exactly where the player aims (Shift 섬광보), no auto-target.</param>
        public IEnumerator Cast(CareerSkill s, SkillNumbers n, bool aimed)
        {
            if (!Prog.CareerUnlocked(s)) yield break;
            string map = Game.Session.MapId;
            int version = castVersion;
            Vector2 dir = aimed ? Aim() : owner.AutoAim(Mathf.Max(4f, Mathf.Max(s.range, s.radius) + 1.5f)); // [AIM] every skill finds its target
            bool authority = !PartyNet.IsMember;
            Windup(s, dir);
            if (s.cast > 0) yield return new WaitForSeconds(s.cast);
            if (version != castVersion || !Valid(map) || !Prog.CareerUnlocked(s)) yield break;
            if (s.kind == CareerSkillKind.Awakening) Awaken(s);
            if (s.career == Career.Fighter) n.damage = Mathf.RoundToInt(n.damage * SpendRhythm(s.id));
            var run = new Run { s = s, n = n, dir = dir, map = map, version = version, authority = authority };
            switch (s.career)
            {
                case Career.Fighter: yield return Fighter(run); break;
                case Career.Guardian: yield return Guardian(run); break;
                case Career.Arcanist: yield return Arcanist(run); break;
                case Career.Bishop: yield return Bishop(run); break;
                case Career.Assassin: yield return Assassin(run); break;
                case Career.Ninja: yield return Ninja(run); break;
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
            if (CareerCatalog.Base(s.career) != CharacterClass.Mage) // [ROGUE] rogue careers take the warrior's stance, not the circle
            {
                owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(s.cast + .12f, s.effect == "execute" || s.effect == "swordrain" || s.effect == "xslash" || s.effect == "aegis" ? 2 : 0, 0, WarriorAttackMotion.Contact);
                if (s.cast >= .15f) SkillVisuals.Flash(owner.Center + dir * .3f, color, 1f, s.cast);
            }
            else
            {
                SkillVisuals.CastCircle(owner.Position, color);
                SkillVisuals.StaffFlash(owner.Center + dir * .35f, color);
            }
            Sound(s.career == Career.Fighter || s.career == Career.Assassin ? "c_dash" : s.career == Career.Guardian || s.career == Career.Ninja ? "swing" : s.career == Career.Arcanist ? "c_arcane" : "magic", .55f);
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
        /// <param name="rapid">The hit leaves no hit invulnerability (several cuts on one monster in the same instant, 환영 삼격).</param>
        bool Strike(Run c, EnemyController e, int damage, Vector2 from, float knockback, int weight, string sound = "hit", bool rapid = false)
        {
            if (e == null || e.IsDead) return false;
            Vector2 dir = e.Center - from;
            dir = dir.sqrMagnitude > .0001f ? dir.normalized : c.dir;
            bool landed = !c.authority;
            if (c.authority)
            {
                string previous = currentSkill;
                currentSkill = c.s.id;
                if (e.IsBoss) damage = Mathf.RoundToInt(damage * owner.Data.Stats.BossMul); // [LEGEND] 토벌형 weapon
                try { landed = e.TakeDamage(new DamageInfo(damage, from, knockback, Team.Player, owner.gameObject) { noHitInvulnerability = rapid }); }
                finally { currentSkill = previous; }
            }
            if (!landed) return false;
            RhythmShards(e, damage); // [CH3 SPEC] 검기 파급 B
            CareerFx.Hit(e.Center, dir, c.s.career, weight);
            if (weight == 2) e.HeavyHit(dir); // [FEEL] big skill hits read instantly
            ImpactSound(sound, weight == 2 ? 1f : .8f);
            Feel(weight, dir);
            return true;
        }

        /// <summary>A hit that does not count as a skill or basic attack (burn ticks).</summary>
        /// <param name="dot">Damage over time (burns, the mage's zones): it goes through armour.</param>
        void SecondaryHit(EnemyController e, int damage, bool dot = false)
        {
            if (e == null || e.IsDead) return;
            bool old = secondaryDamage;
            secondaryDamage = true;
            try { e.TakeDamage(new DamageInfo(damage, e.Position, 0f, Team.Player, owner.gameObject) { noHitInvulnerability = true, dot = dot }); }
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
                // [CH3 SPEC] 집중 치유 (A): +20% on the cast's first heal only. 넓은 숨 (B): +6% (and the wider reach in Bishop()).
                char mercy = rank > 0 ? Prog.SpecOf("b_mercy") : '\0';
                if (mercy == 'a') value = Mathf.RoundToInt(value * MercyFirst(c));
                else if (mercy == 'b') value = Mathf.RoundToInt(value * 1.06f);
                else if (rank > 0) value = Mathf.RoundToInt(value * (1 + (10 + 5 * (rank - 1)) / 100f));
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
                // [CH3 SPEC] 단단한 그릇 (A): +25% on the bishop's own shields only. 나눔의 그릇 (B): +8%, and a shield that
                // breaks early passes 20% of itself to the allies within 3 m.
                char grace = rank > 0 ? Prog.SpecOf("b_grace") : '\0';
                float bonus = grace == 'a' ? (p == owner ? 1.25f : 1f) : grace == 'b' ? 1.08f : rank > 0 ? 1 + (12 + 4 * (rank - 1)) / 100f : 1;
                int amount = Mathf.RoundToInt(p.Health.Max * fraction * c.Scale * bonus);
                For(p).AddShield(amount, duration, c.s.career);
                if (grace == 'b') StartCoroutine(GraceShare(p, amount, duration));
            }
            CareerFx.Bless(p.Center, CareerFx.Main(c.s.career), false); // [UX] no little shield picture: the shield shows on the HP bar
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
