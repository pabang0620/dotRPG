namespace DotRPG.EditorTools
{
    /// <summary>[CH3 SPEC] skill_specs.json (Docs/ch3/SKILL_SPECIALIZATION.md 4): points rule and the 96 specializations, all chapter 3.</summary>
    public static partial class GameDataExport
    {
        static string SkillSpecsJson()
        {
            var j = Doc();
            j.Key("points").Obj().Num("perLevel", 1).Num("from", SkillSpecs.FromLevel + 1).Num("to", SkillSpecs.FromLevel + SkillSpecs.MaxPoints)
                .Num("active", SkillSpecs.ActiveCost).Num("passive", SkillSpecs.PassiveCost).End();
            j.Arr("specs", SkillSpecs.All, (o, s) => o.Obj().Str("id", s.Id).Str("skill", s.skill).Str("side", s.side.ToString())
                .Str("axis", s.axis).Num("cost", SkillSpecs.Cost(s.skill)).Str("name", s.name).Num("chapter", 3).End());
            return j.End().ToString();
        }
    }
}
