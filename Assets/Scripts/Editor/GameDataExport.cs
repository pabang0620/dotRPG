using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [SERVER] Exports the game data the online server judges with (Docs/PLAN_SERVER.md §4) to
    /// <c>server/data/*.json</c>: items and prices, enhancement tables, monsters and drops, dungeons,
    /// skills and the passive tree, levels, maps, starter pack, quests. The C# code stays the source;
    /// <c>version.json</c> holds a hash of every file so client and server can refuse a mismatch.
    /// Menu: dotRPG ▸ Export Server Data. Batch: -executeMethod DotRPG.EditorTools.GameDataExport.Export
    /// </summary>
    public static class GameDataExport
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "server", "data"));

        [MenuItem("dotRPG/Export Server Data")]
        public static void Export()
        {
            Directory.CreateDirectory(OutDir);
            // Phase 1-2 files follow Docs/server/phase1_2_mapping.md §4 exactly; the rest are for phase 3.
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["maps.json"] = Maps(),
                ["starter.json"] = Starter(),
                ["items.json"] = Items(),
                ["passive_tree.json"] = PassiveTreeJson(),
                ["skill_gems.json"] = SkillGemsJson(),
                ["quest_index.json"] = QuestIndex(),
                ["enums.json"] = Enums(),
                ["shop.json"] = Shop(),
                ["enhance.json"] = Enhance(),
                ["monsters.json"] = Monsters(),
                ["dungeons.json"] = Dungeons(),
                ["progression.json"] = Progression(),
            };

            using (var sha = SHA256.Create())
            {
                string Hex(string text) => BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
                var all = new StringBuilder();
                foreach (var pair in files)
                {
                    File.WriteAllText(Path.Combine(OutDir, pair.Key), pair.Value, new UTF8Encoding(false));
                    all.Append(pair.Value);
                }
                string hash = Hex(all.ToString()).Substring(0, 16);
                var v = new J().Obj().Num("schema", 1).Str("version", hash).Str("exportedAt", DateTime.UtcNow.ToString("o")).Key("files").Obj();
                foreach (var pair in files) v.Str(pair.Key, Hex(pair.Value));
                v.End().End();
                File.WriteAllText(Path.Combine(OutDir, "data_version.json"), v.ToString(), new UTF8Encoding(false));
                string old = Path.Combine(OutDir, "version.json");
                if (File.Exists(old)) File.Delete(old); // replaced by data_version.json (first export of this session)
                // The game reads the same hash at runtime (X-Data-Version header).
                File.WriteAllText(Path.Combine(Application.dataPath, "Resources", "Data", "DataVersion.txt"), hash, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                Debug.Log($"[GameDataExport] {files.Count} files -> {OutDir}, dataVersion {hash}");
            }
        }

        // ---------------- builders ----------------

        static J Doc() => new J().Obj().Num("schema", 1);

        static string Items()
        {
            var rows = new List<(string id, string kind, bool stackable)>();
            foreach (var e in EquipmentDatabase.All) rows.Add((e.id, "equipment", false));
            foreach (var m in EquipmentDatabase.AllMaterials) rows.Add((m.id, "material", true));
            foreach (var id in new[] { ConsumableDatabase.Gold, ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll, ConsumableDatabase.ProtectTicket, DungeonDatabase.SealKey })
                if (ConsumableDatabase.Get(id) != null) rows.Add((id, id == ConsumableDatabase.Gold ? "currency" : "consumable", true));
            foreach (var id in new[] { ItemIds.Wood, ItemIds.Stone, ItemIds.Carrot }) rows.Add((id, "world", true));
            return Doc().Arr("items", rows, (o, r) => o.Obj().Str("id", r.id).Str("kind", r.kind).Bool("stackable", r.stackable).End()).End().ToString();
        }

        static string Starter()
        {
            var j = Doc().Str("startMap", MapRegistry.Village);
            j.Num("gold", ConsumableDatabase.StarterPack.Where(s => s.id == ConsumableDatabase.Gold).Sum(s => s.count));
            j.Arr("items", ConsumableDatabase.StarterPack.Where(s => s.id != ConsumableDatabase.Gold), (o, s) => o.Obj().Str("itemKey", s.id).Num("count", s.count).End());
            j.Key("gear").Obj();
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
                j.Key(cls.ToString().ToLowerInvariant()).Obj().Str("itemKey", EquipmentDatabase.StarterWeapon(cls)).Num("slot", (int)EquipSlot.Weapon).End();
            return j.End().End().ToString();
        }

        /// <summary>[Phase 3] Store stock and prices, equipment details and trade binding.</summary>
        static string Shop()
        {
            var j = Doc();
            j.Arr("stock", ItemPrices.ShopStock, (o, id) => o.Obj().Str("id", id).Num("buyPrice", ItemPrices.BuyPrice(id)).End());
            j.Arr("equipment", EquipmentDatabase.All, (o, e) => o.Obj()
                .Str("id", e.id).Str("category", e.category.ToString()).Str("rarity", e.rarity.ToString())
                .Str("classOnly", e.classOnly.HasValue ? e.classOnly.Value.ToString().ToLowerInvariant() : null)
                .Num("attack", e.attack).Num("maxHealth", e.maxHealth).Num("block", e.block).Num("speed", e.speed)
                .Bool("starter", e.starter).Num("dropWeight", e.dropWeight).Num("tier", e.Tier)
                .Num("sellPrice", ItemPrices.SellPrice(e.id)).Str("bind", AuctionRules.BindOf(e.id).ToString()).End());
            j.Arr("materials", EquipmentDatabase.AllMaterials, (o, m) => o.Obj()
                .Str("id", m.id).Str("rarity", m.rarity.ToString()).Num("dropChance", m.dropChance).Num("minDrop", m.minDrop).Num("maxDrop", m.maxDrop)
                .Num("sellPrice", ItemPrices.SellPrice(m.id)).End());
            j.Arr("sellPrices", new[] { ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll, ConsumableDatabase.ProtectTicket, ItemIds.Wood, ItemIds.Stone, ItemIds.Carrot },
                (o, id) => o.Obj().Str("id", id).Num("sellPrice", ItemPrices.SellPrice(id)).End());
            return j.End().ToString();
        }

        static string PassiveTreeJson()
        {
            var j = Doc().Str("start", PassiveTree.Start);
            j.Arr("nodes", PassiveTree.All.OrderBy(n => n.id, StringComparer.Ordinal), (o, n) =>
            {
                o.Obj().Str("id", n.id).Str("kind", n.kind.ToString());
                o.Arr("links", n.links, (x, l) => x.Val(l));
                return o.End();
            });
            return j.End().ToString();
        }

        static string SkillGemsJson()
        {
            var j = Doc().Num("slots", SkillGems.Slots).Num("supportsPerSlot", SkillGems.SupportsPerSlot);
            j.Arr("slotLevels", SkillGems.SlotLevels, (o, v) => o.Val(v));
            j.Arr("gems", SkillGems.All, (o, g) => o.Obj()
                .Str("id", g.id).Str("kind", g.kind == GemKind.Active ? "active" : "support")
                .Str("classOnly", g.classOnly.HasValue ? g.classOnly.Value.ToString().ToLowerInvariant() : null)
                .Num("unlockLevel", g.unlockLevel).Num("slot", g.kind == GemKind.Active ? g.slot : -1).End());
            return j.End().ToString();
        }

        static string QuestIndex()
        {
            var db = new QuestDatabase(Resources.Load<TextAsset>(QuestDatabase.ResourcePath));
            var flags = new SortedSet<string>(StringComparer.Ordinal);
            // Every "setFlags" / "requiresFlags" list anywhere in Quests.json (steps, quest end, rewards).
            var questText = Resources.Load<TextAsset>(QuestDatabase.ResourcePath).text;
            foreach (System.Text.RegularExpressions.Match list in System.Text.RegularExpressions.Regex.Matches(questText, "\"(?:setFlags|requiresFlags)\"\\s*:\\s*\\[([^\\]]*)\\]"))
                foreach (System.Text.RegularExpressions.Match f in System.Text.RegularExpressions.Regex.Matches(list.Groups[1].Value, "\"([^\"]+)\""))
                    flags.Add(f.Groups[1].Value);
            foreach (var f in StoryIds.PrologueFlags) flags.Add(f);
            flags.Add(QuestManager.WorkshopBuiltFlag);
            // Cutscene steps {"op": "flag", "id": "..."} (Cutscenes.json).
            var cut = Resources.Load<TextAsset>("Data/Cutscenes");
            if (cut != null)
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cut.text, "\"op\"\\s*:\\s*\"flag\"\\s*,\\s*\"id\"\\s*:\\s*\"([^\"]+)\""))
                    flags.Add(m.Groups[1].Value);
            flags.RemoveWhere(string.IsNullOrEmpty);
            var j = Doc();
            j.Arr("quests", db.All, (o, q) => o.Obj().Str("id", q.id).Num("stepCount", q.steps.Count)
                .Num("maxObjectives", q.steps.Count == 0 ? 0 : q.steps.Max(st => st.objectives.Count)).End());
            j.Arr("flags", flags, (o, f) => o.Val(f));
            return j.End().ToString();
        }

        static string Enums() => Doc()
            .Num("facingCount", Enum.GetValues(typeof(Facing)).Length)
            .Num("questStatusMax", Enum.GetValues(typeof(QuestStatus)).Cast<int>().Max()).End().ToString();

        static string Enhance()
        {
            var j = Doc().Num("maxEnhance", EquipmentDatabase.MaxEnhance);
            // One row per equipment and current level: everything Equipment.TryEnhance decides with.
            j.Arr("steps", EquipmentDatabase.All.Where(e => !e.starter), (o, e) =>
            {
                o.Obj().Str("id", e.id);
                o.Arr("levels", Enumerable.Range(0, EquipmentDatabase.MaxEnhance), (l, lv) => l.Obj()
                    .Num("from", lv).Num("successPercent", EnhanceRules.SuccessPercent(lv))
                    .Str("failure", EnhanceRules.FailureFor(e, lv).ToString()).Bool("pity", EnhanceRules.HasPity(e, lv))
                    .Num("gold", EnhanceRules.GoldFor(e, lv)).Num("bone", EnhanceRules.BoneFor(lv)).Num("ore", EnhanceRules.OreFor(lv)).Num("essence", EnhanceRules.EssenceFor(lv))
                    .Num("statsAttack", EquipmentDatabase.StatsAt(e, lv + 1).attack).Num("statsHealth", EquipmentDatabase.StatsAt(e, lv + 1).maxHealth).End());
                return o.End();
            });
            return j.End().ToString();
        }

        static string Monsters()
        {
            var j = Doc();
            j.Num("fieldGoldMin", 8).Num("fieldGoldMaxExclusive", 17).Num("equipmentDropChance", 0.4f); // EnemyController.DropLoot
            j.Num("hpPerLevel", 0.12f).Num("damagePerLevel", 0.08f);                                      // MonsterDatabase
            j.Arr("monsters", MonsterDatabase.All.OrderBy(m => m.id), (o, m) => o.Obj()
                .Str("id", m.id).Str("name", m.name).Str("kind", m.kind.ToString()).Num("hp", m.hp).Num("damage", m.damage).Num("xp", m.xp)
                .Bool("boss", m.boss).Bool("raid", m.raid).Bool("noLoot", m.noLoot).Num("goldMin", m.goldMin).Num("goldMax", m.goldMax).End());
            return j.End().ToString();
        }

        static J Difficulty(J o, DifficultyDef d) => o.Obj()
            .Str("id", d.id.ToString()).Str("name", d.name).Num("recommendedLevel", d.recommendedLevel).Num("recommendedPower", d.recommendedPower)
            .Num("hpMul", d.hpMul).Num("damageMul", d.damageMul).Num("rewardMul", d.rewardMul).Num("monsterLevel", d.monsterLevel)
            .Num("revives", d.revives).Str("minGearRarity", d.minGearRarity.ToString()).Num("ticketWeight", d.ticketWeight).End();

        static string Dungeons()
        {
            var j = Doc();
            j.Arr("difficulties", Enumerable.Range(0, DungeonDatabase.DifficultyCount).Select(i => DungeonDatabase.Difficulty((DungeonDifficulty)i)), Difficulty);
            j.Arr("partyHpScale", DungeonDatabase.PartyHpScale, (o, v) => o.Val(v));
            j.Arr("dungeons", DungeonDatabase.Weekday.Concat(DungeonDatabase.Raids), (o, d) =>
            {
                o.Obj().Str("id", d.id).Str("name", d.name).Bool("isRaid", d.isRaid).Str("raidTier", d.raidTier.ToString())
                    .Num("chapter", d.chapter).Str("unlockQuest", d.unlockQuest).Num("keyCost", d.keyCost).Num("clearXp", d.clearXp).Num("xpMul", d.xpMul)
                    .Num("bossRoom", d.bossRoom).Num("maxParty", d.maxParty);
                o.Arr("openDays", d.openDays ?? new DayOfWeek[0], (x, day) => x.Val(day.ToString()));
                o.Arr("referenceSeconds", d.referenceSeconds ?? new float[0], (x, s) => x.Val(s));
                o.Arr("rooms", d.rooms, (r, room) =>
                {
                    r.Obj().Str("mapId", room.mapId).Bool("isBoss", room.isBoss);
                    r.Arr("groups", room.groups, (g, grp) => g.Obj().Str("monsterId", grp.monsterId).Num("count", grp.count).Num("levelOffset", grp.levelOffset).Bool("isBoss", grp.isBoss).End());
                    return r.End();
                });
                if (d.isRaid) { o.Key("raidNumbers"); Difficulty(o, DungeonDatabase.DifficultyFor(d, DungeonDifficulty.Normal)); }
                return o.End();
            });
            return j.End().ToString();
        }

        static string Progression()
        {
            var j = Doc().Num("maxLevel", DotRPG.Progression.MaxLevel);
            j.Arr("xpToNext", Enumerable.Range(1, DotRPG.Progression.MaxLevel), (o, lv) => o.Val(DotRPG.Progression.XpToNext(lv)));
            return j.End().ToString();
        }

        static string Maps()
        {
            var j = Doc();
            j.Arr("maps", MapRegistry.All.Concat(MapRegistry.Rooms), (o, m) =>
            {
                o.Obj().Str("id", m.id).Bool("instanced", m.instanced).Bool("safe", m.safe);
                var size = LayoutSize(m.resource);
                if (size.x > 0) o.Key("bounds").Obj().Num("minX", 0).Num("minY", 0).Num("maxX", size.x).Num("maxY", size.y).End();
                return o.End();
            });
            return j.End().ToString();
        }

        /// <summary>World size of a text layout: WorldBuilder makes Bounds = (0, 0, width, height) in tiles.</summary>
        static Vector2Int LayoutSize(string resource)
        {
            var text = string.IsNullOrEmpty(resource) ? null : Resources.Load<TextAsset>(resource);
            if (text == null) return Vector2Int.zero;
            var rows = text.text.Replace("\r", "").Split('\n').Where(l => l.Length > 0 && !l.StartsWith("//")).ToList();
            return rows.Count == 0 ? Vector2Int.zero : new Vector2Int(rows.Max(r => r.Length), rows.Count);
        }

        // ---------------- tiny JSON writer (no package dependency) ----------------

        sealed class J
        {
            readonly StringBuilder sb = new StringBuilder();
            readonly Stack<bool> first = new Stack<bool>();
            bool pendingKey;

            void Sep()
            {
                if (pendingKey) { pendingKey = false; return; }
                if (first.Count == 0) return;
                if (first.Peek()) { first.Pop(); first.Push(false); } else sb.Append(',');
            }

            public J Key(string k)
            {
                Sep();
                sb.Append('"').Append(Esc(k)).Append("\":");
                pendingKey = true;
                return this;
            }

            public J Obj() { Sep(); sb.Append('{'); first.Push(true); return this; }
            public J End() { first.Pop(); sb.Append('}'); return this; }
            public J Str(string k, string v) { Key(k); Raw(v == null ? "null" : "\"" + Esc(v) + "\""); return this; }
            public J Num(string k, double v) { Key(k); Raw(Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture)); return this; } // floats: 0.4f -> 0.4
            public J Bool(string k, bool v) { Key(k); Raw(v ? "true" : "false"); return this; }
            public J Val(string v) { Sep(); sb.Append(v == null ? "null" : "\"" + Esc(v) + "\""); return this; }
            public J Val(double v) { Sep(); sb.Append(Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture)); return this; }

            public J Arr<T>(string k, IEnumerable<T> items, Func<J, T, J> each)
            {
                Key(k);
                pendingKey = false;
                sb.Append('[');
                first.Push(true);
                foreach (var item in items) each(this, item);
                first.Pop();
                sb.Append(']');
                return this;
            }

            void Raw(string s) { pendingKey = false; sb.Append(s); }

            static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            public override string ToString() => sb.ToString();
        }
    }
}
