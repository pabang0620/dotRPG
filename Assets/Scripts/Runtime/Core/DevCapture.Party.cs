using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY] <c>-dotrpgParty &lt;folder&gt;</c>: party foundation checks. A mage hires three mercenaries in the
    /// 파티 window, travels to the forest, fights staged skeletons for about 40 seconds (the local player is
    /// scripted, companions use their AI), then deterministic checks: war-cry threat switch, local-only
    /// pickups, save / continue with the roster, and solo play afterwards. Report lines "PARTY &lt;what&gt;
    /// PASS|FAIL" and "PARTY summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        bool partyOnly;
        int partyPassed, partyFailed;

        const float FightSeconds = 40f;
        static readonly Vector2 FightSpot = new Vector2(15.5f, 25.5f);

        void PCheck(string what, bool ok)
        {
            if (ok) partyPassed++;
            else partyFailed++;
            log?.WriteLine($"PARTY {what} {(ok ? "PASS" : "FAIL")}");
        }

        static int TotalXp(Progression p)
        {
            int total = p.Xp;
            for (int l = 1; l < p.Level; l++) total += Progression.XpToNext(l);
            return total;
        }

        static List<EnemyController> AliveEnemies() =>
            EnemyController.Active.Where(e => e != null && !e.IsDead && e.isActiveAndEnabled).ToList();

        static void MoveEnemy(EnemyController e, Vector2 p)
        {
            var rb = e.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = p;
            e.transform.position = p;
        }

        /// <summary>Keeps at least <paramref name="want"/> skeletons around <paramref name="center"/> (pulls live ones in, spawns extras).</summary>
        static void Restock(Vector2 center, int want)
        {
            var alive = AliveEnemies();
            int near = alive.Count(e => Vector2.Distance(e.Position, center) < 9f);
            foreach (var e in alive)
            {
                if (near >= want) break;
                if (Vector2.Distance(e.Position, center) < 9f) continue;
                MoveEnemy(e, center + new Vector2(UnityEngine.Random.Range(3.5f, 6f), UnityEngine.Random.Range(-1.5f, 1.5f)));
                near++;
            }
            for (; near < want; near++)
            {
                Vector2 p = center + new Vector2(UnityEngine.Random.Range(3.5f, 6f), UnityEngine.Random.Range(-1.5f, 1.5f));
                if (!Game.World.IsFree(p)) p = center + Vector2.right * 4f;
                EnemyController.Create(Game.Config.skeletonStats, CharacterLook.Skeleton, p, Game.World.ObjectsRoot);
            }
        }

        IEnumerator PartyRun()
        {
            partyPassed = partyFailed = 0;
            bool autosave = Game.Config.autosave;
            Game.Config.autosave = false;
            yield return Wait(1.5f);
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.6f);
            var party = Game.Party;
            var local = Game.Player;
            var prog = Game.Session.Progression;
            PCheck($"solo start: members={party.Count} local==Game.Player={party.Local == Game.Player} leader==local={party.Leader == local}",
                party.Count == 1 && party.Local == Game.Player && party.Leader == local && !party.HasCompanions);
            prog.AddXp(2600); // Lv10: all skill slots open, band-2 mercenary gear
            yield return Wait(0.5f);

            // ---------- 파티 window ----------
            Game.Flow.OpenWindow(Game.UI.Party);
            yield return Wait(0.6f);
            PCheck($"party window open: state={Game.State.Current} active={Game.UI.Party.gameObject.activeInHierarchy}",
                Game.State.Current == GameState.Inventory && Game.UI.Party.gameObject.activeInHierarchy);
            yield return Shot("party_01_window_empty");
            bool addBron = Game.UI.Party.DevToggle("merc_bron");
            bool addKai = Game.UI.Party.DevToggle("merc_kai");
            bool addElin = Game.UI.Party.DevToggle("merc_elin");
            bool addSera = Game.UI.Party.DevToggle("merc_sera");
            var roster = Game.Session.PartyRoster;
            PCheck($"hire 3 (4th refused): bron={addBron} kai={addKai} elin={addElin} sera={addSera} members={party.Count} roster=[{string.Join(",", roster)}] seraButton='{Game.UI.Party.DevCardButton("merc_sera")}'",
                addBron && addKai && addElin && !addSera && party.Count == PartyManager.MaxMembers && roster.Count == 3 && Game.UI.Party.DevCardButton("merc_sera") == "자리 없음");
            yield return Wait(0.5f);
            yield return Shot("party_02_window_full");
            bool removed = Game.UI.Party.DevToggle("merc_kai");
            int afterRemove = party.Count;
            bool readded = Game.UI.Party.DevToggle("merc_kai");
            PCheck($"dismiss + rehire kai: removed={removed} count={afterRemove} readded={readded} count={party.Count} kaiObject={(party.Find("merc_kai") != null)}",
                removed && afterRemove == 3 && readded && party.Count == 4 && party.Find("merc_kai") != null);
            Game.Flow.CloseInventory();
            yield return Wait(0.5f);

            var bron = party.Find("merc_bron");
            var kai = party.Find("merc_kai");
            var elin = party.Find("merc_elin");
            var mates = new[] { bron, kai, elin };
            PCheck($"companions spawned beside the player: dist=[{string.Join(",", mates.Select(m => Vector2.Distance(m.Position, local.Position).ToString("0.0")))}] levels=[{string.Join(",", mates.Select(m => m.Data.Level))}] local Lv{prog.Level}",
                mates.All(m => Vector2.Distance(m.Position, local.Position) < 3f && m.Data.Level == prog.Level && !m.IsLocal));
            var looks = mates.Select(m => m.Data.Look.id).Distinct().Count();
            PCheck($"distinct looks={looks} classes=[{string.Join(",", mates.Select(m => m.Class))}] weapons=[{string.Join(",", mates.Select(m => m.Data.Equipment[EquipSlot.Weapon]))}]",
                looks == 3 && bron.Class == CharacterClass.Warrior && elin.Class == CharacterClass.Mage);
            float scale = MercenaryDatabase.DamageScale;
            int elinAtk = elin.Data.Stats.AttackDamage(CharacterClass.Mage);
            int sameGear = Mathf.Max(1, Mathf.RoundToInt((CharacterClassInfo.Get(CharacterClass.Mage).damage + elin.Data.Equipment.AttackBonus) * (1f + elin.Data.Stats.IncDamage / 100f)));
            PCheck($"companion damage scale={scale} elin atk={elinAtk} vs same-gear player {sameGear} ({elinAtk * 100 / Mathf.Max(1, sameGear)}%) local atk={CharacterStats.AttackDamage(local.Class)}",
                scale >= 0.6f && scale <= 0.7f && Mathf.Abs(elinAtk - sameGear * scale) <= 1f);
            PCheck($"skill slots by level: bron open={string.Join("", Enumerable.Range(0, 5).Select(s => bron.Data.Progression.IsSlotOpen(s) ? "1" : "0"))} supports={string.Join("/", bron.Data.Progression.Supports(0).Select(g => g.id))}",
                bron.Data.Progression.IsSlotOpen(3) && bron.Data.Progression.Active(3)?.id == "cry");
            PCheck($"static CharacterStats = local member: maxHp {CharacterStats.MaxHp}=={local.Data.Stats.MaxHp} mp {CharacterStats.MaxMp}=={local.MaxMana} session mana mirrors={Mathf.Approximately(Game.Session.PlayerMana, local.Data.Mana)}",
                CharacterStats.MaxHp == local.Data.Stats.MaxHp && CharacterStats.MaxMp == local.MaxMana && Mathf.Approximately(Game.Session.PlayerMana, local.Data.Mana));

            // ---------- Travel ----------
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(2.6f);
            PCheck($"4 members follow across travel: map={Game.World.MapId} dist=[{string.Join(",", mates.Select(m => m != null ? Vector2.Distance(m.Position, local.Position).ToString("0.0") : "gone"))}]",
                Game.World.MapId == MapRegistry.Forest && party.Count == 4 && mates.All(m => m != null && m.gameObject.activeInHierarchy && Vector2.Distance(m.Position, local.Position) < 4f));
            var frames = FindAnyObjectByType<PartyFramesView>();
            PCheck($"party frames shown: {(frames != null ? frames.DevVisible : -1)}", frames != null && frames.DevVisible == 3);

            // ---------- No friendly damage (direct) ----------
            int localHp = local.Health.Current, bronHp = bron.Health.Current;
            bool f1 = local.TakeDamage(new DamageInfo(50, bron.Position, 0f, Team.Enemy, bron.gameObject));
            bool f2 = bron.TakeDamage(new DamageInfo(50, local.Position, 0f, Team.Player, local.gameObject));
            bool f3 = kai.TakeDamage(new DamageInfo(50, bron.Position, 0f, Team.Enemy, bron.gameObject));
            PCheck($"members refuse member hits: {f1}/{f2}/{f3} hp {localHp}->{local.Health.Current} {bronHp}->{bron.Health.Current}",
                !f1 && !f2 && !f3 && local.Health.Current == localHp && bron.Health.Current == bronHp);

            // ---------- Local-only pickups ----------
            yield return PickupChecks(local, bron);

            // ---------- The fight ----------
            yield return StageSkeletons(FightSpot);
            yield return Wait(0.3f);
            Restock(FightSpot, 5);
            var script = new ScriptedInput();
            local.Input = script;
            int expectedXp = 0, kills = 0, friendly = 0;
            Action<EnemyController, int> onKill = (e, xp) => { expectedXp += xp; kills++; };
            EnemyController.Killed += onKill;
            var downed = new List<PlayerController>();
            var revived = new Dictionary<PlayerController, (float hp, float at)>();
            float kaiDownAt = -1f;
            Action<PlayerController> onDown = m => { downed.Add(m); if (m == kai && kaiDownAt < 0f) kaiDownAt = Time.time; };
            Action<PlayerController> onRevive = m => { if (!revived.ContainsKey(m)) revived[m] = ((float)m.Health.Current / m.Health.Max, Time.time); };
            party.MemberDowned += onDown;
            party.MemberRevived += onRevive;
            var friendlyHooks = new List<(Health h, Action<DamageInfo> a)>();
            foreach (var m in party.Members)
            {
                Action<DamageInfo> a = info => { if (info.AttackerMember != null) friendly++; };
                m.Health.Damaged += a;
                friendlyHooks.Add((m.Health, a));
            }
            party.ResetMeters();
            int xp0 = TotalXp(prog);
            bool targetedCompanion = false, companionSkill = false;
            var mpBefore = mates.ToDictionary(m => m, m => m.Data.Mana);
            float start = Time.realtimeSinceStartup, nextShot = 0f, nextAttack = 0f, nextRestock = 0f;
            bool downedKai = false;
            int shots = 0;
            while (Time.realtimeSinceStartup - start < FightSeconds)
            {
                float t = Time.realtimeSinceStartup - start;
                if (local.Health.Current < local.Health.Max * 0.6f) local.HealFull();
                foreach (var e in AliveEnemies())
                {
                    var table = e.GetComponent<ThreatTable>();
                    if (table != null && table.Current != null && !table.Current.IsLocal) targetedCompanion = true;
                }
                foreach (var m in mates) if (m != null && m.Skills.IsCasting) companionSkill = true;
                if (t >= nextAttack)
                {
                    nextAttack = t + 0.9f;
                    var target = AliveEnemies().OrderBy(e => Vector2.Distance(e.Position, local.Position)).FirstOrDefault();
                    if (target != null && Vector2.Distance(target.Position, local.Position) < 6f)
                    {
                        script.Aim = (target.Center - local.Center).normalized;
                        script.PressAttack();
                    }
                }
                if (t >= nextRestock)
                {
                    nextRestock = t + 3f;
                    Restock(local.Position, 4);
                }
                if (!downedKai && t >= 10f && !kai.IsDead)
                {
                    // Force a companion down (as if a monster had landed the blow).
                    kai.TakeDamage(new DamageInfo(99999, kai.Position + Vector2.right, 0f, Team.Enemy));
                }
                if (kai.IsDead) downedKai = true;
                if (t >= nextShot && shots < 3 && t > 6f)
                {
                    nextShot = t + 11f;
                    shots++;
                    yield return Shot($"party_0{2 + shots}_fight");
                    continue;
                }
                yield return null;
            }
            EnemyController.Killed -= onKill;
            party.MemberDowned -= onDown;
            party.MemberRevived -= onRevive;
            foreach (var (h, a) in friendlyHooks) h.Damaged -= a;
            script.Aim = Vector2.zero;

            var meters = string.Join(", ", party.DamageDealt.Select(p => $"{p.Key}={p.Value}"));
            PCheck($"companions deal damage: {meters}", party.DamageOf(bron) > 0 && party.DamageOf(kai) > 0 && party.DamageOf(elin) > 0);
            PCheck($"companions used skills: {companionSkill} mp spent=[{string.Join(",", mates.Select(m => (mpBefore[m] - m.Data.Mana).ToString("0")))}]", companionSkill);
            PCheck($"enemies targeted a companion: {targetedCompanion}", targetedCompanion);
            int gained = TotalXp(prog) - xp0;
            PCheck($"xp once per kill: kills={kills} gained={gained} expected={expectedXp} Lv{prog.Level}", kills > 0 && gained == expectedXp);
            bool kaiRevived = revived.TryGetValue(kai, out var kr);
            PCheck($"companion downed -> auto revive: downed={downed.Contains(kai)} revived={kaiRevived} hp={(kaiRevived ? kr.hp : 0f):0.00} after={(kaiRevived ? kr.at - kaiDownAt : 0f):0.0}s alive={!kai.IsDead}",
                downed.Contains(kai) && kaiRevived && Mathf.Abs(kr.hp - PartyManager.FieldReviveHp) < 0.06f && Mathf.Abs(kr.at - kaiDownAt - PartyManager.FieldReviveSeconds) < 1f);
            PCheck($"no friendly damage during the fight: memberHits={friendly} local alive={!local.IsDead}", friendly == 0 && !local.IsDead);

            // ---------- War cry threat switch ----------
            yield return WarCryChecks(party, local, bron, kai, elin);

            // ---------- Save / continue keeps the roster ----------
            local.Input = new LocalInput();
            var rosterBefore = string.Join(",", Game.Session.PartyRoster);
            Game.Config.autosave = autosave;
            Game.Flow.SaveGame();
            Game.Config.autosave = false;
            yield return Wait(0.6f);
            Game.Flow.ReturnToTitle();
            yield return Wait(1.6f);
            int companionObjects = FindObjectsByType<PlayerController>(FindObjectsInactive.Include).Count(p => !p.IsLocal);
            PCheck($"title removes companions: members={party.Count} companionObjects={companionObjects} state={Game.State.Current}",
                party.Count == 1 && companionObjects == 0 && Game.State.Current == GameState.Title);
            Game.Flow.ContinueGame();
            yield return Wait(2.2f);
            local = Game.Player;
            PCheck($"continue restores roster: [{string.Join(",", Game.Session.PartyRoster)}] (was [{rosterBefore}]) members={party.Count} near={party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 4f)} map={Game.World.MapId}",
                string.Join(",", Game.Session.PartyRoster) == rosterBefore && party.Count == 4 && party.Members.All(m => Vector2.Distance(m.Position, local.Position) < 4f));
            yield return Wait(0.5f);
            yield return Shot("party_06_continued");

            // ---------- No mercenaries = the old solo behaviour ----------
            party.ClearCompanions();
            yield return Wait(0.3f);
            frames = FindAnyObjectByType<PartyFramesView>();
            PCheck($"solo again: members={party.Count} roster={Game.Session.PartyRoster.Count} frames={(frames != null ? frames.DevVisible : -1)}",
                party.Count == 1 && Game.Session.PartyRoster.Count == 0 && frames != null && frames.DevVisible == 0);
            if (Game.World.MapId != MapRegistry.Forest)
            {
                Game.Flow.TravelTo(MapRegistry.Forest);
                yield return Wait(2.6f);
            }
            yield return StageSkeletons(FightSpot);
            Restock(FightSpot, 2);
            yield return Wait(0.5f);
            var soloEnemies = AliveEnemies();
            bool allLocal = soloEnemies.All(e => party.SelectTarget(e) == local);
            PCheck($"solo: every monster targets the local player ({soloEnemies.Count} monsters)", soloEnemies.Count > 0 && allLocal);
            var victim = soloEnemies.OrderBy(e => Vector2.Distance(e.Position, local.Position)).First();
            int soloXp0 = TotalXp(prog), soloExpected = 0;
            Action<EnemyController, int> soloKill = (e, xp) => soloExpected += xp;
            EnemyController.Killed += soloKill;
            var vh = victim.GetComponent<Health>();
            victim.TakeDamage(new DamageInfo(vh.Current + 999, local.Center, 0f, Team.Player, local.gameObject));
            yield return Wait(0.3f);
            EnemyController.Killed -= soloKill;
            PCheck($"solo kill xp: gained={TotalXp(prog) - soloXp0} expected={soloExpected}", soloExpected > 0 && TotalXp(prog) - soloXp0 == soloExpected);

            Game.Config.autosave = autosave;
            log?.WriteLine($"PARTY summary: {partyPassed} passed, {partyFailed} failed");
        }

        /// <summary>A companion standing on a drop does not collect it; the local player does.</summary>
        IEnumerator PickupChecks(PlayerController local, PlayerController bron)
        {
            var brain = bron.Input;
            bron.Input = new ScriptedInput(); // stand still for the check
            Vector2 spot = local.Position + Vector2.right * 7f;
            if (!Game.World.IsFree(spot)) spot = local.Position + Vector2.left * 7f;
            bron.Place(spot, Facing.Down);
            yield return Wait(0.2f);
            int gold = Game.Session.Gold;
            var pickup = Pickup.Create(ConsumableDatabase.Gold, 7, bron.Position, Game.World.ObjectsRoot);
            yield return Wait(1.4f);
            bool stillThere = pickup != null;
            int goldMid = Game.Session.Gold;
            local.Place(bron.Position + Vector2.down * 0.2f, Facing.Down);
            yield return Wait(1.0f);
            PCheck($"local-only pickups: companion on it -> gold {gold}->{goldMid} dropExists={stillThere}; local walks over -> {Game.Session.Gold}",
                stillThere && goldMid == gold && Game.Session.Gold == gold + 7);
            bron.Input = brain;
        }

        /// <summary>Local player hurts a monster (it turns on the player); Bron's war cry takes it; the 1.2x rule decides switching back.</summary>
        IEnumerator WarCryChecks(PartyManager party, PlayerController local, PlayerController bron, PlayerController kai, PlayerController elin)
        {
            var inputs = new Dictionary<PlayerController, IActorInput>();
            foreach (var m in new[] { bron, kai, elin }) { inputs[m] = m.Input; m.Input = new ScriptedInput(); }
            // Clear the field, then one tough skeleton 3 units right of the player.
            foreach (var e in AliveEnemies()) MoveEnemy(e, local.Position + new Vector2(30f, 30f));
            yield return Wait(0.2f);
            Vector2 p = local.Position + Vector2.right * 3f;
            if (!Game.World.IsFree(p)) p = local.Position + Vector2.left * 3f;
            var boss = EnemyController.Create(Game.Config.skeletonStats, CharacterLook.Skeleton, p, Game.World.ObjectsRoot);
            var health = boss.GetComponent<Health>();
            health.Init(20000, 20000, 0.05f);
            bron.Place(p + new Vector2(0f, 6f), Facing.Down);
            kai.Place(local.Position + new Vector2(-4f, 2f), Facing.Down);
            elin.Place(local.Position + new Vector2(-4f, -2f), Facing.Down);
            yield return Wait(0.3f);
            var table = ThreatTable.For(boss);
            boss.TakeDamage(new DamageInfo(30, local.Center, 0f, Team.Player, local.gameObject));
            yield return Wait(0.2f);
            var first = party.SelectTarget(boss);
            PCheck($"hurt monster targets its attacker: {first?.DisplayName} threat local={table.ThreatOf(local):0}", first == local);
            // War cry next to it.
            bron.Place(boss.Position + Vector2.left * 1.3f, Facing.Right);
            bron.Skills.ResetCooldowns();
            bron.Data.Mana = bron.Data.Stats.MaxMp;
            yield return Wait(0.1f);
            bron.FaceTowards(boss.Position);
            bron.Skills.TryCast(3);
            yield return Wait(0.35f);
            var afterCry = party.SelectTarget(boss);
            float bronThreat = table.ThreatOf(bron), localThreat = table.ThreatOf(local);
            PCheck($"war cry takes aggro: target={afterCry?.DisplayName} threat bron={bronThreat:0} local={localThreat:0} buff={bron.Data.BuffDamage}% localBuff={CharacterStats.BuffDamage}%",
                afterCry == bron && bronThreat >= localThreat * ThreatTable.SwitchRatio && bron.Data.BuffDamage > 0 && CharacterStats.BuffDamage == 0);
            // Step Bron out of the close-range bonus so only threat counts.
            bron.Place(boss.Position + Vector2.up * 4f, Facing.Down);
            yield return Wait(0.1f);
            // Local catches up to 1.1x of Bron's threat: not enough to switch.
            bronThreat = table.ThreatOf(bron);
            int toLow = Mathf.CeilToInt(bronThreat * 1.1f - table.ThreatOf(local));
            if (toLow > 0) boss.TakeDamage(new DamageInfo(toLow, local.Center, 0f, Team.Player, local.gameObject));
            yield return Wait(0.15f);
            var mid = party.SelectTarget(boss);
            string midLine = $"local={table.ThreatOf(local):0} bron={table.ThreatOf(bron):0} -> {mid?.DisplayName}";
            int toHigh = Mathf.CeilToInt(table.ThreatOf(bron) * 1.3f - table.ThreatOf(local));
            if (toHigh > 0) boss.TakeDamage(new DamageInfo(toHigh, local.Center, 0f, Team.Player, local.gameObject));
            yield return Wait(0.15f);
            var high = party.SelectTarget(boss);
            PCheck($"switch only at >=1.2x: 1.1x {midLine}; 1.3x local={table.ThreatOf(local):0} -> {high?.DisplayName}", mid == bron && high == local);
            // A dead member is dropped from the table.
            MoveEnemy(boss, local.Position + new Vector2(40f, -40f));
            UnityEngine.Object.Destroy(boss.gameObject);
            foreach (var pair in inputs) pair.Key.Input = pair.Value;
            yield return Wait(0.2f);
        }
    }
}
