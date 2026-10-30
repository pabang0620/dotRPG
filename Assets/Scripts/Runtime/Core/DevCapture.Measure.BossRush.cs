using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // =====================================================================================================
        // [MEASURE 2026-10-20] -measureOnly bossrush: the 3-minute 월드보스 / 길드보스 attempt, offline (no server, hp_mul 1,
        // damage_mul 1, BossRushDirector.DevEnter). Each career alone and with three AI, in two Lv40 sets: "raid" (the raid
        // measure's epic set, weapon +5) and "clear" (Tools/balance/theory_gold.py KIT at Lv40: epic weapon +11, epic armour
        // +10, rare neck / ring +8). Each row also carries the server's damage cap for the same hero (bossRushRules
        // attemptDamageCap), so the cap can be checked against what a hero really deals.
        // =====================================================================================================

        // Server policy defaults (server/src/config/env.ts POWER_*, bossRushRules DAMAGE_SLACK, starshopDefs cosmetic maxima).
        const float CapPassivePerLevel = .11f, CapSkillFactor = 2f, CapSlack = .6f, CapCosmeticPercent = 8f + 8f;

        IEnumerator MeasureBossRush(List<object> rows, float scale)
        {
            var careers = MeasureCareerList(MeasureCareers); // [MEASURE CH3] all six careers by name
            var sets = MeasureArg("-measureSets", "raid,clear").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] mercs = { "merc_bron", "merc_kai", "merc_elin" };
            foreach (string set in sets)
                foreach (var career in careers)
                    foreach (int companions in new[] { 0, 3 })
                    {
                        var cls = CareerCatalog.Base(career);
                        MeasureSeedRow($"bossrush:{set}:{career}:{BossRushCatalog.DefaultLevel}:{companions}");
                        yield return MeasureHero(cls, career, BossRushCatalog.DefaultLevel, "e");
                        if (set == "clear") MeasureClearKit(cls);
                        var party = Game.Party;
                        party.ClearCompanions();
                        for (int i = 0; i < companions && i < mercs.Length; i++) party.AddCompanion(mercs[i]);
                        yield return Wait(.5f);
                        Game.Player.HealFull();
                        var row = HRow(("set", set), ("career", MeasureCareerName(cls, career)), ("party", companions == 0 ? "solo" : "1+3ai"), ("level", BossRushCatalog.DefaultLevel));
                        yield return MeasureOneAttempt(scale, row);
                        rows.Add(row);
                        measureCheckpoint?.Invoke();
                        party.ClearCompanions();
                    }
        }

        /// <summary>The Lv40 "clear" set of theory_gold.py KIT (warrior ids; the mage wears the same grades and levels).</summary>
        void MeasureClearKit(CharacterClass cls)
        {
            var (weapon, body, legs) = MeasureGearStems(cls); // [ROGUE] dagger + plate + greaves
            var bag = Game.Session.Inventory;
            foreach (var gear in new[]
            {
                weapon + "40_e+11", body + "40_e+10",
                legs + "40_e+10", "eq_neck_40_r+8", "eq_ring_40_r+8",
            })
            {
                if (bag.Count(gear) == 0) bag.Add(gear, 1);
                if (!Game.Session.Equipment.Equip(gear, cls)) Log("measure gear not worn: " + gear);
            }
            Game.Player.HealFull();
        }

        IEnumerator MeasureOneAttempt(float scale, Dictionary<string, object> row)
        {
            MeasureReseed();
            row["seed"] = measureRowSeed;
            var dir = Game.BossRush;
            if (dir == null || !dir.DevEnter(BossRushClient.World))
            {
                row["result"] = "entry_failed";
                Log("bossrush: entry failed");
                yield break;
            }
            float guard = Time.realtimeSinceStartup + 120f;
            while (dir.Busy && Time.realtimeSinceStartup < guard) yield return null;
            QuestAutoPilot.MeasureSkilled = true;
            QuestAutoPilot.MeasureDodge = MeasureArg("-measureDodge", "1") != "0";
            QuestAutoPilot.ResetMeasureTrace();
            var tally = new MeasureTally();
            int[] kunaiStart = (int[])CareerCombat.KunaiStats.Clone();
            float firstDamageSeconds = -1f, nextTraceSeconds = 30f;
            float measureStarted = Time.time;
            Time.timeScale = scale;
            guard = Time.realtimeSinceStartup + 900f;
            while (dir.Active && !dir.Busy && Time.realtimeSinceStartup < guard)
            {
                tally.Track();
                // DevFightSeconds is populated only when an attempt ends, so it cannot drive live traces.
                float elapsed = Time.time - measureStarted;
                if (firstDamageSeconds < 0f && dir.MyDamage > 0) firstDamageSeconds = elapsed;
                if (elapsed >= nextTraceSeconds)
                {
                    nextTraceSeconds += 30f;
                    var trace = HRow(("elapsed_s", R1(elapsed)), ("damage", dir.MyDamage), ("hits", dir.Hits));
                    MeasureTraceRow(trace);
                    Log("bossrush trace " + MiniJson.Write(trace));
                }
                MeasureScale(scale);
                if (Game.Player != null && !Game.Player.IsDead) MeasureEnsureHunting();
                yield return null;
            }
            Time.timeScale = 1f;
            QuestAutoPilot.Stop("");
            QuestAutoPilot.MeasureSkilled = false;
            QuestAutoPilot.MeasureDodge = false;
            tally.Close();
            if (dir.Active && !dir.Busy) { Log("bossrush: attempt did not end, leaving"); dir.Leave(); }
            // Back in the village before the next hero.
            guard = Time.realtimeSinceStartup + 60f;
            while ((dir.Active || dir.Busy) && Time.realtimeSinceStartup < guard) yield return null;
            while (Game.Flow.IsTransitioning) yield return null;
            if (Game.UI.Top != null) Game.Flow.CloseInventory();

            var worn = WornKeys();
            float secs = Mathf.Max(1f, dir.DevFightSeconds);
            long cap = ServerAttemptCap(BossRushCatalog.DefaultLevel, worn, secs);
            long cap180 = ServerAttemptCap(BossRushCatalog.DefaultLevel, worn, BossRushClient.DefaultAttemptSeconds);
            row["result"] = dir.DevEndReason ?? "unknown";
            row["fight_s"] = R1(dir.DevFightSeconds);
            row["damage"] = dir.MyDamage;
            row["hits"] = dir.Hits;
            row["dps"] = R1(dir.MyDamage / secs);
            row["deaths"] = dir.DevEndReason == "death" ? 1 : 0;
            row["potions"] = tally.potions;
            row["damage_taken"] = Mathf.RoundToInt(tally.damage);
            row["server_cap"] = cap;
            row["server_cap_180"] = cap180;
            row["cap_use"] = cap > 0 ? R2((float)dir.MyDamage / cap) : 0f;
            row["worn"] = string.Join(" ", worn);
            row["first_damage_s"] = R2(firstDamageSeconds);
            row["kunai_body"] = KunaiDelta(kunaiStart, 0);
            row["kunai_clone"] = KunaiDelta(kunaiStart, 5);
            row["dodges"] = QuestAutoPilot.MeasureDodges;
            row["cover_moves"] = QuestAutoPilot.MeasureCoverMoves;
            MeasureTraceRow(row);
            Log($"bossrush {row["set"]} {row["career"]} {row["party"]}: {row["result"]} {dir.DevFightSeconds:0}s damage {dir.MyDamage:N0} hits {dir.Hits} dps {dir.MyDamage / secs:0} cap {cap:N0} (180s {cap180:N0}) use {(cap > 0 ? 100f * dir.MyDamage / cap : 0f):0}% potions {tally.potions}");
        }

        static List<string> WornKeys()
        {
            var eq = Game.Session.Equipment;
            var keys = new List<string>();
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
                if (!string.IsNullOrEmpty(eq[slot])) keys.Add(eq[slot]);
            return keys;
        }

        /// <summary>
        /// The server's cap for one attempt (bossRushService.damageCap): attackCap(level, worn) / attackCooldown x skill
        /// factor x seconds x slack. attackCap = (base attack + gear attack) x (1 + passive x (level - 1)) x (1 + cosmetic %);
        /// gear attack per key is enhance.json's statsAttack of its level (= StatsAt(level + 1)), past the table one more step.
        /// </summary>
        static long ServerAttemptCap(int level, List<string> worn, float seconds)
        {
            var ps = Game.Config.playerStats;
            float gear = 0f;
            foreach (var key in worn)
            {
                var item = EquipmentDatabase.Get(key);
                if (item == null) continue;
                int lv = EquipmentDatabase.LevelOfKey(key);
                if (lv < EquipmentDatabase.MaxEnhance) gear += EquipmentDatabase.StatsAt(item, lv + 1).attack;
                else
                {
                    int last = EquipmentDatabase.StatsAt(item, EquipmentDatabase.MaxEnhance).attack;
                    int prev = EquipmentDatabase.StatsAt(item, EquipmentDatabase.MaxEnhance - 1).attack;
                    gear += last + Mathf.Max(0, last - prev);
                }
            }
            GearSets.Totals(worn, out int setAttack, out _, out int setFinal); // [SET] killRules.attackCap: ceil(gear x (1 + set %)) x (1 + final %)
            if (setAttack > 0) gear = Mathf.Ceil(gear * (1f + setAttack / 100f));
            double attackCap = (ps.attackDamage + gear) * (1 + CapPassivePerLevel * (level - 1)) * (1 + CapCosmeticPercent / 100f) * (1 + setFinal / 100.0);
            double dps = attackCap / ps.attackCooldown * CapSkillFactor;
            double t = Math.Max(1, Math.Min(BossRushClient.DefaultAttemptSeconds, seconds));
            return (long)Math.Floor(dps * t * CapSlack);
        }
    }
}
