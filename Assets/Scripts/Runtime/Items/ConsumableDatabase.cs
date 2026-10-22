using System;
using System.Collections.Generic;

namespace DotRPG
{
    public enum ConsumableKind
    {
        /// <summary>Gold: shown as money, never as a bag item.</summary>
        Currency,
        /// <summary>Restores a share of max HP.</summary>
        HealHp,
        /// <summary>Restores a share of max MP.</summary>
        HealMp,
        /// <summary>Teleports back to the village.</summary>
        TownScroll,
        /// <summary>
        /// Used up on its own when a failed enhancement would destroy gear (the gear survives at +0).
        /// Sits in the bag's "기타" tab and cannot be used by hand or by a quick key.
        /// </summary>
        Protection,
        /// <summary>[RAID] Seal key fragment: opens final raids. Sits in the "기타" tab, never used by hand.</summary>
        Key,
        /// <summary>[SWEEP] Dungeon clear ticket: kept in the account wallet on the server, never in the bag.</summary>
        Sweep,
        /// <summary>[CASH] Timed buff scroll (power = % more damage, minutes in <see cref="ConsumableItem.minutes"/>).</summary>
        Buff,
        /// <summary>[CASH] +N enhancement ticket (power = N): used on a piece of gear at the forge, never fails.</summary>
        EnhanceTicket,
        /// <summary>[CASH] "+N 강화권 상자 (P%)": the server opens it (power = N, chance = P).</summary>
        LuckBox,
        /// <summary>[CASH] 봉인된 상자 kept in the bag (pass rewards): opened by the server with the shop table.</summary>
        SealedBox,
        /// <summary>[DUNGEON 2026-10-14] 요일 던전 초기화권: today's weekday dungeon entries back to the full count (the server records it).</summary>
        DungeonReset,
    }

    /// <summary>Money and usable items (bag "소모품" tab, quick-use keys Q / R / T) plus the protection ticket.</summary>
    public sealed class ConsumableItem
    {
        public string id, name, iconKey, description;
        public ConsumableKind kind;
        /// <summary>% of max HP / MP restored.</summary>
        public int power;
        public GameAction? hotkey;
        /// <summary>Grade colour for the name and the icon frame (null = plain item).</summary>
        public ItemRarity? grade;
        /// <summary>[CASH] Buff length in minutes; % chance of a luck box.</summary>
        public int minutes, chance;
    }

    public static class ConsumableDatabase
    {
        public const string Gold = "gold";
        public const string HpPotion = "potion_hp";
        public const string MpPotion = "potion_mp";
        public const string TownScroll = "scroll_town";
        public const string ProtectTicket = "ticket_protect";
        // [CASH] Docs/PLAN_CASH_BOX_PASS.md
        /// <summary>[DUNGEON 2026-10-14] 요일 던전 초기화권 (account-bound, from mail or events).</summary>
        public const string DungeonResetTicket = "ticket_dungeon_reset";
        public const string HpPotionHi = "potion_hp_hi", MpPotionHi = "potion_mp_hi", PowerScroll = "scroll_power", SealedBox = "box_sealed";
        public static string EnhanceTicket(int level) => "ticket_enh" + level;
        public static string LuckBox(int level, int chance) => $"box_enh{level}_{chance}";

        static readonly List<ConsumableItem> Items = new List<ConsumableItem>
        {
            new ConsumableItem { id = Gold, name = L.T("골드"), iconKey = "icon_gold", kind = ConsumableKind.Currency,
                description = L.T("마을 상점에서 쓰는 돈. 해골을 쓰러뜨리거나 물건을 팔아서 모은다.") },
            new ConsumableItem { id = HpPotion, name = L.T("체력 물약"), iconKey = "icon_potion_hp", kind = ConsumableKind.HealHp, power = 40, hotkey = GameAction.UseItem,
                description = L.T("붉은 약초를 달인 물약. 마시면 최대 HP의 40%를 회복한다.") },
            new ConsumableItem { id = MpPotion, name = L.T("마나 물약"), iconKey = "icon_potion_mp", kind = ConsumableKind.HealMp, power = 50, hotkey = GameAction.UseMana,
                description = L.T("푸른 샘물로 만든 물약. 마시면 최대 MP의 50%를 회복한다.") },
            new ConsumableItem { id = TownScroll, name = L.T("마을 귀환 주문서"), iconKey = "icon_scroll", kind = ConsumableKind.TownScroll, hotkey = GameAction.TownScroll,
                description = L.T("펼치면 빛에 감싸여 작은 마을 광장으로 돌아간다. 사냥터에서 쓰면 편리하다.") },
            new ConsumableItem { id = ProtectTicket, name = L.T("장비 보호권"), iconKey = "icon_ticket", kind = ConsumableKind.Protection, grade = ItemRarity.Unique,
                description = L.T("강화 실패로 장비가 파괴될 때 자동으로 소모되어 장비를 지킨다. 지켜진 장비는 +0으로 초기화된다.") },
            new ConsumableItem { id = DungeonDatabase.SealKey, name = L.T("봉인 열쇠 조각"), iconKey = "icon_key", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = L.T("중간 레이드 보스가 지키던 봉인의 파편. 모으면 챕터 최종 레이드의 문이 열린다. (최종 레이드 클리어 시 소모)") },
            new ConsumableItem { id = DungeonDatabase.RaidCore, name = L.T("고대의 핵"), iconKey = "icon_core", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = L.T("레이드 보스에게서만 얻는 고대 마력의 핵. 대장간에서 장비를 한 등급 위 장비로 승급할 때 쓴다. (강화 수치 유지)") },
            // [RAID] Raid-only materials (Docs/server/phase13_raid_rewards.md §4): paid for weapon boxes in the raid shop.
            new ConsumableItem { id = RaidRewards.RaidMatKing, name = L.T("해골왕의 왕관 조각"), iconKey = "maticon_bone", kind = ConsumableKind.Key, grade = ItemRarity.Unique,
                description = L.T("해골왕이 쓰고 있던 왕관의 파편. 해골왕 레이드 보상 카드로만 얻고, 레이드 상점에서 해골왕 무기 상자로 바꾼다.\n레이드 상점에서 무기 상자로 바꿉니다.") },
            new ConsumableItem { id = RaidRewards.RaidMatGrah, name = L.T("수호석 파편"), iconKey = "maticon_ore", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = L.T("수호자 그라흐가 지키던 수호석의 파편. 그라흐 레이드 보상 카드로만 얻고, 레이드 상점에서 그라흐 무기 상자로 바꾼다.\n레이드 상점에서 무기 상자로 바꿉니다.") },
            // [CH3] Chapter 3 raid materials (Docs/ch3/RAIDS.md). Stand-in icons until ART_LIST 6절 adds their own.
            new ConsumableItem { id = RaidRewards.RaidMatXibak, name = L.T("침식 결정"), iconKey = "maticon_ore", kind = ConsumableKind.Key, grade = ItemRarity.Unique,
                description = L.T("공허 침식체 시바크의 몸에서 굳은 결정. 시바크 레이드 보상 카드로만 얻고, 레이드 상점의 시바크 무기 상자와 장비 계승에 쓴다.") },
            new ConsumableItem { id = RaidRewards.RaidMatKamatz, name = L.T("잿빛 인장"), iconKey = "maticon_bone", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = L.T("잿빛 손 지휘관 카마츠가 명령서에 찍던 인장. 카마츠 레이드 보상 카드로만 얻고, 레이드 상점의 카마츠 무기 상자와 장비 계승에 쓴다.") },
            new ConsumableItem { id = DungeonSweep.TicketItem, name = L.T("던전 클리어권"), iconKey = "icon_sweep", kind = ConsumableKind.Sweep, grade = ItemRarity.Epic,
                description = L.T("직접 B등급 이상으로 깬 요일 던전을 전투 없이 한 번 끝낸다. 입장 횟수 1회를 함께 쓰고, 보상은 기본 경험치와 카드 1장이다. (계정 공용)") },
            new ConsumableItem { id = DungeonSweep.EventTicketItem, name = L.T("이벤트 클리어권"), iconKey = "icon_sweep_event", kind = ConsumableKind.Sweep, grade = ItemRarity.Unique,
                description = L.T("던전 클리어권과 같지만 받은 날부터 14일 안에 써야 한다. 기한이 가까운 것부터 먼저 쓰인다. (계정 공용)") },
            // [CASH] Sealed box rewards and their luck boxes (Docs/PLAN_CASH_BOX_PASS.md).
            new ConsumableItem { id = HpPotionHi, name = L.T("상급 체력 물약"), iconKey = "icon_potion_hp_hi", kind = ConsumableKind.HealHp, power = 70, grade = ItemRarity.Rare,
                description = L.T("진하게 달인 물약. 마시면 최대 HP의 70%를 회복한다. 물약 단축키는 일반 물약을 다 쓴 뒤에 이것을 쓴다.") },
            new ConsumableItem { id = MpPotionHi, name = L.T("상급 마나 물약"), iconKey = "icon_potion_mp_hi", kind = ConsumableKind.HealMp, power = 70, grade = ItemRarity.Rare,
                description = L.T("진하게 달인 마나 물약. 마시면 최대 MP의 70%를 회복한다. 마나 단축키는 일반 마나 물약을 다 쓴 뒤에 이것을 쓴다.") },
            new ConsumableItem { id = PowerScroll, name = L.T("투지의 주문서"), iconKey = "icon_scroll_power", kind = ConsumableKind.Buff, power = 15, minutes = 30, grade = ItemRarity.Epic,
                description = L.T("펼치면 30분 동안 공격 피해가 15% 늘어난다. 다시 쓰면 남은 시간이 30분으로 갱신된다.") },
            Ticket(10, ItemRarity.Epic), Ticket(12, ItemRarity.Unique), Ticket(13, ItemRarity.Legendary), Ticket(15, ItemRarity.Legendary),
            Luck(10, 30, ItemRarity.Rare), Luck(12, 10, ItemRarity.Epic), Luck(12, 50, ItemRarity.Unique), Luck(15, 50, ItemRarity.Legendary),
            new ConsumableItem { id = DungeonResetTicket, name = L.T("요일 던전 초기화권"), iconKey = "icon_ticket", kind = ConsumableKind.DungeonReset, grade = ItemRarity.Epic,
                description = L.T("쓰면 오늘 요일 던전 입장 횟수가 처음(3회)으로 돌아간다. 입장 횟수를 하나도 쓰지 않았으면 쓸 수 없다. (계정 공용)") },
            new ConsumableItem { id = SealedBox, name = L.T("봉인된 상자"), iconKey = "icon_box_sealed", kind = ConsumableKind.SealedBox, grade = ItemRarity.Epic,
                description = L.T("캐시샵의 봉인된 상자와 같은 상자. 열면 확률표에 따라 보상 하나가 나온다. 봉인 해제 게이지도 함께 찬다.") },
        };

        static ConsumableItem Ticket(int level, ItemRarity grade) => new ConsumableItem
        {
            id = EnhanceTicket(level), name = L.F("+{0} 강화권", level), iconKey = "icon_ticket_enh" + level, kind = ConsumableKind.EnhanceTicket, power = level, grade = grade,
            description = L.F("대장간에서 장비 하나의 강화 단계를 +{0}로 바로 올린다. 실패하지 않는다. 이미 +{0} 이상인 장비에는 쓸 수 없다.", level),
        };

        static ConsumableItem Luck(int level, int chance, ItemRarity grade) => new ConsumableItem
        {
            id = LuckBox(level, chance), name = L.F("+{0} 강화권 상자 ({1}%)", level, chance), iconKey = level >= 15 ? "icon_box_enh_gold" : "icon_box_enh", kind = ConsumableKind.LuckBox,
            power = level, chance = chance, grade = grade,
            description = L.F("열면 {0}% 확률로 +{1} 강화권이 나온다. 아니면 마력 정수 20개.", chance, level),
        };

        /// <summary>[CASH] The sealed-box family: high potions, the buff scroll, enhancement tickets and boxes.</summary>
        public static IEnumerable<ConsumableItem> Cash
        {
            get { foreach (var i in Items) if (i.kind == ConsumableKind.Buff || i.kind == ConsumableKind.EnhanceTicket || i.kind == ConsumableKind.LuckBox || i.kind == ConsumableKind.SealedBox || i.kind == ConsumableKind.DungeonReset || i.id == HpPotionHi || i.id == MpPotionHi) yield return i; }
        }

        /// <summary>Usable items in bag order (money and the protection ticket excluded).</summary>
        public static IEnumerable<ConsumableItem> Usable
        {
            get { foreach (var i in Items) if (IsUsableKind(i.kind)) yield return i; }
        }

        /// <summary>Items that work on their own from the bag ("기타" tab): the equipment protection ticket.</summary>
        public static IEnumerable<ConsumableItem> Tickets
        {
            get { foreach (var i in Items) if (i.kind == ConsumableKind.Protection || i.kind == ConsumableKind.Key || i.kind == ConsumableKind.EnhanceTicket) yield return i; }
        }

        static bool IsUsableKind(ConsumableKind kind) => kind != ConsumableKind.Currency && kind != ConsumableKind.Protection && kind != ConsumableKind.Key && kind != ConsumableKind.Sweep && kind != ConsumableKind.EnhanceTicket;

        public static ConsumableItem Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var i in Items) if (i.id == id) return i;
            return null;
        }

        /// <summary>True for items the player uses by hand (bag click / quick key).</summary>
        public static bool IsUsable(string id)
        {
            var i = Get(id);
            return i != null && IsUsableKind(i.kind);
        }

        /// <summary>True for the protection ticket (auto-used by enhancement, bag "기타" tab).</summary>
        public static bool IsTicket(string id)
        {
            var i = Get(id);
            return i != null && i.kind == ConsumableKind.Protection;
        }

        /// <summary>What a new character starts with (and what old saves receive once).</summary>
        public static readonly (string id, int count)[] StarterPack =
        {
            (Gold, 100), (HpPotion, 20), (MpPotion, 20), (TownScroll, 5),
        };
    }

    /// <summary>General store (잡화상인) stock and buy / sell prices in gold.</summary>
    public static class ItemPrices
    {
        /// <summary>What the general store sells, in shelf order.</summary>
        static readonly string[] BaseStock =
        {
            ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll,
            "mat_bone", "mat_ore", "mat_essence", ConsumableDatabase.ProtectTicket,
        };

        /// <summary>
        /// What the general store sells, in shelf order: the goods above, then every common piece of gear (all tiers and
        /// classes; the store window shows only my class up to my level, and the server checks the level when buying).
        /// </summary>
        public static readonly string[] ShopStock = BuildStock();

        static string[] BuildStock()
        {
            var list = new System.Collections.Generic.List<string>(BaseStock);
            foreach (var e in EquipmentDatabase.All)
                if (!e.starter && !e.bossOnly && e.rarity == ItemRarity.Common) list.Add(e.id);
            return list.ToArray();
        }

        public static int BuyPrice(string id)
        {
            // Common gear: 100 gold per tier step (Lv.1 100 ... Lv.40 800).
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return gear.starter || gear.bossOnly || gear.rarity != ItemRarity.Common ? 0 : 100 * (gear.levelTier + 1);
            switch (id)
            {
                case ConsumableDatabase.HpPotion: return 30;
                case ConsumableDatabase.MpPotion: return 30;
                case ConsumableDatabase.TownScroll: return 60;
                case "mat_bone": return 15;
                case "mat_ore": return 60;
                case "mat_essence": return 250;
                case ConsumableDatabase.ProtectTicket: return 3000;
                default: return 0;
            }
        }

        // [BALANCE 2026-10-11] x5 with the field drop chance 0.4 -> 0.08 (same gold per kill).
        // [BALANCE 2026-10-16] x0.4: vendor gear sales were a third of all gold; the auction is where drops are worth money (theory_gold.py).
        static readonly int[] GearSell = { 20, 50, 120, 300, 600, 1200 };

        /// <summary>Selling price gained from enhancing is capped at this share of the gold spent on the successful path up to that level.</summary>
        public const double EnhancedSellGoldRatio = 0.5;

        /// <summary>
        /// Gold the store pays for one item (0 = cannot be sold). Gear keys pay the grade price
        /// × (1 + 0.25 per +level), rounded, with the gain capped by the gold spent enhancing.
        /// </summary>
        public static int SellPrice(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
            {
                // Grade price x (1 + 0.25 per level tier): Lv.40 gear sells for close to triple the Lv.1 price.
                int basePrice = gear.starter ? 2 : (int)System.Math.Round(GearSell[(int)gear.rarity] * (1.0 + 0.25 * gear.levelTier), System.MidpointRounding.AwayFromZero);
                int level = EquipmentDatabase.LevelOfKey(id);
                int plain = (int)Math.Round(basePrice * (1.0 + 0.25 * level), MidpointRounding.AwayFromZero);
                int spent = 0;
                for (int k = 0; k < level; k++) spent += EnhanceRules.GoldFor(gear, k);
                return Math.Min(plain, basePrice + (int)Math.Floor(EnhancedSellGoldRatio * spent));
            }
            switch (id)
            {
                // [BALANCE 2026-10-21] Materials sell at 70% of before (5 / 20 / 80): leftover materials were a third of a
                // no-spend hunter's income (Tools/balance/theory_gold.py, BALANCE_MEASURE 7). Buy prices are unchanged.
                case "mat_bone": return 4;
                case "mat_ore": return 14;
                case "mat_essence": return 56;
                case ConsumableDatabase.HpPotion: return 12;
                case ConsumableDatabase.MpPotion: return 12;
                case ConsumableDatabase.TownScroll: return 25;
                case ConsumableDatabase.ProtectTicket: return 0; // bought with gold, never resold
                default: return 0;
            }
        }
    }
}
