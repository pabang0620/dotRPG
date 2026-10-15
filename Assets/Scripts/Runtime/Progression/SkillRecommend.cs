using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [UX] 추천 배치: the standard build of each career. Learns with every point that can be spent and puts a skill on
    /// every open key, at any level. Order: rank 1 of each key skill (and the chain before it) first, so every open key
    /// gets a skill; then the scripted ranks; then whatever is still learnable (key skills, other actives, passives).
    /// Keys whose planned skill is still locked take the next learned active skill instead of staying empty, and empty
    /// support sockets get 피해 증가 / 범위 확대 (or 효율). Learned ranks and filled sockets are kept.
    /// </summary>
    public static class SkillRecommend
    {
        /// <summary>The k-th recommended key: Q W E R are slots 0-3, then A S D are slots 5-7 (T, slot 4, is the awakening).</summary>
        public static int KeySlot(int k) => k < 4 ? k : k + 1;

        /// <summary>(skill index, target rank) in learning order, then the skill indexes for Q W E R (A S D) in order.</summary>
        public static (int[,] ranks, int[] keys) Plan(Career c)
        {
            switch (c)
            {
                // 섬광보 (2) goes out on Shift once learned, so W takes 검기 태세 (9).
                case Career.Fighter: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 1 }, { 9, 1 }, { 3, 1 }, { 4, 1 }, { 5, 3 }, { 3, 3 }, { 1, 2 }, { 0, 3 }, { 4, 3 }, { 2, 2 }, { 1, 3 }, { 2, 3 }, { 6, 1 }, { 7, 1 } }, new[] { 1, 9, 5, 3, 6, 7 });
                case Career.Guardian: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 3 }, { 4, 1 }, { 5, 1 }, { 6, 1 }, { 7, 1 }, { 0, 3 }, { 4, 3 }, { 3, 1 }, { 5, 2 }, { 6, 2 }, { 7, 2 } }, new[] { 2, 5, 6, 7, 1, 3 });
                // Q W E R all at rank 1 first, then the spammable 연쇄전격 and the charge shot; 매직 배리어 (9) on D.
                case Career.Arcanist: return (new[,] { { 0, 1 }, { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 1 }, { 9, 1 }, { 5, 1 }, { 3, 3 }, { 5, 3 }, { 1, 3 }, { 2, 2 }, { 0, 3 }, { 4, 3 }, { 6, 1 }, { 7, 1 } }, new[] { 1, 2, 3, 5, 7, 6, 9 });
                case Career.Bishop: return (new[,] { { 0, 1 }, { 1, 2 }, { 2, 1 }, { 3, 1 }, { 4, 1 }, { 5, 3 }, { 6, 2 }, { 0, 3 }, { 1, 3 }, { 7, 1 }, { 4, 2 } }, new[] { 5, 1, 2, 6, 7, 3 });
                default: return (new int[0, 2], new int[0]);
            }
        }

        /// <summary>Learns rank by rank along the prerequisite chain until <paramref name="target"/>. Returns ranks learned.</summary>
        static int LearnTo(Progression p, CareerSkill s, int target)
        {
            if (s == null || s.kind == CareerSkillKind.Awakening) return 0;
            int learned = 0;
            if (s.Prerequisite != null && p.Rank(s.Prerequisite) == 0) learned += LearnTo(p, CareerCatalog.Get(s.Prerequisite), 1);
            while (p.Rank(s.id) < target && p.NodeLock(s) == "" && p.Learn(s.id)) learned++;
            return learned;
        }

        /// <summary>Applies the build. Returns (ranks learned, keys filled).</summary>
        public static (int learned, int placed) Apply(Progression p)
        {
            if (p == null || !p.IsPromoted) return (0, 0);
            var (ranks, keys) = Plan(p.Career);
            var skills = CareerCatalog.For(p.Career);
            int learned = 0;
            // 1) Every key gets its skill as soon as it can (rank 1 along its chain), the passives' first rank too.
            foreach (int k in keys) learned += LearnTo(p, skills[k], 1);
            // 2) The scripted ranks.
            for (int r = 0; r < ranks.GetLength(0); r++) learned += LearnTo(p, skills[ranks[r, 0]], ranks[r, 1]);
            // 3) No point left unspent while something can be learned: key skills first, then other actives, then passives.
            var order = new List<CareerSkill>();
            foreach (int k in keys) order.Add(skills[k]);
            foreach (var s in skills) if (s.kind == CareerSkillKind.Active && !order.Contains(s)) order.Add(s);
            foreach (var s in skills) if (s.kind == CareerSkillKind.Passive && !order.Contains(s)) order.Add(s);
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var s in order)
                    if (p.NodeLock(s) == "" && p.Learn(s.id)) { learned++; progress = true; break; } // one rank at a time, cheapest first next round
            }
            // Keys: the planned skill, else the next learned active skill not on a key yet.
            int placed = 0;
            var spare = new List<CareerSkill>();
            foreach (int k in keys) if (p.CareerUnlocked(skills[k]) && skills[k].id != SkillCaster.RushId) spare.Add(skills[k]);
            foreach (var s in skills) if (s.kind == CareerSkillKind.Active && p.CareerUnlocked(s) && s.id != SkillCaster.RushId && !spare.Contains(s)) spare.Add(s);
            var used = new HashSet<string>();
            for (int k = 0; k < keys.Length; k++)
            {
                int slot = KeySlot(k);
                if (!p.IsSlotOpen(slot)) continue;
                var want = skills[keys[k]];
                CareerSkill pick = p.CareerUnlocked(want) && want.id != SkillCaster.RushId && !used.Contains(want.id) ? want : spare.Find(s => !used.Contains(s.id));
                if (pick != null && p.EquipSkill(slot, pick.id)) { used.Add(pick.id); placed++; }
            }
            if (p.Awakened) p.EquipSkill(SkillGems.UltimateSlot, skills[8].id);
            FillSupports(p);
            return (learned, placed);
        }

        // ---------- [2026-10-14] Auto-socket support gems ----------
        static Progression watched;
        static bool filling;

        /// <summary>
        /// Watches the local character: whenever a support gem or a socket becomes available (level up, new key), empty
        /// sockets of the equipped skills get the best fitting support gem. Gems the player placed are never moved.
        /// </summary>
        public static void Watch(Progression p)
        {
            if (p == null || p == watched) return;
            if (watched != null) watched.Changed -= OnChanged;
            watched = p;
            p.Changed += OnChanged;
            OnChanged();
        }

        static void OnChanged()
        {
            if (filling || watched == null || Game.Settings == null || !Game.Settings.Data.autoGem) return;
            filling = true;
            try
            {
                int n = FillSupports(watched);
                if (n > 0) GameEvents.RaiseToast(L.F("보조 젬 {0}개를 빈 칸에 자동으로 끼웠습니다.", n));
            }
            finally { filling = false; }
        }

        /// <summary>Empty support sockets of open keys: 피해 증가 first, then 범위 확대 (효율 if that is not open yet). Returns how many it placed.</summary>
        static int FillSupports(Progression p)
        {
            int placed = 0;
            string[] prefer = { "sup_dmg", "sup_aoe", "sup_eff" };
            for (int slot = 0; slot < SkillGems.Slots; slot++)
            {
                if (!p.IsSlotOpen(slot) || p.Active(slot) == null) continue;
                for (int socket = 1; socket <= SkillGems.SupportsPerSlot; socket++)
                {
                    if (p.SlotGem(slot, socket) != null) continue;
                    var options = p.OptionsFor(slot, socket);
                    foreach (var id in prefer)
                        if (options.Contains(id)) { p.SetGem(slot, socket, id); placed++; break; }
                }
            }
            return placed;
        }

        /// <summary>[DEV] Problems of the build at a level: points left while something is learnable, open keys left empty.</summary>
        public static List<string> Check(Career c, int level)
        {
            var issues = new List<string>();
            var p = new Progression();
            p.Reset(CareerCatalog.Base(c));
            p.SetFromServer(level, 0);
            if (!p.Promote(c)) { issues.Add($"{c} Lv{level}: cannot promote"); return issues; }
            Apply(p);
            foreach (var s in CareerCatalog.For(c))
                if (s.kind != CareerSkillKind.Awakening && p.NodeLock(s) == "") issues.Add($"{c} Lv{level}: {s.id} still learnable ({p.CareerPoints} points left)");
            // An open key may stay empty only when every learned active skill already sits on a key.
            var unplaced = new List<string>();
            foreach (var s in CareerCatalog.For(c))
                if (s.kind == CareerSkillKind.Active && s.id != SkillCaster.RushId && p.CareerUnlocked(s) && p.SlotOf(s.id) < 0) unplaced.Add(s.id);
            for (int slot = 0; slot < SkillGems.Slots; slot++)
                if (slot != SkillGems.UltimateSlot && p.IsSlotOpen(slot) && p.Active(slot) == null && unplaced.Count > 0)
                    issues.Add($"{c} Lv{level}: key {slot} empty while {string.Join(",", unplaced)} unplaced");
            return issues;
        }
    }
}
