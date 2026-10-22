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
        int miniPassed, miniFailed;

        void MiniCheck(string name, bool ok)
        {
            if (ok) miniPassed++; else miniFailed++;
            Log("MINI " + (ok ? "PASS " : "FAIL ") + name);
        }

        static string MiniInventorySnapshot() => string.Join("|", Game.Session.Inventory.ToList()
            .OrderBy(s => s.id).Select(s => s.id + ":" + s.count));

        static EnemyController[] MiniEnemies() => EnemyController.Active
            .Where(e => e != null && !e.IsDead && e.isActiveAndEnabled).ToArray();

        /// <summary>Interactive local preview; -dotrpgMiniDemo redirects saves before scene load.</summary>
        IEnumerator MiniDemoRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1f);
            Game.Config.autosave = false;
            SkillCaster.NoCooldown = false;
            var profiles = new[]
            {
                (level: 10, cls: CharacterClass.Warrior, career: Career.None, name: "Lv.10 숲 수련"),
                (level: 20, cls: CharacterClass.Warrior, career: Career.Fighter, name: "Lv.20 석실 수련"),
                (level: 30, cls: CharacterClass.Mage, career: Career.Arcanist, name: "Lv.30 서리 수련"),
                (level: 39, cls: CharacterClass.Mage, career: Career.Bishop, name: "Lv.39 서리 수련"),
            };
            for (int slot = 0; slot < profiles.Length; slot++)
            {
                if (Game.Saves.HasSave(slot)) { Log("kept mini preview slot " + slot); continue; }
                SaveSystem.ActiveSlot = slot;
                var d = profiles[slot];
                Game.Flow.NewGame(d.cls, d.name);
                yield return Wait(.15f);
                while (Game.Flow.IsTransitioning) yield return null;
                var progression = Game.Session.Progression;
                progression.SetFromServer(d.level, 0);
                if (d.career != Career.None)
                {
                    progression.Promote(d.career);
                    foreach (var skill in CareerCatalog.For(d.career))
                        if (skill.kind != CareerSkillKind.Awakening && skill.level <= d.level) progression.Learn(skill.id);
                    int key = 0;
                    foreach (var skill in CareerCatalog.For(d.career))
                        if (key < 4 && skill.kind == CareerSkillKind.Active && progression.Rank(skill.id) > 0 && progression.EquipSkill(key, skill.id)) key++;
                }
                int gearTier = 1;
                foreach (int tier in new[] { 1, 10, 15, 20, 25, 30, 35 }) if (tier <= d.level) gearTier = tier;
                bool warrior = d.cls == CharacterClass.Warrior;
                foreach (string gear in new[]
                {
                    (warrior ? "eq_sword_" : "eq_staff_") + gearTier + "_r",
                    (warrior ? "eq_plate_" : "eq_robe_") + gearTier + "_r",
                    (warrior ? "eq_greaves_" : "eq_skirt_") + gearTier + "_r",
                    "eq_neck_" + gearTier + "_r", "eq_ring_" + gearTier + "_r",
                })
                {
                    Game.Session.Inventory.Add(gear, 1);
                    if (!Game.Session.Equipment.Equip(gear, d.cls)) throw new InvalidOperationException("Mini preview equipment rejected: " + gear);
                }
                Game.Session.MiniDungeonProgress.Reset();
                Game.Player.HealFull();
                var save = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
                if (!Game.Saves.Write(save, slot)) throw new IOException("Could not write mini preview slot " + slot);
                var read = Game.Saves.Read(slot);
                var restored = new Progression(); restored.Restore(read, d.cls);
                if (restored.Level != d.level || restored.Career != d.career || read.miniDungeon == null || read.miniDungeon.usedSeconds != 0)
                    throw new InvalidOperationException("Invalid mini preview slot " + slot);
                Log("created and verified mini preview slot " + slot + ": " + d.name + " " + d.career);
            }
            Game.Config.autosave = true;
            Game.Flow.ReturnToTitle(); yield return Wait(.2f);
            while (Game.Flow.IsTransitioning) yield return null;
            SaveSystem.ActiveSlot = 0;
            Game.UI.Hud.ClearToasts();
            Game.UI.Slots.Open(true);
            yield return Wait(.3f);
            yield return Shot("00-mini-preview-character-select");
            Log("mini preview ready: four separate characters, normal cooldowns and real daily allowance");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-miniDemoVerify") >= 0) Application.Quit();
        }

        void MiniBudgetChecks(DateTime now)
        {
            // [CH3 step 5] Tier 40 (Lv.40~49, 10 minutes) exists in the data; it is entered only while chapter 3 is open.
            MiniCheck("four ten-level entry bands", MiniDungeonCatalog.All.Length == 4 &&
                MiniDungeonCatalog.All.Select(d => d.Tier).SequenceEqual(new[] { 10, 20, 30, 40 }) &&
                MiniDungeonCatalog.All.All(d => d.MinLevel == d.Tier && d.MaxLevel == d.Tier + 9));
            var budget = new MiniDungeonProgress();
            MiniCheck("one shared 1800-second daily allowance", budget.RemainingSeconds(now) == 1800);
            budget.Consume(120, now);
            MiniCheck("spent time subtracts from allowance", budget.RemainingSeconds(now) == 1680);
            budget.Consume(-300, now); budget.Consume(float.NaN, now); budget.Consume(float.PositiveInfinity, now);
            MiniCheck("invalid duration cannot increase allowance", budget.RemainingSeconds(now) == 1680);
            var saved = budget.Capture(now);
            var restored = new MiniDungeonProgress(); restored.Restore(saved, now);
            MiniCheck("usage survives capture and restore", restored.RemainingSeconds(now) == 1680);
            MiniCheck("reentry uses remaining daily time", restored.Begin(now, 1800, MapRegistry.Canyon, new Vector2(9, 7), Facing.Left));
            MiniCheck("entry cannot be duplicated", !restored.Begin(now, 1800, MapRegistry.Village, Vector2.zero, Facing.Down));
            MiniCheck("active wall-clock consumption", restored.RemainingSeconds(now.AddSeconds(60)) == 1620);
            MiniCheck("return map, position and facing retained", restored.ReturnMap == MapRegistry.Canyon &&
                restored.ReturnPosition == new Vector2(9, 7) && restored.ReturnFacing == Facing.Left);
            saved = restored.Capture(now.AddSeconds(60));
            var interrupted = new MiniDungeonProgress(); interrupted.Restore(saved, now.AddSeconds(90));
            MiniCheck("interrupted session accounts for elapsed time and ends", !interrupted.Active && interrupted.RemainingSeconds(now.AddSeconds(90)) == 1590);
            interrupted.Consume(10000, now.AddSeconds(90));
            MiniCheck("budget cannot become negative", interrupted.RemainingSeconds(now.AddSeconds(90)) == 0);
            MiniCheck("exhausted allowance blocks reentry", !interrupted.Begin(now.AddSeconds(90), 1800, MapRegistry.Village, Vector2.zero, Facing.Down));
            MiniCheck("invalid entry allowance is rejected", !interrupted.Begin(now.AddSeconds(90), float.NaN, MapRegistry.Village, Vector2.zero, Facing.Down));
            MiniCheck("earlier clock cannot reset spent allowance", interrupted.RemainingSeconds(now.AddDays(-1)) == 0);
            DateTime reset = MiniDungeonProgress.NextResetUtc(now);
            MiniCheck("Korea 06:00 reset is UTC 21:00", reset.Hour == 21);
            MiniCheck("one second before reset remains exhausted", interrupted.RemainingSeconds(reset.AddSeconds(-1)) == 0);
            MiniCheck("daily reset replenishes allowance", interrupted.RemainingSeconds(reset) == 1800);
            var nearReset = new MiniDungeonProgress();
            MiniCheck("near-reset entry starts", nearReset.Begin(reset.AddSeconds(-10), 1800, MapRegistry.Village, Vector2.zero, Facing.Down));
            MiniCheck("run ends at daily reset boundary", nearReset.DeadlineUtc == reset);
            nearReset.Refresh(reset);
            MiniCheck("old-day run ends and new-day time remains available", !nearReset.Active && nearReset.RemainingSeconds(reset) == 1800);
            foreach (var d in MiniDungeonCatalog.All)
            {
                string layout = MiniDungeonCatalog.Layout(d.MapId);
                MiniCheck(d.MapId + " is a separate instance", MapRegistry.Get(d.MapId)?.instanced == true && !WorldRoutes.Neighbors(d.MapId).Any());
                MiniCheck(d.MapId + " six spawn courts", layout != null && layout.Count(c => c >= '1' && c <= '6') == MiniDungeonCatalog.SpawnCount);
                MiniCheck(d.MapId + " catch-up XP is ten times the normal rate", MiniDungeonCatalog.XpPerKill(d.MinLevel, true) >= MiniDungeonCatalog.XpPerKill(d.MinLevel) * (MiniDungeonCatalog.CatchUpMultiplier - 1));
            }
        }

        IEnumerator MiniDungeonRun()
        {
            miniPassed = miniFailed = 0;
            yield return Wait(1f);
            ApplyRequestedResolution(); yield return Wait(.25f);
            bool autosave = Game.Config.autosave;
            var originalClock = MiniDungeonDirector.UtcNow;
            DateTime clock = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc);
            MiniDungeonDirector.UtcNow = () => clock;
            Game.Config.autosave = false;
            AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0);
            try
            {
                MiniBudgetChecks(clock);
                Game.Flow.NewGame(CharacterClass.Warrior, "미니던전 검증");
                yield return Wait(1.5f);
                MiniCheck("verification is silent before entering", AudioListener.volume == 0);
                var director = Game.MiniDungeon;
                if (director == null) throw new InvalidOperationException("Mini dungeon director was not created.");
                director.Refresh();
                MiniCheck("local allowance ready", director.Ready && !director.Busy);
                foreach (int level in new[] { 1, 9, 10, 19, 20, 29, 30, 39, 40 })
                {
                    Game.Session.Progression.SetFromServer(level, 0);
                    foreach (var d in MiniDungeonCatalog.All)
                        MiniCheck("Lv." + level + " tier " + d.Tier + " gate",
                            director.CanEnter(d.Tier, out _) == (level >= d.MinLevel && level <= d.MaxLevel && level < Progression.LevelCap && MiniDungeonCatalog.Visible(d)));
                }
                Game.Session.Progression.SetFromServer(10, 0);
                Game.UI.Hud.ClearToasts();
                Game.UI.MiniDungeon.Open(); yield return Wait(.2f);
                yield return Shot("01-mini-dungeon-entry");
                Game.UI.ApplyUiScale(3); yield return Wait(.2f);
                var visibleButtons = Game.UI.MiniDungeon.GetComponentsInChildren<UnityEngine.UI.Button>();
                MiniCheck("entry controls remain inside the screen at 130 percent UI scale", visibleButtons.All(b =>
                {
                    var corners = new Vector3[4]; ((RectTransform)b.transform).GetWorldCorners(corners);
                    return corners.All(c => c.x >= -.5f && c.y >= -.5f && c.x <= Screen.width + .5f && c.y <= Screen.height + .5f);
                }));
                yield return Shot("01-mini-dungeon-entry-ui130");
                Game.UI.ApplyUiScale(Game.Settings.Data.uiScale); yield return Wait(.2f);
                string returnMap = Game.World.MapId;
                Vector2 returnPosition = Game.Player.Position;
                Facing returnFacing = Game.Player.Facing;
                var manual = new ScriptedInput(); Game.Player.Input = manual;
                Game.Player.Health.SetInvulnerable(120);
                string inventory = MiniInventorySnapshot();
                MiniCheck("entry starts tier 10", director.Enter(10));
                yield return Wait(.8f);
                MiniCheck("entry closes the selection menu", !Game.UI.MiniDungeon.gameObject.activeSelf && Game.State.Current == GameState.Playing);
                MiniCheck("private training map loaded", director.Active && Game.World.MapId == MiniDungeonCatalog.Get(10).MapId);
                MiniCheck("automatic hunting starts on entry", QuestAutoPilot.Hunting);
                float spawnDeadline = Time.realtimeSinceStartup + 5f;
                while (MiniEnemies().Length == 0 && Time.realtimeSinceStartup < spawnDeadline) yield return null;
                MiniCheck("training monsters spawn without an entry server", MiniEnemies().Length > 0);
                MiniCheck("spawn and every enemy court are walkable", Game.World.IsFree(Game.Player.Position) && MiniEnemies().All(e => Game.World.IsFree(e.Position)));
                int xp = TotalXp(), kills = director.Kills;
                var victim = MiniEnemies().OrderByDescending(e => Vector2.Distance(e.Position, Game.Player.Position)).FirstOrDefault();
                if (victim != null) victim.TakeDamage(new DamageInfo(999999, victim.Position, 0, Team.Player, Game.Player.gameObject));
                yield return Wait(.3f);
                int expected = MiniDungeonCatalog.XpPerKill(10, director.CatchingUp);
                MiniCheck("real kill grants the entry-level XP amount (catch-up aware)", TotalXp() - xp == expected);
                MiniCheck("kill counter and XP recap update", director.Kills == kills + 1 && director.EarnedXp >= expected);
                MiniCheck("XP-only kill creates no pickups", FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length == 0 && inventory == MiniInventorySnapshot());
                kills = director.Kills; xp = TotalXp();
                director.CreditKill(victim); director.CreditKill(victim);
                MiniCheck("one monster cannot grant XP twice", director.Kills == kills && TotalXp() == xp);
                int entryXp = director.XpPerKill;
                Game.Session.Progression.SetFromServer(20, 0);
                MiniCheck("crossing a level band does not reset the active run", director.Active && director.Tier == 10 && director.XpPerKill == entryXp);
                Game.Session.Progression.SetFromServer(10, 0);
                kills = director.Kills;
                float deadline = Time.realtimeSinceStartup + 22f;
                while (director.Active && director.Kills == kills && Time.realtimeSinceStartup < deadline) yield return null;
                MiniCheck("warrior auto input moves and defeats another monster", director.Kills > kills);
                Game.UI.Hud.ClearToasts();
                yield return Shot("02-mini-dungeon-warrior");
                Game.UI.ApplyUiScale(3); yield return Wait(.15f);
                yield return Shot("02-mini-dungeon-warrior-ui130");
                Game.UI.ApplyUiScale(Game.Settings.Data.uiScale); yield return Wait(.15f);
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    RenderRegion(Path.Combine(folder, "mini-dungeon-forest-overview.png"), Game.World.Bounds, 32);

                Game.UI.MiniDungeon.Open(); yield return Wait(.2f);
                MiniCheck("open menu keeps world simulation running", Game.State.Current == GameState.Inventory && Time.timeScale == 1);
                kills = director.Kills;
                deadline = Time.realtimeSinceStartup + 22f;
                while (director.Active && director.Kills == kills && Time.realtimeSinceStartup < deadline) yield return null;
                MiniCheck("automatic combat continues behind menu", director.Kills > kills);
                float remaining = director.RemainingSeconds;
                clock = clock.AddSeconds(7); yield return Wait(.1f);
                MiniCheck("menu does not freeze daily timer", Mathf.Abs(director.RemainingSeconds - (remaining - 7)) < .2f);
                Game.Flow.CloseInventory(); yield return Wait(.2f);
                manual.Move = Vector2.up; yield return Wait(.15f); manual.Move = Vector2.zero;
                MiniCheck("manual movement stops automatic hunting", !QuestAutoPilot.Hunting);
                QuestAutoPilot.ToggleHunt(); yield return Wait(.2f);
                MiniCheck("HUD command can restart automatic hunting", QuestAutoPilot.Hunting);
                remaining = director.RemainingSeconds;
                clock = clock.AddSeconds(13); yield return Wait(.1f);
                director.Leave(); yield return Wait(.7f);
                MiniCheck("leave restores original map", !director.Active && Game.World.MapId == returnMap);
                MiniCheck("leave restores exact safe location and facing", Vector2.Distance(Game.Player.Position, returnPosition) < .1f && Game.Player.Facing == returnFacing);
                MiniCheck("leave restores previous input", ReferenceEquals(Game.Player.Input, manual));
                MiniCheck("no currency or item rewards across auto combat", inventory == MiniInventorySnapshot());
                MiniCheck("partial session preserves remaining time", Mathf.Abs(director.RemainingSeconds - (remaining - 13)) < .2f);
                var save = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
                var reload = new GameSession(); reload.Restore(save, Game.Config);
                MiniCheck("save retains spent daily allowance", Mathf.Abs(reload.MiniDungeonProgress.RemainingSeconds(clock) - director.RemainingSeconds) < .2f);
                MiniCheck("reentry succeeds without another 30-minute allowance", director.Enter(10));
                yield return Wait(.6f);
                MiniCheck("reentry retains already spent time", Mathf.Abs(director.RemainingSeconds - (remaining - 13)) < .2f);
                clock = clock.AddSeconds(director.RemainingSeconds + 1); yield return Wait(.8f);
                MiniCheck("expiry automatically exits", !director.Active && Game.World.MapId == returnMap);
                MiniCheck("exhausted daily allowance blocks entry", director.RemainingSeconds == 0 && !director.CanEnter(10, out _) && !director.Enter(10));

                clock = MiniDungeonProgress.NextResetUtc(clock).AddSeconds(1); director.Refresh();
                MiniCheck("next day unlocks the full allowance", director.RemainingSeconds == 1800 && director.CanEnter(10, out _));
                foreach (int tier in new[] { 20, 30 })
                {
                    Game.Session.Progression.SetFromServer(tier, 0);
                    MiniCheck("tier " + tier + " starts", director.Enter(tier)); yield return Wait(2.6f);
                    MiniCheck("tier " + tier + " themed map exists and is walkable", director.Active && Game.World.MapId == MiniDungeonCatalog.Get(tier).MapId && Game.World.IsFree(Game.Player.Position));
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                        RenderRegion(Path.Combine(folder, "mini-dungeon-" + tier + "-overview.png"), Game.World.Bounds, 32);
                    director.Leave(); yield return Wait(.6f);
                }
                Game.Flow.NewGame(CharacterClass.Mage, "마법사 미니던전 검증"); yield return Wait(1f);
                Game.Session.Progression.SetFromServer(10, 0); Game.Player.Health.SetInvulnerable(120);
                Game.Player.Input = new ScriptedInput(); inventory = MiniInventorySnapshot();
                MiniCheck("mage entry starts", director.Enter(10)); yield return Wait(.6f);
                kills = director.Kills; deadline = Time.realtimeSinceStartup + 25f;
                while (director.Active && director.Kills == kills && Time.realtimeSinceStartup < deadline) yield return null;
                MiniCheck("mage auto input defeats a monster", director.Kills > kills && director.EarnedXp > 0);
                MiniCheck("mage also receives XP without loot", inventory == MiniInventorySnapshot() && FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length == 0);
                Game.UI.Hud.ClearToasts();
                yield return Shot("03-mini-dungeon-mage");
                director.Leave(); yield return Wait(.6f);
                MiniCheck("exit cleans training enemies", !MiniDungeonCatalog.IsMap(Game.World.MapId) && FindObjectsByType<MiniDungeonEnemy>(FindObjectsSortMode.None).Length == 0);
                MiniCheck("exit releases per-spawn stats", !Resources.FindObjectsOfTypeAll<EnemyStats>().Any(s => s != null && s.name.StartsWith("MiniTraining_", StringComparison.Ordinal)));
                MiniCheck("verification remains silent", AudioListener.volume == 0);
            }
            finally
            {
                Game.MiniDungeon?.Abort();
                MiniDungeonDirector.UtcNow = originalClock;
                Game.Config.autosave = autosave;
                Log($"MINI RESULTS: {miniPassed} passed, {miniFailed} failed");
            }
        }
    }
}
