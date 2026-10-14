using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [DEV] 추천 배치 self-check: every career at every level from the promotion (15) to 40 must spend every point that
    /// can be spent and leave no open key empty while a learned skill is off the keys.
    ///   Unity -batchmode -quit -projectPath . -executeMethod DotRPG.EditorTools.SkillRecommendCheck.Run
    /// </summary>
    public static class SkillRecommendCheck
    {
        [MenuItem("dotRPG/Checks/Recommended skill build")]
        public static void Run()
        {
            var issues = new List<string>();
            foreach (var c in new[] { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop })
                for (int level = 15; level <= Progression.MaxLevel; level++)
                    issues.AddRange(SkillRecommend.Check(c, level));
            if (issues.Count == 0) Debug.Log("[dotRPG] Recommended skill build: OK (4 careers, Lv15-40)");
            else foreach (var i in issues) Debug.LogError("[dotRPG] Recommended skill build: " + i);
            if (Application.isBatchMode) EditorApplication.Exit(issues.Count == 0 ? 0 : 1);
        }
    }
}
