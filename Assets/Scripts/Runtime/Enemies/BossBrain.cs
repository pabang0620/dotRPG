using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Boss pattern sequencer (every <see cref="MonsterKind.Boss"/>). Walks towards its threat target and,
    /// between short gaps, plays one <see cref="BossPattern"/> picked by <see cref="ChoosePattern"/> - the single
    /// decision point a host / server can take over through <see cref="PatternAuthority"/>. Phases come from
    /// <see cref="MonsterDef.phases"/>. The raid boss 해골왕 adds its gimmicks: phase 2 raises two 사령 토템
    /// (both must fall within <see cref="TotemWindow"/> seconds of each other, otherwise the broken one comes
    /// back and the summons go on), phase 3 enrages and casts 망자의 심판 (break the groggy gauge during the
    /// cast → long groggy, otherwise everyone takes huge damage).
    /// </summary>
    public sealed class BossBrain : MonsterBehaviour
    {
        // ---------- Tuning ----------
        const float GapMin = 0.6f, GapMax = 1.3f;
        const float FirstPatternDelay = 1.2f;
        /// <summary>Raid totems: both must break within this many seconds of each other.</summary>
        public const float TotemWindow = 8f; // [P1] 5 s left no room once the party split up
        const float TotemOffsetX = 4.5f, TotemOffsetY = 1.2f;
        /// <summary>Phase-2 summons while the totems stand: this many skeletons every <see cref="SummonEvery"/> s.</summary>
        public const float SummonEvery = 8f;
        const int SummonBatch = 2;
        /// <summary>망자의 심판: cast time, groggy length when broken, first cast after phase 3 starts, repeat.</summary>
        public const float JudgmentCast = 6f, JudgmentGroggy = 10f, JudgmentFirstDelay = 4f, JudgmentEvery = 34f;
        const float JudgmentVisualRadius = 9f;
        /// <summary>Phase 3: move speed ×, cooldowns ×.</summary>
        public const float EnrageSpeed = 1.3f, EnrageCooldown = 0.7f;

        static readonly Color GoldGlow = new Color(1f, 0.85f, 0.3f, 0.8f);
        static readonly Color SoulGlow = new Color(0.45f, 1f, 0.55f, 0.75f);

        /// <summary>Host / server hook: when set, decides every pattern (return null = walk this beat).</summary>
        public static Func<BossBrain, BossPattern> PatternAuthority;
        /// <summary>Big warnings for the HUD (text, colour): phases, 광폭화, 망자의 심판, totems.</summary>
        public static event Action<BossBrain, string, Color> Announce;

        public int Phase { get; private set; } = 1;
        public int PhaseCount => (Def.phases != null ? Def.phases.Length : 0) + 1;
        public event Action<BossBrain, int> PhaseChanged;
        public BossPattern CurrentPattern { get; private set; }
        public bool Enraged => Def.raid && Phase >= 3;
        public bool IsKing => Def.id == "boss_skeleton_king";
        public EnemyController Controller => E;

        public bool CastingJudgment { get; private set; }
        public float JudgmentProgress { get; private set; }
        /// <summary>(boss, broken): the cast ended - broken by groggy, or resolved on the party.</summary>
        public event Action<BossBrain, bool> JudgmentEnded;
        public int JudgmentHits { get; private set; }

        /// <summary>Guard pattern active (frontal hits −80%).</summary>
        public bool Guarding { get; private set; }

        readonly Dictionary<BossPattern, float> readyAt = new Dictionary<BossPattern, float>();
        float nextPatternAt, nextSummonAt, nextJudgmentAt = float.MaxValue;
        bool judgmentRequested;

        class TotemSlot
        {
            public Vector2 spot;
            public EnemyController live;
            public float diedAt = -1f;
        }

        readonly List<TotemSlot> totems = new List<TotemSlot>();
        public bool TotemsCleared { get; private set; }
        public bool TotemsUp => totems.Count > 0 && !TotemsCleared;
        public int TotemRevives { get; private set; }
        public int Summoned { get; private set; }

        public IEnumerable<EnemyController> Totems
        {
            get { foreach (var t in totems) if (t.live != null && !t.live.IsDead) yield return t.live; }
        }

        float Mul => Enraged ? EnrageCooldown : 1f;
        float Scale => Def.size / 2f;

        protected override void OnInit()
        {
            nextPatternAt = Time.time + FirstPatternDelay;
            foreach (var p in Def.patterns) readyAt[p] = 0f;
        }

        // =============================== Frame ===============================

        public override bool Tick(bool engaged)
        {
            UpdatePhase();
            UpdateTotems();
            if (Busy) return true;
            if (!engaged) return false;
            var target = E.MonsterTarget;
            float now = Time.time;
            if (IsKing && TotemsUp && now >= nextSummonAt)
            {
                nextSummonAt = now + SummonEvery;
                if (NecroBehaviour.CountMinions(E) < Def.maxMinions)
                {
                    Run(Summon(SummonBatch));
                    return true;
                }
            }
            if (Def.raid && (judgmentRequested || now >= nextJudgmentAt))
            {
                judgmentRequested = false;
                nextJudgmentAt = now + JudgmentEvery;
                Run(Judgment());
                return true;
            }
            if (target == null)
            {
                E.MonsterHalt();
                E.MonsterAnim(CharacterAnim.Idle);
                return true;
            }
            if (now >= nextPatternAt)
            {
                var p = ChoosePattern();
                if (p != null)
                {
                    Run(Play(p));
                    return true;
                }
            }
            float dist = Vector2.Distance(E.Position, target.Position);
            if (dist > Def.attackRange * 0.8f) E.MonsterMoveTo(target.Position, E.Speed * (Enraged ? EnrageSpeed : 1f));
            else
            {
                E.MonsterHalt();
                E.MonsterFace(target.Position);
            }
            E.MonsterAutoAnim();
            if (Enraged && Time.frameCount % 20 == 0)
                SkillFx.Spawn("fx_glow", E.Center, new Color(1f, 0.2f, 0.15f, 0.35f), 0.5f, SkillFx.At(E.Position.y, -1)).Additive().Scale(2.2f, 3f);
            return true;
        }

        protected override void Engaged(PlayerController target, float dist) { }

        /// <summary>
        /// The one place a pattern is decided. Offline: weighted pick among the patterns allowed in this phase,
        /// off cooldown and in range, using the boss's seeded random numbers. Online: <see cref="PatternAuthority"/>.
        /// </summary>
        public BossPattern ChoosePattern()
        {
            if (PatternAuthority != null) return PatternAuthority(this);
            var target = E.MonsterTarget;
            float dist = target != null ? Vector2.Distance(E.Position, target.Position) : 0f;
            float now = Time.time, total = 0f;
            var options = new List<BossPattern>();
            foreach (var p in Def.patterns)
            {
                if (p.weight <= 0f || Phase < p.minPhase || Phase > p.maxPhase) continue;
                if (readyAt.TryGetValue(p, out float at) && now < at) continue;
                if (p.maxRange > 0f && dist > p.maxRange * Scale) continue;
                options.Add(p);
                total += p.weight;
            }
            if (options.Count == 0) return null;
            float roll = (float)E.Rng.NextDouble() * total;
            foreach (var p in options)
            {
                roll -= p.weight;
                if (roll <= 0f) return p;
            }
            return options[options.Count - 1];
        }

        IEnumerator Play(BossPattern p)
        {
            CurrentPattern = p;
            readyAt[p] = Time.time + p.cooldown * Mul;
            switch (p.kind)
            {
                case PatternKind.Slash3: yield return Slash3(p); break;
                case PatternKind.Charge: yield return BossCharge(p); break;
                case PatternKind.Slam: yield return Slam(p, 3.2f); break;
                case PatternKind.Fields: yield return Fields(p, false); break;
                case PatternKind.GoldRain: yield return Fields(p, true); break;
                case PatternKind.Donut: yield return DonutPattern(p); break;
                case PatternKind.ArrowVolley: yield return Volley(p); break;
                case PatternKind.BoltRing: yield return BoltRing(p); break;
                case PatternKind.Summon: yield return Summon(p.count); break;
                case PatternKind.Guard: yield return Guard(p); break;
                case PatternKind.Judgment: yield return Judgment(); break;
            }
            CurrentPattern = null;
            nextPatternAt = Time.time + NextFloat(GapMin, GapMax) * Mul;
        }

        /// <summary>Test / script hook: plays <paramref name="kind"/> now if the boss has it.</summary>
        public bool DevPlay(PatternKind kind)
        {
            foreach (var p in Def.patterns)
                if (p.kind == kind) { Run(Play(p)); return true; }
            return false;
        }

        /// <summary>Test / script hook: casts 망자의 심판 on the next frame the boss can act.</summary>
        public void DevRequestJudgment() => judgmentRequested = true;

        // =============================== Patterns ===============================

        PlayerController Target => E.MonsterTarget;

        Vector2 AimDir() => Target != null ? Dir(E.Position, Target.Position) : E.MonsterFacing.ToVector();

        IEnumerator Slash3(BossPattern p)
        {
            for (int i = 0; i < p.count; i++)
            {
                Vector2 dir = AimDir();
                E.MonsterFace(E.Position + dir);
                float windup = (i == 0 ? 0.6f : 0.42f) * (Enraged ? 0.85f : 1f);
                Track(Telegraph.Cone(E, E.Position, dir, 3.0f * Scale, 110f, windup, E.MonsterDamage(p.damageMul), 7f));
                Game.Audio.PlaySfx("enemy_windup");
                yield return Hold(windup, CharacterAnim.Attack, true);
                Game.Audio.PlaySfx("enemy_attack");
                Fx.Spawn("fx_slash", E.Center + dir * 1.2f * Scale, Vector2.zero, 0f, 0.18f, 0f, 10);
                yield return Hold(0.12f, CharacterAnim.Attack);
            }
            yield return Hold(0.5f);
        }

        IEnumerator BossCharge(BossPattern p)
        {
            Vector2 dir = AimDir();
            yield return Charge(dir, 9f, 1.8f * Scale, Enraged ? 0.8f : 1.0f, 14f, 1.1f * Scale, p.damageMul, 10f, 0.7f);
        }

        IEnumerator Slam(BossPattern p, float radius)
        {
            Track(Telegraph.Circle(E, E.Position, radius * Scale, 1.0f, E.MonsterDamage(p.damageMul), 9f));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(1.0f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(0.18f, 0.2f);
            Fx.Burst("fx_dust", E.Position + new Vector2(0f, 0.2f), 10, 4f, 0.6f);
            yield return Hold(0.6f, CharacterAnim.Attack);
        }

        IEnumerator Fields(BossPattern p, bool gold)
        {
            var spots = new List<Vector2>();
            foreach (var m in MonsterHits.All()) if (spots.Count < p.count) spots.Add(m.Position);
            while (spots.Count < p.count)
            {
                float a = NextFloat(0f, Mathf.PI * 2f), r = NextFloat(1.5f, 5f);
                spots.Add(FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
            }
            float windup = 1.3f * (Enraged ? 0.85f : 1f);
            foreach (var s in spots)
            {
                var t = Track(Telegraph.Circle(E, s, 1.4f, windup, E.MonsterDamage(p.damageMul), 5f));
                if (gold) t.OnResolve += (tele, hit) => GoldNugget(tele.Origin);
                else t.OnResolve += (tele, hit) => SkillVisuals.Flash(tele.Origin + Vector2.up * 0.3f, SoulGlow, 2.2f, 0.25f);
            }
            Game.Audio.PlaySfx("magic");
            yield return Hold(windup, CharacterAnim.Attack, true);
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        void GoldNugget(Vector2 at)
        {
            Fx.Burst("fx_chip", at + new Vector2(0f, 0.2f), 4, 2.5f, 0.5f);
            SkillVisuals.Flash(at + Vector2.up * 0.3f, GoldGlow, 2.2f, 0.25f);
            // A little gold for the brave (deterministic: every other nugget).
            if (E.Rng.Next(2) == 0 && Game.World != null)
                Pickup.Create(ConsumableDatabase.Gold, E.Rng.Next(2, 6), at, Game.World.ObjectsRoot);
        }

        IEnumerator DonutPattern(BossPattern p)
        {
            Track(Telegraph.Donut(E, E.Position, 2.2f * Scale, 6.5f * Scale, 1.5f, E.MonsterDamage(p.damageMul), 8f));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(1.5f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(0.15f, 0.2f);
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        IEnumerator Volley(BossPattern p)
        {
            Vector2 dir = AimDir();
            E.MonsterFace(E.Position + dir);
            var dirs = new List<Vector2>();
            for (int i = 0; i < p.count; i++) dirs.Add(Rotate(dir, (i - (p.count - 1) * 0.5f) * 14f));
            foreach (var d in dirs) Track(Telegraph.Rect(E, E.Position, d, 10f, 0.6f, 0.8f, 0));
            yield return Hold(0.8f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("swing");
            foreach (var d in dirs)
                MonsterProjectile.Fire(E, "mon_arrow", E.Center + d * 0.6f, d, 11f, 10f, 0.25f, E.MonsterDamage(p.damageMul), 5f, new Color(0f, 0f, 0f, 0f));
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        IEnumerator BoltRing(BossPattern p)
        {
            MonsterFx.SummonCircle(E.Position, 1f);
            Game.Audio.PlaySfx("magic");
            yield return Hold(0.8f, CharacterAnim.Attack, true);
            float offset = NextFloat(0f, 360f);
            for (int i = 0; i < p.count; i++)
            {
                Vector2 d = Rotate(Vector2.right, offset + i * 360f / p.count);
                MonsterProjectile.Fire(E, "mon_bolt_big", E.Center + d * 0.8f, d, 3.6f, 11f, 0.35f, E.MonsterDamage(p.damageMul), 4f, SoulGlow);
            }
            yield return Hold(0.6f, CharacterAnim.Attack);
        }

        IEnumerator Summon(int count)
        {
            int room = Def.maxMinions - NecroBehaviour.CountMinions(E);
            count = Mathf.Min(count, room);
            if (count <= 0) yield break;
            var spots = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                float a = (i * 360f / count + NextFloat(0f, 40f)) * Mathf.Deg2Rad;
                spots.Add(FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * 2.4f));
            }
            foreach (var s in spots) MonsterFx.SummonCircle(s, 1.3f);
            Game.Audio.PlaySfx("magic");
            yield return Hold(1.0f, CharacterAnim.Attack, true);
            string minion = Def.raid ? (E.Rng.Next(3) == 0 ? "skel_knight" : "skel_warrior") : Def.minionId;
            foreach (var s in spots)
            {
                var m = MonsterDatabase.SpawnMinion(minion, s, E.transform.parent, E);
                MonsterFx.Rise(m);
                Summoned++;
            }
            yield return Hold(0.4f);
        }

        IEnumerator Guard(BossPattern p)
        {
            Guarding = true;
            Announce?.Invoke(this, "방패 태세!", new Color(0.75f, 0.85f, 1f));
            float t = 0f;
            while (t < 2.2f)
            {
                if (E.CanAct)
                {
                    t += Time.deltaTime;
                    if (Target != null) E.MonsterFace(Target.Position);
                    E.MonsterHalt();
                    E.MonsterAnim(CharacterAnim.Idle);
                }
                yield return null;
            }
            Guarding = false;
            yield return Slam(p, 3.4f);
        }

        public override bool BeforeDamage(ref DamageInfo info)
        {
            if (!Guarding) return true;
            Vector2 to = info.sourcePosition - E.Position;
            if (to.sqrMagnitude > 0.0001f && Vector2.Angle(E.MonsterFacing.ToVector(), to) <= ShieldGuardBehaviour.FrontalHalfAngle)
            {
                info.amount = Mathf.Max(1, Mathf.RoundToInt(info.amount * ShieldGuardBehaviour.FrontalKeep));
                Game.Audio.PlaySfx("mine");
            }
            return true;
        }

        IEnumerator Judgment()
        {
            CastingJudgment = true;
            JudgmentProgress = 0f;
            Announce?.Invoke(this, "망자의 심판", new Color(1f, 0.35f, 0.3f));
            var tele = Track(Telegraph.Circle(E, E.Position, JudgmentVisualRadius, JudgmentCast, 0));
            Game.Audio.PlaySfx("magic");
            float t = 0f;
            while (t < JudgmentCast)
            {
                // The cast only stops for groggy (which interrupts this coroutine) or pause.
                if (Game.IsWorldRunning) t += Time.deltaTime;
                JudgmentProgress = t / JudgmentCast;
                E.MonsterHalt();
                E.MonsterAnim(CharacterAnim.Attack, true);
                if (Time.frameCount % 12 == 0)
                    SkillFx.Spawn("fx_glow", E.Center, new Color(0.6f, 0.2f, 1f, 0.28f), 0.45f, SkillFx.At(E.Position.y, -1)).Additive().Scale(1.8f, 2.8f);
                yield return null;
            }
            if (tele != null && !tele.Cancelled) tele.ResolveNow();
            // Everyone, wherever they stand.
            JudgmentHits = MonsterHits.Apply(MonsterHits.All(), E.MonsterDamage(JudgmentMul), E.Position, 12f, true, E.gameObject);
            CastingJudgment = false;
            Game.Camera?.Shake(0.4f, 0.5f);
            SkillVisuals.Flash(E.Center, new Color(0.8f, 0.3f, 1f, 0.9f), 14f, 0.4f);
            Game.Audio.PlaySfx("rock_break");
            JudgmentEnded?.Invoke(this, false);
            yield return Hold(1.2f);
        }

        float JudgmentMul
        {
            get
            {
                foreach (var p in Def.patterns) if (p.kind == PatternKind.Judgment) return p.damageMul;
                return 8f;
            }
        }

        public override float OnGroggyBroken()
        {
            bool wasCasting = CastingJudgment;
            Interrupt();
            if (!wasCasting) return EnemyController.GroggySeconds;
            Announce?.Invoke(this, "심판 저지! 그로기", new Color(1f, 0.9f, 0.35f));
            JudgmentEnded?.Invoke(this, true);
            return JudgmentGroggy;
        }

        protected override void OnInterrupted()
        {
            CastingJudgment = false;
            Guarding = false;
            CurrentPattern = null;
            nextPatternAt = Time.time + GapMax;
        }

        // =============================== Phases / gimmicks ===============================

        void UpdatePhase()
        {
            if (E.IsDead || Def.phases == null) return;
            float ratio = E.Health.Max > 0 ? (float)E.Health.Current / E.Health.Max : 0f;
            int phase = 1;
            foreach (float th in Def.phases) if (ratio <= th) phase++;
            while (Phase < phase)
            {
                Phase++;
                OnPhase(Phase);
                PhaseChanged?.Invoke(this, Phase);
            }
        }

        void OnPhase(int phase)
        {
            Game.Camera?.Shake(0.25f, 0.3f);
            if (!IsKing)
            {
                Announce?.Invoke(this, $"PHASE {phase}", new Color(1f, 0.8f, 0.4f));
                return;
            }
            if (phase == 2)
            {
                Announce?.Invoke(this, "사령 토템을 동시에 파괴하라!", SoulGlow);
                SpawnTotems();
                nextSummonAt = Time.time + 1f;
            }
            else if (phase == 3)
            {
                Announce?.Invoke(this, "해골왕이 광폭해진다!", new Color(1f, 0.3f, 0.25f));
                Game.Audio?.PlayMusic(MapRegistry.MusicRaidEnrage); // [BGM] enrage track until the room / result changes it
                nextJudgmentAt = Time.time + JudgmentFirstDelay;
            }
        }

        void SpawnTotems()
        {
            if (totems.Count > 0) return;
            foreach (float side in new[] { -1f, 1f })
            {
                var slot = new TotemSlot { spot = FreeNear(E.Home + new Vector2(side * TotemOffsetX, TotemOffsetY)) };
                totems.Add(slot);
                RaiseTotem(slot);
            }
        }

        void RaiseTotem(TotemSlot slot)
        {
            var t = MonsterDatabase.SpawnMinion(MonsterDatabase.Totem, slot.spot, E.transform.parent, E);
            // [P1] Half of the party breaks each totem: half the summoner's HP scaling.
            t.Health.SetMax(Mathf.Max(1, t.Health.Max / 2), true);
            slot.live = t;
            slot.diedAt = -1f;
            MonsterFx.SummonCircle(slot.spot, 0.8f);
            MonsterFx.Rise(t);
            t.Died += dead => { if (slot.live == dead) { slot.live = null; slot.diedAt = Time.time; } };
        }

        void UpdateTotems()
        {
            if (totems.Count == 0 || TotemsCleared || E.IsDead) return;
            bool allDown = true;
            float first = float.MaxValue, last = float.MinValue;
            foreach (var s in totems)
            {
                if (s.live != null) { allDown = false; continue; }
                first = Mathf.Min(first, s.diedAt);
                last = Mathf.Max(last, s.diedAt);
            }
            if (allDown && last - first <= TotemWindow)
            {
                TotemsCleared = true;
                CompanionBrain.PriorityTarget = null;
                CompanionBrain.PriorityTargetAlt = null;
                Announce?.Invoke(this, "사령 토템 파괴!", SoulGlow);
                return;
            }
            // Too slow: a broken totem rises again.
            foreach (var s in totems)
            {
                if (s.live != null || Time.time - s.diedAt < TotemWindow) continue;
                RaiseTotem(s);
                TotemRevives++;
                Announce?.Invoke(this, "사령 토템 부활", SoulGlow);
            }
            // Companions go for a standing totem first.
            // [P1] Two standing totems: the party splits so both break inside the window.
            EnemyController focus = null, second = null;
            foreach (var s in totems)
                if (s.live != null && !s.live.IsDead) { if (focus == null) focus = s.live; else if (second == null) second = s.live; }
            CompanionBrain.PriorityTarget = focus;
            CompanionBrain.PriorityTargetAlt = second;
        }

        protected override void OnDied(EnemyController e)
        {
            base.OnDied(e);
            if (CompanionBrain.PriorityTarget != null && CompanionBrain.PriorityTarget.Summoner == E) CompanionBrain.PriorityTarget = null;
            // Everything it raised falls with it.
            foreach (var m in new List<EnemyController>(EnemyController.Active))
                if (m != null && !m.IsDead && m.Summoner == E)
                    m.TakeDamage(new DamageInfo(m.Health.Current + 9999, m.Position, 0f, Team.Neutral));
        }
    }
}
