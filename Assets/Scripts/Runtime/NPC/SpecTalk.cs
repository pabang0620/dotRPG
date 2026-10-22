using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CH3 step 5] "특화 조정" talk (Docs/ch3/SKILL_SPECIALIZATION.md 1, 5): 이츠나 (전직 안내원, 코바 마을) and 익시 (사얍 마을)
    /// offer the skill window's 특화 tab once specialization is open (chapter 3 open, Lv.41). Same tab the skill window opens;
    /// both NPCs stand in towns, so the choice can be changed right there.
    /// </summary>
    public static class SpecTalk
    {
        public const string Trainer = "career_trainer", Ixi = "ixi";

        /// <summary>True when this NPC offers "특화 조정" now.</summary>
        public static bool Offers(string npcId) =>
            (npcId == Trainer || npcId == Ixi) && Game.Session != null && ContentUnlocks.IsOpen(ContentUnlocks.Feature.Specialization);

        /// <summary>
        /// Asks what to do: 이츠나 keeps her 전직·스킬 answer next to "특화 조정"; 익시 only offers the adjustment.
        /// Returns false (nothing shown) when the NPC does not offer it now.
        /// </summary>
        public static bool Ask(string npcId)
        {
            if (!Offers(npcId) || Game.UI == null) return false;
            var options = new List<(string label, Action act)>();
            if (npcId == Trainer) options.Add((L.T("전직·스킬"), () => Game.UI.Skills.ShowAwakening()));
            options.Add((L.T("특화 조정"), () => Game.UI.Skills.ShowSpecs()));
            options.Add((L.T("그만두기"), null));
            int left = Game.Session.Progression.SpecPointsLeft;
            string question = npcId == Trainer
                ? L.F("무엇을 도와드릴까요? (남은 특화 포인트 {0}점)", left)
                : L.F("갈래를 다시 고르고 싶으면 말해. (남은 특화 포인트 {0}점)", left);
            Game.UI.Choose(question, options);
            return true;
        }
    }
}
