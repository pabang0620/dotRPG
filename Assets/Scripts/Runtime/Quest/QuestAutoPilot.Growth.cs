using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [GROWTH] 성장 추천 사냥 (Docs/PLAN_GROWTH_GUIDE.md): with no quest to follow (the next one needs a higher level),
    /// auto-progress walks to <see cref="GrowthGuide.BestZone"/> and hunts there instead of stopping. Once a quest can be
    /// worked on again (level reached) it goes back to the quests; a level-up into a higher ground moves it there.
    /// </summary>
    public sealed partial class QuestAutoPilot
    {
        bool growth;
        float growthCheckAt;

        /// <summary>The 성장 추천 button: walk to the recommended hunting ground and hunt there.</summary>
        public static void StartGrowth()
        {
            if (Active) Stop("");
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = false;
            Instance.autoStarted = false; Instance.quietStart = false; Instance.subMode = false;
            Instance.growth = true;
            Instance.Begin();
        }

        Goal GrowthGoal()
        {
            var zone = GrowthGuide.BestZone(Game.Session.Progression.Level);
            if (zone == null) return default;
            return new Goal { kind = GoalKind.Hunt, id = "*", map = zone.id, label = L.F("성장 추천: {0}에서 사냥", L.T(zone.name)) };
        }

        /// <summary>
        /// The goal when <see cref="Resolve"/> found nothing: hunt for the 성장 추천 when no quest can be worked on, or take
        /// up the side quests that are open (not checked nor pinned). Default (None) = nothing at all.
        /// </summary>
        Goal NoQuestGoal()
        {
            GrowthGuide.Invalidate();
            if (GrowthGuide.Active)
            {
                var g = GrowthGoal();
                if (g.kind == GoalKind.None) return g;
                growth = true;
                GameEvents.RaiseToast(L.T("따라갈 퀘스트가 없어 성장 추천 사냥터에서 사냥합니다."));
                return g;
            }
            if (!subMode && Targets.Count == 0 && FillSubTargets() > 0)
            {
                subMode = true;
                GameEvents.RaiseToast(L.F("받을 수 있는 서브퀘스트 {0}개를 이어서 진행합니다.", Targets.Count));
                return Resolve();
            }
            return default;
        }

        /// <summary>
        /// Once a second while hunting for the 성장 추천: back to the quests when one can be worked on, or on to a higher
        /// ground after a level-up (<paramref name="atGround"/>: already hunting there; on the way the walk follows the
        /// best ground by itself). True when the hunt was handed back (the walk resumes next frame).
        /// </summary>
        bool GrowthHuntLeaves(bool atGround)
        {
            if (!growth || Time.unscaledTime < growthCheckAt) return false;
            growthCheckAt = Time.unscaledTime + 1f;
            GrowthGuide.Invalidate();
            if (!GrowthGuide.Active)
            {
                growth = false; huntMode = false; goalKey = "";
                GameEvents.RaiseToast(L.T("받을 수 있는 퀘스트가 생겨 퀘스트를 이어 갑니다."));
                return true;
            }
            var zone = GrowthGuide.BestZone(Game.Session.Progression.Level);
            if (atGround && zone != null && zone.id != huntMap)
            {
                huntMode = false; goalKey = "";
                GameEvents.RaiseToast(L.F("레벨이 올라 {0}(으)로 사냥터를 옮깁니다.", L.T(zone.name)));
                return true;
            }
            return false;
        }
    }
}
