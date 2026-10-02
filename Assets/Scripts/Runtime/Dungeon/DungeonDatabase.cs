using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Dungeon&amp;Fighter difficulty names: 일반 / 모험 / 왕 / 영웅.</summary>
    public enum DungeonDifficulty
    {
        Normal = 0,
        Adventure = 1,
        King = 2,
        Hero = 3,
    }

    /// <summary>Numbers of one difficulty (monster multipliers, rewards, revives).</summary>
    public sealed class DifficultyDef
    {
        public DungeonDifficulty id;
        public string name;
        public int recommendedLevel;
        /// <summary>Shown next to the player's 전투력 in the select window.</summary>
        public int recommendedPower;
        public float hpMul, damageMul, rewardMul;
        /// <summary>Added to every monster group's level offset (for MonsterDatabase; see DungeonMonsters).</summary>
        public int monsterLevel;
        /// <summary>Revives per run (shared by the party).</summary>
        public int revives;
        /// <summary>Lowest grade a gear card can roll.</summary>
        public ItemRarity minGearRarity;
        /// <summary>Weight of the protection-ticket card (0 = never).</summary>
        public int ticketWeight;
    }

    /// <summary>
    /// Monsters placed on the spawn cells marked with one digit (1-9) of a room map. <see cref="count"/>
    /// monsters are spread over those cells.
    /// </summary>
    public sealed class SpawnGroup
    {
        public int digit;
        public string monsterId;
        public int count;
        public int levelOffset;
        public bool isBoss;

        public SpawnGroup(int digit, string monsterId, int count, int levelOffset = 0, bool isBoss = false)
        {
            this.digit = digit;
            this.monsterId = monsterId;
            this.count = count;
            this.levelOffset = levelOffset;
            this.isBoss = isBoss;
        }
    }

    /// <summary>One room = one small map (Resources/Maps/Dungeons/*.txt, see <see cref="MapRegistry"/>).</summary>
    public sealed class RoomDef
    {
        public string mapId;
        public SpawnGroup[] groups;
        public bool isBoss;
        /// <summary>Indices of the rooms the door leads to (the run takes the first one; empty = last room).</summary>
        public int[] next;
    }

    /// <summary>One card in a dungeon's reward table.</summary>
    public sealed class RewardEntry
    {
        /// <summary>Item id ("gold", "mat_ore", "ticket_protect") or <see cref="DungeonDatabase.GearReward"/>.</summary>
        public string itemId;
        public int min, max;
        public int weight;

        public RewardEntry(string itemId, int min, int max, int weight)
        {
            this.itemId = itemId;
            this.min = min;
            this.max = max;
            this.weight = weight;
        }
    }

    /// <summary>[RAID] Chapter raids: a mid raid (주 3회, 수·토·일) and a final raid (일요일, needs seal key fragments).</summary>
    public enum RaidTier
    {
        None,
        Mid,
        Final,
    }

    public sealed class DungeonDef
    {
        public string id;
        public string name;
        public MapTheme theme;
        public string themeName;
        /// <summary>Weekdays it is open (Saturday/Sunday open everything, see <see cref="ResetClock.IsOpen"/>).</summary>
        public DayOfWeek[] openDays;
        public RoomDef[] rooms;
        public int bossRoom;
        /// <summary>Clear time (s) that still gives the full time score, per difficulty.</summary>
        public float[] referenceSeconds;
        public RewardEntry[] rewards;
        /// <summary>Base clear XP before difficulty and rank bonus.</summary>
        public int clearXp;
        /// <summary>Extra clear XP multiplier (수련의 숲 = experience dungeon).</summary>
        public float xpMul = 1f;
        public string description;
        public string specialty;
        public string featureMonster;
        public string bossName;
        public bool isRaid;
        public int maxParty = 4;
        // [RAID] Story raids.
        public RaidTier raidTier;
        public int chapter;
        /// <summary>Quest that opens the raid (accepted or done). "" = always open.</summary>
        public string unlockQuest = "";
        /// <summary>Final raids: seal key fragments needed to enter (taken on a rewarded clear).</summary>
        public int keyCost;
        /// <summary>Mid raids: seal key fragments dropped on a rewarded clear (min..max).</summary>
        public int keyMin, keyMax;
        /// <summary>[J10] This raid's own numbers (null = the shared <see cref="DungeonDatabase.RaidDifficulty"/>).</summary>
        public DifficultyDef raidNumbers;

        public int RoomCount => rooms.Length;
    }

    /// <summary>
    /// Code table of every weekday dungeon and the raid (like <see cref="EquipmentDatabase"/>), plan §6.0-§6.2.
    /// Numbers designers tune are at the top.
    /// </summary>
    public static class DungeonDatabase
    {
        /// <summary>Reward id of "one piece of gear" (rolled per class and difficulty).</summary>
        public const string GearReward = "gear";
        /// <summary>Weekday dungeon entries per day (shared by all of them).</summary>
        public const int DailyEntries = 3;
        /// <summary>Raid revives (shared by the party).</summary>
        public const int RaidRevives = 3;
        /// <summary>Monster HP multiplier by party size 1..4 (DNF-style head-count scaling).</summary>
        public static readonly float[] PartyHpScale = { 1.0f, 1.7f, 2.4f, 3.0f };
        /// <summary>Boss monsters: HP multiplier on top of the difficulty (see <see cref="DungeonMonsters"/>).</summary>
        public const float BossHpMul = 8f;
        public const float BossScale = 1.6f;

        public const string Raid = "raid_skeleton_king";
        public const string RaidBargas = "raid_bargas";
        public const string RaidGolem = "raid_golem", RaidGrah = "raid_grah";
        /// <summary>[RAID] Item that opens final raids.</summary>
        public const string SealKey = "key_seal";

        // [P5] Multipliers tuned by Tools/balance/theory_balance.py: monster level growth (+12% HP, +8% damage per
        // level) already scales the higher tiers, so these stay small. Target: 1.2 / 1.4 / 1.6x the Normal clear time.
        static readonly DifficultyDef[] Difficulties =
        {
            new DifficultyDef { id = DungeonDifficulty.Normal, name = "일반", recommendedLevel = 5, recommendedPower = 1500, hpMul = 1.0f, damageMul = 1.0f, rewardMul = 1.0f, monsterLevel = 0, revives = 5, minGearRarity = ItemRarity.Common, ticketWeight = 0 },
            new DifficultyDef { id = DungeonDifficulty.Adventure, name = "모험", recommendedLevel = 12, recommendedPower = 2600, hpMul = 1.2f, damageMul = 1.05f, rewardMul = 1.6f, monsterLevel = 7, revives = 4, minGearRarity = ItemRarity.Uncommon, ticketWeight = 0 },
            new DifficultyDef { id = DungeonDifficulty.King, name = "왕", recommendedLevel = 20, recommendedPower = 4200, hpMul = 2.0f, damageMul = 1.25f, rewardMul = 2.4f, monsterLevel = 15, revives = 3, minGearRarity = ItemRarity.Rare, ticketWeight = 0 },
            new DifficultyDef { id = DungeonDifficulty.Hero, name = "영웅", recommendedLevel = 27, recommendedPower = 6000, hpMul = 2.7f, damageMul = 1.6f, rewardMul = 3.4f, monsterLevel = 22, revives = 2, minGearRarity = ItemRarity.Epic, ticketWeight = 6 },
        };

        /// <summary>The raid has one difficulty of its own.</summary>
        public static readonly DifficultyDef RaidDifficulty = new DifficultyDef
        {
            // Tuned for a Lv27 party of 4 (monster HP x3.0 from party size) to clear in 5-10 minutes.
            id = DungeonDifficulty.Normal, name = "레이드", recommendedLevel = 25, recommendedPower = 5200, hpMul = 3.0f, damageMul = 1.6f, rewardMul = 3.0f,
            monsterLevel = 16, revives = RaidRevives, minGearRarity = ItemRarity.Rare, ticketWeight = 8,
        };

        public static int DifficultyCount => Difficulties.Length;

        public static DifficultyDef Difficulty(DungeonDifficulty d) => Difficulties[Mathf.Clamp((int)d, 0, Difficulties.Length - 1)];

        /// <summary>The difficulty numbers a run of this dungeon uses.</summary>
        public static DifficultyDef DifficultyFor(DungeonDef dungeon, DungeonDifficulty d) =>
            dungeon != null && dungeon.isRaid ? dungeon.raidNumbers ?? RaidDifficulty : Difficulty(d);

        /// <summary>
        /// [J10] Story raid numbers. Chapter 1's mid raid opens around 20 hours of play (Lv 22, quest 1-8), the
        /// final raid a week of keys later; chapter 2 continues to the Lv 40 cap. Party HP scaling still applies.
        /// </summary>
        static DifficultyDef RaidNumbers(int recommendedLevel, int power, float hpMul, float damageMul, int monsterLevel, ItemRarity minGear, int ticketWeight) => new DifficultyDef
        {
            id = DungeonDifficulty.Normal, name = "레이드", recommendedLevel = recommendedLevel, recommendedPower = power, hpMul = hpMul, damageMul = damageMul,
            rewardMul = RaidDifficulty.rewardMul, monsterLevel = monsterLevel, revives = RaidRevives, minGearRarity = minGear, ticketWeight = ticketWeight,
        };

        public static float PartyScale(int partySize) => PartyHpScale[Mathf.Clamp(partySize, 1, PartyHpScale.Length) - 1];

        // ---------- Rooms ----------

        static RoomDef Room(string mapId, params SpawnGroup[] groups) => new RoomDef { mapId = mapId, groups = groups, next = new int[0] };

        static RoomDef Boss(string mapId, string bossId, string addId, int adds) => new RoomDef
        {
            mapId = mapId, isBoss = true, next = new int[0],
            groups = adds > 0
                ? new[] { new SpawnGroup(1, bossId, 1, 3, true), new SpawnGroup(2, addId, adds) }
                : new[] { new SpawnGroup(1, bossId, 1, 3, true) },
        };

        /// <summary>Links the rooms in order (room i → room i + 1).</summary>
        static RoomDef[] Chain(params RoomDef[] rooms)
        {
            for (int i = 0; i < rooms.Length - 1; i++) rooms[i].next = new[] { i + 1 };
            return rooms;
        }

        static readonly DungeonDef[] Dungeons =
        {
            new DungeonDef
            {
                id = "gold_vein", name = "황금 광맥", theme = MapTheme.Canyon, themeName = "협곡 광산",
                openDays = new[] { DayOfWeek.Monday },
                rooms = Chain(
                    Room(MapRegistry.DgnCanyon1, new SpawnGroup(1, "skel_gold", 3), new SpawnGroup(2, "skel_warrior", 2)),
                    Room(MapRegistry.DgnCanyon2, new SpawnGroup(1, "skel_gold", 3), new SpawnGroup(2, "skel_miner", 2, 1)),
                    Room(MapRegistry.DgnCanyon3, new SpawnGroup(1, "skel_gold", 4), new SpawnGroup(2, "skel_warrior", 2, 1)),
                    Boss(MapRegistry.DgnCanyonBoss, "boss_gold_foreman", "skel_gold", 2)),
                bossRoom = 3, referenceSeconds = new[] { 150f, 170f, 190f, 210f }, clearXp = 120,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 300, 600, 50),
                    new RewardEntry(ConsumableDatabase.Gold, 800, 1200, 12),
                    new RewardEntry(EnhanceRules.Bone, 4, 8, 22),
                    new RewardEntry(EnhanceRules.Ore, 1, 2, 10),
                    new RewardEntry(GearReward, 1, 1, 6),
                },
                description = "협곡 아래 황금 광맥에 해골 광부들이 몰려들었다.\n황금 해골은 도망치며 금화를 흩뿌린다.",
                specialty = "골드", featureMonster = "황금 해골 (도망치며 골드를 뿌림)", bossName = "황금 광부장",
            },
            new DungeonDef
            {
                id = "smelter", name = "버려진 제련소", theme = MapTheme.Canyon, themeName = "협곡 광산",
                openDays = new[] { DayOfWeek.Tuesday },
                rooms = Chain(
                    Room(MapRegistry.DgnCanyon2, new SpawnGroup(1, "skel_miner", 3), new SpawnGroup(2, "skel_warrior", 2)),
                    Room(MapRegistry.DgnCanyon3, new SpawnGroup(1, "skel_miner", 3), new SpawnGroup(2, "skel_warrior", 2, 1)),
                    Room(MapRegistry.DgnCanyon1, new SpawnGroup(1, "skel_miner", 4), new SpawnGroup(2, "skel_shield", 1, 1)),
                    Boss(MapRegistry.DgnCanyonBoss, "boss_mine_captain", "skel_miner", 2)),
                bossRoom = 3, referenceSeconds = new[] { 150f, 170f, 190f, 210f }, clearXp = 120,
                rewards = new[]
                {
                    new RewardEntry(EnhanceRules.Ore, 3, 6, 45),
                    new RewardEntry(EnhanceRules.Ore, 8, 12, 10),
                    new RewardEntry(EnhanceRules.Bone, 4, 8, 20),
                    new RewardEntry(ConsumableDatabase.Gold, 150, 300, 19),
                    new RewardEntry(GearReward, 1, 1, 6),
                },
                description = "불이 꺼진 제련소에 해골 광부들이 곡괭이를 들고 모여 있다.\n해골 광부는 멀리서 돌진해 온다.",
                specialty = "강화석", featureMonster = "해골 광부 (돌진)", bossName = "광산 해골대장",
            },
            new DungeonDef
            {
                id = "mana_graveyard", name = "마력의 묘지", theme = MapTheme.Forest, themeName = "숲 묘지",
                openDays = new[] { DayOfWeek.Wednesday },
                rooms = Chain(
                    Room(MapRegistry.DgnForest1, new SpawnGroup(1, "skel_necro", 2), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnForest2, new SpawnGroup(1, "skel_necro", 2, 1), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnForest3, new SpawnGroup(1, "skel_necro", 3, 1), new SpawnGroup(2, "skel_archer", 2)),
                    Boss(MapRegistry.DgnForestBoss, "boss_lich", "skel_warrior", 2)),
                bossRoom = 3, referenceSeconds = new[] { 160f, 180f, 200f, 220f }, clearXp = 130,
                rewards = new[]
                {
                    new RewardEntry(EnhanceRules.Essence, 1, 3, 42),
                    new RewardEntry(EnhanceRules.Essence, 4, 6, 8),
                    new RewardEntry(EnhanceRules.Bone, 4, 8, 22),
                    new RewardEntry(ConsumableDatabase.Gold, 150, 300, 22),
                    new RewardEntry(GearReward, 1, 1, 6),
                },
                description = "마력이 고인 숲속 묘지. 해골 사령술사가 멀리서 주문을 쏘고\n쓰러진 해골을 다시 일으킨다.",
                specialty = "마력 정수", featureMonster = "해골 사령술사 (원거리, 해골 소환)", bossName = "묘지기 리치",
            },
            new DungeonDef
            {
                id = "training_forest", name = "수련의 숲", theme = MapTheme.Forest, themeName = "숲 묘지",
                openDays = new[] { DayOfWeek.Thursday },
                rooms = Chain(
                    Room(MapRegistry.DgnForest3, new SpawnGroup(1, "skel_archer", 3), new SpawnGroup(2, "skel_warrior", 2)),
                    Room(MapRegistry.DgnForest2, new SpawnGroup(1, "skel_archer", 3, 1), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnForest1, new SpawnGroup(1, "skel_archer", 3, 1), new SpawnGroup(2, "skel_shield", 2)),
                    Boss(MapRegistry.DgnForestBoss, "boss_archer_chief", "skel_archer", 2)),
                bossRoom = 3, referenceSeconds = new[] { 160f, 180f, 200f, 220f }, clearXp = 140, xpMul = 2.5f,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 200, 400, 35),
                    new RewardEntry(EnhanceRules.Bone, 5, 10, 30),
                    new RewardEntry(ConsumableDatabase.HpPotion, 2, 4, 20),
                    new RewardEntry(EnhanceRules.Ore, 1, 3, 9),
                    new RewardEntry(GearReward, 1, 1, 6),
                },
                description = "숲의 수련장에 해골 궁수들이 진을 쳤다.\n클리어 경험치가 크게 늘어나는 수련 던전.",
                specialty = "경험치", featureMonster = "해골 궁수 (원거리)", bossName = "해골 사수장",
            },
            new DungeonDef
            {
                id = "armory", name = "망자의 무기고", theme = MapTheme.Winter, themeName = "겨울 동굴",
                openDays = new[] { DayOfWeek.Friday },
                rooms = Chain(
                    Room(MapRegistry.DgnWinter1, new SpawnGroup(1, "skel_shield", 2), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnWinter2, new SpawnGroup(1, "skel_shield", 2, 1), new SpawnGroup(2, "skel_knight", 2)),
                    Room(MapRegistry.DgnWinter3, new SpawnGroup(1, "skel_shield", 3, 1), new SpawnGroup(2, "skel_knight", 2, 1)),
                    Boss(MapRegistry.DgnWinterBoss, "boss_armory_warden", "skel_shield", 2)),
                bossRoom = 3, referenceSeconds = new[] { 170f, 190f, 210f, 230f }, clearXp = 130,
                rewards = new[]
                {
                    new RewardEntry(GearReward, 1, 1, 40),
                    new RewardEntry(EnhanceRules.Ore, 2, 4, 20),
                    new RewardEntry(EnhanceRules.Bone, 4, 8, 20),
                    new RewardEntry(ConsumableDatabase.Gold, 200, 400, 20),
                },
                description = "얼어붙은 동굴 깊은 곳, 망자들이 지키는 옛 무기고.\n해골 방패병은 정면 공격을 막아 낸다.",
                specialty = "장비", featureMonster = "해골 방패병 (정면 방어, 슈퍼아머)", bossName = "무기고 수문장",
            },
        };

        static readonly DungeonDef RaidDef = new DungeonDef
        {
            id = Raid, name = "해골왕", theme = MapTheme.Canyon, themeName = "북쪽 고개 성채", isRaid = true,
            // [RAID] Chapter 1 mid raid: three gates a week, a seal key fragment chance on each rewarded clear.
            raidTier = RaidTier.Mid, chapter = 1, unlockQuest = "c1_fortress", keyMin = 20, keyMax = 50,
            raidNumbers = RaidNumbers(22, 4400, 2.6f, 1.4f, 14, ItemRarity.Rare, 6),
            openDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnRaid1, new SpawnGroup(1, "skel_knight", 4, 2), new SpawnGroup(2, "skel_shield", 2, 2)),
                Room(MapRegistry.DgnRaid2, new SpawnGroup(1, "skel_necro", 3, 2), new SpawnGroup(2, "skel_knight", 3, 2)),
                Boss(MapRegistry.DgnRaidBoss, "boss_skeleton_king", "skel_knight", 0)),
            bossRoom = 2, referenceSeconds = new[] { 300f, 300f, 300f, 300f }, clearXp = 400,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 45),
                new RewardEntry(EnhanceRules.Essence, 3, 6, 20),
                new RewardEntry(EnhanceRules.Ore, 6, 10, 15),
                new RewardEntry(ConsumableDatabase.Gold, 800, 1500, 20),
            },
            description = "북쪽 고개 너머에서 깨어난 해골왕과 그의 친위대.\n친위대 전초 두 곳을 돌파하고 해골왕을 쓰러뜨리자.",
            specialty = "유니크 · 레전더리 장비", featureMonster = "해골 기사단, 해골 사령술사", bossName = "해골왕",
        };

        /// <summary>[RAID] Chapter 1 final raid: 흑철의 바르가스, Sunday only, costs seal key fragments.</summary>
        static readonly DungeonDef BargasDef = new DungeonDef
        {
            id = RaidBargas, name = "흑철의 바르가스", theme = MapTheme.Winter, themeName = "흑철 진영", isRaid = true,
            raidTier = RaidTier.Final, chapter = 1, unlockQuest = "c1_bargas", keyCost = 100,
            raidNumbers = RaidNumbers(24, 4900, 2.1f, 1.6f, 17, ItemRarity.Rare, 8),
            openDays = new[] { DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnBargas1, new SpawnGroup(1, "skel_knight", 5, 2), new SpawnGroup(2, "skel_archer", 3, 2)),
                Room(MapRegistry.DgnBargas2, new SpawnGroup(1, "skel_shield", 3, 2), new SpawnGroup(2, "skel_necro", 3, 2)),
                Boss(MapRegistry.DgnBargasBoss, "boss_bargas", "skel_knight", 0)),
            bossRoom = 2, referenceSeconds = new[] { 360f, 360f, 360f, 360f }, clearXp = 700,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 50),
                new RewardEntry(EnhanceRules.Essence, 5, 9, 20),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 10),
                new RewardEntry(ConsumableDatabase.Gold, 2000, 3500, 20),
            },
            description = "마을을 불태운 마족 강경파 지휘관 바르가스의 진영.\n선봉대와 사령 부대를 뚫고 지휘 천막의 바르가스를 쓰러뜨리자.",
            specialty = "유니크 · 레전더리 장비, 장비 보호권", featureMonster = "흑철 선봉대, 사령 부대", bossName = "흑철의 바르가스",
        };

        /// <summary>[RAID] Chapter 2 mid raid: the stone-hearted gate keeper of the guardian stone chamber.</summary>
        static readonly DungeonDef GolemDef = new DungeonDef
        {
            id = RaidGolem, name = "바위 심장", theme = MapTheme.Canyon, themeName = "수호석 외곽 석실", isRaid = true,
            raidTier = RaidTier.Mid, chapter = 2, unlockQuest = "c2_golem", keyMin = 20, keyMax = 50,
            raidNumbers = RaidNumbers(28, 6200, 2.2f, 1.8f, 22, ItemRarity.Epic, 8),
            openDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnGolem1, new SpawnGroup(1, "skel_miner", 5, 2), new SpawnGroup(2, "skel_gold", 3, 2)),
                Room(MapRegistry.DgnGolem2, new SpawnGroup(1, "skel_shield", 4, 2), new SpawnGroup(2, "skel_archer", 3, 2)),
                Boss(MapRegistry.DgnGolemBoss, "boss_rock_golem", "skel_miner", 0)),
            bossRoom = 2, referenceSeconds = new[] { 330f, 330f, 330f, 330f }, clearXp = 900,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 45),
                new RewardEntry(EnhanceRules.Essence, 4, 8, 20),
                new RewardEntry(EnhanceRules.Ore, 8, 12, 15),
                new RewardEntry(ConsumableDatabase.Gold, 2500, 4000, 20),
            },
            description = "검은 수호석 석실 앞을 지키는 바위 거인.\n광부 해골과 방패 부대를 뚫고 골렘의 심장을 부숴라.",
            specialty = "유니크 · 레전더리 장비, 봉인 열쇠 조각", featureMonster = "해골 광부대, 방패 부대", bossName = "바위 심장 골렘",
        };

        /// <summary>[RAID] Chapter 2 final raid: 수호자 그라흐 inside the guardian stone chamber (Sunday).</summary>
        static readonly DungeonDef GrahDef = new DungeonDef
        {
            id = RaidGrah, name = "수호자 그라흐", theme = MapTheme.Canyon, themeName = "협곡 수호석 석실", isRaid = true,
            raidTier = RaidTier.Final, chapter = 2, unlockQuest = "c2_grah", keyCost = 100,
            raidNumbers = RaidNumbers(31, 7200, 1.9f, 1.7f, 25, ItemRarity.Epic, 10),
            openDays = new[] { DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnGrah1, new SpawnGroup(1, "skel_knight", 6, 2), new SpawnGroup(2, "skel_necro", 3, 2)),
                Boss(MapRegistry.DgnGrahBoss, "boss_grah", "skel_knight", 0)),
            bossRoom = 1, referenceSeconds = new[] { 360f, 360f, 360f, 360f }, clearXp = 1500,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 55),
                new RewardEntry(EnhanceRules.Essence, 6, 10, 15),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 2, 10),
                new RewardEntry(ConsumableDatabase.Gold, 4000, 6000, 20),
            },
            description = "검은 수호석을 지키는 마족 장수 그라흐.\n석실의 정예를 뚫고 수호자를 쓰러뜨린 뒤, 네 손으로 수호석을 깨라.",
            specialty = "레전더리 장비, 장비 보호권", featureMonster = "석실 정예 기사, 사령술사", bossName = "수호자 그라흐",
        };

        static readonly DungeonDef[] RaidDefs = { RaidDef, BargasDef, GolemDef, GrahDef };

        /// <summary>The five weekday dungeons (Monday first).</summary>
        public static IReadOnlyList<DungeonDef> Weekday => Dungeons;

        /// <summary>[RAID] Story raids in chapter order (mid, final).</summary>
        public static IReadOnlyList<DungeonDef> Raids => RaidDefs;

        public static DungeonDef SkeletonKing => RaidDef;

        public static DungeonDef Get(string id)
        {
            foreach (var r in RaidDefs) if (r.id == id) return r;
            foreach (var d in Dungeons) if (d.id == id) return d;
            return null;
        }

        /// <summary>Korean weekday name ("월요일").</summary>
        public static string DayName(DayOfWeek d) => DayNames[(int)d];

        /// <summary>One-letter weekday ("월").</summary>
        public static string DayShort(DayOfWeek d) => DayNames[(int)d].Substring(0, 1);

        static readonly string[] DayNames = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };

        /// <summary>"월" / "화" ... for the dungeon's own open days (raid: "매일").</summary>
        public static string OpenDaysLabel(DungeonDef d)
        {
            if (d.openDays.Length >= 7) return "매일";
            var parts = new List<string>();
            foreach (var day in d.openDays) parts.Add(DayShort(day));
            return d.isRaid ? string.Join("·", parts) : string.Join("·", parts) + "·토·일";
        }

        /// <summary>Display name of a reward id.</summary>
        public static string ItemName(string id)
        {
            if (id == ConsumableDatabase.Gold) return "골드";
            if (id == GearReward) return "장비";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.name;
            var c = ConsumableDatabase.Get(id);
            if (c != null) return c.name;
            if (EquipmentDatabase.IsEquipment(id)) return EquipmentDatabase.NameOfKey(id);
            return id;
        }

        /// <summary>Icon sprite key of a reward id.</summary>
        public static string ItemIcon(string id)
        {
            if (id == ConsumableDatabase.Gold) return "icon_gold";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.iconKey;
            var c = ConsumableDatabase.Get(id);
            if (c != null) return c.iconKey;
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return gear.iconKey;
            return "icon_chest";
        }
    }
}
