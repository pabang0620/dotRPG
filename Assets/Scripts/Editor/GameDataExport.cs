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
    public static partial class GameDataExport
    {
        [Serializable] sealed class CareerCatalogJson { public int schema = 1; public CareerSkill[] skills = CareerCatalog.All; }

        /// <summary>
        /// careers.json: the skill catalog plus the awakening trial rules the server enforces (phase9 19.4): the least
        /// seconds a successful trial can take and the nodes it needs, keyed by career number (CareerTrials.Begin, Update).
        /// </summary>
        static string CareersJson()
        {
            string json = JsonUtility.ToJson(new CareerCatalogJson());
            const string trials = ",\"trials\":{" +
                "\"1\":{\"minSeconds\":4,\"requiredNodes\":[]}," +
                "\"2\":{\"minSeconds\":20,\"requiredNodes\":[\"g_taunt\",\"g_wall\"]}," +
                "\"3\":{\"minSeconds\":4,\"requiredNodes\":[\"m_fire\",\"m_ice\",\"m_storm\"]}," +
                "\"4\":{\"minSeconds\":20,\"requiredNodes\":[\"b_cleanse\",\"b_wing\"]}}";
            return json.Substring(0, json.Length - 1) + trials + "}";
        }

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
                ["careers.json"] = CareersJson(),
                ["quest_index.json"] = QuestIndex(),
                ["enums.json"] = Enums(),
                ["shop.json"] = Shop(),
                ["enhance.json"] = Enhance(),
                ["monsters.json"] = Monsters(),
                ["dungeons.json"] = Dungeons(),
                ["progression.json"] = Progression(),
                ["gameconfig.json"] = GameConfigJson(),
                ["player.json"] = PlayerJson(),
                ["chat.json"] = ChatJson(), // [SERVER 5] chat limits and report reasons
                ["auction.json"] = AuctionJson(), // [SERVER 6] auction house rules
                ["sweep.json"] = SweepJson(), // [SWEEP] clear ticket rules (phase10 10)
                ["raid_shop.json"] = RaidShopJson(), // [RAID] raid shop products (phase13 5.4)
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
                Directory.CreateDirectory(Application.streamingAssetsPath);
                File.WriteAllText(Path.Combine(Application.streamingAssetsPath, "DataVersion.txt"), hash, new UTF8Encoding(false));
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
            foreach (var id in new[] { ConsumableDatabase.Gold, ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll, ConsumableDatabase.ProtectTicket, DungeonDatabase.SealKey, DungeonDatabase.RaidCore, RaidRewards.RaidMatKing, RaidRewards.RaidMatGrah })
                if (ConsumableDatabase.Get(id) != null) rows.Add((id, id == ConsumableDatabase.Gold ? "currency" : "consumable", true));
            foreach (var id in new[] { ItemIds.Wood, ItemIds.Stone, ItemIds.Carrot }) rows.Add((id, "world", true));
            foreach (var id in new[] { DungeonSweep.TicketItem, DungeonSweep.EventTicketItem }) rows.Add((id, "consumable", true)); // [SWEEP] display only, wallet on the server
            // [CASH] Sealed box rewards (Docs/PLAN_CASH_BOX_PASS.md): account-bound, never on the auction.
            var cash = new HashSet<string>();
            foreach (var c in ConsumableDatabase.Cash) { rows.Add((c.id, "consumable", true)); cash.Add(c.id); }
            return Doc().Arr("items", rows, (o, r) =>
            {
                o.Obj().Str("id", r.id).Str("kind", r.kind).Bool("stackable", r.stackable)
                    .Str("name", DungeonDatabase.ItemName(r.id))
                    // [SERVER 6] the binding floor of the kind; how an item was obtained can bind it further (server)
                    .Str("bind", cash.Contains(r.id) || r.id == DungeonSweep.TicketItem || r.id == DungeonSweep.EventTicketItem ? "account" : AuctionRules.BindFloor(r.id) == ItemBind.CharacterBound ? "character" : "none")
                    .Bool("usable", ConsumableDatabase.IsUsable(r.id) || r.id == ItemIds.Carrot);
                var ci = cash.Contains(r.id) ? ConsumableDatabase.Get(r.id) : null;
                if (ci != null) o.Str("use", ci.kind.ToString()).Num("power", ci.power).Num("chance", ci.chance).Num("minutes", ci.minutes);
                return o.End();
            }).End().ToString();
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
            var j = Doc().Num("enhancedSellBonusPerLevel", 0.25).Num("enhancedSellGoldRatio", ItemPrices.EnhancedSellGoldRatio).Str("sellRounding", "awayFromZero"); // ItemPrices.SellPrice
            j.Arr("stock", ItemPrices.ShopStock, (o, id) => o.Obj().Str("id", id).Num("buyPrice", ItemPrices.BuyPrice(id)).End());
            j.Arr("equipment", EquipmentDatabase.All, (o, e) => o.Obj()
                .Str("id", e.id).Str("category", e.category.ToString()).Str("rarity", e.rarity.ToString())
                .Str("classOnly", e.classOnly.HasValue ? e.classOnly.Value.ToString().ToLowerInvariant() : null)
                .Num("attack", e.attack).Num("maxHealth", e.maxHealth).Num("block", e.block).Num("speed", e.speed)
                .Bool("starter", e.starter).Num("dropWeight", e.dropWeight).Num("tier", e.Tier)
                .Bool("bossOnly", e.bossOnly).Num("xpBonus", e.xpBonus).Num("aoeBonus", e.aoeBonus)
                .Num("reqLevel", e.reqLevel).Num("levelTier", e.levelTier)
                .Num("sellPrice", ItemPrices.SellPrice(e.id)).End());
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
            j.Arr("quests", db.All, (o, q) =>
            {
                o.Obj().Str("id", q.id).Num("stepCount", q.steps.Count)
                    .Num("maxObjectives", q.steps.Count == 0 ? 0 : q.steps.Max(st => st.objectives.Count))
                    .Str("kind", q.kind).Num("minLevel", q.minLevel);
                o.Arr("requires", q.requires ?? new List<string>(), (x, v) => x.Val(v));
                o.Arr("requiresFlags", q.requiresFlags ?? new List<string>(), (x, v) => x.Val(v));
                var r = q.reward ?? new QuestRewardDef();
                o.Key("reward").Obj().Num("xp", DotRPG.Progression.QuestXp(r.xp)).Num("gold", r.gold).Num("maxHealth", r.maxHealth); // scaled like the level curve
                o.Arr("items", r.items ?? new List<ItemStack>(), (x, it) => x.Obj().Str("id", it.id).Num("count", it.count).End());
                o.Arr("setFlags", r.setFlags ?? new List<string>(), (x, v) => x.Val(v));
                o.End();
                // Objectives summed by kind over every step: what the server can check and what it cannot.
                var all = q.steps.SelectMany(st => st.objectives).ToList();
                o.Key("objectives").Obj().Num("levelNeed", all.Where(ob => ob.type == ObjectiveTypes.Level).Select(ob => ob.count).DefaultIfEmpty(0).Max());
                o.Key("killNeeds").Obj();
                foreach (var g in all.Where(ob => ob.type == ObjectiveTypes.Kill).GroupBy(ob => ob.target)) o.Num(g.Key, g.Sum(ob => ob.count));
                o.End();
                o.Arr("consumes", all.Where(ob => ob.type == ObjectiveTypes.Collect && ob.consume), (x, ob) => x.Obj().Str("itemKey", ob.target).Num("count", ob.count).End());
                o.Arr("dungeonNeeds", all.Where(ob => ob.type == ObjectiveTypes.Dungeon), (x, ob) => x.Obj().Str("target", ob.target).Num("count", ob.count).End());
                o.Arr("raidNeeds", all.Where(ob => ob.type == ObjectiveTypes.Raid), (x, ob) => x.Obj().Str("target", ob.target).Num("count", ob.count).End());
                o.Arr("flagNeeds", all.Where(ob => ob.type == ObjectiveTypes.Flag).Select(ob => ob.target).Distinct(), (x, v) => x.Val(v));
                o.Arr("unverifiable", all.Select(ob => ob.type).Where(t => t == ObjectiveTypes.Talk || t == ObjectiveTypes.Cutscene || t == ObjectiveTypes.Interact || t == ObjectiveTypes.Reach || t == ObjectiveTypes.Quests).Distinct(), (x, v) => x.Val(v));
                o.End();
                return o.End();
            });
            j.Arr("flags", flags, (o, f) => o.Val(f));
            return j.End().ToString();
        }

        static string GameConfigJson()
        {
            var cfg = Resources.Load<GameConfig>("Data/GameConfig");
            var j = Doc();
            j.Key("nodeKinds").Obj()
                .Key("tree").Obj().Str("item", ItemIds.Wood).Num("amount", cfg.treeWoodDrop).Num("respawnSeconds", cfg.treeRegrowSeconds).End()
                .Key("rock").Obj().Str("item", ItemIds.Stone).Num("amount", cfg.rockStoneDrop).Num("respawnSeconds", cfg.rockRespawnSeconds).End()
                .Key("crop").Obj().Str("item", ItemIds.Carrot).Num("amount", 1).Num("respawnSeconds", cfg.cropRegrowSeconds).End() // CropPlot gives 1
                .End();
            var usable = ConsumableDatabase.Usable.Select(c => (c.id, c.kind.ToString())).ToList();
            usable.Add((ItemIds.Carrot, "HealHp")); // PlayerController.TryEatCarrot
            j.Arr("usableItems", usable, (o, u) => o.Obj().Str("id", u.Item1).Str("kind", u.Item2).End());
            j.Num("storageCapacity", StorageScreen.Capacity);
            j.Key("chestReward").Obj().Str("itemKey", TreasureChest.Reward).Num("count", 1).End();
            var db = new QuestDatabase(Resources.Load<TextAsset>(QuestDatabase.ResourcePath));
            var workshop = db.All.FirstOrDefault(q => q.id == QuestManager.WorkshopQuest);
            var qc = cfg.mainQuest;
            j.Key("deliverySites").Obj().Key("workshop").Obj().Str("questId", QuestManager.WorkshopQuest);
            j.Arr("requiresQuestClaimed", workshop != null ? workshop.requires : new List<string>(), (o, v) => o.Val(v));
            j.Arr("items", new[] { (ItemIds.Wood, qc != null ? qc.requiredWood : 6), (ItemIds.Stone, qc != null ? qc.requiredStone : 4) },
                (o, it) => o.Obj().Str("itemKey", it.Item1).Num("required", it.Item2).End());
            j.End().End();
            return j.End().ToString();
        }

        static string PlayerJson()
        {
            var cfg = Resources.Load<GameConfig>("Data/GameConfig");
            var ps = cfg.playerStats;
            return Doc().Num("attackDamage", ps.attackDamage).Num("attackCooldown", ps.attackCooldown).Num("maxHealth", ps.maxHealth)
                .Num("mageBoltDamage", CharacterClassInfo.Get(CharacterClass.Mage).damage).Num("mageBoltCooldown", CharacterClassInfo.Get(CharacterClass.Mage).cooldown)
                // [SERVER 4] CharacterStatsCalc.Power terms the server can know (level, gear); passives are left out.
                .Key("power").Obj()
                    .Num("attackWeight", 10).Num("hpWeight", 5).Num("mpWeight", 3).Num("blockWeight", 20).Num("speedWeight", 10).Num("levelWeight", 50)
                    .Num("baseMana", CharacterStatsCalc.BaseMana).Num("hpPerLevel", CharacterStatsCalc.HpPerLevel).Num("mpPerLevel", CharacterStatsCalc.MpPerLevel)
                    .Num("maxBlock", Equipment.MaxBlock)
                .End()
                .End().ToString();
        }

        /// <summary>[SWEEP] Clear ticket rules from <see cref="DungeonSweep"/> (Docs/server/phase10_sweep_mail.md 10).</summary>
        static string SweepJson() => Doc()
            .Str("ticketItem", DungeonSweep.TicketItem).Str("eventTicketItem", DungeonSweep.EventTicketItem)
            .Num("eventTicketDays", DungeonSweep.EventTicketDays).Num("minRank", (int)DungeonSweep.MinRank)
            .Num("xpBonusPercent", DungeonSweep.XpBonusPercent).Num("cardCount", DungeonSweep.CardCount).Num("gearKeepPercent", DungeonSweep.GearKeepPercent)
            .Key("shop").Obj().Num("basePrice", DungeonSweep.ShopBasePrice).Num("weeklyLimit", DungeonSweep.ShopWeeklyLimit).End()
            .Key("weekly").Obj().Num("directClears", DungeonSweep.WeeklyDirectClears).Num("rewardTickets", DungeonSweep.WeeklyRewardTickets).End()
            .End().ToString();

        static string AuctionJson()
        {
            var j = Doc();
            j.Arr("durations", AuctionRules.Durations, (o, v) => o.Val(v));
            j.Key("feePct").Obj().Num("equipment", AuctionRules.FeePctGear).Num("stack", AuctionRules.FeePctStack).End();
            j.Num("depositBps", AuctionRules.DepositBps).Num("depositMin", AuctionRules.DepositMin).Num("depositMax", AuctionRules.DepositMax)
                .Num("maxListings", AuctionRules.MaxListings).Num("minBidStepBps", AuctionRules.MinBidStepBps)
                .Num("priceFloorBps", AuctionRules.PriceFloorBps).Num("priceCeilBps", AuctionRules.PriceCeilBps)
                .Num("maxStack", AuctionRules.MaxStack)
                .Num("extendWindowMinutes", AuctionRules.ExtendWindowMinutes).Num("extendMinutes", AuctionRules.ExtendMinutes).Num("extendMax", AuctionRules.ExtendMax)
                .Num("mailDays", AuctionRules.MailDays);
            j.Arr("timeBands", AuctionRules.TimeBands, (o, v) => o.Val(v));
            return j.End().ToString();
        }

        static string ChatJson()
        {
            var j = Doc().Num("maxLength", ChatRules.MaxLength).Num("minIntervalSeconds", ChatRules.MinInterval)
                .Num("repeatLimit", ChatRules.RepeatLimit).Num("muteSeconds", ChatRules.MuteSeconds).Num("reportLines", ChatRules.ReportLines);
            j.Arr("reportReasons", Enumerable.Range(0, ChatRules.ReportReasons.Length),
                (o, i) => o.Obj().Str("code", ChatRules.ReportReasonCodes[i]).Str("label", ChatRules.ReportReasons[i]).End());
            return j.End().ToString();
        }

        static string Enums() => Doc()
            .Num("facingCount", Enum.GetValues(typeof(Facing)).Length)
            .Num("questStatusMax", Enum.GetValues(typeof(QuestStatus)).Cast<int>().Max()).End().ToString();

        static string Enhance()
        {
            var j = Doc().Num("maxEnhance", EquipmentDatabase.MaxEnhance)
                .Num("maxPity", EnhanceRules.MaxPity).Num("pityPerFailure", 1).Num("drop3Levels", 3).Num("rollRange", 100) // Equipment.TryEnhance, EnhanceRules
                .Str("ticketItem", ConsumableDatabase.ProtectTicket);
            j.Key("materials").Obj().Str("bone", "mat_bone").Str("ore", "mat_ore").Str("essence", "mat_essence").End();
            // One row per equipment and current level: everything Equipment.TryEnhance decides with.
            j.Arr("steps", EquipmentDatabase.All, (o, e) => // starter gear can be enhanced too
            {
                o.Obj().Str("id", e.id);
                o.Arr("levels", Enumerable.Range(0, EquipmentDatabase.MaxEnhance), (l, lv) => l.Obj()
                    .Num("from", lv).Num("successPercent", EnhanceRules.SuccessPercent(lv))
                    .Str("failure", EnhanceRules.FailureFor(e, lv).ToString()).Bool("pity", EnhanceRules.HasPity(e, lv))
                    .Num("gold", EnhanceRules.GoldFor(e, lv)).Num("bone", EnhanceRules.BoneFor(lv)).Num("ore", EnhanceRules.OreFor(lv)).Num("essence", EnhanceRules.EssenceFor(lv))
                    .Num("statsAttack", EquipmentDatabase.StatsAt(e, lv + 1).attack).Num("statsHealth", EquipmentDatabase.StatsAt(e, lv + 1).maxHealth).End());
                return o.End();
            });
            // Gear promotion (PromoteRules): the next grade of the same slot, paid with raid-only cores.
            j.Key("promote").Obj().Str("coreItem", PromoteRules.CoreItem);
            j.Arr("rows", PromoteRules.Rows(), (o, r) => o.Obj().Str("from", r.from.id).Str("to", r.to.id).Num("cores", r.cores).Num("gold", r.gold).End());
            j.Arr("raidCoreMid", PromoteRules.RaidCoreMid, (o, n) => o.Val(n));
            j.Arr("raidCoreFinal", PromoteRules.RaidCoreFinal, (o, n) => o.Val(n));
            j.End();
            return j.End().ToString();
        }

        static string Monsters()
        {
            var j = Doc();
            j.Num("fieldGoldMin", 8).Num("fieldGoldMaxExclusive", 17).Num("equipmentDropChance", 0.08f); // EnemyController.DropLoot
            j.Num("hpPerLevel", MonsterDatabase.HpPerLevel).Num("damagePerLevel", MonsterDatabase.DamagePerLevel).Num("xpPerLevel", MonsterDatabase.XpPerLevel);
            j.Key("loot").Obj().Num("goldPileDivisor", 20).Num("goldPileMin", 2).Num("goldPileMax", 6).End(); // EnemyController.MonsterLoot
            var cfg = Resources.Load<GameConfig>("Data/GameConfig");
            var skel = cfg != null ? cfg.skeletonStats : null;
            j.Arr("monsters", MonsterDatabase.All.OrderBy(m => m.id), (o, m) =>
            {
                o.Obj().Str("id", m.id).Str("name", m.name).Str("kind", m.kind.ToString()).Num("hp", m.hp).Num("damage", m.damage).Num("xp", m.xp)
                    .Bool("boss", m.boss).Bool("raid", m.raid).Bool("noLoot", m.noLoot).Num("goldMin", m.goldMin).Num("goldMax", m.goldMax)
                    .Num("goldPerHitMin", m.goldPerHitMin).Num("goldPerHitMax", m.goldPerHitMax)
                    .Str("bossGear", m.bossGear).Num("bossGearPermille", m.bossGearPermille);
                // Same float maths as MonsterDatabase.SpawnDef, so the server never re-derives the rounding.
                o.Arr("xpByLevel", Enumerable.Range(1, 30), (x, lv) => x.Val(Mathf.RoundToInt(m.xp * (1f + MonsterDatabase.XpPerLevel * (lv - 1)))));
                return o.End();
            });
            // The field skeleton is an EnemyStats asset, not a MonsterDatabase entry (quest kill target "skeleton").
            if (skel != null)
                j.Key("fieldSkeleton").Obj().Str("id", skel.enemyId).Str("kind", "Melee").Num("hp", skel.maxHealth).Num("damage", skel.attackDamage)
                    .Num("xp", skel.xpReward).Bool("noLoot", false).Bool("boss", false).Bool("raid", false).Num("goldMin", 0).Num("goldMax", 0)
                    .Num("respawnSeconds", skel.respawnDelay).Num("respawnMinPlayerDistance", skel.respawnMinPlayerDistance).End();
            return j.End().ToString();
        }

        static J Difficulty(J o, DifficultyDef d) => o.Obj()
            .Str("id", d.id.ToString()).Str("name", d.name).Num("recommendedLevel", d.recommendedLevel).Num("recommendedPower", d.recommendedPower)
            .Num("hpMul", d.hpMul).Num("damageMul", d.damageMul).Num("rewardMul", d.rewardMul).Num("monsterLevel", d.monsterLevel)
            .Num("revives", d.revives).Str("minGearRarity", d.minGearRarity.ToString()).Num("ticketWeight", d.ticketWeight).Num("jackpotPerMille", d.jackpotPerMille).End();

        static string Dungeons()
        {
            var j = Doc();
            j.Arr("difficulties", Enumerable.Range(0, DungeonDatabase.DifficultyCount).Select(i => DungeonDatabase.Difficulty((DungeonDifficulty)i)), Difficulty);
            j.Arr("partyHpScale", DungeonDatabase.PartyHpScale, (o, v) => o.Val(v));
            j.Num("dailyEntries", DungeonDatabase.DailyEntries).Bool("weekendOpensAll", true); // ResetClock.IsOpen
            j.Key("cards").Obj().Num("count", DungeonRewards.CardCount).Num("gearRareWeight", DungeonRewards.RareGearWeight).Num("gearDropTries", DungeonRewards.DropTries).End();
            j.Key("ranking").Obj().Num("timeMax", DungeonRanking.TimeMax).Num("hitsMax", DungeonRanking.HitsMax).Num("killsMax", DungeonRanking.KillsMax)
                .Num("comboMax", DungeonRanking.ComboMax).Num("revivePenalty", DungeonRanking.RevivePenalty).Num("timeZeroAt", DungeonRanking.TimeZeroAt)
                .Num("pointsPerHit", DungeonRanking.PointsPerHit).Num("comboTarget", DungeonRanking.ComboTarget).Num("comboWindow", DungeonRanking.ComboWindow);
            j.Arr("thresholds", DungeonRanking.Thresholds, (o, v) => o.Val(v));
            j.Arr("xpBonus", DungeonRanking.XpBonus, (o, v) => o.Val(v));
            j.End();
            // Measured fastest normal clears 87 s (theory_balance.py); the server takes 60% of these as the floor.
            j.Arr("minClearSeconds", new[] { 87, 0, 0, 0 }, (o, v) => o.Val(v));
            // [SERVER 4] Raids: no measured floor yet (the server falls back to 0.5 x referenceSeconds), key item, AI mercenaries.
            j.Key("raidMinClearSeconds").Obj().End();
            j.Str("keyItem", DungeonDatabase.SealKey);
            j.Key("mercenary").Obj().Num("damageScale", MercenaryDatabase.DamageScale).Num("maxCompanions", PartyManager.MaxCompanions).End();
            j.Arr("dungeons", DungeonDatabase.Weekday.Concat(DungeonDatabase.Raids), (o, d) =>
            {
                o.Obj().Str("id", d.id).Str("name", d.name).Bool("isRaid", d.isRaid).Str("raidTier", d.raidTier.ToString())
                    .Num("chapter", d.chapter).Str("unlockQuest", d.unlockQuest).Num("keyCost", d.keyCost).Num("clearXp", d.clearXp).Num("xpMul", d.xpMul)
                    .Num("bossRoom", d.bossRoom).Num("maxParty", d.maxParty);
                o.Arr("openDays", d.openDays ?? new DayOfWeek[0], (x, day) => x.Val(day.ToString()));
                o.Arr("referenceSeconds", d.referenceSeconds ?? new float[0], (x, s) => x.Val(s));
                o.Arr("clearXpFloor", Enumerable.Range(0, 4), (x, i) => x.Val(HuntingGrounds.DungeonClearFloor(d, DungeonDatabase.DifficultyFor(d, (DungeonDifficulty)i))));
                o.Arr("rewards", d.rewards ?? new RewardEntry[0], (x, r) => x.Obj().Str("itemId", r.itemId).Num("min", r.min).Num("max", r.max).Num("weight", r.weight).End());
                o.Arr("rooms", d.rooms, (r, room) =>
                {
                    r.Obj().Str("mapId", room.mapId).Bool("isBoss", room.isBoss);
                    r.Arr("groups", room.groups, (g, grp) => g.Obj().Str("monsterId", grp.monsterId).Num("count", grp.count).Num("levelOffset", grp.levelOffset).Bool("isBoss", grp.isBoss).End());
                    return r.End();
                });
                if (d.isRaid)
                {
                    o.Key("raidNumbers"); Difficulty(o, DungeonDatabase.DifficultyFor(d, DungeonDifficulty.Normal));
                    o.Num("keyMin", d.keyMin).Num("keyMax", d.keyMax); // [SERVER 4] mid raids drop seal key fragments
                    if (d.raidReward != null) RaidRewardJson(o, d.raidReward); // [RAID] sure gold + cards (phase13 6.1); "rewards" above is empty for these
                }
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
