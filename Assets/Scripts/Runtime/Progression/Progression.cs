using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Character progression: level &amp; experience from hunting, passive tree allocation (one point per
    /// level) and the five skill slots. Each slot opens at a level and holds the class's skill for it
    /// (slot 5 = awakening ultimate); the player links two support gems into each slot. Saved with the game.
    /// </summary>
    public sealed partial class Progression
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

        /// <summary>
        /// Level-up XP is the base curve times <see cref="CurveScale"/>: the mid raid (Lv22) after about 20 hours and Lv40
        /// after about 50 hours of play, quests, daily dungeons and raids included (Tools/balance/theory_progress.py).
        /// Kill XP and the dungeon floors are priced from the base curve (HuntingGrounds.XpAt), so only the pace changes.
        /// </summary>
        public const float CurveScale = 22f;
        /// <summary>Quest rewards: data XP x this. Kept apart from the curve so quests stay a share of the growth, not most of it.</summary>
        public const float QuestScale = 6f;
        public static int BaseXpToNext(int level) => 40 + (level - 1) * 30 + (level - 1) * (level - 1) * 5;
        public static int XpToNext(int level) => (int)Math.Round(BaseXpToNext(level) * CurveScale);

        /// <summary>An XP amount as a share of a level's bar ("12.3%"), the way players read progress. Max level: "만렙".</summary>
        public static string XpPercent(long xp, int level)
        {
            if (level >= MaxLevel) return "만렙";
            double pct = xp * 100.0 / Math.Max(1, XpToNext(level));
            return pct >= 10 ? $"{pct:0}%" : pct >= 1 ? $"{pct:0.#}%" : $"{pct:0.##}%";
        }
        /// <summary>Quest XP rewards: data value x <see cref="QuestScale"/>.</summary>
        public static int QuestXp(int dataXp) => (int)Math.Round(dataXp * QuestScale);
        public int XpNeeded => Level >= MaxLevel ? 0 : XpToNext(Level);

        /// <summary>Passive points earned minus spent (the start node is free).</summary>
        public int PointsLeft => (Level - 1) - Math.Max(0, Allocated.Count - 1);

        public void Reset(CharacterClass playerClass)
        {
            cls = playerClass;
            careerState = new CareerSave();
            Level = 1;
            Xp = 0;
            Allocated.Clear();
            Allocated.Add(PassiveTree.Start);
            Array.Clear(slots, 0, slots.Length);
            Changed?.Invoke();
        }

        /// <summary>[SERVER] Level and XP decided by the server; level-ups still raise LeveledUp (toasts, skills).</summary>
        public void SetFromServer(int level, int xp)
        {
            level = Math.Max(1, Math.Min(MaxLevel, level));
            int before = Level;
            Level = level;
            Xp = Math.Max(0, xp);
            for (int l = before + 1; l <= level; l++) LeveledUp?.Invoke(l);
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

        /// <summary>Whether a node could be refunded (every other allocated node stays connected to the start).</summary>
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

        public void ResetTree()
        {
            Allocated.Clear();
            Allocated.Add(PassiveTree.Start);
            Changed?.Invoke();
        }

        // ---------- Skill slots ----------

        public static int SlotLevel(int slot) => SkillGems.SlotLevels[slot];

        public bool IsSlotOpen(int slot) => slot >= 0 && slot < SkillGems.Slots && Level >= SlotLevel(slot);

        public bool IsUnlocked(SkillGem gem) => gem != null && gem.id != "blades" && gem.id != "meteor" && gem.UsableBy(cls) && Level >= gem.unlockLevel && (CareerCatalog.Get(gem.id) == null || CareerUnlocked(CareerCatalog.Get(gem.id)));

        /// <summary>The slot's skill, or null while the slot is still locked.</summary>
        public SkillGem Active(int slot)
        {
            if(!IsSlotOpen(slot)) return null;
            var chosen=SkillGems.Get(slots[slot,0]);
            if(chosen!=null && IsUnlocked(chosen) && (slot==4)==chosen.IsUltimate) return chosen;
            if(slot==4) return Awakened ? CareerCatalog.For(Career)[8].Gem : null;
            var basic=SkillGems.ForSlot(cls,slot); return IsUnlocked(basic)?basic:null;
        }

        /// <summary>Gem id in a socket (socket 0 = the slot's skill).</summary>
        public string SlotGem(int slot, int socket) => socket == 0 ? Active(slot)?.id : slots[slot, socket];

        public IEnumerable<SkillGem> Supports(int slot)
        {
            for (int s = 1; s <= SkillGems.SupportsPerSlot; s++)
            {
                var g = SkillGems.Get(slots[slot, s]);
                if (g != null && (CareerCatalog.Get(Active(slot)?.id)==null || g.id=="sup_dmg" || g.id=="sup_aoe" || g.id=="sup_eff")) yield return g;
            }
        }

        /// <summary>Sets a support socket (socket 1..2). The skill socket is fixed and ignores this.</summary>
        public void SetGem(int slot, int socket, string gemId)
        {
            if(socket==0) { EquipSkill(slot,gemId); return; }
            if (socket < 1 || socket > SkillGems.SupportsPerSlot || !IsSlotOpen(slot) || (gemId!=null && !OptionsFor(slot,socket).Contains(gemId))) return;
            slots[slot, socket] = gemId;
            Changed?.Invoke();
        }

        /// <summary>Candidates for a support socket in cycling order (first = empty); a gem can't be linked twice into one slot.</summary>
        public List<string> OptionsFor(int slot, int socket)
        {
            var list = new List<string> { null };
            if (!IsSlotOpen(slot)) return list;
            if(socket==0) {
                list.Clear();
                foreach(var g in SkillGems.All) if(g.kind==GemKind.Active && IsUnlocked(g) && (slot==4)==g.IsUltimate) list.Add(g.id);
                foreach(var s in CareerCatalog.All) if(s.kind!=CareerSkillKind.Passive && CareerUnlocked(s) && (slot==4)==(s.kind==CareerSkillKind.Awakening)) list.Add(s.id);
                return list;
            }
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
            data.career = CaptureCareer();
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
            RestoreCareer(data);
            if (data.gemSlots != null)
                for (int i = 0; i < data.gemSlots.Count && i < slots.Length; i++)
                {
                    int s = i / (1 + SkillGems.SupportsPerSlot), k = i % (1 + SkillGems.SupportsPerSlot);
                    if (k == 0) { EquipSkill(s,data.gemSlots[i]); continue; }
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
