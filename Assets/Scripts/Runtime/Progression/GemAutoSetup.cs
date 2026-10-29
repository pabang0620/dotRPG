using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [GEM AUTO 2026-10-29] 간편 젬 세팅: picks the support gems of every equipped skill for a 흡혈 / 범위 / 타격 mix
    /// (Docs/PLAN_GEM_AUTOSETUP_2026-10-29.md). Pure math, no Unity types: the skill window builds the inputs from the
    /// real skill numbers (CharacterStatsCalc.Skill with no supports) and applies the plan through Progression.SetSupports.
    ///
    /// Per skill and gem pair G (D = damage per second of the skill):
    ///   D = damage x more(G) x hits x (1 + repeats(G)) / max(MinPeriod, cooldown)
    ///   E = clamp(PackDensity x pi x (radius x sqrt(area(G)))^2, 1, min(PackSize, maxTargets))   expected monsters hit
    ///   T (타격) = D x (1 + boss%(G)),  A (범위) = D x E,  S (흡혈) = A x leech%(G)  (heal skills: S = D, A = T = 0)
    /// Objective: max sum_k w_k x (sum over skills of axis_k) / Best_k, Best_k = the best total of that axis alone,
    /// subject to: MP per second of the bar (mp x manaMult(G) / cooldown, summed) within the cost of today's
    /// auto-socket build (피해 + 범위 on every skill). Support copies are unlimited (unlocked by level, not owned) and a
    /// gem may not sit twice in one skill, so the only coupling is the MP budget: a multiple-choice knapsack.
    /// </summary>
    public static class GemAutoSetup
    {
        /// <summary>Modelling assumptions: a hunting pack of 6 at 0.12 monsters per square metre (about 4 inside a
        /// 3.2 m spin), and a spammable skill is pressed at most every 0.35 s.</summary>
        public const float PackSize = 6f, PackDensity = 0.12f, MinPeriod = 0.35f;
        /// <summary>MP budget per skill relative to no supports: 피해 증가 (x1.3) + 범위 확대 (x1.15), what auto-socket fills.</summary>
        public const float MpBudgetMult = 1.3f * 1.15f;
        /// <summary>Steps the MP budget is cut into for the knapsack (costs round up, so the budget always holds).</summary>
        public const int Buckets = 2000;

        public enum Axis { Sustain = 0, Aoe = 1, Single = 2 }

        /// <summary>Preset weights (흡혈, 범위, 타격) in percent: 흡혈 세팅, 범위 세팅, 타격 세팅.</summary>
        public static readonly int[][] Presets = { new[] { 60, 25, 15 }, new[] { 10, 75, 15 }, new[] { 10, 15, 75 } };

        public sealed class Gem
        {
            public string id;
            public int moreDamage, moreAoe, repeats, leechPct, bossPct;
            public float manaMult = 1f;
        }

        public sealed class Skill
        {
            public int slot;
            /// <summary>Damage per hit with no supports, hits per cast, extra casts from the tree.</summary>
            public float damage;
            public int hits = 1, repeats;
            public float cooldown, radius;
            /// <summary>MP per cast with no supports.</summary>
            public float mpCost;
            /// <summary>Most monsters one cast can reach (0 = no limit beyond the pack).</summary>
            public int maxTargets;
            /// <summary>Heals instead of hitting (counts on 흡혈 only); a guard or buff with no damage counts nowhere.</summary>
            public bool heal, noDamage;
            /// <summary>Support gems that change this skill (a career skill takes only 피해 / 범위 / 효율).</summary>
            public List<string> allowed = new List<string>();
            /// <summary>Gems in the two sockets now (null = empty), kept when nothing is better.</summary>
            public string current1, current2;
        }

        public struct Axes
        {
            public float sustain, aoe, single;
            public float this[Axis a] => a == Axis.Sustain ? sustain : a == Axis.Aoe ? aoe : single;
            public static Axes operator +(Axes x, Axes y) => new Axes { sustain = x.sustain + y.sustain, aoe = x.aoe + y.aoe, single = x.single + y.single };
        }

        public sealed class Plan
        {
            public int slot;
            public string gem1, gem2;
            public Axes value;
            public float mana;
        }

        public sealed class Result
        {
            public readonly List<Plan> plans = new List<Plan>();
            public Axes total, best, current;
            public float score;
            /// <summary>MP per second of the plan on cooldown, and the budget it had to stay within (0 = none).</summary>
            public float mpUsed, mpBudget;
            /// <summary>Share of the best reachable value of an axis in percent, or -1 when nothing reaches that axis.</summary>
            public int Percent(Axis a) => Pct(total, a);
            public int CurrentPercent(Axis a) => Pct(current, a);
            int Pct(Axes v, Axis a) => best[a] <= 0f ? -1 : (int)Math.Round(100.0 * v[a] / best[a]);
        }

        /// <summary>Expected monsters hit by one cast of this radius.</summary>
        public static float Targets(float radius, int maxTargets)
        {
            float cap = maxTargets > 0 ? Math.Min(PackSize, maxTargets) : PackSize;
            float e = PackDensity * (float)Math.PI * radius * radius;
            return Math.Max(1f, Math.Min(cap, e));
        }

        /// <summary>The three axis values of a skill with gems a and b (either may be null), and the MP multiplier.</summary>
        public static Axes Evaluate(Skill s, Gem a, Gem b, out float mana)
        {
            float more = 1f, area = 1f;
            int repeats = s.repeats, leech = 0, boss = 0;
            mana = 1f;
            foreach (var g in new[] { a, b })
            {
                if (g == null) continue;
                more *= 1f + g.moreDamage / 100f;
                area *= 1f + g.moreAoe / 100f;
                mana *= g.manaMult;
                repeats += g.repeats;
                leech += g.leechPct;
                boss += g.bossPct;
            }
            if (s.noDamage || s.damage <= 0f) return default;
            float perSecond = s.damage * more * Math.Max(1, s.hits) * (1 + repeats) / Math.Max(MinPeriod, s.cooldown);
            if (s.heal) return new Axes { sustain = perSecond };
            float pack = perSecond * Targets(s.radius * (float)Math.Sqrt(area), s.maxTargets);
            return new Axes { aoe = pack, single = perSecond * (1f + boss / 100f), sustain = pack * leech / 100f };
        }

        /// <summary>Every gem pair a skill can take. With <paramref name="fillAll"/> (the auto-socket setting is on and would
        /// fill an empty socket anyway) both sockets are filled whenever two gems fit.</summary>
        public static List<(Gem a, Gem b)> Combos(Skill s, IList<Gem> gems, bool fillAll)
        {
            var list = new List<Gem>();
            foreach (var g in gems) if (g != null && s.allowed.Contains(g.id)) list.Add(g);
            var combos = new List<(Gem, Gem)>();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++) combos.Add((list[i], list[j]));
            if (!fillAll || list.Count < 2) foreach (var g in list) combos.Add((g, null));
            if (!fillAll || list.Count == 0) combos.Add((null, null));
            return combos;
        }

        static Gem Find(IList<Gem> gems, Skill s, string id)
        {
            if (id == null || !s.allowed.Contains(id)) return null; // a gem that does nothing on this skill counts as empty
            foreach (var g in gems) if (g != null && g.id == id) return g;
            return null;
        }

        static float Score(Axes v, Axes best, float[] w)
        {
            float j = 0f;
            for (int k = 0; k < 3; k++) if (best[(Axis)k] > 0f) j += w[k] * v[(Axis)k] / best[(Axis)k];
            return j;
        }

        static float[] Weights(float sustain, float aoe, float single)
        {
            float sum = Math.Max(0f, sustain) + Math.Max(0f, aoe) + Math.Max(0f, single);
            if (sum <= 0f) return new[] { 1f / 3f, 1f / 3f, 1f / 3f };
            return new[] { Math.Max(0f, sustain) / sum, Math.Max(0f, aoe) / sum, Math.Max(0f, single) / sum };
        }

        /// <summary>MP per second of a skill with no supports when pressed on cooldown.</summary>
        public static float MpDemand(Skill s) => Math.Max(0f, s.mpCost) / Math.Max(MinPeriod, s.cooldown);

        /// <summary>The MP budget: what the bar costs per second with 피해 증가 + 범위 확대 on every skill (today's
        /// auto-socket build), so the auto setup never needs more MP potions than the standard build.</summary>
        public static float MpBudget(IList<Skill> skills)
        {
            float b = 0f;
            foreach (var s in skills) b += MpDemand(s) * MpBudgetMult;
            return b;
        }

        sealed class Option { public Gem a, b; public Axes v; public float mana, cost; }

        /// <summary>
        /// Multiple-choice knapsack: one pair per skill, total MP per second within the budget, maximise the sum of
        /// <paramref name="value"/>. Dynamic programme over the budget cut into <see cref="Buckets"/> steps (costs round
        /// up, so the budget is never exceeded): skills x buckets x pairs = 8 x 2000 x 22, about 350k steps.
        /// Returns the chosen option of each skill, or null when even the cheapest bar does not fit.
        /// </summary>
        static int[] Knapsack(List<List<Option>> options, float budget, Func<Axes, float> value, out float total)
        {
            int n = options.Count;
            total = 0f;
            var pick = new int[n];
            if (budget <= 0f)
            {
                // Nothing costs MP: every skill takes its own best (first one on ties = preferred order).
                for (int i = 0; i < n; i++)
                {
                    float top = float.MinValue;
                    for (int c = 0; c < options[i].Count; c++) { float v = value(options[i][c].v); if (v > top + 1e-9f) { top = v; pick[i] = c; } }
                    total += top;
                }
                return pick;
            }
            float unit = budget / Buckets;
            var dp = new float[n + 1, Buckets + 1];
            var choice = new int[n, Buckets + 1];
            for (int b = 0; b <= Buckets; b++) dp[0, b] = b == 0 ? 0f : float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                for (int b = 0; b <= Buckets; b++) { dp[i + 1, b] = float.NegativeInfinity; choice[i, b] = -1; }
                for (int c = 0; c < options[i].Count; c++)
                {
                    var o = options[i][c];
                    int cb = (int)Math.Ceiling(o.cost / unit - 1e-4);
                    if (cb > Buckets) continue;
                    float v = value(o.v);
                    for (int b = cb; b <= Buckets; b++)
                    {
                        float prev = dp[i, b - cb];
                        if (float.IsNegativeInfinity(prev)) continue;
                        float sum = prev + v;
                        if (sum > dp[i + 1, b] + 1e-7f * Math.Max(1f, Math.Abs(sum))) { dp[i + 1, b] = sum; choice[i, b] = c; }
                    }
                }
            }
            // The best total; on a tie the cheapest MP.
            int at = -1;
            float bestTotal = float.NegativeInfinity;
            for (int b = 0; b <= Buckets; b++)
                if (dp[n, b] > bestTotal + 1e-6f * Math.Max(1f, Math.Abs(dp[n, b]))) { bestTotal = dp[n, b]; at = b; }
            if (at < 0) return null;
            total = bestTotal;
            for (int i = n - 1; i >= 0; i--)
            {
                int c = choice[i, at];
                pick[i] = c;
                at -= (int)Math.Ceiling(options[i][c].cost / unit - 1e-4);
            }
            return pick;
        }

        /// <summary>
        /// The optimal plan: the best reachable total of each axis alone under the MP budget (3 knapsacks), then the
        /// weighted, normalised objective (a 4th). About 1.4M simple steps at most, a few milliseconds.
        /// Pairs are tried current gems first, then more sockets filled, so ties keep what the player has.
        /// </summary>
        public static Result Solve(IList<Skill> skills, IList<Gem> gems, float sustain, float aoe, float single, bool fillAll)
        {
            var w = Weights(sustain, aoe, single);
            var r = new Result();
            var options = new List<List<Option>>();
            foreach (var s in skills)
            {
                var list = new List<Option>();
                foreach (var (a, b) in Combos(s, gems, fillAll))
                {
                    var v = Evaluate(s, a, b, out float mana);
                    list.Add(new Option { a = a, b = b, v = v, mana = mana, cost = MpDemand(s) * mana });
                }
                list.Sort((x, y) =>
                {
                    int cx = SameAsCurrent(s, x.a?.id, x.b?.id) ? 0 : 1, cy = SameAsCurrent(s, y.a?.id, y.b?.id) ? 0 : 1;
                    if (cx != cy) return cx.CompareTo(cy);
                    int fx = (x.a != null ? 1 : 0) + (x.b != null ? 1 : 0), fy = (y.a != null ? 1 : 0) + (y.b != null ? 1 : 0);
                    return fy.CompareTo(fx);
                });
                options.Add(list);
                r.current += Evaluate(s, Find(gems, s, s.current1), Find(gems, s, s.current2), out _);
            }
            float budget = MpBudget(skills);
            // A bar that cannot fit the budget at all (not with the gems in the game) runs without it.
            if (Knapsack(options, budget, v => 0f, out _) == null) budget = 0f;
            Knapsack(options, budget, v => v.sustain, out r.best.sustain);
            Knapsack(options, budget, v => v.aoe, out r.best.aoe);
            Knapsack(options, budget, v => v.single, out r.best.single);
            var best = r.best;
            var pick = Knapsack(options, budget, v => Score(v, best, w), out r.score);
            for (int i = 0; i < skills.Count; i++)
            {
                var s = skills[i];
                var o = options[i][pick[i]];
                var plan = new Plan { slot = s.slot, gem1 = o.a?.id, gem2 = o.b?.id, value = o.v, mana = o.mana };
                // Keep the socket order the player sees when the pair is the same.
                if (plan.gem1 != null && plan.gem1 == s.current2 && plan.gem2 == s.current1) { plan.gem1 = s.current1; plan.gem2 = s.current2; }
                r.plans.Add(plan);
                r.total += o.v;
                r.mpUsed += o.cost;
            }
            r.mpBudget = budget;
            return r;
        }

        static bool SameAsCurrent(Skill s, string a, string b) => (a == s.current1 && b == s.current2) || (a == s.current2 && b == s.current1);

        /// <summary>Sets slider <paramref name="changed"/> to <paramref name="value"/> and rescales the other two in
        /// proportion so the three always sum to 100 (split evenly when both were 0).</summary>
        public static int[] Rebalance(int[] weights, int changed, int value)
        {
            var w = (int[])weights.Clone();
            value = Math.Max(0, Math.Min(100, value));
            int o1 = (changed + 1) % 3, o2 = (changed + 2) % 3;
            int rest = 100 - value, sum = w[o1] + w[o2];
            int n1 = sum <= 0 ? (rest + 1) / 2 : (int)Math.Round((double)rest * w[o1] / sum);
            w[changed] = value;
            w[o1] = n1;
            w[o2] = rest - n1;
            return w;
        }

        // ---------- Self-check (no Unity needed; Tools/tests/gem-autosetup runs it) ----------

        /// <summary>The live support gems (SkillGems v2 numbers) for the self-check.</summary>
        static List<Gem> CheckGems() => new List<Gem>
        {
            new Gem { id = "sup_dmg", moreDamage = 35, manaMult = 1.3f },
            new Gem { id = "sup_aoe", moreAoe = 35, manaMult = 1.15f },
            new Gem { id = "sup_multi", repeats = 1, moreDamage = -30, manaMult = 1.4f },
            new Gem { id = "sup_eff", manaMult = 0.7f },
            new Gem { id = "sup_leech", leechPct = 10 },
            new Gem { id = "sup_chain", bossPct = 30 },
        };

        static Skill CheckSkill(int slot, float damage, float cooldown, float radius, float mp, bool career, int hits = 1)
        {
            var s = new Skill { slot = slot, damage = damage, cooldown = cooldown, radius = radius, mpCost = mp, hits = hits };
            foreach (var g in CheckGems()) if (!career || g.id == "sup_dmg" || g.id == "sup_aoe" || g.id == "sup_eff") s.allowed.Add(g.id);
            return s;
        }

        /// <summary>A warrior-like bar: 파쇄 일격 (single), 회전 베기 (area), 전쟁 함성, a career spam skill, a career guard.</summary>
        public static List<Skill> CheckBar()
        {
            var skills = new List<Skill>
            {
                CheckSkill(0, 27f, 1.6f, 1.3f, 4f, false),
                CheckSkill(1, 17f, 3f, 3.2f, 8f, false),
                CheckSkill(2, 5f, 10f, 4.2f, 12f, false),
                CheckSkill(3, 14f, 0f, 1.2f, 4f, true, 4),
                CheckSkill(5, 0f, 62f, 0f, 20f, true),
            };
            skills[4].noDamage = true;
            skills[0].current1 = "sup_dmg"; skills[0].current2 = "sup_aoe";
            return skills;
        }

        public static List<Gem> CheckGemList() => CheckGems();

        /// <summary>Problems found (empty = all good): sliders sum to 100, the knapsack equals an exhaustive search over
        /// every combination of every skill, the MP budget holds, and each preset leans the way its name says.</summary>
        public static List<string> SelfCheck()
        {
            var issues = new List<string>();
            var gems = CheckGems();
            int[][] cases = { new[] { 30, 40, 30 }, new[] { 0, 0, 100 }, new[] { 50, 50, 0 }, new[] { 33, 33, 34 } };
            foreach (var c in cases)
                for (int k = 0; k < 3; k++)
                    for (int v = 0; v <= 100; v += 7)
                    {
                        var w = Rebalance(c, k, v);
                        if (w[0] + w[1] + w[2] != 100 || w[k] != v || Array.Exists(w, x => x < 0)) issues.Add($"Rebalance {string.Join(",", c)} k{k}={v} -> {string.Join(",", w)}");
                    }
            var skills = CheckBar();
            float budget = MpBudget(skills);
            foreach (bool fill in new[] { true, false })
                foreach (var wt in new[] { new[] { 1f, 0f, 0f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0f, 1f }, new[] { .3f, .5f, .2f }, new[] { .6f, .25f, .15f } })
                {
                    var r = Solve(skills, gems, wt[0], wt[1], wt[2], fill);
                    var weights = Weights(wt[0], wt[1], wt[2]);
                    // Exhaustive reference: exact budget (upper bound) and the budget less the rounding slack (lower bound).
                    float upper = Brute(skills, gems, r.best, weights, fill, 0, 0f, 0f, budget);
                    float lower = Brute(skills, gems, r.best, weights, fill, 0, 0f, 0f, budget * (1f - (float)skills.Count / Buckets));
                    if (r.score > upper + 1e-4f || r.score < lower - 1e-4f) issues.Add($"Solve {string.Join(",", wt)} fill={fill}: {r.score} outside brute [{lower}, {upper}]");
                    if (r.mpUsed > budget * 1.0001f) issues.Add($"Solve {string.Join(",", wt)}: MP {r.mpUsed} over budget {budget}");
                    foreach (var p in r.plans)
                    {
                        var s = skills.Find(x => x.slot == p.slot);
                        if ((p.gem1 != null && !s.allowed.Contains(p.gem1)) || (p.gem2 != null && !s.allowed.Contains(p.gem2)) || (p.gem1 != null && p.gem1 == p.gem2))
                            issues.Add($"Solve slot {p.slot}: illegal pair {p.gem1}/{p.gem2}");
                        if (fill && s.allowed.Count >= 2 && (p.gem1 == null || p.gem2 == null)) issues.Add($"Solve slot {p.slot}: socket left empty with auto-fill on");
                    }
                }
            bool Has(Result r, int slot, string id) => r.plans.Exists(p => p.slot == slot && (p.gem1 == id || p.gem2 == id));
            var full = new Result[3];
            for (int k = 0; k < 3; k++) full[k] = Solve(skills, gems, Presets[k][0], Presets[k][1], Presets[k][2], true);
            if (Array.Exists(full, r => Has(r, 3, "sup_leech"))) issues.Add("leech on a career skill (it does nothing there)");
            if (Array.Exists(full, r => Has(r, 4, "sup_dmg"))) issues.Add("guard skill took more damage (more MP for nothing)");
            // Before the career: 파쇄 일격, 회전 베기, 전쟁 함성, 돌진 베기.
            var bar = skills.GetRange(0, 3);
            var charge = CheckSkill(3, 25f, 4.5f, 1.6f, 9f, false);
            bar.Add(charge);
            var pre = new Result[3];
            for (int k = 0; k < 3; k++) pre[k] = Solve(bar, gems, Presets[k][0], Presets[k][1], Presets[k][2], true);
            if (!Has(pre[0], 1, "sup_leech")) issues.Add("sustain preset: no leech on the area skill");
            // A slider at 100% reaches the whole of its axis. (Mixed presets trade axes, so no per-axis order between them.)
            for (int k = 0; k < 3; k++)
            {
                var pure = Solve(bar, gems, k == 0 ? 100 : 0, k == 1 ? 100 : 0, k == 2 ? 100 : 0, true);
                if (pure.Percent((Axis)k) != 100) issues.Add($"axis {k} at 100% reaches only {pure.Percent((Axis)k)}%");
            }
            return issues;
        }

        /// <summary>Exhaustive search over every pair of every skill within an MP budget (the self-check's reference).</summary>
        static float Brute(List<Skill> skills, List<Gem> gems, Axes best, float[] w, bool fill, int i, float sum, float mp, float budget)
        {
            if (mp > budget) return float.MinValue;
            if (i == skills.Count) return sum;
            float top = float.MinValue;
            foreach (var (a, b) in Combos(skills[i], gems, fill))
            {
                var v = Evaluate(skills[i], a, b, out float mana);
                top = Math.Max(top, Brute(skills, gems, best, w, fill, i + 1, sum + Score(v, best, w), mp + MpDemand(skills[i]) * mana, budget));
            }
            return top;
        }
    }
}
