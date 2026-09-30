using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// What the player is wearing (6 slots). Every piece of gear is an instance key that carries its own
    /// +level: the base id at +0, "{baseId}+{level}" when enhanced ("eq_sword_iron+12", see
    /// <see cref="EquipmentDatabase.KeyFor"/>). Unequipped gear lives in <see cref="GameSession.Inventory"/>
    /// under its key (identical keys stack), so the bag and the save file need no extra structure.
    /// Also runs enhancement attempts (<see cref="TryEnhance"/>) and keeps their pity.
    /// </summary>
    public sealed class Equipment
    {
        public const int SlotCount = 6;
        const int MaxBlock = 60;

        readonly string[] slots = new string[SlotCount];
        readonly Inventory bag;
        /// <summary>Enhancement pity per key attempted from (weapons at +10 / +11), in %p.</summary>
        readonly Dictionary<string, int> pity = new Dictionary<string, int>();

        /// <summary>Raised after any equip/unequip/load/enhancement.</summary>
        public event Action Changed;

        public Equipment(Inventory bag)
        {
            this.bag = bag;
        }

        /// <summary>Key worn in a slot ("eq_sword_iron+12"), null when empty.</summary>
        public string this[EquipSlot slot] => slots[(int)slot];

        /// <summary>Base item worn in a slot (its level is <see cref="LevelOf"/> of the slot's key).</summary>
        public EquipmentItem ItemIn(EquipSlot slot) => EquipmentDatabase.Get(slots[(int)slot]);

        // ---------- Keys ----------

        /// <summary>+level of a key (same as <see cref="EquipmentDatabase.LevelOfKey"/>).</summary>
        public int LevelOf(string key) => EquipmentDatabase.LevelOfKey(key);

        /// <summary>Stats of a key at its own +level (default for null / non-gear).</summary>
        public GearStats StatsOfKey(string key) => EquipmentDatabase.StatsOfKey(key);

        /// <summary>How much a key adds to "전투력" (also used to pick the best gear); 0 for null / non-gear.</summary>
        public int ScoreOf(string key) => StatsOfKey(key).Score;

        // ---------- Stat totals ----------

        public int AttackBonus => Sum(s => s.attack);
        public int MaxHealthBonus => Sum(s => s.maxHealth);
        public int BlockChance => Mathf.Clamp(Sum(s => s.block), 0, MaxBlock);
        public int SpeedBonus => Sum(s => s.speed);
        public float SpeedMultiplier => Mathf.Max(0.5f, 1f + SpeedBonus / 100f);

        int Sum(Func<GearStats, int> pick)
        {
            int total = 0;
            foreach (var key in slots)
                if (EquipmentDatabase.IsEquipment(key)) total += pick(StatsOfKey(key));
            return total;
        }

        // ---------- Power / auto-equip ----------

        public int GearScore => Sum(s => s.Score);

        /// <summary>Wears the strongest usable gear from the bag in every slot. Returns how many pieces changed.</summary>
        public int AutoEquip(CharacterClass cls)
        {
            int changes = 0;
            foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Necklace, EquipSlot.Top, EquipSlot.Bottom })
            {
                string best = BestInBag(cls, slot);
                if (best != null && ScoreOf(best) > ScoreOf(slots[(int)slot]) && Equip(best, cls)) changes++;
            }
            // Rings: keep the two strongest overall.
            for (int pass = 0; pass < 2; pass++)
            {
                string best = BestInBag(cls, EquipSlot.Ring1);
                if (best == null) break;
                if (ItemIn(EquipSlot.Ring1) != null && ItemIn(EquipSlot.Ring2) != null)
                {
                    var weakSlot = ScoreOf(slots[(int)EquipSlot.Ring1]) <= ScoreOf(slots[(int)EquipSlot.Ring2]) ? EquipSlot.Ring1 : EquipSlot.Ring2;
                    if (ScoreOf(best) <= ScoreOf(slots[(int)weakSlot])) break;
                    Unequip(weakSlot);
                }
                if (Equip(best, cls)) changes++;
            }
            return changes;
        }

        /// <summary>Strongest usable key in the bag for a slot (ties: bag order), or null.</summary>
        string BestInBag(CharacterClass cls, EquipSlot slot)
        {
            string best = null;
            int bestScore = 0;
            foreach (var key in EquipmentDatabase.GearKeys(bag))
            {
                var item = EquipmentDatabase.Get(key);
                if (!item.UsableBy(cls) || !EquipmentDatabase.Fits(item.category, slot)) continue;
                int score = ScoreOf(key);
                if (best == null || score > bestScore)
                {
                    best = key;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>True when wearing this bag key would raise the power of its slot (the green corner mark).</summary>
        public bool IsUpgrade(string key, CharacterClass cls)
        {
            var item = EquipmentDatabase.Get(key);
            if (item == null || !item.UsableBy(cls)) return false;
            if (item.category == EquipCategory.Ring)
            {
                if (ItemIn(EquipSlot.Ring1) == null || ItemIn(EquipSlot.Ring2) == null) return true;
                return ScoreOf(key) > Mathf.Min(ScoreOf(slots[(int)EquipSlot.Ring1]), ScoreOf(slots[(int)EquipSlot.Ring2]));
            }
            return ScoreOf(key) > ScoreOf(slots[(int)TargetSlotFor(item)]);
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

        /// <summary>Moves one piece (by key) from the bag into its slot; whatever was there goes back to the bag.</summary>
        public bool Equip(string key, CharacterClass cls)
        {
            var item = EquipmentDatabase.Get(key);
            if (item == null || !item.UsableBy(cls) || bag.Count(key) <= 0) return false;
            var slot = TargetSlotFor(item);
            bag.Remove(key, 1);
            string previous = slots[(int)slot];
            if (!string.IsNullOrEmpty(previous)) bag.Add(previous, 1);
            slots[(int)slot] = key;
            Changed?.Invoke();
            return true;
        }

        public bool Unequip(EquipSlot slot)
        {
            string key = slots[(int)slot];
            if (string.IsNullOrEmpty(key)) return false;
            slots[(int)slot] = null;
            bag.Add(key, 1);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Puts gear straight into a slot (new game starter weapon), bypassing the bag.</summary>
        public void Set(EquipSlot slot, string key)
        {
            slots[(int)slot] = key;
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
            if (FixSlots(cls)) Changed?.Invoke();
        }

        /// <summary>Unusable gear back to the bag (keeping its +level), the starter weapon into an empty weapon slot.</summary>
        bool FixSlots(CharacterClass cls)
        {
            bool changed = false;
            for (int i = 0; i < SlotCount; i++)
            {
                var item = EquipmentDatabase.Get(slots[i]);
                if (item == null || item.UsableBy(cls)) continue;
                bag.Add(slots[i], 1);
                slots[i] = null;
                changed = true;
            }
            if (string.IsNullOrEmpty(slots[(int)EquipSlot.Weapon]))
            {
                slots[(int)EquipSlot.Weapon] = EquipmentDatabase.StarterWeapon(cls);
                changed = true;
            }
            return changed;
        }

        public List<string> ToList() => new List<string>(slots);

        /// <summary>Worn keys from a save; unknown keys and keys that do not fit their slot are dropped.</summary>
        public void Load(List<string> saved)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                string key = saved != null && i < saved.Count ? saved[i] : null;
                var item = EquipmentDatabase.Get(key);
                slots[i] = item != null && EquipmentDatabase.Fits(item.category, (EquipSlot)i) ? key : null;
            }
            Changed?.Invoke();
        }

        // ---------- Enhancement ----------

        /// <summary>Pity (%p) collected by failed attempts from this key (weapons at +10 / +11 only).</summary>
        public int PityOf(string key) => key != null && pity.TryGetValue(key, out int p) ? p : 0;

        public List<ItemStack> PityToList()
        {
            var list = new List<ItemStack>();
            foreach (var pair in pity)
                if (pair.Value > 0) list.Add(new ItemStack(pair.Key, pair.Value));
            list.Sort((a, b) => EquipmentDatabase.CompareKeys(a.id, b.id));
            return list;
        }

        /// <summary>Pity from a save (null = none); keys that cannot have pity are dropped.</summary>
        public void LoadPity(List<ItemStack> saved)
        {
            pity.Clear();
            if (saved == null) return;
            foreach (var s in saved)
                if (s != null && s.count > 0 && EnhanceRules.HasPity(EquipmentDatabase.Get(s.id), EquipmentDatabase.LevelOfKey(s.id)))
                    pity[s.id] = Math.Min(s.count, EnhanceRules.MaxPity);
        }

        /// <summary>The key at a target: the worn key, or the bag key while the bag holds one (else null).</summary>
        public string KeyAt(EnhanceTarget target)
        {
            if (target.slot.HasValue) return slots[(int)target.slot.Value];
            return bag.Count(target.bagKey) > 0 ? target.bagKey : null;
        }

        /// <summary>Price, chance and risk of enhancing a key once, with its pity and the bag's protection tickets.</summary>
        public EnhanceCost CostFor(string key) =>
            EnhanceRules.CostFor(EquipmentDatabase.Get(key), EquipmentDatabase.LevelOfKey(key), PityOf(key), bag.Count(ConsumableDatabase.ProtectTicket) > 0);

        /// <summary>True when the bag holds the gold and materials of an attempt.</summary>
        public bool CanAfford(EnhanceCost cost) =>
            bag.Count(ConsumableDatabase.Gold) >= cost.gold && bag.Count(EnhanceRules.Bone) >= cost.bone &&
            bag.Count(EnhanceRules.Ore) >= cost.ore && bag.Count(EnhanceRules.Essence) >= cost.essence;

        /// <summary>
        /// One enhancement attempt on a worn slot or a bag key, decided by <paramref name="roll"/> (0..99,
        /// success when below the chance). Checks the max level and the price, pays gold and materials, then
        /// applies the outcome: success +1 (pity for that key cleared) · keep · −3 (+1%p pity) · destroyed
        /// (a protection ticket in the bag is used up instead and the item returns to +0). A destroyed worn
        /// weapon is replaced by the starter weapon of <paramref name="cls"/>. Raises <see cref="Changed"/>.
        /// </summary>
        public EnhanceResult TryEnhance(EnhanceTarget target, int roll, CharacterClass cls)
        {
            string key = KeyAt(target);
            var item = EquipmentDatabase.Get(key);
            int level = EquipmentDatabase.LevelOfKey(key);
            var result = new EnhanceResult
            {
                kind = EnhanceOutcome.Invalid, oldKey = key, newKey = key, oldLevel = level, newLevel = level,
                slot = target.slot, roll = roll,
            };
            if (item == null) return result;
            if (level >= EquipmentDatabase.MaxEnhance)
            {
                result.kind = EnhanceOutcome.MaxLevel;
                return result;
            }
            var cost = CostFor(key);
            result.cost = cost;
            if (!CanAfford(cost))
            {
                result.kind = EnhanceOutcome.NotEnough;
                return result;
            }

            bag.Remove(ConsumableDatabase.Gold, cost.gold);
            bag.Remove(EnhanceRules.Bone, cost.bone);
            bag.Remove(EnhanceRules.Ore, cost.ore);
            bag.Remove(EnhanceRules.Essence, cost.essence);

            if (EnhanceRules.Succeeds(roll, cost.successPercent))
            {
                result.kind = EnhanceOutcome.Success;
                result.newLevel = level + 1;
                result.newKey = item.KeyAt(level + 1);
                pity.Remove(key);
            }
            else
            {
                if (EnhanceRules.HasPity(item, level)) pity[key] = Math.Min(EnhanceRules.MaxPity, PityOf(key) + 1);
                switch (cost.failure)
                {
                    case EnhanceFailure.Keep:
                        result.kind = EnhanceOutcome.Keep;
                        break;
                    case EnhanceFailure.Drop3:
                        result.kind = EnhanceOutcome.Drop3;
                        result.newLevel = cost.DroppedLevel;
                        result.newKey = item.KeyAt(result.newLevel);
                        break;
                    default:
                        bool saved = bag.Remove(ConsumableDatabase.ProtectTicket, 1);
                        result.kind = saved ? EnhanceOutcome.Protected : EnhanceOutcome.Destroyed;
                        result.newLevel = 0;
                        result.newKey = saved ? item.id : null;
                        break;
                }
            }
            if (result.newKey != key) Replace(target, key, result.newKey);
            if (result.kind == EnhanceOutcome.Destroyed) FixSlots(cls);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Swaps the target's unit for another key (null = the unit is gone).</summary>
        void Replace(EnhanceTarget target, string oldKey, string newKey)
        {
            if (target.slot.HasValue)
            {
                slots[(int)target.slot.Value] = newKey;
                return;
            }
            bag.Remove(oldKey, 1);
            if (newKey != null) bag.Add(newKey, 1);
        }
    }
}
