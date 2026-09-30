using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The vertical slice's main quest: rebuild the village workshop.
    /// Requirements and the NPCs involved are data so the quest can be re-tuned without code.
    /// </summary>
    [CreateAssetMenu(menuName = "dotRPG/Quest Config", fileName = "QuestConfig")]
    public class QuestConfig : ScriptableObject
    {
        public string questId = "rebuild_workshop";
        public string title = "공방 재건";

        [Header("Requirements")]
        public int requiredWood = 6;
        public int requiredStone = 4;
        public int requiredSkeletonKills = 3;
        public string targetEnemyId = "skeleton";

        [Header("Reward")]
        [Tooltip("Extra max health on completion (2 = one heart).")]
        public int rewardMaxHealth = 20;

        [Header("NPCs")]
        public string questGiverNpcId = "chief";
        public string builderNpcId = "builder";

        [Header("Dialogue ids (see Resources/Data/Dialogues.json)")]
        public string giverIntroDialogue = "chief_intro";
        public string giverProgressDialogue = "chief_progress";
        public string giverReportDialogue = "chief_report";
        public string giverAfterDialogue = "chief_after";
        public string builderBeforeDialogue = "builder_before";
        public string builderProgressDialogue = "builder_progress";
        public string builderDoneDialogue = "builder_done";
        public string siteLockedDialogue = "site_locked";
        public string siteBuiltDialogue = "site_built";
    }
}
