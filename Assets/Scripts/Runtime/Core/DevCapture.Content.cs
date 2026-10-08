using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CONTENT] Dungeon content checks (appended to <c>-dotrpgDungeon</c>): themed monsters and bosses per
    /// weekday dungeon, boss bar binding and HUD layering (no overlaps at 1280x720 and 1920x1080, quest tracker
    /// hidden in dungeons), a field boss, and one full raid clear by scripted play (king phases, weekly lock).
    /// <c>-dotrpgBalance &lt;folder&gt; [-dotrpgBalanceSet normal|hero|raid]</c>: time-scaled scripted clears of
    /// every weekday dungeon (일반 Lv5 / 영웅 Lv27) and the raid (Lv27), lines "BAL ... PASS|FAIL".
    /// </summary>
    public partial class DevCapture
    {
        // ---------- Simulation tuning ----------
        /// <summary>Time scale of the scripted balance runs (game-time clear times are reported).</summary>
        const float SimTimeScale = 4f;
        /// <summary>Target clear window in game seconds (weekday dungeons, raid).</summary>
        const float DungeonMinSeconds = 120f, DungeonMaxSeconds = 300f, RaidMinSeconds = 300f, RaidMaxSeconds = 600f;
        const float SimCapSeconds = 700f;
        /// <summary>Time scale of the -dotrpgDungeon raid clear (2x keeps telegraph / wind-up timing faithful).</summary>
        const float RaidCheckTimeScale = 2f;
        /// <summary>Scripted play: drink an HP potion below this HP fraction, an MP potion below this MP fraction.</summary>
        const float SimPotionHp = 0.5f, SimPotionMp = 0.25f;
        /// <summary>Scripted play: back off below this HP fraction or when 3+ monsters crowd in.</summary>
        const float SimRetreatHp = 0.3f, SimRetreatSeconds = 1.2f, SimRetreatCooldown = 3f;
        /// <summary>Potions handed to the scripted hero (a player going into a raid carries a stack).</summary>
        const int SimHpPotions = 30, SimMpPotions = 20;
        static readonly string[] Mercs = { "merc_bron", "merc_kai", "merc_elin" };

        bool balanceOnly;
        int balPassed, balFailed;

        void BCheck(string what, bool ok)
        {
            if (ok) balPassed++;
            else balFailed++;
            log?.WriteLine($"BAL {what} {(ok ? "PASS" : "FAIL")}");
        }

        static DateTime WeekdayClock(int index) => new DateTime(2025, 3, 3 + index, 10, 0, 0);

        // =============================== Shared helpers ===============================

        static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Union of the active child graphics under <paramref name="root"/> (screen pixels).</summary>
        static Rect? GraphicsRect(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return null;
            Rect? r = null;
            foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
            {
                if (!g.enabled || g.color.a < 0.05f) continue;
                var s = ScreenRect(g.rectTransform);
                if (s.width < 1f || s.height < 1f) continue;
                r = r == null ? s : Rect.MinMaxRect(Mathf.Min(r.Value.xMin, s.xMin), Mathf.Min(r.Value.yMin, s.yMin), Mathf.Max(r.Value.xMax, s.xMax), Mathf.Max(r.Value.yMax, s.yMax));
            }
            return r;
        }

        static Transform HudChild(string name)
        {
            if (Game.UI == null || Game.UI.Hud == null) return null;
            foreach (var t in Game.UI.Hud.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>"" when the boss bar overlaps none of the other HUD blocks, else the names it touches.</summary>
        static string BossBarOverlaps()
        {
            var bar = BossHpBarView.Instance != null ? BossHpBarView.Instance.transform.Find("Bar") : null;
            var barRect = GraphicsRect(bar);
            if (barRect == null) return "noBar";
            var hits = new List<string>();
            foreach (var n in new[] { "Quest", "PartyFrames", "RoomMap", "Clock", "Minimap", "Status" })
            {
                var r = GraphicsRect(HudChild(n));
                if (r != null && r.Value.Overlaps(barRect.Value)) hits.Add(n);
            }
            return string.Join("+", hits);
        }

        void SetupHero(int level, bool goodGear)
        {
            var prog = Game.Session.Progression;
            while (prog.Level < level) prog.AddXp(prog.XpNeeded);
            var bag = Game.Session.Inventory;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item.starter || !item.UsableBy(CharacterClass.Warrior)) continue;
                if (goodGear ? item.rarity >= ItemRarity.Rare : item.rarity <= ItemRarity.Uncommon) bag.Add(item.id, 1);
            }
            Game.Session.Equipment.AutoEquip(CharacterClass.Warrior);
            int hpHave = bag.Count(ConsumableDatabase.HpPotion), mpHave = bag.Count(ConsumableDatabase.MpPotion);
            if (hpHave < SimHpPotions) bag.Add(ConsumableDatabase.HpPotion, SimHpPotions - hpHave);
            if (mpHave < SimMpPotions) bag.Add(ConsumableDatabase.MpPotion, SimMpPotions - mpHave);
            var party = Game.Party;
            foreach (var id in Mercs) if (party.Find(id) == null) party.AddCompanion(id);
            Game.Player.HealFull();
        }

        public sealed class SimResult
        {
            public bool cleared, failed;
            public float seconds;
            public int revives, maxPhase = 1;
            public DungeonRank rank;
        }

        /// <summary>
        /// Basic scripted play for the local hero until the run ends: walk to the nearest monster, basic attack,
        /// cycle the four skills, take revives, step through open gates. Companions use their own AI.
        /// </summary>
        /// <summary>Monster ids killed since the last room check (themed-monster check counts them as present).</summary>
        static readonly List<string> roomKills = new List<string>();
        static bool roomKillHooked;

        static int lastTotemRevives;
        static int KingTotemRevives() => lastTotemRevives;

        /// <summary>Shortest way out of a telegraphed area.</summary>
        static Vector2 EscapeDirection(Telegraph t, Vector2 feet)
        {
            Vector2 to = feet - t.Origin;
            switch (t.Shape)
            {
                case TelegraphShape.Donut: return to.sqrMagnitude > 0.0001f ? -to.normalized : Vector2.down; // the safe spot is the middle
                case TelegraphShape.Rect:
                    var side = new Vector2(-t.Direction.y, t.Direction.x);
                    return (Vector2.Dot(to, side) >= 0f ? side : -side).normalized;
                default: return to.sqrMagnitude > 0.0001f ? to.normalized : Vector2.down;
            }
        }

        IEnumerator ScriptedRun(SimResult res, float scale, float cap)
        {
            var dir = Game.Dungeon;
            var run = dir.Run;
            var local = Game.Player;
            var script = new ScriptedInput();
            var brain = local.Input;
            local.Input = script;
            float nextDiag = 0f;
            float nextAttack = 0f, nextSkill = 0f, doorAt = -1f, nextPotion = 0f, nextManaPotion = 0f, retreatUntil = 0f, retreatReadyAt = 0f;
            int slot = 0;
            while (run != null && !run.IsOver && run.Elapsed < cap)
            {
                if (!dir.IsBusy && Time.timeScale > 0.5f && !Mathf.Approximately(Time.timeScale, scale)) Time.timeScale = scale;
                if (dir.ReviveOpen)
                {
                    script.Move = Vector2.zero;
                    if (run.RevivesLeft > 0) dir.AcceptRevive();
                    yield return null;
                    continue;
                }
                if (dir.IsBusy || local.IsDead) { script.Move = Vector2.zero; yield return null; continue; }
                foreach (var e in EnemyController.Active)
                    if (e != null && !e.IsDead && e.IsBoss && e.Behaviour is BossBrain bb) { res.maxPhase = Mathf.Max(res.maxPhase, bb.Phase); lastTotemRevives = bb.TotemRevives; }
                // Raid diagnosis: one line every 20 game seconds (boss HP, phase, totems, adds, party alive).
                if (run.Elapsed >= nextDiag)
                {
                    nextDiag = run.Elapsed + 20f;
                    var boss = EnemyController.Active.FirstOrDefault(e => e != null && !e.IsDead && e.IsBoss);
                    var bbd = boss != null ? boss.Behaviour as BossBrain : null;
                    int adds = DungeonEnemies().Count(e => !e.IsBoss && e.Def != null && e.Def.id != MonsterDatabase.Totem);
                    int tot = DungeonEnemies().Count(e => e.Def != null && e.Def.id == MonsterDatabase.Totem);
                    int alive = Game.Party.Members.Count(m => m != null && !m.IsDead);
                    var nearest = DungeonEnemies().OrderBy(e => Vector2.Distance(e.Position, local.Position)).FirstOrDefault();
                    string nearDiag = nearest != null ? $"{nearest.Def?.id}@{Vector2.Distance(nearest.Position, local.Position):0.0}" : "none";
                    log?.WriteLine($"DIAG {run.Dungeon.id} t={run.Elapsed:0} room={run.RoomIndex} boss={(boss != null ? $"{boss.Health.Current * 100 / Mathf.Max(1, boss.Health.Max)}%" : "-")} phase={bbd?.Phase} totems={tot} adds={adds} alive={alive}/{Game.Party.Members.Count} hp={local.Health.Current}/{local.Health.Max} revivesUsed={run.RevivesUsed} nearest={nearDiag} me=({local.Position.x:0.0},{local.Position.y:0.0}) door={(dir.Door != null && dir.Door.IsOpen)} potions={Game.Session.Inventory.Count(ConsumableDatabase.HpPotion)}");
                }
                // [P1] Raid gimmick: while totems stand, take the totem the companions are NOT on so both fall together.
                var totems = DungeonEnemies().Where(e => e.Def != null && e.Def.id == MonsterDatabase.Totem).ToList();
                var target = totems.Count > 0
                    ? totems.OrderBy(e => e == CompanionBrain.PriorityTarget ? 1 : 0).ThenBy(e => Vector2.Distance(e.Position, local.Position)).First()
                    : DungeonEnemies().OrderBy(e => Vector2.Distance(e.Position, local.Position)).FirstOrDefault();
                if (target == null)
                {
                    script.Move = Vector2.zero;
                    if (dir.Door != null && dir.Door.IsOpen)
                    {
                        if (doorAt < 0f) doorAt = Time.time + 1f;
                        else if (Time.time >= doorAt) { dir.DevEnterDoor(); doorAt = -1f; }
                    }
                    yield return null;
                    continue;
                }
                doorAt = -1f;
                // Potions like a player would: HP below the threshold, MP too low for skills.
                float hpFrac = local.Health != null && local.Health.Max > 0 ? (float)local.Health.Current / local.Health.Max : 1f;
                if (hpFrac < SimPotionHp && Time.time >= nextPotion)
                {
                    nextPotion = Time.time + 0.5f;
                    local.UseHealing();
                }
                if (local.MaxMana > 0 && local.Mana < local.MaxMana * SimPotionMp && Time.time >= nextManaPotion)
                {
                    nextManaPotion = Time.time + 0.5f;
                    local.UseConsumable(ConsumableDatabase.MpPotion);
                }
                // Step out of a boss warning area like a player would (망자의 심판 cannot be dodged: it is broken by hitting).
                var danger = Telegraph.Active.FirstOrDefault(t => t != null && !t.Resolved && !t.Cancelled && t.Contains(local.Position)
                    && !(t.Owner != null && t.Owner.Behaviour is BossBrain jb && jb.CastingJudgment && t.Shape == TelegraphShape.Circle && t.Radius > 8f));
                if (danger != null)
                {
                    script.Move = EscapeDirection(danger, local.Position);
                    script.Aim = (target.Center - local.Center).normalized;
                    yield return null;
                    continue;
                }
                Vector2 to = target.Position - local.Position;
                float reach = 0.9f + 0.35f * target.Size;
                // Reposition: at low HP (potion on cooldown) or when three or more monsters crowd in, step
                // away from their centre for a moment instead of trading hits; companions keep the aggro.
                var near = DungeonEnemies().Where(e => Vector2.Distance(e.Position, local.Position) < 2.2f).ToList();
                bool wantRetreat = (hpFrac < SimRetreatHp && near.Count > 0) || near.Count >= 3;
                if (Time.time >= retreatUntil && wantRetreat && Time.time >= retreatReadyAt)
                {
                    retreatUntil = Time.time + SimRetreatSeconds;
                    retreatReadyAt = retreatUntil + SimRetreatCooldown;
                }
                if (Time.time < retreatUntil)
                {
                    Vector2 centre = near.Count > 0 ? (Vector2)near.Aggregate(Vector3.zero, (s, e) => s + (Vector3)e.Position) / near.Count : target.Position;
                    Vector2 away = local.Position - centre;
                    if (away.sqrMagnitude < 0.01f) away = Vector2.down;
                    script.Move = away.normalized;
                    script.Aim = (target.Center - local.Center).normalized;
                    // Ranged skills still fire while backing off.
                    if (Time.time >= nextSkill)
                    {
                        nextSkill = Time.time + 1.2f;
                        script.PressSkill(slot);
                        slot = (slot + 1) % 4;
                    }
                    yield return null;
                    continue;
                }
                script.Move = to.magnitude > reach ? GridPath.Steer(local.Position, target.Position) : Vector2.zero; // a player walks round the water
                script.Aim = (target.Center - local.Center).normalized;
                if (to.magnitude <= reach + 0.6f)
                {
                    if (Time.time >= nextSkill)
                    {
                        nextSkill = Time.time + 1.2f;
                        script.PressSkill(slot);
                        slot = (slot + 1) % 4;
                    }
                    else if (Time.time >= nextAttack)
                    {
                        nextAttack = Time.time + 0.25f;
                        script.PressAttack();
                    }
                }
                yield return null;
            }
            local.Input = brain;
            script.Move = Vector2.zero;
            Time.timeScale = Game.IsPlaying ? 1f : Time.timeScale;
            res.seconds = run != null ? run.Elapsed : 0f;
            res.revives = run != null ? run.RevivesUsed : 0;
            res.rank = run != null ? run.Rank : DungeonRank.F;
            res.cleared = run != null && run.State == DungeonRunState.Cleared;
            res.failed = run != null && run.State == DungeonRunState.Failed;
            if (run != null && run.State == DungeonRunState.Playing)
            {
                // Time cap: leave the run and go home.
                dir.AbortRun();
                Game.Flow.TravelTo(MapRegistry.Village);
            }
        }

        /// <summary>Waits for the result screen after a run and leaves to the village.</summary>
        IEnumerator LeaveResult()
        {
            Time.timeScale = 1f;
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && Game.UI.Top != Game.UI.DungeonResult && Game.Dungeon.InRun) yield return null;
            if (Game.UI.Top != Game.UI.DungeonResult) { yield return Wait(1.6f); yield break; }
            var result = Game.UI.DungeonResult;
            // Pick a card (refused until the stamp delay has passed), then wait for every companion flip:
            // 마을로 only works once the cards are done.
            until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && !result.DevDone)
            {
                result.PlayerPick(0);
                yield return null;
            }
            yield return Wait(0.3f);
            result.DevLeave(false);
            // Wait for the fade and the village to be loaded and playable again.
            until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && (Game.Dungeon.InRun || Game.Dungeon.IsBusy || !Game.IsPlaying || Game.World.MapId != MapRegistry.Village)) yield return null;
            yield return Wait(0.5f);
        }
    }
}
