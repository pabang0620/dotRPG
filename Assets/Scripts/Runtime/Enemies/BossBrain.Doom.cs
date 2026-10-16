using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Lv40 instant-death gimmicks (흑철의 바르가스, 수호자 그라흐).
    /// 파멸의 의식: the boss channels and raises three ritual totems; all must break before the cast ends (→ groggy),
    /// otherwise everyone dies. 운명의 낙인: two teal circles, each member is told which one is theirs; anyone not
    /// standing in their own circle when the mark lands dies. Both are driven from Tick, so a groggy that cuts the
    /// pattern coroutine never leaves a gimmick half done. Members see the circles (snapshot telegraphs), the lines
    /// (RaidSeal line relay) and the totems (synced monsters); only the host resolves them.
    /// </summary>
    public sealed partial class BossBrain
    {
        public const float RuinCast = 14f, RuinGroggy = 6f, RuinTotemHpShare = .01f, RuinTotemDistance = 4.5f;
        public const float FateTime = 7f, FateRadius = 1.6f, FateOffset = 3.6f;
        /// <summary>Extra radius a remote member gets at the mark (one 20 Hz report plus relay latency at walking speed).</summary>
        public const float FateNetGrace = .3f;
        const int RuinTotems = 3;

        static readonly Color DoomRed = new Color(1f, .25f, .3f);

        readonly List<EnemyController> ruinTotems = new List<EnemyController>();
        float ruinUntil = -1f, fateUntil = -1f;
        readonly Vector2[] fateCircles = new Vector2[2];

        public bool RuinActive => ruinUntil > 0f;
        public bool FateActive => fateUntil > 0f;
        public IEnumerable<EnemyController> RuinTotemsLeft
        {
            get { foreach (var t in ruinTotems) if (t != null && !t.IsDead) yield return t; }
        }

        IEnumerator RuinRitual(BossPattern p)
        {
            if (Sealed || RuinActive || FateActive) yield break;
            float cast = RuinCast * (Heroic ? .85f : 1f);
            string line = L.T("파멸의 의식! 토템 3개를 모두 부수세요!");
            RaidSeal.ShowLine(E, line);
            RaidSeal.RaiseLine(E, line);
            Telegraph.Warning(E, E.Position, 13f * Scale, cast);
            ruinTotems.Clear();
            float first = NextFloat(0f, Mathf.PI * 2f);
            for (int i = 0; i < RuinTotems; i++)
            {
                float a = first + i * Mathf.PI * 2f / RuinTotems;
                Vector2 spot = FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .8f) * RuinTotemDistance * Scale);
                var t = MonsterDatabase.SpawnMinion(MonsterDatabase.Totem, spot, E.transform.parent, E);
                t.Health.SetMax(Mathf.Max(1, Mathf.RoundToInt(E.Health.Max * RuinTotemHpShare)), true);
                MonsterFx.SummonCircle(spot, .8f);
                MonsterFx.Rise(t);
                ruinTotems.Add(t);
            }
            ruinUntil = Time.time + cast;
            Game.Audio.PlaySfx("magic");
            yield return Hold(.4f, CharacterAnim.Attack, true);
        }

        IEnumerator MarkOfFate(BossPattern p)
        {
            if (Sealed || RuinActive || FateActive) yield break;
            var members = MonsterHits.All();
            if (members.Count == 0) yield break;
            float time = FateTime * (Heroic ? .85f : 1f);
            for (int side = 0; side < 2; side++)
            {
                fateCircles[side] = FreeNear(E.Position + new Vector2(side == 0 ? -FateOffset * Scale : FateOffset * Scale, 0f));
                Telegraph.SafeCircle(E, fateCircles[side], FateRadius, time + .3f);
            }
            RaidDoom.Begin(time);
            int start = NextFloat(0f, 1f) < .5f ? 0 : 1;
            var text = new System.Text.StringBuilder(L.T("운명의 낙인!"));
            for (int i = 0; i < members.Count; i++)
            {
                int side = (start + i) % 2;
                RaidDoom.Assign(members[i], fateCircles[side]);
                text.Append(i == 0 ? " " : ", ").Append(members[i].DisplayName).Append(" → ").Append(side == 0 ? L.T("왼쪽 원") : L.T("오른쪽 원"));
            }
            string line = text.ToString();
            RaidSeal.ShowLine(E, line);
            RaidSeal.RaiseLine(E, line);
            fateUntil = Time.time + time;
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(.4f, CharacterAnim.Attack, true);
        }

        /// <summary>
        /// A gimmick clock does not stop for the boss's own groggy, stun or freeze (Tick is skipped then): resolve it on time
        /// from here, otherwise the mark lands late, after the circles are gone and the party has walked off them.
        /// </summary>
        void Update()
        {
            if (E == null || E.Puppet || E.IsDead || !Game.IsWorldRunning || E.CanAct) return;
            // Only the resolution (the channel look stays off while the boss is reeling).
            bool ruinDue = RuinActive && (Time.time >= ruinUntil || !ruinTotems.Exists(t => t != null && !t.IsDead));
            bool fateDue = FateActive && Time.time >= fateUntil;
            if (ruinDue || fateDue) DoomTick();
        }

        /// <summary>Channels a running gimmick and resolves it when its time is up. True = the boss does nothing else this frame.</summary>
        bool DoomTick()
        {
            if (E.IsDead) { EndDoom(); return false; }
            if (RuinActive)
            {
                ruinTotems.RemoveAll(t => t == null || t.IsDead);
                if (ruinTotems.Count == 0)
                {
                    EndRuin();
                    Announce?.Invoke(this, L.T("의식 붕괴! 그로기"), new Color(1f, .9f, .35f));
                    E.ForceGroggy(RuinGroggy);
                    return true;
                }
                CompanionBrain.PriorityTarget = ruinTotems[0];
                CompanionBrain.PriorityTargetAlt = ruinTotems.Count > 1 ? ruinTotems[1] : null;
                if (Time.time >= ruinUntil)
                {
                    foreach (var t in ruinTotems)
                        if (t != null && !t.IsDead) t.TakeDamage(new DamageInfo(t.Health.Current + 9999, t.Position, 0f, Team.Neutral));
                    EndRuin();
                    Announce?.Invoke(this, L.T("파멸!"), DoomRed);
                    Doom(MonsterHits.All());
                    return true;
                }
                Channel(new Color(1f, .2f, .25f, .3f));
                return true;
            }
            if (FateActive)
            {
                if (Time.time >= fateUntil)
                {
                    var marked = new List<PlayerController>();
                    // A remote member is judged where its PC last reported it (NetTarget), not where the lagging host copy has
                    // walked to, plus a small allowance for the report in flight: nobody dies to latency on the circle's edge.
                    foreach (var m in MonsterHits.All())
                        if (RaidDoom.SpotFor(m, out Vector2 spot, true) && Vector2.Distance(m.NetTarget, spot) > FateRadius + (m.NetDriven ? FateNetGrace : 0f)) marked.Add(m);
                    fateUntil = -1f;
                    RaidDoom.End();
                    if (marked.Count > 0) Announce?.Invoke(this, L.T("낙인 폭발!"), DoomRed);
                    Doom(marked);
                    return true;
                }
                Channel(new Color(.6f, .2f, 1f, .28f));
                return true;
            }
            return false;
        }

        void Channel(Color glow)
        {
            E.MonsterHalt();
            E.MonsterAnim(CharacterAnim.Attack, true);
            if (Time.frameCount % 12 == 0)
                SkillFx.Spawn("fx_glow", E.Center, glow, .45f, SkillFx.At(E.Position.y, -1)).Additive().Scale(1.8f, 2.8f);
        }

        /// <summary>Kills through the normal death path (revive coins and party revives still work).</summary>
        void Doom(List<PlayerController> victims)
        {
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(.45f, .5f);
            SkillVisuals.Flash(E.Center, new Color(1f, .25f, .3f, .9f), 14f, .4f);
            foreach (var m in victims)
            {
                if (m == null || m.IsDead) continue;
                var info = new DamageInfo(m.Health.Max * 20 + 9999, E.Position, 0f, Team.Enemy, E.gameObject) { unblockable = true, lethal = true };
                m.TakeDamage(info);
            }
        }

        void EndRuin()
        {
            ruinUntil = -1f;
            ruinTotems.Clear();
            CompanionBrain.PriorityTarget = null;
            CompanionBrain.PriorityTargetAlt = null;
        }

        void EndDoom()
        {
            if (RuinActive) EndRuin();
            if (FateActive) { fateUntil = -1f; RaidDoom.End(); }
        }
    }

    /// <summary>[RAID] 운명의 낙인: which circle each member must stand in (read by <see cref="CompanionBrain"/>).</summary>
    public static class RaidDoom
    {
        static readonly Dictionary<PlayerController, Vector2> spots = new Dictionary<PlayerController, Vector2>();
        static float until = -1f;

        public static void Begin(float seconds)
        {
            spots.Clear();
            until = Time.time + seconds + .5f;
        }

        public static void Assign(PlayerController m, Vector2 spot) { if (m != null) spots[m] = spot; }

        public static void End()
        {
            spots.Clear();
            until = -1f;
        }

        /// <summary>The circle <paramref name="m"/> must stand in while a mark is running.</summary>
        public static bool SpotFor(PlayerController m, out Vector2 spot, bool ignoreClock = false)
        {
            spot = default;
            if (m == null || (!ignoreClock && Time.time >= until)) return false;
            return spots.TryGetValue(m, out spot);
        }
    }
}
