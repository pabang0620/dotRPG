using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Stage of the workshop quest (1-6 마을 복구), kept for the construction site.</summary>
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

    /// <summary>What floats over an NPC's head.</summary>
    public enum QuestMark
    {
        None,
        /// <summary>Has a quest to give ("!").</summary>
        Available,
        /// <summary>Is the next step of an active quest, or waits for the report ("?").</summary>
        Progress,
    }

    /// <summary>
    /// Data-driven quests (Resources/Data/Quests.json): unlocks quests whose requirements are met, offers
    /// them through their giver, advances objectives from gameplay signals (talk, kill, items, maps,
    /// world objects, dungeon clears, story flags, level, cutscenes), plays step cutscenes, pays rewards
    /// and tells NPCs which conversation to use. State lives in <see cref="GameSession.Journal"/> so it is saved.
    /// </summary>
    public sealed partial class QuestManager
    {
        /// <summary>The workshop quest the construction site belongs to.</summary>
        public const string WorkshopQuest = "c1_rebuild";
        public const string WorkshopBuiltFlag = "workshop_built";
        /// <summary>Automated checks other than the story run turn quest progress off (no prologue scenes).</summary>
        public static bool StoryEnabled = true;

        readonly QuestConfig config;
        readonly QuestDatabase db;
        bool bound;
        /// <summary>Re-entrancy guard: objective updates triggered while a step is being applied wait for the next pass.</summary>
        bool evaluating;

        public event Action Changed;

        public QuestManager(QuestConfig config, QuestDatabase database)
        {
            this.config = config;
            db = database;
            GameEvents.EnemyKilled += OnEnemyKilled;
            GameEvents.Interacted += OnInteracted;
            GameEvents.MapEntered += OnMapEntered;
        }

        /// <summary>Hooks systems created after the quest manager (bootstrap order).</summary>
        public void Bind()
        {
            if (bound) return;
            bound = true;
            Game.Session.Inventory.Changed += OnInventoryChanged;
            Game.Session.Progression.LeveledUp += OnLeveledUp;
            if (Game.Dungeon != null) Game.Dungeon.RunEnded += OnRunEnded;
        }

        public void Dispose()
        {
            GameEvents.EnemyKilled -= OnEnemyKilled;
            GameEvents.Interacted -= OnInteracted;
            GameEvents.MapEntered -= OnMapEntered;
            if (!bound || Game.Session == null) return;
            Game.Session.Inventory.Changed -= OnInventoryChanged;
            Game.Session.Progression.LeveledUp -= OnLeveledUp;
            if (Game.Dungeon != null) Game.Dungeon.RunEnded -= OnRunEnded;
        }

        public QuestDatabase Database => db;
        public QuestJournal Journal => Game.Session.Journal;
        public QuestConfig Config => config;

        // =============================== Workshop (construction site) ===============================

        public QuestProgress Progress => Game.Session.Quest;

        public QuestStage Stage
        {
            get
            {
                switch (Journal.Status(WorkshopQuest))
                {
                    case QuestStatus.Active: return QuestStage.Active;
                    case QuestStatus.ReadyToTurnIn: return QuestStage.ReadyToReport;
                    case QuestStatus.Completed: return QuestStage.Completed;
                    default: return QuestStage.NotStarted;
                }
            }
        }

        public int WoodStillNeeded => Math.Max(0, config.requiredWood - Progress.woodDelivered);
        public int StoneStillNeeded => Math.Max(0, config.requiredStone - Progress.stoneDelivered);
        public bool MaterialsComplete => WoodStillNeeded == 0 && StoneStillNeeded == 0;

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

        /// <summary>[SERVER] Delivered totals as the server counts them (site_deliveries).</summary>
        public void SetDeliveredFromServer(int wood, int stone)
        {
            Progress.woodDelivered = Math.Max(0, wood);
            Progress.stoneDelivered = Math.Max(0, stone);
            Changed?.Invoke();
        }

        public void MarkWorkshopBuilt()
        {
            if (Progress.workshopBuilt) return;
            Progress.workshopBuilt = true;
            SetFlag(WorkshopBuiltFlag);
            Game.Flow.Autosave();
        }

        // =============================== Queries ===============================

        /// <summary>Call after loading / new game / travel so listeners refresh.</summary>
        public void NotifyChanged() => Refresh();

        public QuestStatus StatusOf(string id) => Journal.Status(id);
        public bool HasFlag(string flag) => Journal.HasFlag(flag);

        public QuestStepDef CurrentStep(QuestDef q)
        {
            var s = Journal.State(q.id);
            return s.step >= 0 && s.step < q.steps.Count ? q.steps[s.step] : null;
        }

        /// <summary>Quests in journal order with the given status.</summary>
        public List<QuestDef> WithStatus(params QuestStatus[] statuses)
        {
            var list = new List<QuestDef>();
            foreach (var q in db.All)
                if (Array.IndexOf(statuses, Journal.Status(q.id)) >= 0) list.Add(q);
            return list;
        }

        /// <summary>The main quest to follow now (active first, else one waiting for its giver).</summary>
        public QuestDef CurrentMain()
        {
            QuestDef offered = null;
            foreach (var q in db.All)
            {
                if (q.Kind != QuestKind.Main) continue;
                var st = Journal.Status(q.id);
                if (st == QuestStatus.Active || st == QuestStatus.ReadyToTurnIn) return q;
                if (st == QuestStatus.Available && offered == null) offered = q;
            }
            return offered;
        }

        public List<QuestObjective> ObjectivesOf(QuestDef q)
        {
            var list = new List<QuestObjective>();
            var st = Journal.Status(q.id);
            if (st == QuestStatus.Available)
            {
                list.Add(new QuestObjective($"{NpcName(q.giver)}에게 말을 건다", false));
                return list;
            }
            if (st == QuestStatus.ReadyToTurnIn)
            {
                list.Add(new QuestObjective($"{NpcName(q.turnIn)}에게 보고한다", false));
                return list;
            }
            if (st == QuestStatus.Completed)
            {
                list.Add(new QuestObjective("완료", true));
                return list;
            }
            var step = CurrentStep(q);
            if (step == null) return list;
            var counts = Journal.State(q.id).counts;
            for (int i = 0; i < step.objectives.Count; i++)
            {
                var o = step.objectives[i];
                int n = i < counts.Count ? counts[i] : 0;
                int need = Need(o);
                if (o.type == ObjectiveTypes.Collect && !IsObjectiveDone(q, i)) n = Mathf.Min(need, Game.Session.Inventory.Count(o.target));
                if (o.type == ObjectiveTypes.Quests) n = Mathf.Min(need, QuestsDone(o));
                string text = string.IsNullOrEmpty(o.text) ? DefaultObjectiveText(o) : o.text;
                text = text.Replace("{n}", n.ToString()).Replace("{count}", need.ToString());
                if (need > 1 && o.text.IndexOf("{n}", StringComparison.Ordinal) < 0 && o.type != ObjectiveTypes.Level) text += $" {Mathf.Min(n, need)}/{need}";
                list.Add(new QuestObjective(FormatTokens(text), IsObjectiveDone(q, i)));
            }
            return list;
        }

        public QuestMark MarkFor(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return QuestMark.None;
            bool available = false;
            foreach (var q in db.All)
            {
                var st = Journal.Status(q.id);
                if (st == QuestStatus.ReadyToTurnIn && q.turnIn == npcId) return QuestMark.Progress;
                if (st == QuestStatus.Active && TalkObjectiveIndex(q, npcId) >= 0) return QuestMark.Progress;
                if (st == QuestStatus.Available && q.giver == npcId) available = true;
            }
            return available ? QuestMark.Available : QuestMark.None;
        }

        /// <summary>A quest conversation is waiting at this NPC (talk objective, report or offer).</summary>
        public bool HasQuestTalk(string npcId) => MarkFor(npcId) != QuestMark.None;

        /// <summary>True when the npc's mark belongs to a main quest (gold) rather than a side quest (blue).</summary>
        public bool MarkIsMain(string npcId)
        {
            foreach (var q in db.All)
            {
                var st = Journal.Status(q.id);
                bool mine = (st == QuestStatus.ReadyToTurnIn && q.turnIn == npcId)
                    || (st == QuestStatus.Active && TalkObjectiveIndex(q, npcId) >= 0)
                    || (st == QuestStatus.Available && q.giver == npcId);
                if (mine) return q.Kind == QuestKind.Main;
            }
            return true;
        }

        public string NpcName(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return "";
            var def = StoryCast.Find(npcId);
            if (def != null) return def.displayName;
            foreach (var n in Game.Config.npcs) if (n.npcId == npcId) return n.displayName;
            foreach (var n in GameConfig.DefaultNpcs()) if (n.npcId == npcId) return n.displayName;
            return npcId;
        }

        // =============================== Conversations ===============================

        /// <summary>Picks the conversation an NPC uses: the next quest step, a report, a quest offer, else its own.</summary>
        public string DialogueFor(string npcId, string defaultId, string afterQuestId)
        {
            foreach (var q in db.All)
            {
                if (Journal.Status(q.id) != QuestStatus.Active) continue;
                int i = TalkObjectiveIndex(q, npcId);
                if (i >= 0)
                {
                    var o = CurrentStep(q).objectives[i];
                    if (!string.IsNullOrEmpty(o.dialogue)) return o.dialogue;
                }
            }
            foreach (var q in db.All)
                if (Journal.Status(q.id) == QuestStatus.ReadyToTurnIn && q.turnIn == npcId && !string.IsNullOrEmpty(q.turnInDialogue)) return q.turnInDialogue;
            foreach (var q in db.All)
                if (Journal.Status(q.id) == QuestStatus.Available && q.giver == npcId && !string.IsNullOrEmpty(q.offerDialogue)) return q.offerDialogue;

            // The carpenter talks about the workshop while it is the open job.
            if (npcId == config.builderNpcId)
            {
                if (Progress.workshopBuilt) return config.builderDoneDialogue;
                if (Stage != QuestStage.NotStarted) return config.builderProgressDialogue;
            }
            if (Journal.IsCompleted(WorkshopQuest) && !string.IsNullOrEmpty(afterQuestId)) return afterQuestId;
            // An npc whose own line is the offer of a quest that is not open (yet / any more) says its idle line.
            foreach (var q in db.All)
                if (q.offerDialogue == defaultId && Journal.Status(q.id) != QuestStatus.Available)
                    return Game.Dialogues.TryGet(npcId + "_idle", out _) ? npcId + "_idle" : defaultId;
            return defaultId;
        }

        /// <summary>Called when a conversation with <paramref name="npcId"/> ends: accepts offers, completes talk objectives and reports.</summary>
        public void OnDialogueFinished(string dialogueId, string npcId = "")
        {
            bool changed = false;
            foreach (var q in db.All)
            {
                var st = Journal.Status(q.id);
                if (st == QuestStatus.Available && q.giver == npcId && q.offerDialogue == dialogueId)
                {
                    Accept(q);
                    changed = true;
                }
                else if (st == QuestStatus.ReadyToTurnIn && q.turnIn == npcId && (string.IsNullOrEmpty(q.turnInDialogue) || q.turnInDialogue == dialogueId))
                {
                    Complete(q);
                    changed = true;
                }
                else if (st == QuestStatus.Active)
                {
                    int i = TalkObjectiveIndex(q, npcId);
                    if (i < 0) continue;
                    var o = CurrentStep(q).objectives[i];
                    if (!string.IsNullOrEmpty(o.dialogue) && o.dialogue != dialogueId) continue;
                    Advance(q, i, 1);
                    changed = true;
                }
            }
            if (changed) Refresh();
        }

        // =============================== Signals ===============================

        void OnEnemyKilled(string enemyId) => Signal(ObjectiveTypes.Kill, enemyId, 1);
        void OnInteracted(string id) => Signal(ObjectiveTypes.Interact, id, 1);
        void OnInventoryChanged(string id, int count, int delta) => Refresh();
        void OnLeveledUp(int level) => Refresh();

        void OnMapEntered(string mapId)
        {
            var map = MapRegistry.Get(mapId);
            StoryCompanions.Refresh(map != null && !map.safe);
            Signal(ObjectiveTypes.Reach, mapId, 1);
            TryAutoCutscenes();
        }

        void OnRunEnded(DungeonRun run)
        {
            if (run == null || run.State != DungeonRunState.Cleared) return;
            Signal(run.Dungeon.isRaid ? ObjectiveTypes.Raid : ObjectiveTypes.Dungeon, run.Dungeon.id, 1);
        }

    }
}
