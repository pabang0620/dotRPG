using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Difficulty names: 일반 / 모험 / 왕 / 영웅.</summary>
    public enum DungeonDifficulty
    {
        Normal = 0,
        Adventure = 1,
        King = 2,
        Hero = 3,
        /// <summary>[CH3] 군주: weekday dungeons only, chapter 3 open and Lv41+ (DungeonDatabase.Lord.cs).</summary>
        Lord = 4,
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
        /// <summary>Chance per reward card (‰) that it turns into a Unique / Legendary piece (the jackpot card).</summary>
        public int jackpotPerMille;
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

    /// <summary>[REWARD 2026-10-14] The sure part of a dungeon clear: a material (min..max) and gold (min..max), times the difficulty reward multiplier.</summary>
    public sealed class SureReward
    {
        public string material;
        public int materialMin, materialMax, goldMin, goldMax;

        public SureReward(string material, int materialMin, int materialMax, int goldMin, int goldMax)
        {
            this.material = material;
            this.materialMin = materialMin;
            this.materialMax = materialMax;
            this.goldMin = goldMin;
            this.goldMax = goldMax;
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
        /// <summary>[RAID] Sure gold, own material and the 4 cards that are all taken (null = the old 4-cards-pick-one table in <see cref="rewards"/>).</summary>
        public RaidRewardDef raidReward;
        /// <summary>[NORMAL 2026-10-14] 일반 던전: open every day, no daily entry count (the weekday dungeons keep theirs).</summary>
        public bool always;
        /// <summary>
        /// [REWARD 2026-10-14] Clear reward of a dungeon (not raids): a sure material card and a sure gold card, then
        /// <see cref="DungeonDatabase.LuckCards"/> luck cards from <see cref="rewards"/> (gear, more materials, jackpot); all are taken.
        /// </summary>
        public SureReward sure;
        /// <summary>[CH3] 군주 only: an extra sure material put straight in the bag on a clear (not a card; the 4 cards stay).</summary>
        public LordBonus lordSure;
        /// <summary>[CH3] 군주 only: patterns the boss gains from phase 2 (DungeonDatabase.Lord.cs).</summary>
        public List<BossPattern> lordPatterns;

        public int RoomCount => rooms.Length;
    }

    /// <summary>
    /// Code table of every weekday dungeon and the raid (like <see cref="EquipmentDatabase"/>), plan §6.0-§6.2.
    /// Numbers designers tune are at the top.
    /// </summary>
    public static partial class DungeonDatabase
    {
        /// <summary>Reward id of "one piece of gear" (rolled per class and difficulty).</summary>
        public const string GearReward = "gear";
        /// <summary>Weekday dungeon entries per day (shared by all of them).</summary>
        public const int DailyEntries = 3;
        /// <summary>[REWARD 2026-10-14] Luck cards rolled from a dungeon's reward table after the sure material and gold cards.</summary>
        public const int LuckCards = 2;
        /// <summary>True when entering uses one of today's weekday dungeon entries (not raids, not 일반 던전).</summary>
        public static bool CountsEntry(DungeonDef d) => d != null && !d.isRaid && !d.always;
        /// <summary>Raid revives (shared by the party).</summary>
        public const int RaidRevives = 3;
        /// <summary>
        /// [REVIVE 2026-10-13] A party with two or more people also shares a pool: each person may revive up to the
        /// difficulty's revives, and the whole party this many more than that in total (raid: 3 each, 5 together).
        /// </summary>
        public const int PartyReviveBonus = 2;
        /// <summary>Monster HP multiplier by party size 1..4 (head-count scaling).</summary>
        public static readonly float[] PartyHpScale = { 1.0f, 1.6f, 2.1f, 2.5f };
        /// <summary>[MEASURE] An AI companion counts as this much of a person in <see cref="PartyHpScale"/> (it deals about
        /// <see cref="MercenaryDatabase.DamageScale"/> of a player's damage; counted whole, raids with AI took 2.3-3.5x longer than solo; measured 2026-10-13: three AI add only ~20% of a player's damage, so 0.1 keeps AI runs at ~0.9x solo time).</summary>
        public const float AiHpWeight = 0.15f; // [BALANCE 2026-10-14] measured AI help is 0.1-0.3 of a person (was 0.35)
        /// <summary>[MEASURE 2026-10-13] Raids: AI add no boss HP. The boss goes for the people (ThreatTable.BossAiShare), so even at 0.1
        /// a party with three AI was as slow as going alone; at 0 it is about 0.9x. Dungeons 0.35 (0.1 made AI runs 0.5x the time, 0.45 about 0.96x).</summary>
        public const float RaidAiHpWeight = 0f;

        public const string Raid = "raid_skeleton_king";
        public const string RaidBargas = "raid_bargas";
        public const string RaidGolem = "raid_golem", RaidGrah = "raid_grah";
        /// <summary>[CH3] Chapter 3 raids (Docs/ch3/RAIDS.md): 공허 침식체 지바크 (mid) and 잿빛 손 지휘관 카마츠 (final).</summary>
        public const string RaidXibak = "raid_xibak", RaidKamatz = "raid_kamatz";
        /// <summary>[RAID] Item that opens final raids.</summary>
        public const string SealKey = "key_seal";
        /// <summary>[RAID] Gear promotion material (승급): only raid rewards give it (PromoteRules).</summary>
        public const string RaidCore = "mat_core";

        // [P5] Multipliers tuned by Tools/balance/theory_balance.py: monster level growth (+12% HP, +8% damage per
        // level) already scales the higher tiers. Target: 1.2 / 1.4 / 1.6x the Normal clear time.
        // [BALANCE 2026-10-11] HP re-solved with Tools/balance/theory_dungeon.py for the career kit (spammable skills,
        // 60 s buffs): 1.2/2.0/2.7 -> 1.9/4.8/6.7. Raids are solved to ~400 s real (Docs/BALANCE_COMBAT_2026-10-11.md).
        static readonly DifficultyDef[] Difficulties =
        {
            new DifficultyDef { id = DungeonDifficulty.Normal, name = L.T("일반"), recommendedLevel = 5, recommendedPower = 1500, hpMul = 1.0f, damageMul = 1.0f, rewardMul = 1.0f, monsterLevel = 0, revives = 5, minGearRarity = ItemRarity.Common, ticketWeight = 0, jackpotPerMille = 3 },
            new DifficultyDef { id = DungeonDifficulty.Adventure, name = L.T("모험"), recommendedLevel = 12, recommendedPower = 2600, hpMul = 1.9f, damageMul = 1.05f, rewardMul = 1.6f, monsterLevel = 7, revives = 4, minGearRarity = ItemRarity.Uncommon, ticketWeight = 0, jackpotPerMille = 4 },
            new DifficultyDef { id = DungeonDifficulty.King, name = L.T("왕"), recommendedLevel = 20, recommendedPower = 4200, hpMul = 7.7f, /* [2026-10-16] was 4.8: measured 84~105 s with 3 AI, target 120~300 (Docs/BALANCE_MEASURE_2026-10-16.md) */ damageMul = 2.0f, /* [2026-10-14] was 1.25: healers/potions must matter */ rewardMul = 2.4f, monsterLevel = 15, revives = 3, minGearRarity = ItemRarity.Rare, ticketWeight = 0, jackpotPerMille = 5 },
            new DifficultyDef { id = DungeonDifficulty.Hero, name = L.T("영웅"), recommendedLevel = 27, recommendedPower = 6000, hpMul = 12f, /* [2026-10-16] was 6.7: measured 82~134 s with 3 AI, target 120~300 */ damageMul = 2.6f, /* [2026-10-14] was 1.6 */ rewardMul = 3.4f, monsterLevel = 22, revives = 2, minGearRarity = ItemRarity.Epic, ticketWeight = 6, jackpotPerMille = 6 },
            LordNumbers, // [CH3] Docs/ch3/WORLD_AND_STORY.md 8절
        };

        /// <summary>The raid has one difficulty of its own.</summary>
        public static readonly DifficultyDef RaidDifficulty = new DifficultyDef
        {
            // Tuned for a Lv27 party of 4 (monster HP x3.0 from party size) to clear in 5-10 minutes.
            id = DungeonDifficulty.Normal, name = L.T("레이드"), recommendedLevel = 25, recommendedPower = 5200, hpMul = 3.0f, damageMul = 1.6f, rewardMul = 3.0f,
            monsterLevel = 16, revives = RaidRevives, minGearRarity = ItemRarity.Rare, ticketWeight = 8,
        };

        public static int DifficultyCount => Difficulties.Length;

        public static DifficultyDef Difficulty(DungeonDifficulty d) => Difficulties[Mathf.Clamp((int)d, 0, Difficulties.Length - 1)];

        /// <summary>The difficulty numbers a run of this dungeon uses (a raid: 레이드, or 영웅 레이드 for <see cref="RaidHeroic"/>).</summary>
        public static DifficultyDef DifficultyFor(DungeonDef dungeon, DungeonDifficulty d)
        {
            if (dungeon == null || !dungeon.isRaid) return Difficulty(d);
            var raid = dungeon.raidNumbers ?? RaidDifficulty;
            return d == RaidHeroic ? Heroic(raid) : raid;
        }

        // [RAID] 영웅 레이드 (Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md D): the raid's second difficulty, open after its
        // first clear. Same rooms and boss, x1.8 HP and x1.3 damage, x1.5 sure gold and XP multiplier, one more ancient
        // core (PromoteRules), rewarded once a week apart from the raid's own reward. Server: dungeons.json raidHeroic.
        public const DungeonDifficulty RaidHeroic = DungeonDifficulty.Adventure;
        public const string RaidHeroicName = "심화 레이드"; // i18n-ignore compared raw; shown through L.T
        /// <summary>[G11 2026-10-13] 심화 레이드: twice as hard as the raid (was 1.8 / 1.3), reward 1.5x.</summary>
        public const float RaidHeroicHpMul = 2.4f, RaidHeroicDamageMul = 1.6f, RaidHeroicRewardMul = 1.5f;
        /// <summary>[G11] Boss pattern cooldowns ×, 망자의 심판 cast, 결계 의식 time × and the boss fight's time limit (then a wipe).</summary>
        public const float RaidHeroicPatternSpeed = .75f, RaidHeroicJudgmentCast = 5f, RaidHeroicSealScale = .85f, RaidHeroicTimeLimit = 480f;

        /// <summary>Heroic numbers from a raid's own (id stays Normal so the clear XP floor is the raid's).</summary>
        static DifficultyDef Heroic(DifficultyDef raid) => new DifficultyDef
        {
            id = raid.id, name = RaidHeroicName, recommendedLevel = raid.recommendedLevel, recommendedPower = Mathf.RoundToInt(raid.recommendedPower * 1.3f),
            hpMul = raid.hpMul * RaidHeroicHpMul, damageMul = raid.damageMul * RaidHeroicDamageMul, rewardMul = raid.rewardMul * RaidHeroicRewardMul,
            monsterLevel = raid.monsterLevel, revives = raid.revives, minGearRarity = raid.minGearRarity, ticketWeight = raid.ticketWeight, jackpotPerMille = raid.jackpotPerMille,
        };

        /// <summary>Sure raid gold multiplier of a run (영웅 레이드 x1.5), the same on the server (dungeonResult).</summary>
        public static float RaidRewardScale(DungeonDef dungeon, DungeonDifficulty d) => dungeon != null && dungeon.isRaid && d == RaidHeroic ? RaidHeroicRewardMul : 1f;

        /// <summary>
        /// [J10] Story raid numbers. Chapter 1's mid raid opens around 20 hours of play (Lv 22, quest 1-8), the
        /// final raid a week of keys later; chapter 2 continues to the Lv 40 cap. Party HP scaling still applies.
        /// </summary>
        static DifficultyDef RaidNumbers(int recommendedLevel, int power, float hpMul, float damageMul, int monsterLevel, ItemRarity minGear, int ticketWeight) => new DifficultyDef
        {
            id = DungeonDifficulty.Normal, name = L.T("레이드"), recommendedLevel = recommendedLevel, recommendedPower = power, hpMul = hpMul, damageMul = damageMul,
            rewardMul = RaidDifficulty.rewardMul, monsterLevel = monsterLevel, revives = RaidRevives, minGearRarity = minGear, ticketWeight = ticketWeight,
        };

        public static float PartyScale(int partySize) => PartyHpScale[Mathf.Clamp(partySize, 1, PartyHpScale.Length) - 1];

        /// <summary>Head-count scaling for <paramref name="humans"/> people and <paramref name="ai"/> AI companions: AI count as
        /// <see cref="AiHpWeight"/> of a person, between two table rows the value is interpolated. Same formula on the server.</summary>
        public static float PartyScale(int humans, int ai, bool raid = false)
        {
            float n = Mathf.Clamp(Mathf.Max(1, humans) + (raid ? RaidAiHpWeight : AiHpWeight) * Mathf.Max(0, ai), 1f, PartyHpScale.Length);
            int lo = Mathf.FloorToInt(n);
            if (lo >= PartyHpScale.Length) return PartyHpScale[PartyHpScale.Length - 1];
            return Mathf.Lerp(PartyHpScale[lo - 1], PartyHpScale[lo], n - lo);
        }

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

        static readonly DungeonDef[] Dungeons = WithLord(new[]
        {
            new DungeonDef
            {
                id = "gold_vein", name = L.T("황금 광맥"), theme = MapTheme.Canyon, themeName = L.T("협곡 광산"),
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
                    new RewardEntry(GearReward, 1, 1, 50),
                },
                sure = new SureReward(EnhanceRules.Bone, 4, 8, 600, 1000),
                description = L.T("협곡 아래 황금 광맥에 해골 광부들이 몰려들었다.\n황금 해골은 도망치며 금화를 흩뿌린다.\n옛 노래가 말하는 다섯 집 가운데 재규어의 집이라고도 한다."),
                specialty = L.T("골드"), featureMonster = L.T("황금 해골 (도망치며 골드를 뿌림)"), bossName = L.T("황금 광부장 타킨"),
            },
            new DungeonDef
            {
                id = "smelter", name = L.T("버려진 제련소"), theme = MapTheme.Canyon, themeName = L.T("협곡 광산"),
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
                    new RewardEntry(GearReward, 1, 1, 50),
                },
                sure = new SureReward(EnhanceRules.Ore, 3, 6, 200, 350),
                description = L.T("불이 꺼진 제련소에 해골 광부들이 곡괭이를 들고 모여 있다.\n해골 광부는 멀리서 돌진해 온다.\n옛 노래가 말하는 다섯 집 가운데 불의 집이라고도 한다."),
                specialty = L.T("강화석"), featureMonster = L.T("해골 광부 (돌진)"), bossName = L.T("해골대장 첸"),
            },
            new DungeonDef
            {
                id = "mana_graveyard", name = L.T("마력의 묘지"), theme = MapTheme.Forest, themeName = L.T("숲 묘지"),
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
                    new RewardEntry(GearReward, 1, 1, 50),
                },
                sure = new SureReward(EnhanceRules.Essence, 1, 3, 200, 350),
                description = L.T("마력이 고인 숲속 묘지. 해골 사령술사가 멀리서 주문을 쏘고\n쓰러진 해골을 다시 일으킨다.\n옛 노래가 말하는 다섯 집 가운데 어둠의 집이라고도 한다."),
                specialty = L.T("마력 정수"), featureMonster = L.T("해골 사령술사 (원거리, 해골 소환)"), bossName = L.T("묘지기 리치 무크"),
            },
            new DungeonDef
            {
                id = "training_forest", name = L.T("수련의 숲"), theme = MapTheme.Forest, themeName = L.T("숲 묘지"),
                openDays = new[] { DayOfWeek.Thursday },
                rooms = Chain(
                    Room(MapRegistry.DgnForest3, new SpawnGroup(1, "skel_archer", 3), new SpawnGroup(2, "skel_warrior", 2)),
                    Room(MapRegistry.DgnForest2, new SpawnGroup(1, "skel_archer", 3, 1), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnForest1, new SpawnGroup(1, "skel_archer", 3, 1), new SpawnGroup(2, "skel_shield", 2)),
                    Boss(MapRegistry.DgnForestBoss, "boss_archer_chief", "skel_archer", 2)),
                bossRoom = 3, referenceSeconds = new[] { 160f, 180f, 200f, 220f }, clearXp = 140, xpMul = 1.5f,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 200, 400, 35),
                    new RewardEntry(EnhanceRules.Bone, 5, 10, 30),
                    new RewardEntry(ConsumableDatabase.HpPotion, 2, 4, 20),
                    new RewardEntry(EnhanceRules.Ore, 1, 3, 9),
                    new RewardEntry(GearReward, 1, 1, 50),
                },
                sure = new SureReward(EnhanceRules.Bone, 5, 10, 250, 450),
                description = L.T("숲의 수련장에 해골 궁수들이 진을 쳤다.\n클리어 경험치가 크게 늘어나는 수련 던전.\n옛 노래가 말하는 다섯 집 가운데 칼의 집이라고도 한다."),
                specialty = L.T("경험치"), featureMonster = L.T("해골 궁수 (원거리)"), bossName = L.T("해골 사수장 훌란"),
            },
            new DungeonDef
            {
                id = "armory", name = L.T("망자의 무기고"), theme = MapTheme.Winter, themeName = L.T("겨울 동굴"),
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
                sure = new SureReward(EnhanceRules.Ore, 2, 4, 250, 450),
                description = L.T("얼어붙은 동굴 깊은 곳, 망자들이 지키는 옛 무기고.\n해골 방패병은 정면 공격을 막아 낸다.\n옛 노래가 말하는 다섯 집 가운데 추위의 집이라고도 한다."),
                specialty = L.T("장비"), featureMonster = L.T("해골 방패병 (정면 방어, 슈퍼아머)"), bossName = L.T("수문장 파칼"),
            },
        });

        static readonly DayOfWeek[] EveryDay =
            { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };

        /// <summary>
        /// [NORMAL 2026-10-14] 일반 던전: open every day without an entry count, the steady growth source next to the
        /// quests and the hunting grounds (user request). Same rooms and bosses as the weekday dungeons of their theme,
        /// XP and gear per minute match hunting at the recommended level (HuntingGrounds.DungeonClearFloor without the weekday
        /// bonus; a luck card is gear about half the time to make up the fewer kills than a field). The four difficulties cover Lv5 to Lv27+.
        /// </summary>
        static readonly DungeonDef[] NormalDefs =
        {
            new DungeonDef
            {
                id = "normal_canyon", name = L.T("해골 광산 소굴"), theme = MapTheme.Canyon, themeName = L.T("협곡 광산"), always = true,
                openDays = EveryDay,
                rooms = Chain(
                    Room(MapRegistry.DgnCanyon1, new SpawnGroup(1, "skel_warrior", 3), new SpawnGroup(2, "skel_miner", 2)),
                    Room(MapRegistry.DgnCanyon2, new SpawnGroup(1, "skel_miner", 3), new SpawnGroup(2, "skel_warrior", 2, 1)),
                    Room(MapRegistry.DgnCanyon3, new SpawnGroup(1, "skel_warrior", 3), new SpawnGroup(2, "skel_shield", 1, 1)),
                    Boss(MapRegistry.DgnCanyonBoss, "boss_mine_captain", "skel_warrior", 2)),
                bossRoom = 3, referenceSeconds = new[] { 150f, 170f, 190f, 210f }, clearXp = 20,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 150, 300, 40),
                    new RewardEntry(EnhanceRules.Bone, 3, 6, 30),
                    new RewardEntry(EnhanceRules.Ore, 1, 3, 20),
                    new RewardEntry(GearReward, 1, 1, 90),
                },
                sure = new SureReward(EnhanceRules.Bone, 3, 6, 150, 300),
                description = L.T("협곡 광산 깊은 곳에 늘 해골이 들끓는 소굴.\n매일 들어갈 수 있고 입장 횟수 제한이 없다."),
                specialty = L.T("매일 · 경험치·재료"), featureMonster = L.T("해골 광부 (돌진)"), bossName = L.T("해골대장 첸"),
            },
            new DungeonDef
            {
                id = "normal_forest", name = L.T("안개 낀 묘지"), theme = MapTheme.Forest, themeName = L.T("숲 묘지"), always = true,
                openDays = EveryDay,
                rooms = Chain(
                    Room(MapRegistry.DgnForest1, new SpawnGroup(1, "skel_warrior", 3), new SpawnGroup(2, "skel_archer", 2)),
                    Room(MapRegistry.DgnForest2, new SpawnGroup(1, "skel_archer", 2, 1), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnForest3, new SpawnGroup(1, "skel_necro", 2, 1), new SpawnGroup(2, "skel_warrior", 2)),
                    Boss(MapRegistry.DgnForestBoss, "boss_lich", "skel_warrior", 2)),
                bossRoom = 3, referenceSeconds = new[] { 160f, 180f, 200f, 220f }, clearXp = 20,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 150, 300, 40),
                    new RewardEntry(EnhanceRules.Essence, 1, 2, 25),
                    new RewardEntry(EnhanceRules.Bone, 3, 6, 25),
                    new RewardEntry(GearReward, 1, 1, 90),
                },
                sure = new SureReward(EnhanceRules.Bone, 3, 6, 150, 300),
                description = L.T("안개가 걷히지 않는 숲속 묘지. 해골 궁수와 사령술사가 지킨다.\n매일 들어갈 수 있고 입장 횟수 제한이 없다."),
                specialty = L.T("매일 · 경험치·재료"), featureMonster = L.T("해골 궁수 (원거리)"), bossName = L.T("묘지기 리치 무크"),
            },
            new DungeonDef
            {
                id = "normal_winter", name = L.T("얼어붙은 회랑"), theme = MapTheme.Winter, themeName = L.T("겨울 동굴"), always = true,
                openDays = EveryDay,
                rooms = Chain(
                    Room(MapRegistry.DgnWinter1, new SpawnGroup(1, "skel_warrior", 3), new SpawnGroup(2, "skel_shield", 2)),
                    Room(MapRegistry.DgnWinter2, new SpawnGroup(1, "skel_shield", 2, 1), new SpawnGroup(2, "skel_warrior", 3)),
                    Room(MapRegistry.DgnWinter3, new SpawnGroup(1, "skel_knight", 2, 1), new SpawnGroup(2, "skel_warrior", 2)),
                    Boss(MapRegistry.DgnWinterBoss, "boss_armory_warden", "skel_warrior", 2)),
                bossRoom = 3, referenceSeconds = new[] { 170f, 190f, 210f, 230f }, clearXp = 20,
                rewards = new[]
                {
                    new RewardEntry(ConsumableDatabase.Gold, 150, 300, 40),
                    new RewardEntry(EnhanceRules.Ore, 1, 3, 25),
                    new RewardEntry(EnhanceRules.Bone, 3, 6, 25),
                    new RewardEntry(GearReward, 1, 1, 90),
                },
                sure = new SureReward(EnhanceRules.Ore, 1, 3, 150, 300),
                description = L.T("얼음에 갇힌 옛 회랑에 해골 방패병과 기사가 서성인다.\n매일 들어갈 수 있고 입장 횟수 제한이 없다."),
                specialty = L.T("매일 · 경험치·재료"), featureMonster = L.T("해골 방패병 (정면 방어, 슈퍼아머)"), bossName = L.T("수문장 파칼"),
            },
        };

        static readonly DungeonDef RaidDef = new DungeonDef
        {
            id = Raid, name = L.T("해골왕"), theme = MapTheme.Canyon, themeName = L.T("북쪽 고개 성채"), isRaid = true,
            // [RAID] Chapter 1 mid raid: three gates a week, a seal key fragment chance on each rewarded clear.
            raidTier = RaidTier.Mid, chapter = 1, unlockQuest = "c1_fortress", keyMin = 20, keyMax = 50,
            raidNumbers = RaidNumbers(20, 4000, 5.7f, 1.4f, 12, ItemRarity.Epic, 0),
            openDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnRaid1, new SpawnGroup(1, "skel_knight", 4, 2), new SpawnGroup(2, "skel_shield", 2, 2)),
                Room(MapRegistry.DgnRaid2, new SpawnGroup(1, "skel_necro", 3, 2), new SpawnGroup(2, "skel_knight", 3, 2)),
                Boss(MapRegistry.DgnRaidBoss, "boss_skeleton_king", "skel_knight", 0)),
            bossRoom = 2, referenceSeconds = new[] { 300f, 450f, 300f, 300f }, clearXp = 400,
            rewards = new RewardEntry[0], raidReward = RaidRewards.King, // [RAID] sure gold + 4 cards, see RaidRewards
            description = L.T("북쪽 고개 너머에서 깨어난 해골왕 에칸과 그의 친위대.\n친위대 전초 두 곳을 돌파하고 에칸을 쓰러뜨리자."),
            specialty = L.T("확정 골드 · 에픽 이상 장비"), featureMonster = L.T("해골 기사단, 해골 사령술사"), bossName = L.T("해골왕 에칸"),
        };

        /// <summary>[RAID] Chapter 1 final raid: 흑철의 바르가스, Sunday only, costs seal key fragments.</summary>
        static readonly DungeonDef BargasDef = new DungeonDef
        {
            id = RaidBargas, name = L.T("흑철의 바르가스"), theme = MapTheme.Winter, themeName = L.T("흑철 진영"), isRaid = true,
            raidTier = RaidTier.Final, chapter = 1, unlockQuest = "c1_bargas", keyCost = 60,
            raidNumbers = RaidNumbers(40, 9600, 6.6f, 1.6f, 33, ItemRarity.Epic, 8),
            openDays = new[] { DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnBargas1, new SpawnGroup(1, "skel_knight", 5, 2), new SpawnGroup(2, "skel_archer", 3, 2)),
                Room(MapRegistry.DgnBargas2, new SpawnGroup(1, "skel_shield", 3, 2), new SpawnGroup(2, "skel_necro", 3, 2)),
                Boss(MapRegistry.DgnBargasBoss, "boss_bargas", "skel_knight", 0)),
            bossRoom = 2, referenceSeconds = new[] { 360f, 540f, 360f, 360f }, clearXp = 700,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 50),
                new RewardEntry(EnhanceRules.Essence, 5, 9, 20),
                new RewardEntry(ConsumableDatabase.ProtectTicket, 1, 1, 10),
                new RewardEntry(ConsumableDatabase.Gold, 2000, 3500, 20),
            },
            description = L.T("마을을 불태운 마족 강경파 지휘관 바르가스의 진영.\n선봉대와 사령 부대를 뚫고 지휘 천막의 바르가스를 쓰러뜨리자."),
            specialty = L.T("유니크 · 레전더리 장비, 장비 보호권"), featureMonster = L.T("흑철 선봉대, 사령 부대"), bossName = L.T("흑철의 바르가스"),
        };

        /// <summary>[RAID] Chapter 2 mid raid: the stone-hearted gate keeper of the guardian stone chamber.</summary>
        static readonly DungeonDef GolemDef = new DungeonDef
        {
            id = RaidGolem, name = L.T("바위 심장"), theme = MapTheme.Canyon, themeName = L.T("수호석 외곽 석실"), isRaid = true,
            raidTier = RaidTier.Mid, chapter = 2, unlockQuest = "c2_golem", keyMin = 20, keyMax = 50,
            raidNumbers = RaidNumbers(30, 6600, 4.4f, 1.8f, 23, ItemRarity.Epic, 8),
            openDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnGolem1, new SpawnGroup(1, "skel_miner", 5, 2), new SpawnGroup(2, "skel_gold", 3, 2)),
                Room(MapRegistry.DgnGolem2, new SpawnGroup(1, "skel_shield", 4, 2), new SpawnGroup(2, "skel_archer", 3, 2)),
                Boss(MapRegistry.DgnGolemBoss, "boss_rock_golem", "skel_miner", 0)),
            bossRoom = 2, referenceSeconds = new[] { 330f, 495f, 330f, 330f }, clearXp = 900,
            rewards = new[]
            {
                new RewardEntry(GearReward, 1, 1, 45),
                new RewardEntry(EnhanceRules.Essence, 4, 8, 20),
                new RewardEntry(EnhanceRules.Ore, 8, 12, 15),
                new RewardEntry(ConsumableDatabase.Gold, 2500, 4000, 20),
            },
            description = L.T("검은 수호석 석실 앞을 지키는 바위 거인 카브란.\n광부 해골과 방패 부대를 뚫고 카브란의 심장을 부숴라."),
            specialty = L.T("유니크 · 레전더리 장비, 봉인 열쇠 조각"), featureMonster = L.T("해골 광부대, 방패 부대"), bossName = L.T("바위 심장 카브란"),
        };

        /// <summary>[RAID] Chapter 2 final raid: 수호자 그라흐 inside the guardian stone chamber (Sunday).</summary>
        static readonly DungeonDef GrahDef = new DungeonDef
        {
            id = RaidGrah, name = L.T("수호자 그라흐"), theme = MapTheme.Canyon, themeName = L.T("협곡 수호석 석실"), isRaid = true,
            raidTier = RaidTier.Final, chapter = 2, unlockQuest = "c2_grah", keyCost = 60,
            raidNumbers = RaidNumbers(40, 10400, 4.6f, 1.7f, 35, ItemRarity.Epic, 0),
            openDays = new[] { DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnGrah1, new SpawnGroup(1, "skel_knight", 6, 2), new SpawnGroup(2, "skel_necro", 3, 2)),
                Boss(MapRegistry.DgnGrahBoss, "boss_grah", "skel_knight", 0)),
            bossRoom = 1, referenceSeconds = new[] { 360f, 540f, 360f, 360f }, clearXp = 1500,
            rewards = new RewardEntry[0], raidReward = RaidRewards.Grah, // [RAID] sure gold + 4 cards, see RaidRewards
            description = L.T("검은 수호석을 지키는 마족 장수 그라흐.\n석실의 정예를 뚫고 수호자를 쓰러뜨린 뒤, 네 손으로 수호석을 깨라."),
            specialty = L.T("확정 골드 · 레전더리 장비"), featureMonster = L.T("석실 정예 기사, 사령술사"), bossName = L.T("수호자 그라흐"),
        };

        /// <summary>[CH3] Chapter 3 mid raid: 공허 침식체 지바크 at the broken guardian stone (Wed / Sat / Sun, Docs/ch3/RAIDS.md 1).</summary>
        static readonly DungeonDef XibakDef = new DungeonDef
        {
            id = RaidXibak, name = L.T("공허 침식체 지바크"), theme = MapTheme.DryCanyon, themeName = L.T("깨진 수호석 자리"), isRaid = true,
            raidTier = RaidTier.Mid, chapter = 3, unlockQuest = "c3_xibak", keyMin = 20, keyMax = 50,
            raidNumbers = RaidNumbers(45, 13000, 5.2f, 1.8f, 40, ItemRarity.Epic, 8),
            openDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnXibak1, new SpawnGroup(1, "void_sentry", 5, 2), new SpawnGroup(2, "void_caller", 3, 2)),
                Room(MapRegistry.DgnXibak2, new SpawnGroup(1, "void_hound", 6, 2), new SpawnGroup(2, "grey_ranger", 3, 2)),
                Boss(MapRegistry.DgnXibakBoss, "boss_xibak", "void_hound", 0)),
            bossRoom = 2, referenceSeconds = new[] { 330f, 495f, 330f, 330f }, clearXp = 1200,
            rewards = new RewardEntry[0], raidReward = RaidRewards.Xibak, // [RAID] sure gold + 4 cards, see RaidRewards
            description = L.T("깨진 수호석 자리에서 자라난 공허의 덩어리 지바크.\n열리는 균열을 메우며 침식체를 쓰러뜨리자."),
            specialty = L.T("확정 골드 · 침식 결정 · 봉인 열쇠 조각"), featureMonster = L.T("공허 파수석, 공허 들개, 잿빛 손 정찰병"), bossName = L.T("공허 침식체 지바크"),
        };

        /// <summary>[CH3] Chapter 3 final raid: 잿빛 손 지휘관 카마츠, the rescue of 발체 (Sunday, Docs/ch3/RAIDS.md 2).</summary>
        static readonly DungeonDef KamatzDef = new DungeonDef
        {
            id = RaidKamatz, name = L.T("잿빛 손 지휘관 카마츠"), theme = MapTheme.DryCanyon, themeName = L.T("잿빛 손 진영"), isRaid = true,
            raidTier = RaidTier.Final, chapter = 3, unlockQuest = "c3_kamatz", keyCost = 60,
            raidNumbers = RaidNumbers(50, 15500, 5.0f, 1.9f, 45, ItemRarity.Epic, 0),
            openDays = new[] { DayOfWeek.Sunday },
            rooms = Chain(
                Room(MapRegistry.DgnKamatz1, new SpawnGroup(1, "grey_knight", 5, 2), new SpawnGroup(2, "grey_priest", 2, 2), new SpawnGroup(2, "grey_ranger", 2, 2)),
                Boss(MapRegistry.DgnKamatzBoss, "boss_kamatz", "grey_knight", 0)),
            bossRoom = 1, referenceSeconds = new[] { 360f, 540f, 360f, 360f }, clearXp = 2000,
            rewards = new RewardEntry[0], raidReward = RaidRewards.Kamatz, // [RAID] sure gold + 4 cards, see RaidRewards
            description = L.T("공허의 틈 끝, 잿빛 손 진영의 지휘관 카마츠.\n사제부터 쓰러뜨리고 지휘관을 꺾어 발체를 구하자."),
            specialty = L.T("확정 골드 · 잿빛 인장 · 레전더리 장비"), featureMonster = L.T("잿빛 손 기사, 사제, 정찰병"), bossName = L.T("잿빛 손 지휘관 카마츠"),
        };

        /// <summary>
        /// Raids that are open now: the level-20 mid raid (해골왕) and the level-40 final raid (그라흐), then chapter 3's
        /// 지바크 (Lv45) and 카마츠 (Lv50). 흑철의 바르가스 and 바위 심장 are kept for a later release (<see cref="Upcoming"/>).
        /// </summary>
        static readonly DungeonDef[] RaidDefs = { RaidDef, GrahDef, XibakDef, KamatzDef };

        /// <summary>[RAID] Designed but not open yet (not listed, not exported to the server).</summary>
        public static readonly IReadOnlyList<DungeonDef> Upcoming = new[] { BargasDef, GolemDef };

        /// <summary>The five weekday dungeons (Monday first).</summary>
        public static IReadOnlyList<DungeonDef> Weekday => Dungeons;

        /// <summary>[NORMAL] 일반 던전 (every day, no entry count).</summary>
        public static IReadOnlyList<DungeonDef> Normal => NormalDefs;

        /// <summary>Weekday and normal dungeons (everything that is not a raid).</summary>
        public static IEnumerable<DungeonDef> NonRaid => Dungeons.Concat(NormalDefs);

        /// <summary>[RAID] Story raids in chapter order (mid, final) that this session can see: chapter 3's only while it is open.</summary>
        public static IReadOnlyList<DungeonDef> Raids => Chapter3.Enabled ? RaidDefs : ClosedRaids;

        static readonly DungeonDef[] ClosedRaids = RaidDefs.Where(r => r.chapter < 3).ToArray();

        /// <summary>[CH3] Every story raid whatever the chapter lock (data export, measure runs).</summary>
        public static IReadOnlyList<DungeonDef> AllRaids => RaidDefs;

        public static DungeonDef SkeletonKing => RaidDef;

        public static DungeonDef Get(string id)
        {
            foreach (var r in RaidDefs) if (r.id == id) return r;
            foreach (var d in Dungeons) if (d.id == id) return d;
            foreach (var d in NormalDefs) if (d.id == id) return d;
            return null;
        }

        /// <summary>Korean weekday name ("월요일").</summary>
        public static string DayName(DayOfWeek d) => DayNames[(int)d];

        /// <summary>Short weekday ("월", "Mon", "月"): its own table entry "day|월" per language.</summary>
        public static string DayShort(DayOfWeek d) => L.T(DayShortKo[(int)d], "day");

        static readonly string[] DayShortKo = { "일", "월", "화", "수", "목", "금", "토" }; // i18n-ignore translated with context "day"

        static readonly string[] DayNames = { L.T("일요일"), L.T("월요일"), L.T("화요일"), L.T("수요일"), L.T("목요일"), L.T("금요일"), L.T("토요일") };

        /// <summary>"월" / "화" ... for the dungeon's own open days (raid: "매일").</summary>
        public static string OpenDaysLabel(DungeonDef d)
        {
            if (d.openDays.Length >= 7) return L.T("매일");
            var parts = new List<string>();
            foreach (var day in d.openDays) parts.Add(DayShort(day));
            return d.isRaid ? string.Join("·", parts) : string.Join("·", parts) + "·" + DayShort(DayOfWeek.Saturday) + "·" + DayShort(DayOfWeek.Sunday);
        }

        /// <summary>Display name of a reward id, in the player's language.</summary>
        public static string ItemName(string id)
        {
            if (id == ConsumableDatabase.Gold) return L.T("골드");
            if (id == GearReward) return L.T("장비");
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return L.T(mat.name);
            var c = ConsumableDatabase.Get(id);
            if (c != null) return L.T(c.name);
            if (EquipmentDatabase.IsEquipment(id)) return L.T(EquipmentDatabase.NameOfKey(id));
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
