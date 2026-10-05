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
    }

    public static class ConsumableDatabase
    {
        public const string Gold = "gold";
        public const string HpPotion = "potion_hp";
        public const string MpPotion = "potion_mp";
        public const string TownScroll = "scroll_town";
        public const string ProtectTicket = "ticket_protect";

        static readonly List<ConsumableItem> Items = new List<ConsumableItem>
        {
            new ConsumableItem { id = Gold, name = "골드", iconKey = "icon_gold", kind = ConsumableKind.Currency,
                description = "마을 상점에서 쓰는 돈. 해골을 쓰러뜨리거나 물건을 팔아서 모은다." },
            new ConsumableItem { id = HpPotion, name = "체력 물약", iconKey = "icon_potion_hp", kind = ConsumableKind.HealHp, power = 40, hotkey = GameAction.UseItem,
                description = "붉은 약초를 달인 물약. 마시면 최대 HP의 40%를 회복한다." },
            new ConsumableItem { id = MpPotion, name = "마나 물약", iconKey = "icon_potion_mp", kind = ConsumableKind.HealMp, power = 50, hotkey = GameAction.UseMana,
                description = "푸른 샘물로 만든 물약. 마시면 최대 MP의 50%를 회복한다." },
            new ConsumableItem { id = TownScroll, name = "마을 귀환 주문서", iconKey = "icon_scroll", kind = ConsumableKind.TownScroll, hotkey = GameAction.TownScroll,
                description = "펼치면 빛에 감싸여 작은 마을 광장으로 돌아간다. 사냥터에서 쓰면 편리하다." },
            new ConsumableItem { id = ProtectTicket, name = "장비 보호권", iconKey = "icon_ticket", kind = ConsumableKind.Protection, grade = ItemRarity.Unique,
                description = "강화 실패로 장비가 파괴될 때 자동으로 소모되어 장비를 지킨다. 지켜진 장비는 +0으로 초기화된다." },
            new ConsumableItem { id = DungeonDatabase.SealKey, name = "봉인 열쇠 조각", iconKey = "icon_key", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = "중간 레이드 보스가 지키던 봉인의 파편. 모으면 챕터 최종 레이드의 문이 열린다. (최종 레이드 클리어 시 소모)" },
            new ConsumableItem { id = DungeonDatabase.RaidCore, name = "고대의 핵", iconKey = "icon_core", kind = ConsumableKind.Key, grade = ItemRarity.Legendary,
                description = "레이드 보스에게서만 얻는 고대 마력의 핵. 대장간에서 장비를 한 등급 위 장비로 승급할 때 쓴다. (강화 수치 유지)" },
        };

        /// <summary>Usable items in bag order (money and the protection ticket excluded).</summary>
        public static IEnumerable<ConsumableItem> Usable
        {
            get { foreach (var i in Items) if (IsUsableKind(i.kind)) yield return i; }
        }

        /// <summary>Items that work on their own from the bag ("기타" tab): the equipment protection ticket.</summary>
        public static IEnumerable<ConsumableItem> Tickets
        {
            get { foreach (var i in Items) if (i.kind == ConsumableKind.Protection || i.kind == ConsumableKind.Key) yield return i; }
        }

        static bool IsUsableKind(ConsumableKind kind) => kind != ConsumableKind.Currency && kind != ConsumableKind.Protection && kind != ConsumableKind.Key;

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
            (Gold, 100), (HpPotion, 3), (MpPotion, 2), (TownScroll, 1),
        };
    }

    /// <summary>General store (잡화상인) stock and buy / sell prices in gold.</summary>
    public static class ItemPrices
    {
        /// <summary>What the general store sells, in shelf order.</summary>
        public static readonly string[] ShopStock =
        {
            ConsumableDatabase.HpPotion, ConsumableDatabase.MpPotion, ConsumableDatabase.TownScroll,
            "mat_bone", "mat_ore", "mat_essence", ConsumableDatabase.ProtectTicket,
        };

        public static int BuyPrice(string id)
        {
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

        static readonly int[] GearSell = { 10, 25, 60, 150, 300, 600 };

        /// <summary>
        /// Gold the store pays for one item (0 = cannot be sold). Gear keys pay the grade price
        /// × (1 + 0.25 per +level), rounded.
        /// </summary>
        public static int SellPrice(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
            {
                // Grade price x (1 + 0.25 per level tier): Lv.40 gear sells for close to triple the Lv.1 price.
                int basePrice = gear.starter ? 2 : (int)System.Math.Round(GearSell[(int)gear.rarity] * (1.0 + 0.25 * gear.levelTier), System.MidpointRounding.AwayFromZero);
                return (int)Math.Round(basePrice * (1.0 + 0.25 * EquipmentDatabase.LevelOfKey(id)), MidpointRounding.AwayFromZero);
            }
            switch (id)
            {
                case ItemIds.Wood: return 3;
                case ItemIds.Stone: return 3;
                case ItemIds.Carrot: return 4;
                case "mat_bone": return 5;
                case "mat_ore": return 20;
                case "mat_essence": return 80;
                case ConsumableDatabase.HpPotion: return 12;
                case ConsumableDatabase.MpPotion: return 12;
                case ConsumableDatabase.TownScroll: return 25;
                case ConsumableDatabase.ProtectTicket: return 0; // bought with gold, never resold
                default: return 0;
            }
        }
    }
}
