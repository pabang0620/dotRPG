using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] 결계 의식 of 수호자 그라흐 (Docs/PLAN_BOSS_DIFFICULTY_2026-10-13.md G6): at set HP the boss speaks its line, becomes
    /// invulnerable and opens one magic circle per living party member at random spots. Every circle must hold a
    /// different member at the same moment before the time runs out: broken = a groggy that leaves the gauge as it
    /// was; not broken = everyone loses 90% of max HP and MP. The boss side (BossBrain.Seal.cs) runs where the boss is
    /// simulated (solo / host); members draw it from PartyMsg.RaidSeal / RaidSealEnd.
    /// </summary>
    public static class RaidSeal
    {
        // ---------- Tuning ----------
        /// <summary>HP ratios that start the ritual (each once per fight).</summary>
        public static readonly float[] At = { .65f, .3f };
        /// <summary>The line stays alone this long before the circles open (the warning).</summary>
        public const float LineLead = 2.2f;
        /// <summary>
        /// Time to fill every circle (2026-10-13: tight but always beatable): a reaction allowance plus the run to the
        /// circle the party reaches last (straight line × <see cref="PathSlack"/> at the slowest member's speed), clamped.
        /// </summary>
        public const float React = 1.5f, PathSlack = 1.3f, WindowMin = 4f, WindowMax = 12f;
        /// <summary>Circle radius; circles open anywhere in the room, at least this far apart.</summary>
        public const float Radius = 1.1f, Apart = 4f;
        /// <summary>Broken: groggy seconds (the gauge stays as it was).</summary>
        public const float BrokenGroggy = 5f;
        /// <summary>Not broken: share of max HP and of max MP everyone loses.</summary>
        public const float FailHpShare = .9f, FailMpShare = .9f;

        static readonly Color Violet = new Color(.75f, .45f, 1f, .95f);

        /// <summary>Only 수호자 그라흐 holds the ritual (every raid boss has its own gimmicks).</summary>
        public static bool UsedBy(string bossId) => bossId == "boss_grah";

        /// <summary>The line the boss speaks before the ritual (the trigger players learn).</summary>
        public const string Line = "수호의 진이여, 침입자를 시험하라!";

        /// <summary>Circles of the ritual running on this PC (null when none). Members read it to find their spot.</summary>
        public static Vector2[] Active { get; private set; }

        /// <summary>Host: the line was spoken (boss, line). PartyNet relays it.</summary>
        public static event Action<EnemyController, string> LineStarted;
        /// <summary>Host: the circles opened (boss, seconds, centres). PartyNet relays it.</summary>
        public static event Action<EnemyController, float, Vector2[]> Opened;
        /// <summary>Host: the ritual ended (boss, broken). PartyNet relays it.</summary>
        public static event Action<EnemyController, bool> Ended;

        /// <summary>Host: a member was thrown by 파동 / lost MP to the ritual. PartyNet tells that member's PC (it owns its body and MP).</summary>
        public static event Action<PlayerController, Vector2, float> MemberLaunched;
        public static event Action<PlayerController, float> MemberDrained;
        public static void RaiseLaunch(PlayerController m, Vector2 velocity, float seconds) => MemberLaunched?.Invoke(m, velocity, seconds);
        public static void RaiseDrain(PlayerController m, float share) => MemberDrained?.Invoke(m, share);

        public static void RaiseLine(EnemyController boss, string line) => LineStarted?.Invoke(boss, line);
        public static void RaiseOpened(EnemyController boss, float seconds, Vector2[] circles) => Opened?.Invoke(boss, seconds, circles);
        public static void RaiseEnded(EnemyController boss, bool broken) => Ended?.Invoke(boss, broken);

        static bool IsAi(PlayerController m) => m != null && m.Data != null && MercenaryDatabase.Get(m.Data.MercenaryId) != null;
        static bool Alive(PlayerController m) => m != null && !m.IsDead && m.isActiveAndEnabled;

        /// <summary>Index of the circle <paramref name="p"/> stands in, or -1.</summary>
        public static int CircleAt(Vector2 p, Vector2[] circles)
        {
            if (circles == null) return -1;
            for (int i = 0; i < circles.Length; i++) if ((p - circles[i]).sqrMagnitude <= Radius * Radius) return i;
            return -1;
        }

        /// <summary>How many circles hold a different living member right now.</summary>
        public static int Filled(Vector2[] circles, IEnumerable<PlayerController> members)
        {
            var held = new bool[circles.Length];
            int n = 0;
            foreach (var m in members)
            {
                if (!Alive(m)) continue;
                int i = CircleAt(m.Position, circles);
                if (i >= 0 && !held[i]) { held[i] = true; n++; }
            }
            return n;
        }

        /// <summary>
        /// The circle <paramref name="self"/> should stand on (AI companions and 자동 사냥): members already in a circle keep
        /// it, everyone else takes the nearest free one in party order (the person first, then the AI).
        /// </summary>
        public static bool SpotFor(PlayerController self, out Vector2 spot)
        {
            spot = default;
            var circles = Active;
            var party = Game.Party;
            if (circles == null || party == null || self == null) return false;
            var taken = new PlayerController[circles.Length];
            foreach (var m in party.Members)
            {
                if (!Alive(m)) continue;
                int i = CircleAt(m.Position, circles);
                if (i >= 0 && taken[i] == null) taken[i] = m;
            }
            for (int i = 0; i < taken.Length; i++) if (taken[i] == self) { spot = circles[i]; return true; }
            for (int pass = 0; pass < 2; pass++)
                foreach (var m in party.Members)
                {
                    if (!Alive(m) || IsAi(m) != (pass == 1) || Array.IndexOf(taken, m) >= 0) continue;
                    int best = -1;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < circles.Length; i++)
                    {
                        if (taken[i] != null) continue;
                        float d = (m.Position - circles[i]).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best < 0) return false;
                    taken[best] = m;
                    if (m == self) { spot = circles[best]; return true; }
                }
            return false;
        }

        // ---------------- presentation (every PC) ----------------

        static readonly List<SkillFx> circleFx = new List<SkillFx>();
        static Presenter presenter;

        /// <summary>The boss speaks the ritual line (the warning before the circles).</summary>
        public static void ShowLine(EnemyController boss, string line)
        {
            if (boss == null) return;
            WorldPopupText.Show(boss.transform, line, Violet, LineLead + 1f, 1.4f + 1.1f * boss.Size);
            var b = boss.GetComponent<BossBrain>();
            if (b != null) BossBrain.RaiseAnnounce(b, line, Violet);
            Game.Audio?.PlaySfx("magic");
        }

        /// <summary>The circles open with a countdown.</summary>
        public static void ShowOpened(EnemyController boss, float seconds, Vector2[] circles)
        {
            Clear();
            Active = circles;
            foreach (var c in circles)
            {
                circleFx.Add(SkillFx.Spawn("fx_ring", c, Violet, seconds + .3f, SkillFx.GroundOrder + 14)
                    .Scale(new Vector2(Radius, Radius * .7f), new Vector2(Radius, Radius * .7f)));
                circleFx.Add(SkillFx.Spawn("fx_glow", c, new Color(.7f, .4f, 1f, .4f), seconds + .3f, SkillFx.GroundOrder + 13).Additive()
                    .Scale(new Vector2(Radius * 1.6f, Radius * 1.1f), new Vector2(Radius * 1.9f, Radius * 1.3f)));
            }
            presenter = new GameObject("RaidSealFx").AddComponent<Presenter>();
            presenter.Begin(boss != null ? boss.GetComponent<BossBrain>() : null, seconds, circles.Length);
            Game.Audio?.PlaySfx("enemy_windup");
        }

        /// <summary>The ritual ended: circles go away and the result is announced.</summary>
        public static void ShowEnded(EnemyController boss, bool broken)
        {
            Clear();
            var b = boss != null ? boss.GetComponent<BossBrain>() : null;
            if (b != null) BossBrain.RaiseAnnounce(b, broken ? "결계 파훼! 그로기" : "결계가 폭발했다!", broken ? new Color(1f, .9f, .35f) : new Color(1f, .3f, .25f));
            if (!broken && boss != null) SkillVisuals.Flash(boss.Center, new Color(.75f, .4f, 1f, .9f), 14f, .4f);
        }

        static void Clear()
        {
            Active = null;
            foreach (var fx in circleFx) if (fx != null) UnityEngine.Object.Destroy(fx.gameObject);
            circleFx.Clear();
            if (presenter != null) UnityEngine.Object.Destroy(presenter.gameObject);
            presenter = null;
        }

        /// <summary>Counts down on the boss bar: "마법진에 올라서라 n/m 7".</summary>
        sealed class Presenter : MonoBehaviour
        {
            BossBrain brain;
            bool hadBrain;
            float until;
            int need, lastCount = -1, lastFilled = -1;

            public void Begin(BossBrain b, float seconds, int circles)
            {
                brain = b;
                hadBrain = b != null;
                until = Time.time + seconds;
                need = circles;
            }

            void Update()
            {
                // The boss is gone without an end (room left, run over): drop the circles so nobody walks to them. A member
                // PC whose boss puppet was not found keeps them until the time runs out (the host's end message clears them).
                if ((hadBrain && brain == null) || Time.time > until + 2f) { Clear(); return; }
                if (Active == null || brain == null) return;
                int count = Mathf.Max(0, Mathf.CeilToInt(until - Time.time));
                int filled = Game.Party != null ? Filled(Active, Game.Party.Members) : 0;
                if (count == lastCount && filled == lastFilled) return;
                lastCount = count;
                lastFilled = filled;
                BossBrain.RaiseAnnounce(brain, $"마법진에 한 명씩 올라서라! {filled}/{need}  {count}", Violet);
            }
        }
    }
}
