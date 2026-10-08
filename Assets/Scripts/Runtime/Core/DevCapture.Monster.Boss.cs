using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // =============================== Bosses ===============================

        IEnumerator BossChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            var shotKinds = new HashSet<PatternKind>();
            int shotIndex = 31;
            foreach (var id in BossIds)
            {
                MonClear();
                PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
                yield return null;
                var boss = MonSpawn(id, new Vector2(5f, 0.3f), 20f);
                var brain = boss.Behaviour as BossBrain;
                yield return MWait(0.45f);
                var bar = BossHpBarView.Instance;
                bool bound = bar != null && bar.Current == boss && bar.DevVisible;
                if (id == BossIds[0]) yield return Shot("mon_30_boss_intro");
                // Super armor: a heavy hit neither staggers nor pushes.
                Vector2 before = boss.Position;
                boss.TakeDamage(FromLocal(10, local.Center, 20f));
                yield return new WaitForFixedUpdate();
                bool armor = !boss.IsStaggered && Vector2.Distance(before, boss.Position) < 0.3f;
                // Every pattern once.
                var results = new List<string>();
                bool allOk = true;
                foreach (var p in boss.Def.patterns)
                {
                    if (p.kind == PatternKind.Judgment) continue;
                    yield return MWaitUntil(() => !brain.Busy, 6f);
                    // Back to the open spot so every pattern screenshot shows boss and party.
                    PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
                    MoveEnemy(boss, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
                    yield return null;
                    int summoned0 = brain.Summoned;
                    brain.DevPlay(p.kind);
                    int maxTele = 0, maxProj = 0;
                    bool guard = false, taken = false;
                    float t0 = Time.realtimeSinceStartup;
                    while (brain.Busy && Time.realtimeSinceStartup - t0 < 8f)
                    {
                        MonTank();
                        int tele = Telegraph.Active.Count(t => t.Owner == boss);
                        maxTele = Mathf.Max(maxTele, tele);
                        maxProj = Mathf.Max(maxProj, MonsterProjectile.Active.Count(x => x != null && x.Owner == boss));
                        guard |= brain.Guarding;
                        if (!taken && !shotKinds.Contains(p.kind) && (tele > 0 && Telegraph.Active.Any(t => t.Owner == boss && t.Progress > 0.55f) || maxProj > 0 || brain.Summoned > summoned0))
                        {
                            taken = true;
                            shotKinds.Add(p.kind);
                            yield return Shot($"mon_{shotIndex++}_{id}_{p.kind}");
                            continue;
                        }
                        yield return null;
                    }
                    bool ok = maxTele > 0 || maxProj > 0 || brain.Summoned > summoned0 || guard;
                    allOk &= ok && !brain.Busy;
                    results.Add($"{p.kind}:{(ok ? "ok" : "none")}");
                }
                MCheck($"{id}: bar bound={bound} lines='{bar?.DevLinesText}' markers={bar?.DevMarkers} super armor={armor} patterns [{string.Join(" ", results)}]",
                    bound && armor && allOk);

                if (id == "boss_gold_foreman")
                {
                    // Groggy: break the gauge, then hits deal +30%.
                    yield return MWaitUntil(() => !brain.Busy, 6f);
                    float g0 = boss.Groggy;
                    boss.TakeDamage(FromLocal(Mathf.CeilToInt(boss.Groggy) + 1, local.Center, 0f));
                    bool groggy = boss.IsGroggy;
                    yield return MWait(0.25f);
                    int h0 = boss.Health.Current;
                    boss.TakeDamage(FromLocal(100, local.Center, 0f));
                    int dealt = h0 - boss.Health.Current;
                    yield return MWait(0.3f);
                    yield return Shot("mon_29_groggy");
                    string gtext = bar != null ? bar.DevGroggyText : "";
                    MCheck($"groggy: gauge {g0:0} broke={groggy} for {boss.GroggyDuration:0.0}s, 100 dmg -> {dealt}, bar='{gtext}'",
                        groggy && Mathf.Abs(boss.GroggyDuration - EnemyController.GroggySeconds) < 0.01f && dealt == 130 && gtext.StartsWith("GROGGY!"));
                    yield return MWait(0.3f);
                }
                boss.TakeDamage(FromLocal(boss.Health.Current + 99999, local.Center, 0f));
                yield return MWait(1.6f);
                if (id == BossIds[0]) MCheck($"bar unbinds after the kill: current={(bar != null && bar.Current != null ? bar.Current.name : "none")} visible={bar?.DevVisible}", bar != null && bar.Current == null && !bar.DevVisible);
            }
        }

        IEnumerator KingChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            yield return null;
            var king = MonSpawn("boss_skeleton_king", new Vector2(5f, 0.3f), 1f);
            var brain = king.Behaviour as BossBrain;
            var hp = king.Health;
            yield return MWait(1.5f);
            bool p1 = brain.Phase == 1;

            // ---------- Phase 2 at 70% ----------
            yield return MWaitUntil(() => !brain.Busy, 6f);
            king.TakeDamage(FromLocal(hp.Current - Mathf.FloorToInt(hp.Max * 0.69f), local.Center, 0f));
            king.EndGroggyNow();
            yield return MWait(0.3f);
            var totems = brain.Totems.ToList();
            bool priority = totems.Contains(CompanionBrain.PriorityTarget);
            MCheck($"king phase 1 -> 2 at {(float)hp.Current / hp.Max:0.00}: was p1={p1} phase={brain.Phase} totems={totems.Count} companion priority on a totem={priority}",
                p1 && brain.Phase == 2 && totems.Count == 2 && priority);
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            MoveEnemy(king, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
            yield return MWaitUntil(() => brain.Summoned >= 2, 5f);
            yield return MWait(0.5f);
            yield return Shot("mon_50_king_phase2_totems");
            int summonedP2 = brain.Summoned;

            // ---------- Totem rule: one broken + TotemWindow -> it rises again ----------
            var a = totems[0];
            a.TakeDamage(FromLocal(a.Health.Current + 999, a.Position));
            yield return MWait(0.3f);
            int standing = brain.Totems.Count();
            yield return MWait(BossBrain.TotemWindow + 0.3f); // past the window: the broken one rises again
            var after = brain.Totems.ToList();
            MCheck($"totem rule (slow): after one broken {standing} standing; {BossBrain.TotemWindow:0} s later {after.Count} standing revives={brain.TotemRevives} cleared={brain.TotemsCleared}",
                standing == 1 && after.Count == 2 && brain.TotemRevives == 1 && !brain.TotemsCleared);

            // ---------- Both within 5 s -> gone for good, summons stop ----------
            after[0].TakeDamage(FromLocal(after[0].Health.Current + 999, after[0].Position));
            yield return MWait(2f);
            after[1].TakeDamage(FromLocal(after[1].Health.Current + 999, after[1].Position));
            yield return MWait(0.4f);
            bool cleared = brain.TotemsCleared && !brain.Totems.Any() && CompanionBrain.PriorityTarget == null;
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));
            int summonedAtClear = brain.Summoned;
            yield return MWait(BossBrain.SummonEvery + 1.5f);
            MCheck($"totem rule (together): cleared={cleared} summons before={summonedP2} at clear={summonedAtClear} after {BossBrain.SummonEvery + 1.5f:0}s={brain.Summoned} revives={brain.TotemRevives}",
                cleared && summonedP2 >= 2 && brain.Summoned == summonedAtClear && brain.TotemRevives == 1);

            // ---------- Phase 3 at 35% ----------
            yield return MWaitUntil(() => !brain.Busy, 6f);
            king.TakeDamage(FromLocal(hp.Current - Mathf.FloorToInt(hp.Max * 0.34f), local.Center, 0f));
            king.EndGroggyNow();
            yield return MWait(0.3f);
            MCheck($"king phase 3 at {(float)hp.Current / hp.Max:0.00}: phase={brain.Phase} enraged={brain.Enraged}", brain.Phase == 3 && brain.Enraged);
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));

            // ---------- 망자의 심판: broken ----------
            int judgmentEnds = 0;
            bool lastBroken = false;
            Action<BossBrain, bool> onEnd = (b, broken) => { judgmentEnds++; lastBroken = broken; };
            brain.JudgmentEnded += onEnd;
            int bigHits = 0;
            int judgmentDamage = king.MonsterDamage(8f);
            var hooks = new List<(Health h, Action<DamageInfo> act)>();
            foreach (var m in MonMembers(local))
            {
                Action<DamageInfo> act = i => { if (i.attacker == king.gameObject && i.amount >= judgmentDamage) bigHits++; };
                m.Health.Damaged += act;
                hooks.Add((m.Health, act));
            }
            yield return MWaitUntil(() => !brain.Busy, 6f);
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            MoveEnemy(king, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
            brain.DevRequestJudgment();
            yield return MWaitUntil(() => brain.CastingJudgment, 8f);
            bool casting = brain.CastingJudgment;
            yield return MWait(1.5f);
            yield return Shot("mon_51_king_judgment_cast");
            king.TakeDamage(FromLocal(Mathf.CeilToInt(king.Groggy) + 1, local.Center, 0f));
            bool groggy = king.IsGroggy;
            float groggyLen = king.GroggyDuration;
            yield return MWait(0.5f);
            yield return Shot("mon_52_king_judgment_broken");
            yield return MWait(1.0f);
            MCheck($"judgment broken: casting={casting} groggy={groggy} for {groggyLen:0.0}s ended={judgmentEnds} broken={lastBroken} big hits={bigHits}",
                casting && groggy && Mathf.Abs(groggyLen - BossBrain.JudgmentGroggy) < 0.01f && judgmentEnds == 1 && lastBroken && bigHits == 0 && !brain.CastingJudgment);
            king.EndGroggyNow();

            // ---------- 망자의 심판: resolved ----------
            yield return MWait(0.3f);
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));
            brain.DevRequestJudgment();
            yield return MWaitUntil(() => brain.CastingJudgment, 8f);
            bool casting2 = brain.CastingJudgment;
            int alive = MonMembers(local).Count(m => !m.IsDead);
            yield return MWaitUntil(() => !brain.CastingJudgment, BossBrain.JudgmentCast + 2f);
            yield return MWait(0.2f);
            yield return Shot("mon_53_king_judgment_resolved");
            MCheck($"judgment resolved: casting={casting2} ended={judgmentEnds} broken={lastBroken} hits={brain.JudgmentHits}/{alive} members big hits={bigHits} (≥{judgmentDamage})",
                casting2 && judgmentEnds == 2 && !lastBroken && brain.JudgmentHits == alive && bigHits == alive);
            brain.JudgmentEnded -= onEnd;
            foreach (var (h, act) in hooks) h.Damaged -= act;

            king.TakeDamage(FromLocal(hp.Current + 99999, local.Center, 0f));
            yield return MWait(1.6f);
            bool minionsGone = !EnemyController.Active.Any(e => e != null && !e.IsDead && e.Summoner == king);
            MCheck($"king killed: minions and totems fall with it={minionsGone} bar unbound={BossHpBarView.Instance != null && BossHpBarView.Instance.Current == null}",
                minionsGone && BossHpBarView.Instance != null && BossHpBarView.Instance.Current == null);
        }

        static List<PlayerController> MonMembers(PlayerController local) =>
            Game.Party != null ? Game.Party.Members.Where(m => m != null).ToList() : new List<PlayerController> { local };
    }
}
