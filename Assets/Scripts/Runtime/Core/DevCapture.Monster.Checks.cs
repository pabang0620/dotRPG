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
        // =============================== Monster checks ===============================

        IEnumerator ChargeChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            // The local player stands in the line; Bron stands well off it.
            PlaceMembers(local, bron, elin, new Vector2(2.5f, 2.4f), new Vector2(-6f, -3f));
            yield return null;
            var miner = MonSpawn("skel_miner", new Vector2(5f, 0f), 5f);
            var charger = miner.Behaviour as ChargerBehaviour;
            int localHits = 0, bronHits = 0;
            Action<DamageInfo> onLocal = i => { if (i.attacker == miner.gameObject) localHits++; };
            Action<DamageInfo> onBron = i => { if (i.attacker == miner.gameObject) bronHits++; };
            local.Health.Damaged += onLocal;
            bron.Health.Damaged += onBron;
            yield return null;
            charger.DevCharge(local.Position - miner.Position);
            yield return MWait(0.55f);
            var tele = Telegraph.Active.FirstOrDefault(t => t.Owner == miner);
            bool inLine = tele != null && tele.Contains(local.Position) && !tele.Contains(bron.Position);
            yield return Shot("mon_20_charge_telegraph");
            yield return MWaitUntil(() => !charger.Busy, 4f);
            local.Health.Damaged -= onLocal;
            bron.Health.Damaged -= onBron;
            MCheck($"miner charge: telegraph line covers local={inLine} hits local={localHits} bron(off line)={bronHits} chargeHits={charger.ChargeHits}",
                inLine && localHits >= 1 && bronHits == 0 && charger.ChargeHits >= 1);
        }

        IEnumerator ArcherChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var archer = MonSpawn("skel_archer", new Vector2(1.6f, 0f), 5f);
            var ab = archer.Behaviour as ArcherBehaviour;
            float d0 = Vector2.Distance(archer.Position, local.Position);
            bool shot = false;
            float end = Time.realtimeSinceStartup + 5.5f;
            while (Time.realtimeSinceStartup < end)
            {
                MonTank();
                if (!shot && Telegraph.Active.Any(t => t.Owner == archer && t.Progress > 0.5f))
                {
                    shot = true;
                    yield return Shot("mon_21_archer_aim");
                }
                yield return null;
            }
            float d1 = Vector2.Distance(archer.Position, local.Position);
            MCheck($"archer keeps distance: {d0:0.0} -> {d1:0.0} (keep {archer.Def.keepDistance}) shots={ab.Shots}",
                d1 >= 3.5f && d1 <= 6.8f && d1 > d0 + 1f && ab.Shots >= 1);
        }

        IEnumerator NecroChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var necro = MonSpawn("skel_necro", new Vector2(4.2f, 0f), 5f);
            var nb = necro.Behaviour as NecroBehaviour;
            int maxAlive = 0;
            bool killedOne = false, shotTaken = false;
            // Game time (hit-stop slows it down while the minions hit the player).
            float start = Time.time, realEnd = Time.realtimeSinceStartup + 30f;
            while (Time.time - start < 14.5f && Time.realtimeSinceStartup < realEnd)
            {
                MonTank();
                maxAlive = Mathf.Max(maxAlive, nb.AliveMinions);
                float t = Time.time - start;
                if (!shotTaken && t > 1.8f)
                {
                    shotTaken = true;
                    yield return Shot("mon_22_necro_summon");
                }
                if (!killedOne && t > 6f)
                {
                    var minion = EnemyController.Active.FirstOrDefault(e => e != null && !e.IsDead && e.Summoner == necro);
                    if (minion != null)
                    {
                        minion.TakeDamage(FromLocal(minion.Health.Current + 999, minion.Position));
                        killedOne = true;
                    }
                }
                yield return null;
            }
            MCheck($"necro summons: total={nb.Summoned} max alive={maxAlive} (cap {necro.Def.maxMinions}) killed one={killedOne} alive now={nb.AliveMinions}",
                maxAlive <= 2 && nb.Summoned >= 3 && killedOne);
        }

        IEnumerator ShieldChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var shield = MonSpawn("skel_shield", new Vector2(3.5f, 0f), 5f);
            var sb = shield.Behaviour as ShieldGuardBehaviour;
            yield return MWait(1.0f);
            yield return MWaitUntil(() => sb.Guarding && !shield.IsStaggered, 3f);
            yield return Shot("mon_23_shield_guard");
            var hp = shield.Health;
            int h0 = hp.Current;
            shield.TakeDamage(FromLocal(50, shield.Position + sb.GuardDirection * 1.5f, 8f));
            int front = h0 - hp.Current;
            bool frontStagger = shield.IsStaggered;
            yield return MWait(0.3f);
            int h1 = hp.Current;
            Vector2 back = shield.Position - sb.GuardDirection * 1.5f;
            shield.TakeDamage(FromLocal(50, back, 8f));
            int behind = h1 - hp.Current;
            bool backStagger = shield.IsStaggered;
            yield return MWait(0.6f);
            yield return MWaitUntil(() => sb.Guarding && !shield.IsStaggered, 3f);
            int h2 = hp.Current;
            Vector2 side = shield.Position + new Vector2(-sb.GuardDirection.y, sb.GuardDirection.x) * 1.5f;
            shield.TakeDamage(FromLocal(50, side, 8f));
            int sideDmg = h2 - hp.Current;
            MCheck($"shield: front 50 -> {front} (stagger {frontStagger}), behind 50 -> {behind} (stagger {backStagger}), side 50 -> {sideDmg}",
                front == 10 && !frontStagger && behind == 50 && backStagger && sideDmg == 50);
        }

        IEnumerator GoldChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var gold = MonSpawn("skel_gold", new Vector2(2.4f, 0f), 3f);
            float d0 = Vector2.Distance(gold.Position, local.Position);
            yield return MWait(2f);
            float d1 = Vector2.Distance(gold.Position, local.Position);
            int p0 = GoldPickups();
            gold.TakeDamage(FromLocal(5, local.Center));
            int p1 = GoldPickups();
            yield return MWait(0.2f);
            yield return Shot("mon_24_gold_spill");
            gold.TakeDamage(FromLocal(gold.Health.Current + 999, local.Center));
            int p2 = GoldPickups();
            MCheck($"gold skeleton: flees {d0:0.0} -> {d1:0.0}, hit spills {p1 - p0} pile(s), death drops {p2 - p1} pile(s)",
                d1 > d0 + 1f && p1 - p0 >= 1 && p2 - p1 >= 2);
            yield return MWait(0.5f);
        }

        IEnumerator ArmorChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var knight = MonSpawn("skel_knight", new Vector2(1.4f, 0f), 10f);
            var kb = knight.Behaviour as KnightBehaviour;
            yield return null;
            kb.DevCombo(local);
            yield return MWait(0.2f);
            Vector2 before = knight.Position;
            bool armored = knight.SuperArmorActive && kb.Attacking;
            knight.TakeDamage(FromLocal(20, local.Center, 14f));
            bool staggered = knight.IsStaggered;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float pushed = Vector2.Distance(before, knight.Position);
            yield return Shot("mon_25_knight_combo");
            // Control: a plain skeleton staggers and slides.
            var warrior = MonSpawn("skel_warrior", new Vector2(1.5f, 2.5f), 10f);
            yield return MWait(0.2f);
            warrior.TakeDamage(FromLocal(20, local.Center, 14f));
            bool warriorStagger = warrior.IsStaggered;
            MCheck($"super armor: knight attacking={armored} staggered={staggered} pushed={pushed:0.00}; plain skeleton staggered={warriorStagger}",
                armored && !staggered && pushed < 0.15f && warriorStagger);
            yield return MWaitUntil(() => !kb.Busy, 3f);
        }

        IEnumerator DodgeChecks(PlayerController local, PlayerController bron, PlayerController elin, Dictionary<PlayerController, IActorInput> brains)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-1.1f, 0.5f), new Vector2(1.1f, 0.5f));
            foreach (var pair in brains) pair.Key.Input = pair.Value;
            yield return MWait(1.2f);
            var hitList = new List<string>();
            var teles = new List<Telegraph>();
            foreach (var m in new[] { bron, elin })
            {
                var t = Telegraph.Circle(null, m.Position, 1.5f, 1.6f, 1, 0f);
                t.OnResolve += (tele, hit) => { foreach (var h in hit) hitList.Add(h.DisplayName); };
                teles.Add(t);
            }
            var startPos = new[] { bron.Position, elin.Position };
            yield return MWait(0.8f);
            yield return Shot("mon_26_companion_dodge");
            yield return MWait(1.0f);
            bool bronOut = !hitList.Contains(bron.DisplayName), elinOut = !hitList.Contains(elin.DisplayName);
            MCheck($"companions dodge telegraphs: bron escaped={bronOut} ({Vector2.Distance(startPos[0], bron.Position):0.0} moved) elin escaped={elinOut} ({Vector2.Distance(startPos[1], elin.Position):0.0} moved) hit=[{string.Join(",", hitList)}]",
                bronOut || elinOut);
            foreach (var m in new[] { bron, elin }) m.Input = new ScriptedInput();
        }

        IEnumerator UnblockableChecks(PlayerController local)
        {
            MonClear();
            yield return MWait(0.3f);
            int block = local.Data.Stats.Block;
            int seed = -1;
            for (int s = 1; s < 5000 && block > 0; s++)
            {
                UnityEngine.Random.InitState(s);
                if (UnityEngine.Random.Range(0, 100) < block) { seed = s; break; }
            }
            yield return MWaitUntil(() => !local.Health.IsInvulnerable, 3f);
            bool blocked = false, landed = false;
            if (seed > 0)
            {
                UnityEngine.Random.InitState(seed);
                blocked = !local.TakeDamage(new DamageInfo(10, local.Position + Vector2.right, 0f, Team.Enemy));
                yield return MWaitUntil(() => !local.Health.IsInvulnerable, 3f);
                UnityEngine.Random.InitState(seed);
                landed = local.TakeDamage(new DamageInfo(10, local.Position + Vector2.right, 0f, Team.Enemy) { unblockable = true });
            }
            UnityEngine.Random.InitState(Environment.TickCount);
            MCheck($"unblockable skips the block roll: block={block}% seed={seed} normal blocked={blocked} unblockable landed={landed}", blocked && landed);
            yield return MWait(0.5f);
        }
    }
}
