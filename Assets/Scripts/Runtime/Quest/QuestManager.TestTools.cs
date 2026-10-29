namespace DotRPG
{
    public sealed partial class QuestManager
    {
        /// <summary>
        /// [TEST TOOLS] The server completed this quest and paid it (test server shortcut): mark it done here without the steps,
        /// cutscenes or claim. Step flags, flag objectives and reward flags are set as if it had been played; XP, gold and items
        /// came with the server's delta. The caller refreshes and saves once after a batch.
        /// </summary>
        public void CompleteFromServer(string questId)
        {
            var q = db.Get(questId);
            if (q == null) return;
            var s = Journal.State(q.id);
            if (s.status == (int)QuestStatus.Completed) return;
            foreach (var step in q.steps)
            {
                foreach (var f in step.setFlags) Journal.Flags.Add(f);
                foreach (var o in step.objectives)
                    if (o.type == ObjectiveTypes.Flag && !string.IsNullOrEmpty(o.target)) Journal.Flags.Add(o.target);
            }
            if (q.reward != null)
            {
                foreach (var f in q.reward.setFlags) Journal.Flags.Add(f);
                if (q.reward.maxHealth > 0 && Game.Player != null) Game.Player.AddMaxHealth(q.reward.maxHealth);
            }
            if (Journal.HasFlag(WorkshopBuiltFlag)) Progress.workshopBuilt = true;
            s.status = (int)QuestStatus.Completed;
            s.step = q.steps.Count;
            s.counts.Clear();
            if (Journal.Tracked == q.id) Journal.Tracked = "";
            GameEvents.RaiseQuestCompleted(q.id);
        }
    }
}
