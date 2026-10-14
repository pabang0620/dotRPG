namespace DotRPG
{
    /// <summary>
    /// [AUTO] 서브퀘스트 자동진행: once every main quest is done, the auto-progress button takes every side quest that
    /// can be taken (walks to its giver and accepts it) and runs them one by one, adding side quests that open up on
    /// the way, until none is left. Steps the player must do by hand (dungeons, raids, guide tasks) are skipped over;
    /// when only those are left it stops.
    /// </summary>
    public sealed partial class QuestAutoPilot
    {
        bool subMode;

        /// <summary>No main quest is left: the auto-progress button runs the side quests instead.</summary>
        public static bool MainStoryDone
        {
            get
            {
                var q = Game.Quest;
                if (q == null || q.Database == null || Game.Session == null) return false;
                foreach (var d in q.Database.All)
                    if (d.Kind == QuestKind.Main && q.StatusOf(d.id) != QuestStatus.Completed) return false;
                return true;
            }
        }

        /// <summary>Adds every side quest that can be worked on now; returns how many are on the list.</summary>
        static int FillSubTargets()
        {
            var q = Game.Quest;
            foreach (var d in q.Database.All)
            {
                if (d.Kind != QuestKind.Sub) continue;
                var st = q.StatusOf(d.id);
                if (st == QuestStatus.Completed || st == QuestStatus.Locked || Targets.Contains(d.id)) continue;
                Targets.Add(d.id);
            }
            return Targets.Count;
        }

        /// <summary>Called by Toggle when the main story is done. False: nothing to run (a toast says so).</summary>
        static bool StartSubQuests()
        {
            Targets.Clear();
            int n = FillSubTargets();
            if (n == 0)
            {
                GameEvents.RaiseToast(L.T("진행할 서브퀘스트가 없습니다."));
                return false;
            }
            Instance.subMode = true;
            Instance.quietStart = true;
            GameEvents.RaiseToast(L.F("서브퀘스트 자동진행: 서브퀘스트 {0}개를 모두 받아서 진행합니다. 아무 키나 누르면 멈춥니다.", n));
            return true;
        }

        /// <summary>After a quest is done in 서브퀘스트 자동진행: take newly opened side quests, stop when none is left.</summary>
        void SubQuestCompleted()
        {
            var q = Game.Quest;
            Targets.RemoveAll(id => q.Database.Get(id) == null || q.StatusOf(id) == QuestStatus.Completed);
            if (FillSubTargets() == 0) Stop(L.T("서브퀘스트를 모두 마쳤습니다."));
        }
    }
}
