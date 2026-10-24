using System;
using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    public partial class DevCapture
    {
        // =====================================================================================================
        // [MEASURE CH3 2026-10-24] Chapter 3 and rogue options of -dotrpgMeasure (Docs/PLAN_CHAPTER3_2026-10-21.md 6절 4,
        // Docs/PLAN_CLASS_ROGUE.md 7절 8단계). Not in a release build: DevCapture only starts without DOTRPG_RELEASE.
        // -measureChapter3 1: opens chapter 3 offline for the whole run (level cap 50, 메마른 협곡, 군주, Lv50 boss room), put back at the end.
        // -measureCareers a,b: careers for every part; hunting also takes the base classes warrior, mage, rogue (Lv1-14 rows).
        // -measureDiffs 0,4: dungeons: only these difficulties (4 = 군주); raids: 0 normal, 1 heroic.
        // =====================================================================================================

        static readonly Career[] MeasureAllCareers = { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop, Career.Assassin, Career.Ninja };

        static string[] MeasureList(string name) =>
            MeasureArg(name, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToArray();

        /// <summary>-measureCareers picked from all six careers, or <paramref name="fallback"/> when the option is absent.</summary>
        static Career[] MeasureCareerList(Career[] fallback)
        {
            var names = MeasureList("-measureCareers");
            if (names.Length == 0) return fallback;
            return MeasureAllCareers.Where(c => names.Contains(c.ToString().ToLowerInvariant())).ToArray();
        }

        /// <summary>Hunting heroes: base classes for the Lv1-14 grounds, careers for Lv15+ (the old default without -measureCareers).</summary>
        static List<(CharacterClass cls, Career career)> MeasureHuntPlans()
        {
            var names = MeasureList("-measureCareers");
            var plans = new List<(CharacterClass, Career)>();
            if (names.Length == 0)
            {
                plans.Add((CharacterClass.Warrior, Career.None));
                plans.Add((CharacterClass.Mage, Career.None));
                plans.AddRange(MeasureCareers.Select(c => (CareerCatalog.Base(c), c)));
                return plans;
            }
            foreach (var (name, cls) in new[] { ("warrior", CharacterClass.Warrior), ("mage", CharacterClass.Mage), ("rogue", CharacterClass.Rogue) })
                if (names.Contains(name)) plans.Add((cls, Career.None));
            plans.AddRange(MeasureCareerList(MeasureCareers).Select(c => (CareerCatalog.Base(c), c)));
            return plans;
        }

        static bool MeasureDiffAllowed(int d)
        {
            var list = MeasureList("-measureDiffs");
            return list.Length == 0 || list.Contains(d.ToString());
        }

        /// <summary>Weapon, body and legs id stems of a class (the rogue wears the warrior's plate and greaves).</summary>
        static (string weapon, string body, string legs) MeasureGearStems(CharacterClass cls) =>
            cls == CharacterClass.Warrior ? ("eq_sword_", "eq_plate_", "eq_greaves_")
            : cls == CharacterClass.Rogue ? ("eq_dagger_", "eq_plate_", "eq_greaves_")
            : ("eq_staff_", "eq_robe_", "eq_skirt_");

        /// <summary>The highest gear tier at or below <paramref name="level"/> (Lv45 / Lv50 tiers with chapter 3).</summary>
        static int MeasureGearTier(int level)
        {
            int tier = 1;
            foreach (int t in GearCatalog.TierLevels) if (t <= level && t <= Progression.LevelCap) tier = t;
            return tier;
        }

        bool measureCh3Was;

        void MeasureChapter3Open()
        {
            if (Game.Config == null || MeasureArg("-measureChapter3", "") != "1") return;
            measureCh3Was = Game.Config.chapter3Open;
            Game.Config.chapter3Open = true;
            Log($"measure: chapter 3 open (level cap {Progression.LevelCap})");
        }

        void MeasureChapter3Restore()
        {
            if (Game.Config != null && MeasureArg("-measureChapter3", "") == "1") Game.Config.chapter3Open = measureCh3Was;
        }
    }
}
