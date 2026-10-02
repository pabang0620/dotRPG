using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Character progression: level &amp; experience from hunting, passive tree allocation (one point per
    /// level) and the five skill slots. Each slot opens at a level and holds the class's skill for it
    /// (slot 5 = awakening ultimate); the player links two support gems into each slot. Saved with the game.
    /// </summary>
    public sealed class Progression
    {
        /// <summary>[J10] 40 since chapter 2 (its raids are tuned for Lv 28-31).</summary>
        public const int MaxLevel = 40;

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public readonly HashSet<string> Allocated = new HashSet<string>();
        /// <summary>[slot, 1..2] = support gem ids. Socket 0 is the slot's fixed active skill.</summary>
        readonly string[,] slots = new string[SkillGems.Slots, 1 + SkillGems.SupportsPerSlot];
        CharacterClass cls;

        public event Action Changed;
        public event Action<int> LeveledUp;

        public static int XpToNext(int level) => 40 + (level - 1) * 30 + (level - 1) * (level - 1) * 5;
        public int XpNeeded => Level >= MaxLevel ? 0 : XpToNext(Level);

        /// <summary>Passive points earned minus spent (the start node is free).</summary>
        public int PointsLeft => (Level - 1) - Math.Max(0, Allocated.Count - 1);

        public void Reset(CharacterClass playerClass)
        {
            cls = playerClass;
            Level = 1;
            Xp = 0;
            Allocated.Clear();
            Allocated.Add(PassiveTree.Start);
            Array.Clear(slots, 0, slots.Length);
            Changed?.Invoke();
        }

        public void AddXp(int amount)
        {
            if (Level >= MaxLevel || amount <= 0) return;
            Xp += amount;
            while (Level < MaxLevel && Xp >= XpToNext(Level))
            {
                Xp -= XpToNext(Level);
                Level++;
                LeveledUp?.Invoke(Level);
            }
            if (Level >= MaxLevel) Xp = 0;
            Changed?.Invoke();
        }

        // ---------- Passive tree ----------

        public bool CanAllocate(PassiveNode node)
        {
            if (node == null || Allocated.Contains(node.id) || PointsLeft <= 0 || node.kind == PassiveKind.Start) return false;
            foreach (var link in node.links) if (Allocated.Contains(link)) return true;
            return false;
        }

        public bool Allocate(PassiveNode node)
        {
            if (!CanAllocate(node)) return false;
            Allocated.Add(node.id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Refunds a node if every other allocated node stays connected to the start.</summary>
        public bool CanRefund(PassiveNode node)
        {
            if (node == null || !Allocated.Contains(node.id) || node.kind == PassiveKind.Start) return false;
            var reach = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(PassiveTree.Start);
            reach.Add(PassiveTree.Start);
            while (queue.Count > 0)
            {
                var n = PassiveTree.Get(queue.Dequeue());
                foreach (var link in n.links)
                    if (link != node.id && Allocated.Contains(link) && reach.Add(link)) queue.Enqueue(link);
            }
            return reach.Count == Allocated.Count - 1;
        }

        public bool Refund(PassiveNode node)
        {
            if (!CanRefund(node)) return false;
            Allocated.Remove(node.id);
            Changed?.Invoke();
            return true;
        }

        public void ResetTree()
        {
            Allocated.Clear();
            Allocated.Add(PassiveTree.Start);
            Changed?.Invoke();
        }

        // ---------- Skill slots ----------

        public static int SlotLevel(int slot) => SkillGems.SlotLevels[slot];

        public bool IsSlotOpen(int slot) => slot >= 0 && slot < SkillGems.Slots && Level >= SlotLevel(slot);

        public bool IsUnlocked(SkillGem gem) => gem != null && gem.UsableBy(cls) && Level >= gem.unlockLevel;

        /// <summary>The slot's skill, or null while the slot is still locked.</summary>
        public SkillGem Active(int slot) => IsSlotOpen(slot) ? SkillGems.ForSlot(cls, slot) : null;

        /// <summary>Gem id in a socket (socket 0 = the slot's skill).</summary>
        public string SlotGem(int slot, int socket) => socket == 0 ? Active(slot)?.id : slots[slot, socket];

        public IEnumerable<SkillGem> Supports(int slot)
        {
            for (int s = 1; s <= SkillGems.SupportsPerSlot; s++)
            {
                var g = SkillGems.Get(slots[slot, s]);
                if (g != null) yield return g;
            }
        }

        /// <summary>Sets a support socket (socket 1..2). The skill socket is fixed and ignores this.</summary>
        public void SetGem(int slot, int socket, string gemId)
        {
            if (socket <= 0 || !IsSlotOpen(slot)) return;
            slots[slot, socket] = gemId;
            Changed?.Invoke();
        }

        /// <summary>Candidates for a support socket in cycling order (first = empty); a gem can't be linked twice into one slot.</summary>
        public List<string> OptionsFor(int slot, int socket)
        {
            var list = new List<string> { null };
            if (socket <= 0 || !IsSlotOpen(slot)) return list;
            foreach (var g in SkillGems.All)
            {
                if (g.kind != GemKind.Support || !IsUnlocked(g)) continue;
                bool used = false;
                for (int s = 1; s <= SkillGems.SupportsPerSlot; s++) if (s != socket && slots[slot, s] == g.id) used = true;
                if (!used) list.Add(g.id);
            }
            return list;
        }

        // ---------- Save ----------

        public void Capture(SaveData data)
        {
            data.level = Level;
            data.xp = Xp;
            data.passives = new List<string>(Allocated);
            data.gemSlots = new List<string>();
            for (int s = 0; s < SkillGems.Slots; s++)
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++) data.gemSlots.Add(k == 0 ? SlotGem(s, 0) ?? "" : slots[s, k] ?? "");
        }

        public void Restore(SaveData data, CharacterClass playerClass)
        {
            Reset(playerClass);
            Level = Math.Max(1, Math.Min(MaxLevel, data.level));
            Xp = Math.Max(0, data.xp);
            // Nodes that no longer exist (older tree layouts) are dropped, which refunds their points.
            if (data.passives != null)
                foreach (var id in data.passives)
                    if (PassiveTree.Get(id) != null) Allocated.Add(id);
            // Never keep more nodes than points, or nodes cut off from the start (e.g. a hand-edited save).
            if (PointsLeft < 0 || !AllConnected()) ResetTree();
            if (data.gemSlots != null)
                for (int i = 0; i < data.gemSlots.Count && i < slots.Length; i++)
                {
                    int s = i / (1 + SkillGems.SupportsPerSlot), k = i % (1 + SkillGems.SupportsPerSlot);
                    if (k == 0) continue;
                    var g = SkillGems.Get(data.gemSlots[i]);
                    slots[s, k] = g != null && g.kind == GemKind.Support && IsUnlocked(g) ? g.id : null;
                }
            Changed?.Invoke();
        }

        bool AllConnected()
        {
            var reach = new HashSet<string> { PassiveTree.Start };
            var queue = new Queue<string>();
            queue.Enqueue(PassiveTree.Start);
            while (queue.Count > 0)
                foreach (var link in PassiveTree.Get(queue.Dequeue()).links)
                    if (Allocated.Contains(link) && reach.Add(link)) queue.Enqueue(link);
            return reach.Count == Allocated.Count;
        }
    }
}
