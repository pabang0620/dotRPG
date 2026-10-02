namespace DotRPG
{
    /// <summary>Quest ids and story flags code refers to (the rest live only in Quests.json / Cutscenes.json).</summary>
    public static class StoryIds
    {
        /// <summary>Chapter 1 prologue (1-1 ~ 1-5): the stable, the festival, the attack, the graves, training.</summary>
        public static readonly string[] Prologue = { "c1_morning", "c1_festival", "c1_burning", "c1_ashes", "c1_rise" };

        /// <summary>World state after the prologue: the stable burned, the family buried, 카엘 is the mentor.</summary>
        public static readonly string[] PrologueFlags = { "attack_done", "stable_burned", "family_buried", "kael_mentor" };

        public const string FlagAttackNight = "attack_night";
    }
}
