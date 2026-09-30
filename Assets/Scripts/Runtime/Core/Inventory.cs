using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Simple stackable item counts keyed by item id. Gear is keyed by its instance key
    /// ("eq_sword_iron+12", see <see cref="EquipmentDatabase.KeyFor"/>), so identical pieces stack.
    /// </summary>
    public sealed class Inventory
    {
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>(itemId, newCount, delta)</summary>
        public event Action<string, int, int> Changed;

        public int Count(string id) => id != null && counts.TryGetValue(id, out int c) ? c : 0;

        /// <summary>
        /// Every id / key held (count above zero), in no particular order. Read-only view: copy it
        /// (e.g. <c>new List&lt;string&gt;(bag.Ids)</c>) before adding or removing items in the same loop.
        /// </summary>
        public IEnumerable<string> Ids
        {
            get
            {
                foreach (var pair in counts)
                    if (pair.Value > 0) yield return pair.Key;
            }
        }

        public void Add(string id, int amount)
        {
            if (amount <= 0) return;
            int next = Count(id) + amount;
            counts[id] = next;
            Changed?.Invoke(id, next, amount);
        }

        public bool Remove(string id, int amount)
        {
            if (amount <= 0) return true;
            int have = Count(id);
            if (have < amount) return false;
            counts[id] = have - amount;
            Changed?.Invoke(id, have - amount, -amount);
            return true;
        }

        public void Clear()
        {
            var ids = new List<string>(counts.Keys);
            counts.Clear();
            foreach (var id in ids) Changed?.Invoke(id, 0, 0);
        }

        public List<ItemStack> ToList()
        {
            var list = new List<ItemStack>();
            foreach (var pair in counts)
                if (pair.Value > 0) list.Add(new ItemStack(pair.Key, pair.Value));
            return list;
        }

        public void Load(List<ItemStack> stacks)
        {
            Clear();
            if (stacks == null) return;
            foreach (var s in stacks)
                if (!string.IsNullOrEmpty(s.id) && s.count > 0) Add(s.id, s.count);
        }
    }
}
