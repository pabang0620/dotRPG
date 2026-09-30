using System;
using System.Collections.Generic;

namespace DotRPG
{
    public enum QuestStage
    {
        NotStarted = 0,
        Active = 1,
        ReadyToReport = 2,
        Completed = 3,
    }

    public struct QuestObjective
    {
        public string text;
        public bool done;

        public QuestObjective(string text, bool done)
        {
            this.text = text;
            this.done = done;
        }
    }

    /// <summary>
    /// The slice's main quest: talk to the chief → gather wood &amp; stone, deliver them to the
    /// construction site, defeat skeletons → report back. State lives in GameSession.Quest so it is
    /// saved automatically. A general quest system (multiple quests, data-driven steps) can replace
    /// this class later behind the same API (Stage, Objectives, DialogueFor, OnDialogueFinished).
    /// </summary>
    public sealed class QuestManager
    {
        readonly QuestConfig config;

        public event Action Changed;

        public QuestManager(QuestConfig config)
        {
            this.config = config;
            GameEvents.EnemyKilled += OnEnemyKilled;
        }

        public void Dispose() => GameEvents.EnemyKilled -= OnEnemyKilled;

        public QuestConfig Config => config;
        public QuestProgress Progress => Game.Session.Quest;
        public QuestStage Stage => (QuestStage)Progress.stage;

        public int WoodStillNeeded => Math.Max(0, config.requiredWood - Progress.woodDelivered);
        public int StoneStillNeeded => Math.Max(0, config.requiredStone - Progress.stoneDelivered);
        public bool MaterialsComplete => WoodStillNeeded == 0 && StoneStillNeeded == 0;
        public bool KillsComplete => Progress.skeletonsDefeated >= config.requiredSkeletonKills;

        /// <summary>Call after loading/new game so listeners refresh.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public void StartQuest()
        {
            if (Stage != QuestStage.NotStarted) return;
            Progress.stage = (int)QuestStage.Active;
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast($"새 의뢰: {config.title}");
            Changed?.Invoke();
            Game.Flow.Autosave();
        }

        void OnEnemyKilled(string enemyId)
        {
            if (Stage != QuestStage.Active || enemyId != config.targetEnemyId) return;
            if (Progress.skeletonsDefeated >= config.requiredSkeletonKills) return;
            Progress.skeletonsDefeated++;
            GameEvents.RaiseToast($"해골 퇴치 {Progress.skeletonsDefeated}/{config.requiredSkeletonKills}");
            CheckReadyToReport();
            Changed?.Invoke();
        }

        /// <summary>Moves as many needed materials as possible from the inventory into the site.</summary>
        public void DeliverMaterials(Inventory inventory, out int wood, out int stone)
        {
            wood = Math.Min(inventory.Count(ItemIds.Wood), WoodStillNeeded);
            stone = Math.Min(inventory.Count(ItemIds.Stone), StoneStillNeeded);
            if (wood > 0) inventory.Remove(ItemIds.Wood, wood);
            if (stone > 0) inventory.Remove(ItemIds.Stone, stone);
            Progress.woodDelivered += wood;
            Progress.stoneDelivered += stone;
            if (wood + stone > 0) Changed?.Invoke();
        }

        public void MarkWorkshopBuilt()
        {
            if (Progress.workshopBuilt) return;
            Progress.workshopBuilt = true;
            CheckReadyToReport();
            Changed?.Invoke();
            Game.Flow.Autosave();
        }

        void CheckReadyToReport()
        {
            if (Stage != QuestStage.Active || !Progress.workshopBuilt || !KillsComplete) return;
            Progress.stage = (int)QuestStage.ReadyToReport;
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast("모든 일을 마쳤다! 촌장에게 보고하자.");
        }

        void CompleteQuest()
        {
            if (Stage != QuestStage.ReadyToReport) return;
            Progress.stage = (int)QuestStage.Completed;
            if (Game.Player != null && config.rewardMaxHealth > 0) Game.Player.AddMaxHealth(config.rewardMaxHealth);
            // Equipment reward: goes to the bag (open it with the inventory key to wear it).
            Game.Session.Inventory.Add("eq_ring_ruby", 1);
            GameEvents.RaiseToast("보상: 루비 반지를 받았다! (가방에서 장착)");
            Changed?.Invoke();
            Game.Flow.Autosave();
            Game.Flow.ShowEnding();
        }

        /// <summary>Picks the dialogue an NPC should use given quest progress.</summary>
        public string DialogueFor(string npcId, string defaultId, string afterQuestId)
        {
            if (npcId == config.questGiverNpcId)
            {
                switch (Stage)
                {
                    case QuestStage.NotStarted: return config.giverIntroDialogue;
                    case QuestStage.Active: return config.giverProgressDialogue;
                    case QuestStage.ReadyToReport: return config.giverReportDialogue;
                    default: return config.giverAfterDialogue;
                }
            }
            if (npcId == config.builderNpcId)
            {
                if (Progress.workshopBuilt) return config.builderDoneDialogue;
                return Stage == QuestStage.NotStarted ? config.builderBeforeDialogue : config.builderProgressDialogue;
            }
            if (Stage == QuestStage.Completed && !string.IsNullOrEmpty(afterQuestId)) return afterQuestId;
            return defaultId;
        }

        /// <summary>Dialogue ids can trigger quest transitions (intro starts the quest, report ends it).</summary>
        public void OnDialogueFinished(string dialogueId)
        {
            if (dialogueId == config.giverIntroDialogue) StartQuest();
            else if (dialogueId == config.giverReportDialogue) CompleteQuest();
        }

        public List<QuestObjective> GetObjectives()
        {
            var list = new List<QuestObjective>();
            switch (Stage)
            {
                case QuestStage.NotStarted:
                    list.Add(new QuestObjective("마을 촌장과 이야기하기", false));
                    break;
                case QuestStage.Active:
                    if (!Progress.workshopBuilt)
                    {
                        list.Add(new QuestObjective($"목재 전달 {Progress.woodDelivered}/{config.requiredWood}  (보유 {Game.Session.Inventory.Count(ItemIds.Wood)})", WoodStillNeeded == 0));
                        list.Add(new QuestObjective($"돌 전달 {Progress.stoneDelivered}/{config.requiredStone}  (보유 {Game.Session.Inventory.Count(ItemIds.Stone)})", StoneStillNeeded == 0));
                    }
                    else list.Add(new QuestObjective("공방 건설", true));
                    list.Add(new QuestObjective($"해골 퇴치 {Progress.skeletonsDefeated}/{config.requiredSkeletonKills}", KillsComplete));
                    break;
                case QuestStage.ReadyToReport:
                    list.Add(new QuestObjective("촌장에게 보고하기", false));
                    break;
                case QuestStage.Completed:
                    list.Add(new QuestObjective("의뢰 완료! 마을을 자유롭게 둘러보자", true));
                    break;
            }
            return list;
        }

        public string FormatTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            return text
                .Replace("{wood_required}", config.requiredWood.ToString())
                .Replace("{stone_required}", config.requiredStone.ToString())
                .Replace("{kills_required}", config.requiredSkeletonKills.ToString())
                .Replace("{wood_left}", WoodStillNeeded.ToString())
                .Replace("{stone_left}", StoneStillNeeded.ToString())
                .Replace("{kills_left}", Math.Max(0, config.requiredSkeletonKills - Progress.skeletonsDefeated).ToString())
                .Replace("{attack_key}", Game.Input.GetBindingLabel(GameAction.Attack))
                .Replace("{interact_key}", Game.Input.GetBindingLabel(GameAction.Interact))
                .Replace("{item_key}", Game.Input.GetBindingLabel(GameAction.UseItem))
                .Replace("{pause_key}", Game.Input.GetBindingLabel(GameAction.Pause));
        }
    }
}
