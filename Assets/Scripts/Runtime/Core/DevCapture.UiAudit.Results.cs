using System.Collections;
using System.Collections.Generic;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator UiAuditResults()
        {
            // Render outcomes only. Never install this fixture on the dungeon director or grant rewards.
            foreach (string dungeon in new[] { "gold_vein", "raid_xibak" })
            foreach (string outcome in new[] { "cards", "failed", "held", "noanswer" })
            {
                var run = new DungeonRun(DungeonDatabase.Get(dungeon), DungeonDifficulty.Normal, 4, 3)
                {
                    State = outcome == "failed" ? DungeonRunState.Failed : DungeonRunState.Cleared,
                    Rank = DungeonRank.SSS, Elapsed = 247f, Kills = 99, Monsters = 99,
                    HitsTaken = 12, MaxCombo = 124, XpGained = 1000,
                    FailReason = L.T("실패"), NoRewardNote = outcome,
                    CardsTakeAll = dungeon == "raid_xibak", RaidGold = 30000,
                    RaidCoreGain = 12, RaidKeyGain = 3,
                    Cards = outcome == "cards" ? new List<RewardCard>
                    {
                        new RewardCard(ConsumableDatabase.Gold, 12000),
                        new RewardCard("mat_bone", 20), new RewardCard("mat_ore", 20),
                        new RewardCard("mat_essence", 20)
                    } : new List<RewardCard>()
                };
                for (int i = 0; i < 4; i++) run.MemberDamage.Add(("QA12345" + i, 987654 - i * 123456, i == 0));
                Game.Flow.CloseInventory();
                Game.UI.DungeonResult.Setup(run);
                Game.Flow.OpenWindow(Game.UI.DungeonResult);
                yield return Wait(3f);
                yield return UiAuditCapture("result_" + dungeon + "_" + outcome, Game.UI.DungeonResult);
                UiAuditEvidence(true, "Synthetic result layout only; no rewards claimed or dungeon completion verified");
            }
            Game.Flow.CloseInventory();
        }
    }
}
