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
    public sealed class QuestManager
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
                string text = string.IsNullOrEmpty(o.text) ? DefaultObjectiveText(o) : o.text;
                text = text.Replace("{n}", n.ToString()).Replace("{count}", need.ToString());
                if (need > 1 && o.text.IndexOf("{n}", StringComparison.Ordinal) < 0 && o.type != ObjectiveTypes.Level) text += $" {Mathf.Min(n, need)}/{need}";
                list.Add(new QuestObjective(FormatTokens(text), IsObjectiveDone(q, i)));
            }
            return list;
        }

        /// <summary>HUD tracker lines of the current main quest (kept for older callers).</summary>
        public List<QuestObjective> GetObjectives()
        {
            var q = CurrentMain();
            return q != null ? ObjectivesOf(q) : new List<QuestObjective>();
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

        /// <summary>Sets a story flag (world change); flag objectives and quest requirements react.</summary>
        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !Journal.Flags.Add(flag)) return;
            Signal(ObjectiveTypes.Flag, flag, 1);
            Refresh();
        }

        public void ClearFlag(string flag)
        {
            if (Journal.Flags.Remove(flag ?? "")) Refresh();
        }

        /// <summary>A cutscene finished: cutscene objectives naming it complete.</summary>
        public void OnCutsceneFinished(string cutsceneId) => Signal(ObjectiveTypes.Cutscene, cutsceneId, 1);

        /// <summary>Automated checks: raise a gameplay signal directly (dungeon / raid clears).</summary>
        public void DevSignal(string type, string target, int amount) => Signal(type, target, amount);

        void Signal(string type, string target, int amount)
        {
            bool changed = false;
            foreach (var q in db.All)
            {
                if (Journal.Status(q.id) != QuestStatus.Active) continue;
                var step = CurrentStep(q);
                if (step == null) continue;
                for (int i = 0; i < step.objectives.Count; i++)
                {
                    var o = step.objectives[i];
                    if (o.type != type || IsObjectiveDone(q, i)) continue;
                    if (o.target != "*" && o.target != target) continue;
                    Advance(q, i, amount);
                    changed = true;
                    if (type == ObjectiveTypes.Kill)
                    {
                        var counts = Journal.State(q.id).counts;
                        GameEvents.RaiseToast($"{q.title}: {Mathf.Min(counts[i], o.count)}/{o.count}");
                    }
                }
            }
            if (changed) Refresh();
        }

        // =============================== Rules ===============================

        int Need(ObjectiveDef o) => Mathf.Max(1, o.count);

        bool IsObjectiveDone(QuestDef q, int index)
        {
            var step = CurrentStep(q);
            if (step == null || index >= step.objectives.Count) return false;
            var o = step.objectives[index];
            switch (o.type)
            {
                case ObjectiveTypes.Collect: return Game.Session.Inventory.Count(o.target) >= Need(o) || Count(q, index) >= Need(o);
                case ObjectiveTypes.Level: return Game.Session.Progression.Level >= o.count;
                case ObjectiveTypes.Flag: return Journal.HasFlag(o.target) || Count(q, index) >= Need(o);
                default: return Count(q, index) >= Need(o);
            }
        }

        int Count(QuestDef q, int index)
        {
            var counts = Journal.State(q.id).counts;
            return index < counts.Count ? counts[index] : 0;
        }

        int TalkObjectiveIndex(QuestDef q, string npcId)
        {
            var step = CurrentStep(q);
            if (step == null) return -1;
            for (int i = 0; i < step.objectives.Count; i++)
            {
                var o = step.objectives[i];
                if (o.type == ObjectiveTypes.Talk && o.target == npcId && !IsObjectiveDone(q, i)) return i;
            }
            return -1;
        }

        void Advance(QuestDef q, int index, int amount)
        {
            var s = Journal.State(q.id);
            while (s.counts.Count <= index) s.counts.Add(0);
            s.counts[index] = Mathf.Min(Need(CurrentStep(q).objectives[index]), s.counts[index] + amount);
        }

        bool Unlocked(QuestDef q)
        {
            foreach (var r in q.requires) if (!Journal.IsCompleted(r)) return false;
            foreach (var f in q.requiresFlags) if (!Journal.HasFlag(f)) return false;
            return Game.Session.Progression.Level >= q.minLevel;
        }

        void Accept(QuestDef q)
        {
            var s = Journal.State(q.id);
            s.status = (int)QuestStatus.Active;
            s.step = 0;
            s.counts.Clear();
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast(q.Kind == QuestKind.Main ? $"메인 퀘스트: {q.DisplayTitle}" : $"새 퀘스트: {q.title}");
            if (q.Kind == QuestKind.Sub && string.IsNullOrEmpty(Journal.Tracked)) Journal.Tracked = q.id;
            if (!string.IsNullOrEmpty(q.startCutscene)) PlayCutscene(q.startCutscene, null);
            GameEvents.RaiseQuestAccepted(q.id);
            Game.Flow.Autosave();
        }

        void Complete(QuestDef q)
        {
            var s = Journal.State(q.id);
            if (s.status == (int)QuestStatus.Completed) return;
            s.status = (int)QuestStatus.Completed;
            if (Journal.Tracked == q.id) Journal.Tracked = "";
            GiveReward(q.reward);
            if (OnlineEconomy.On) OnlineEconomy.ClaimQuest(q.id, (ok, bonus) => { }); // [SERVER] XP, gold and items come from the claim
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast($"퀘스트 완료: {q.title}");
            GameEvents.RaiseQuestCompleted(q.id);
            Game.Flow.Autosave();
        }

        void GiveReward(QuestRewardDef r)
        {
            if (r == null) return;
            var parts = new List<string>();
            bool online = OnlineEconomy.On; // [SERVER] online the claim's delta adds these
            if (r.xp > 0)
            {
                int xp = Progression.QuestXp(r.xp); // scaled with the level curve (the server pays the same exported value)
                if (!online) Game.Session.Progression.AddXp(xp);
                parts.Add($"경험치 {xp:N0}");
            }
            if (r.gold > 0)
            {
                if (!online) Game.Session.Inventory.Add(ConsumableDatabase.Gold, r.gold);
                parts.Add($"골드 {r.gold:N0}");
            }
            if (r.items != null)
                foreach (var it in r.items)
                {
                    if (string.IsNullOrEmpty(it.id) || it.count <= 0) continue;
                    if (!online) Game.Session.Inventory.Add(it.id, it.count);
                    parts.Add(it.count > 1 ? $"{ItemName(it.id)} x{it.count}" : ItemName(it.id));
                }
            if (r.maxHealth > 0 && Game.Player != null)
            {
                Game.Player.AddMaxHealth(r.maxHealth);
                parts.Add($"최대 체력 +{EquipmentDatabase.Hearts(r.maxHealth)}");
            }
            if (r.setFlags != null) foreach (var f in r.setFlags) Journal.Flags.Add(f);
            if (parts.Count > 0) GameEvents.RaiseToast("보상: " + string.Join(", ", parts));
        }

        public static string ItemName(string id)
        {
            switch (id)
            {
                case ItemIds.Wood: return "목재";
                case ItemIds.Stone: return "돌";
                case ItemIds.Carrot: return "당근";
            }
            var eq = EquipmentDatabase.Get(EquipmentDatabase.BaseId(id));
            if (eq != null) return eq.name;
            var c = ConsumableDatabase.Get(id);
            return c != null ? c.name : id;
        }

        /// <summary>
        /// Re-checks everything: unlocks quests, accepts giver-less ones, finishes steps whose objectives
        /// are all done (consuming collected items, playing the step cutscene) and quests whose last step ended.
        /// </summary>
        public void Refresh()
        {
            if (evaluating || Game.Session == null || !StoryEnabled)
            {
                Changed?.Invoke();
                return;
            }
            evaluating = true;
            try
            {
                for (int pass = 0; pass < 8; pass++)
                {
                    bool again = false;
                    foreach (var q in db.All)
                    {
                        var s = Journal.State(q.id);
                        var st = (QuestStatus)s.status;
                        if (st == QuestStatus.Locked && Unlocked(q))
                        {
                            s.status = (int)QuestStatus.Available;
                            if (string.IsNullOrEmpty(q.giver)) Accept(q);
                            again = true;
                        }
                        else if (st == QuestStatus.Active && !cutscenePlaying && StepDone(q))
                        {
                            FinishStep(q);
                            again = true;
                        }
                    }
                    if (!again) break;
                }
            }
            finally { evaluating = false; }
            Changed?.Invoke();
            TryAutoCutscenes();
        }

        bool StepDone(QuestDef q)
        {
            var step = CurrentStep(q);
            if (step == null) return true;
            for (int i = 0; i < step.objectives.Count; i++) if (!IsObjectiveDone(q, i)) return false;
            return true;
        }

        void FinishStep(QuestDef q)
        {
            var step = CurrentStep(q);
            if (step != null)
            {
                foreach (var o in step.objectives)
                    if (o.type == ObjectiveTypes.Collect && o.consume) Game.Session.Inventory.Remove(o.target, Need(o));
                foreach (var f in step.setFlags) Journal.Flags.Add(f);
            }
            var s = Journal.State(q.id);
            s.step++;
            s.counts.Clear();
            if (step != null && !string.IsNullOrEmpty(step.cutscene))
            {
                // The step's scene plays first; the next step (or the report) follows when it ends.
                PlayCutscene(step.cutscene, () => AfterStep(q));
                return;
            }
            AfterStep(q);
        }

        void AfterStep(QuestDef q)
        {
            var s = Journal.State(q.id);
            if (s.step < q.steps.Count)
            {
                Game.Audio.PlaySfx("select");
                if (q.Kind == QuestKind.Main) GameEvents.RaiseToast(q.steps[s.step].text);
                return;
            }
            if (string.IsNullOrEmpty(q.turnIn)) Complete(q);
            else
            {
                s.status = (int)QuestStatus.ReadyToTurnIn;
                Game.Audio.PlaySfx("quest");
                GameEvents.RaiseToast($"{NpcName(q.turnIn)}에게 보고하자.");
            }
        }

        // =============================== Cutscenes ===============================

        bool cutscenePlaying;

        void PlayCutscene(string id, Action done)
        {
            if (Game.Cutscenes == null || Game.Cutscenes.Get(id) == null)
            {
                if (Game.Cutscenes != null) Debug.LogWarning($"[dotRPG] Cutscene '{id}' not found.");
                OnCutsceneFinished(id);
                done?.Invoke();
                return;
            }
            cutscenePlaying = true;
            Game.Cutscenes.Play(id, () =>
            {
                cutscenePlaying = false;
                OnCutsceneFinished(id);
                done?.Invoke();
                Refresh();
            });
        }

        /// <summary>Starts cutscene objectives of the current steps whose map is the one the player is on.</summary>
        void TryAutoCutscenes()
        {
            if (cutscenePlaying || Game.Cutscenes == null || Game.Cutscenes.IsPlaying || !Game.IsPlaying) return;
            // Story scenes wait until the party is back on the map (not in the raid boss room before the results).
            if (Game.Dungeon != null && Game.Dungeon.InRun) return;
            string map = Game.Session.MapId;
            foreach (var q in db.All)
            {
                if (Journal.Status(q.id) != QuestStatus.Active) continue;
                var step = CurrentStep(q);
                if (step == null) continue;
                for (int i = 0; i < step.objectives.Count; i++)
                {
                    var o = step.objectives[i];
                    if (o.type != ObjectiveTypes.Cutscene || IsObjectiveDone(q, i)) continue;
                    if (!string.IsNullOrEmpty(o.map) && o.map != map) continue;
                    PlayCutscene(o.target, null);
                    return;
                }
            }
        }

        /// <summary>Called every frame by the cutscene player's host so queued scenes start once gameplay resumes.</summary>
        public void Tick()
        {
            if (!cutscenePlaying && Game.IsPlaying && Game.Cutscenes != null && !Game.Cutscenes.IsPlaying) TryAutoCutscenes();
            StoryRespawn.Tick(this);
        }

        // =============================== Text ===============================

        static string DefaultObjectiveText(ObjectiveDef o)
        {
            switch (o.type)
            {
                case ObjectiveTypes.Talk: return $"{Game.Quest.NpcName(o.target)}와(과) 이야기한다";
                case ObjectiveTypes.Kill: return o.target == "*" ? "몬스터 처치" : $"{MonsterName(o.target)} 처치";
                case ObjectiveTypes.Collect: return $"{ItemName(o.target)} 모으기";
                case ObjectiveTypes.Reach: return $"{(MapRegistry.Get(o.target)?.displayName ?? o.target)}(으)로 간다";
                case ObjectiveTypes.Dungeon: return "던전 클리어";
                case ObjectiveTypes.Raid: return "레이드 클리어";
                case ObjectiveTypes.Level: return $"레벨 {o.count} 달성";
                default: return o.target;
            }
        }

        static string MonsterName(string id)
        {
            var def = MonsterDatabase.Get(id);
            return def != null ? def.name : id;
        }

        public string FormatTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            return text
                .Replace("{name}", Journal.PlayerName)
                .Replace("{wood_required}", config.requiredWood.ToString())
                .Replace("{stone_required}", config.requiredStone.ToString())
                .Replace("{wood_left}", WoodStillNeeded.ToString())
                .Replace("{stone_left}", StoneStillNeeded.ToString())
                .Replace("{attack_key}", Game.Input.GetBindingLabel(GameAction.Attack))
                .Replace("{interact_key}", Game.Input.GetBindingLabel(GameAction.Interact))
                .Replace("{item_key}", Game.Input.GetBindingLabel(GameAction.UseItem))
                .Replace("{mobility_key}", Game.Input.GetBindingLabel(GameAction.Mobility))
                .Replace("{skill1_key}", Game.Input.GetBindingLabel(GameAction.Skill1))
                .Replace("{skill2_key}", Game.Input.GetBindingLabel(GameAction.Skill2))
                .Replace("{inventory_key}", Game.Input.GetBindingLabel(GameAction.Inventory))
                .Replace("{pause_key}", Game.Input.GetBindingLabel(GameAction.Pause));
        }
    }
}
