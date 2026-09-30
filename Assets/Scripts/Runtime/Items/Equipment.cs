using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// What the player is wearing (6 slots). Unequipped items live in <see cref="GameSession.Inventory"/>
    /// under their equipment id, so the bag and the save file need no extra structure.
    /// </summary>
    public sealed class Equipment
    {
        public const int SlotCount = 6;
        const int MaxBlock = 60;

        readonly string[] slots = new string[SlotCount];
        readonly Inventory bag;

        /// <summary>Raised after any equip/unequip/load.</summary>
        public event Action Changed;

        public Equipment(Inventory bag)
        {
            this.bag = bag;
        }

        public string this[EquipSlot slot] => slots[(int)slot];

        public EquipmentItem ItemIn(EquipSlot slot) => EquipmentDatabase.Get(slots[(int)slot]);

        // ---------- Enhancement levels ----------

        // Enhancement is kept per item kind: every copy of "철검" shares its +level.
        readonly Dictionary<string, int> levels = new Dictionary<string, int>();

        public int LevelOf(string id) => id != null && levels.TryGetValue(id, out int l) ? l : 0;

        public void SetLevel(string id, int level)
        {
            levels[id] = Mathf.Clamp(level, 0, EquipmentDatabase.MaxEnhance);
            Changed?.Invoke();
        }

        public GearStats StatsOf(EquipmentItem item) => item != null ? item.StatsAt(LevelOf(item.id)) : default;

        public List<ItemStack> LevelsToList()
        {
            var list = new List<ItemStack>();
            foreach (var pair in levels) if (pair.Value > 0) list.Add(new ItemStack(pair.Key, pair.Value));
            return list;
        }

        public void LoadLevels(List<ItemStack> saved)
        {
            levels.Clear();
            if (saved != null)
                foreach (var s in saved)
                    if (EquipmentDatabase.IsEquipment(s.id) && s.count > 0) levels[s.id] = Mathf.Min(s.count, EquipmentDatabase.MaxEnhance);
            Changed?.Invoke();
        }

        // ---------- Stat totals ----------

        public int AttackBonus => Sum(s => s.attack);
        public int MaxHealthBonus => Sum(s => s.maxHealth);
        public int BlockChance => Mathf.Clamp(Sum(s => s.block), 0, MaxBlock);
        public int SpeedBonus => Sum(s => s.speed);
        public float SpeedMultiplier => Mathf.Max(0.5f, 1f + SpeedBonus / 100f);

        int Sum(Func<GearStats, int> pick)
        {
            int total = 0;
            foreach (var id in slots)
            {
                var item = EquipmentDatabase.Get(id);
                if (item != null) total += pick(StatsOf(item));
            }
            return total;
        }

        // ---------- Power / auto-equip ----------

        /// <summary>How much an item adds to "전투력" at its current enhancement (also used to pick the best gear).</summary>
        public int Score(EquipmentItem item) => item == null ? 0 : StatsOf(item).Score;

        public int GearScore => Sum(s => s.Score);

        /// <summary>Wears the strongest usable gear from the bag in every slot. Returns how many pieces changed.</summary>
        public int AutoEquip(CharacterClass cls)
        {
            int changes = 0;
            foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Necklace, EquipSlot.Top, EquipSlot.Bottom })
            {
                var best = BestInBag(cls, slot, null);
                if (best != null && Score(best) > Score(ItemIn(slot)) && Equip(best.id, cls)) changes++;
            }
            // Rings: keep the two strongest overall.
            for (int pass = 0; pass < 2; pass++)
            {
                var best = BestInBag(cls, EquipSlot.Ring1, null);
                if (best == null) break;
                var r1 = ItemIn(EquipSlot.Ring1);
                var r2 = ItemIn(EquipSlot.Ring2);
                if (r1 != null && r2 != null)
                {
                    var weakSlot = Score(r1) <= Score(r2) ? EquipSlot.Ring1 : EquipSlot.Ring2;
                    if (Score(best) <= Score(ItemIn(weakSlot))) break;
                    Unequip(weakSlot);
                }
                if (Equip(best.id, cls)) changes++;
            }
            return changes;
        }

        EquipmentItem BestInBag(CharacterClass cls, EquipSlot slot, EquipmentItem except)
        {
            EquipmentItem best = null;
            foreach (var item in EquipmentDatabase.All)
            {
                if (item == except || bag.Count(item.id) <= 0 || !item.UsableBy(cls) || !EquipmentDatabase.Fits(item.category, slot)) continue;
                if (best == null || Score(item) > Score(best)) best = item;
            }
            return best;
        }

        /// <summary>True when wearing this bag item would raise the power of its slot (the green corner mark).</summary>
        public bool IsUpgrade(EquipmentItem item, CharacterClass cls)
        {
            if (item == null || !item.UsableBy(cls)) return false;
            if (item.category == EquipCategory.Ring)
            {
                var r1 = ItemIn(EquipSlot.Ring1);
                var r2 = ItemIn(EquipSlot.Ring2);
                if (r1 == null || r2 == null) return true;
                return Score(item) > Mathf.Min(Score(r1), Score(r2));
            }
            return Score(item) > Score(ItemIn(TargetSlotFor(item)));
        }

        // ---------- Changing gear ----------

        /// <summary>Slot an item would go into: its own slot, or for rings the first empty ring slot.</summary>
        public EquipSlot TargetSlotFor(EquipmentItem item)
        {
            switch (item.category)
            {
                case EquipCategory.Weapon: return EquipSlot.Weapon;
                case EquipCategory.Necklace: return EquipSlot.Necklace;
                case EquipCategory.Top: return EquipSlot.Top;
                case EquipCategory.Bottom: return EquipSlot.Bottom;
                default: return string.IsNullOrEmpty(this[EquipSlot.Ring1]) || !string.IsNullOrEmpty(this[EquipSlot.Ring2]) ? EquipSlot.Ring1 : EquipSlot.Ring2;
            }
        }

        /// <summary>Moves an item from the bag into its slot; whatever was there goes back to the bag.</summary>
        public bool Equip(string itemId, CharacterClass cls)
        {
            var item = EquipmentDatabase.Get(itemId);
            if (item == null || !item.UsableBy(cls) || bag.Count(itemId) <= 0) return false;
            var slot = TargetSlotFor(item);
            bag.Remove(itemId, 1);
            string previous = slots[(int)slot];
            if (!string.IsNullOrEmpty(previous)) bag.Add(previous, 1);
            slots[(int)slot] = itemId;
            Changed?.Invoke();
            return true;
        }

        public bool Unequip(EquipSlot slot)
        {
            string id = slots[(int)slot];
            if (string.IsNullOrEmpty(id)) return false;
            slots[(int)slot] = null;
            bag.Add(id, 1);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Puts gear straight into a slot (new game starter weapon), bypassing the bag.</summary>
        public void Set(EquipSlot slot, string itemId)
        {
            slots[(int)slot] = itemId;
            Changed?.Invoke();
        }

        public void Clear()
        {
            for (int i = 0; i < SlotCount; i++) slots[i] = null;
            Changed?.Invoke();
        }

        /// <summary>A weapon the current class cannot use (e.g. after a class change) goes back to the bag.</summary>
        public void EnsureUsable(CharacterClass cls)
        {
            bool changed = false;
            for (int i = 0; i < SlotCount; i++)
            {
                var item = EquipmentDatabase.Get(slots[i]);
                if (item == null || item.UsableBy(cls)) continue;
                bag.Add(item.id, 1);
                slots[i] = null;
                changed = true;
            }
            if (cls != CharacterClass.Warrior && string.IsNullOrEmpty(slots[(int)EquipSlot.Weapon]))
            {
                slots[(int)EquipSlot.Weapon] = EquipmentDatabase.StarterWeapon(cls);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        public List<string> ToList() => new List<string>(slots);

        public void Load(List<string> saved)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                string id = saved != null && i < saved.Count ? saved[i] : null;
                var item = EquipmentDatabase.Get(id);
                slots[i] = item != null && EquipmentDatabase.Fits(item.category, (EquipSlot)i) ? id : null;
            }
            Changed?.Invoke();
        }
    }
}
