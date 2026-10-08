using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Quest auto-progress: walks the hero to the next thing the followed quest needs (giver, talk target,
    /// story object, hunting ground, resource, report), crossing maps through their portals, talks / uses it,
    /// and hunts for kill and gathering steps. Any key the player presses hands control back at once.
    /// Steps a player must choose (dungeons, raids) walk to the dungeon guide, open its window and stop.
    /// The main quest is followed first; when it waits on something manual the pinned side quest is tried.
    /// </summary>
    public sealed partial class QuestAutoPilot : MonoBehaviour
    {
        enum GoalKind { None, Npc, Prop, Hunt, Gather, Map, Site, DungeonGuide, Wait }

        struct Goal
        {
            public GoalKind kind;
            public string id;     // npc id, interact id, enemy id ("*" any), item id
            public string map;    // map the goal is on ("" = here)
            public string label;  // what the HUD says
        }

        const float TalkReach = 1.25f, AttackReach = 1.05f, StuckSeconds = 3f, GiveUpSeconds = 25f;

        public static QuestAutoPilot Instance { get; private set; }
        public static bool Active => Instance != null && Instance.on;
        /// <summary>One line for the HUD ("자동 진행: 한나에게 가는 중").</summary>
        public static string Status => Active ? Instance.status : "";
        /// <summary>
        /// [AUTO] 자동 사냥: fights in the hunting ground the hero stands in. Only in hunting-ground fields, never in a
        /// dungeon, weekday dungeon or raid (those are always played by hand). Turned on by its own button, or by
        /// auto-progress on arriving at the ground of a kill / level step (then it goes back to the story when done).
        /// </summary>
        public static bool Hunting => Active && Instance.huntMode;
        bool huntMode, huntForQuest;
        string huntTarget = "*";

        static bool InHuntingGround => Game.World != null && HuntingGrounds.Get(Game.World.MapId) != null && (Game.Dungeon == null || !Game.Dungeon.InRun);
        /// <summary>[AUTO] For the HUD button state: 자동 사냥 can start on this map.</summary>
        public static bool CanHuntHere => InHuntingGround;

        public static void ToggleHunt()
        {
            if (Hunting) { Stop("자동 사냥을 멈췄습니다."); return; }
            if (!InHuntingGround) { GameEvents.RaiseToast("자동 사냥은 사냥터에서만 켤 수 있습니다. (던전·요일던전·레이드는 직접 플레이)"); Game.Audio.PlaySfx("cancel"); return; }
            if (Active) Stop("");
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = true; Instance.huntForQuest = false; Instance.huntTarget = "*";
            Instance.Begin();
        }

        sealed class AutoInput : IActorInput
        {
            public ActorCommand next = ActorCommand.None;
            readonly LocalInput local = new LocalInput();

            public ActorCommand Read(PlayerController self)
            {
                var mine = local.Read(self);
                bool touched = mine.move.sqrMagnitude > 0.04f || mine.attack || mine.mobility || mine.interact || mine.skillSlot >= 0
                    || mine.useHealing || mine.useMana || mine.townScroll;
                if (touched)
                {
                    Stop("직접 조작으로 자동 진행을 멈췄습니다.");
                    return mine;
                }
                var cmd = next;
                next.attack = false;
                next.skillSlot = -1;
                next.useHealing = false;
                return cmd;
            }
        }

        bool on;
        string status = "";
        AutoInput input;
        IActorInput previous;
        PlayerController driven;
        Vector2 lastPos;
        float lastProgressAt, nextAttack, nextSkill, nextHeal, interactCooldown;
        int skillSlot;
        string goalKey = "";
        static Dictionary<string, string> npcMaps;

        public static void Toggle()
        {
            if (Active) { Stop(Hunting ? "자동 사냥을 멈췄습니다." : "자동 진행을 멈췄습니다."); return; }
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = false;
            Instance.Begin();
        }

        public static void Stop(string why)
        {
            if (Instance == null || !Instance.on) return;
            Instance.on = false;
            Instance.huntMode = false;
            Instance.status = "";
            Instance.Release();
            if (!string.IsNullOrEmpty(why)) GameEvents.RaiseToast(why);
        }

        void Begin()
        {
            var p = Game.Player;
            if (p == null || p.IsDead) return;
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast("던전 안에서는 자동 진행을 쓸 수 없습니다."); return; }
            on = true;
            goalKey = "";
            Acquire(p);
            lastPos = p.Position;
            lastProgressAt = Time.time;
            if (huntMode) { GameEvents.RaiseToast("자동 사냥을 시작합니다. 아무 키나 누르면 멈춥니다."); return; }
            GameEvents.RaiseToast(Targets.Count > 0 ? $"체크한 퀘스트 {Targets.Count}개를 자동 진행합니다. 아무 키나 누르면 멈춥니다."
                : "퀘스트 자동 진행을 시작합니다. 퀘스트 창에서 진행할 퀘스트를 체크할 수 있습니다.");
        }

        void Acquire(PlayerController p)
        {
            if (driven == p && p.Input == input) return;
            Release();
            input = new AutoInput();
            previous = p.Input;
            driven = p;
            p.Input = input;
        }

        void Release()
        {
            if (driven != null && driven.Input == input) driven.Input = previous ?? new LocalInput();
            driven = null;
            previous = null;
        }

        void OnDestroy() { Release(); if (Instance == this) Instance = null; }

        // Auto-progress is a helper, not an idle mode: it stops at every quest boundary and the player presses
        // [자동 진행] again to carry on with the next one.
        void OnEnable()
        {
            GameEvents.QuestCompleted += OnQuestCompleted;
            GameEvents.QuestAccepted += OnQuestAccepted;
        }

        void OnDisable()
        {
            GameEvents.QuestCompleted -= OnQuestCompleted;
            GameEvents.QuestAccepted -= OnQuestAccepted;
        }

        void OnQuestCompleted(string id) { if (on && !(huntMode && !huntForQuest)) Stop("퀘스트 완료! 다음 퀘스트도 진행하려면 [자동 진행]을 다시 누르세요."); }
        void OnQuestAccepted(string id) { if (on && !(huntMode && !huntForQuest)) Stop("새 퀘스트를 받았습니다. 이어서 하려면 [자동 진행]을 다시 누르세요."); }

        void Update()
        {
            if (!on) return;
            var p = Game.Player;
            if (p == null || Game.Quest == null || Game.World == null) { Stop(""); return; }
            if (p.IsDead) { Stop("쓰러져서 자동 진행을 멈췄습니다."); return; }
            if (Game.Dungeon != null && Game.Dungeon.InRun) { Stop(""); return; }
            if (driven != p) Acquire(p); // the map was rebuilt with a new player body
            input.next = ActorCommand.None;
            // Conversations, windows and map changes pause the walk; it carries on afterwards.
            if (!Game.IsPlaying || (Game.Flow != null && Game.Flow.IsTransitioning)) { lastProgressAt = Time.time; return; }

            // [AUTO] 자동 사냥: stays in this hunting ground; a quest hunt hands back to the story once its step is done.
            if (huntMode)
            {
                if (!InHuntingGround) { Stop("사냥터를 벗어나 자동 사냥을 멈췄습니다."); return; }
                if (huntForQuest)
                {
                    var q = Resolve();
                    if (q.kind != GoalKind.Hunt) { huntMode = false; GameEvents.RaiseToast("사냥을 마쳐서 이야기를 이어 갑니다."); return; }
                    huntTarget = q.id;
                }
                var hunt = new Goal { kind = GoalKind.Hunt, id = huntTarget, map = "", label = huntForQuest ? "자동 사냥 중 (퀘스트)" : "자동 사냥 중" };
                status = hunt.label;
                DoHunt(p, hunt);
                return;
            }

            var goal = Resolve();
            string key = goal.kind + ":" + goal.id + ":" + goal.map;
            if (key != goalKey) { goalKey = key; lastProgressAt = Time.time; lastPos = p.Position; }
            status = goal.label;
            switch (goal.kind)
            {
                case GoalKind.None: Stop("따라갈 퀘스트가 없습니다."); return;
                case GoalKind.Wait: Stop(goal.label); return;
            }

            // Another map first: walk to the portal on the way there.
            string here = Game.World.MapId;
            if (!string.IsNullOrEmpty(goal.map) && goal.map != here)
            {
                string step = NextMapTowards(here, goal.map);
                if (step == null || !Game.World.PortalTowards(step, out var portal)) { Stop($"{MapName(goal.map)}(으)로 가는 길을 찾지 못했습니다."); return; }
                status = $"{MapName(goal.map)}(으)로 이동 중 · {goal.label}";
                WalkTo(p, portal, 0f);
                return;
            }

            switch (goal.kind)
            {
                case GoalKind.Npc: DoNpc(p, goal); break;
                case GoalKind.Prop: DoProp(p, goal); break;
                case GoalKind.Site: DoSite(p, goal); break;
                case GoalKind.Hunt:
                    // [AUTO] At the hunting ground: the fighting is done by 자동 사냥 (fields only).
                    if (InHuntingGround) { huntMode = true; huntForQuest = true; huntTarget = goal.id; GameEvents.RaiseToast("사냥터에 도착해 자동 사냥을 켰습니다."); }
                    else DoHunt(p, goal);
                    break;
                case GoalKind.Gather: DoGather(p, goal); break;
                case GoalKind.DungeonGuide: DoDungeonGuide(p, goal); break;
                case GoalKind.Map: Stop($"{MapName(goal.map)}에 도착했습니다."); break;
            }
        }

        // =============================== What to do next ===============================

        /// <summary>
        /// The quests checked in the quest log, in the order they were checked. Auto-progress works on the first one
        /// that has something to do; with nothing checked it follows the main quest, then the pinned side quest.
        /// </summary>
        public static readonly List<string> Targets = new List<string>();

        public static bool IsTarget(string questId) => Targets.Contains(questId);

        public static void ToggleTarget(string questId)
        {
            if (!Targets.Remove(questId)) Targets.Add(questId);
        }

        Goal Resolve()
        {
            var q = Game.Quest;
            Targets.RemoveAll(id => q.Database.Get(id) == null || q.StatusOf(id) == QuestStatus.Completed); // done: off the list
            Goal waiting = default;
            foreach (var id in Targets)
            {
                var picked = q.Database.Get(id);
                var st = q.StatusOf(id);
                if (st == QuestStatus.Locked)
                {
                    if (waiting.kind == GoalKind.None) waiting = Wait($"'{picked.title}'은(는) 아직 받을 수 없는 퀘스트입니다.");
                    continue;
                }
                var pg = GoalFor(picked);
                if (pg.kind != GoalKind.None && pg.kind != GoalKind.Wait) return pg;
                if (waiting.kind == GoalKind.None) waiting = pg;
            }
            if (Targets.Count > 0) return waiting.kind != GoalKind.None ? waiting : Wait("체크한 퀘스트에 지금 할 일이 없습니다.");
            var main = q.CurrentMain();
            var g = main != null ? GoalFor(main) : default;
            if (g.kind == GoalKind.None || g.kind == GoalKind.Wait)
            {
                var sub = q.Database.Get(Game.Session.Journal.Tracked);
                if (sub != null && q.StatusOf(sub.id) != QuestStatus.Completed && q.StatusOf(sub.id) != QuestStatus.Locked)
                {
                    var sg = GoalFor(sub);
                    if (sg.kind != GoalKind.None && sg.kind != GoalKind.Wait) return sg;
                }
            }
            return g;
        }

        Goal GoalFor(QuestDef def)
        {
            var q = Game.Quest;
            var st = q.StatusOf(def.id);
            if (st == QuestStatus.Available)
                return string.IsNullOrEmpty(def.giver) ? Wait("퀘스트가 시작되기를 기다립니다.") : NpcGoal(def.giver, "에게 퀘스트 받으러 가는 중");
            if (st == QuestStatus.ReadyToTurnIn)
                return string.IsNullOrEmpty(def.turnIn) ? Wait("") : NpcGoal(def.turnIn, "에게 보고하러 가는 중");
            if (st != QuestStatus.Active) return default;
            var step = q.CurrentStep(def);
            if (step == null) return default;
            var objectives = q.ObjectivesOf(def);
            bool outOfEntries = false;
            for (int i = 0; i < step.objectives.Count; i++)
            {
                if (i < objectives.Count && objectives[i].done) continue;
                var o = step.objectives[i];
                // Weekday dungeons used up for today: grow in the field instead of stopping at the guide.
                if (o.type == ObjectiveTypes.Dungeon && Game.Session.Dungeons.EntriesLeft(ResetClock.Now) <= 0) { outOfEntries = true; continue; }
                switch (o.type)
                {
                    case ObjectiveTypes.Talk: return NpcGoal(o.target, "에게 가는 중");
                    case ObjectiveTypes.Quests:
                        // An errand board: carry on with the first listed errand that is not finished yet.
                        foreach (var id in o.target.Split(','))
                        {
                            var sub = q.Database.Get(id.Trim());
                            if (sub == null || q.StatusOf(sub.id) == QuestStatus.Completed || q.StatusOf(sub.id) == QuestStatus.Locked) continue;
                            var g = GoalFor(sub);
                            if (g.kind != GoalKind.None && g.kind != GoalKind.Wait) return g;
                        }
                        return Wait("남은 부탁을 직접 골라 진행해 주세요.");
                    case ObjectiveTypes.Interact: return PropGoal(o);
                    case ObjectiveTypes.Kill: return KillGoal(o, def);
                    case ObjectiveTypes.Collect: return CollectGoal(o.target, o.map);
                    case ObjectiveTypes.Reach: return new Goal { kind = GoalKind.Map, map = o.target, label = $"{MapName(o.target)}(으)로 이동 중" };
                    case ObjectiveTypes.Flag:
                        if (o.target == QuestManager.WorkshopBuiltFlag) return WorkshopGoal();
                        return Wait("이 단계는 직접 진행해 주세요.");
                    case ObjectiveTypes.Dungeon:
                    case ObjectiveTypes.Raid:
                        return new Goal { kind = GoalKind.DungeonGuide, map = DungeonGuideMap(), label = o.type == ObjectiveTypes.Raid ? "레이드 안내인에게 가는 중" : "던전 안내인에게 가는 중" };
                    case ObjectiveTypes.Level:
                        return new Goal { kind = GoalKind.Hunt, id = "*", map = HuntingMap(), label = $"레벨 {o.count}까지 사냥 중" };
                    case ObjectiveTypes.Cutscene:
                        if (!string.IsNullOrEmpty(o.map) && o.map != Game.World.MapId)
                            return new Goal { kind = GoalKind.Map, map = o.map, label = "이야기가 이어지는 곳으로 이동 중" };
                        return Wait("이야기가 이어지기를 기다립니다.");
                }
            }
            if (outOfEntries)
                return new Goal { kind = GoalKind.Hunt, id = "*", map = HuntingMap(), label = "오늘 던전 횟수를 다 써서 레벨에 맞는 사냥터에서 사냥 중" };
            return Wait("");
        }

        static Goal Wait(string why) => new Goal { kind = GoalKind.Wait, label = why };

        Goal NpcGoal(string npcId, string what)
        {
            string map = FindNpc(npcId) != null ? Game.World.MapId : NpcMap(npcId);
            foreach (var it in Interactable.All)
                if (it is ServiceDoor door && WorldBuilder.ServiceNpc(door.Service)?.npcId == npcId) { map = Game.World.MapId; break; }
            return new Goal { kind = GoalKind.Npc, id = npcId, map = map ?? "", label = Game.Quest.NpcName(npcId) + what };
        }

        Goal PropGoal(ObjectiveDef o)
        {
            string map = o.map;
            if (string.IsNullOrEmpty(map))
                foreach (var pr in StoryCast.Props)
                    if (pr.interactId == o.target && StoryCast.Visible(pr)) { map = pr.map; break; }
            string text = string.IsNullOrEmpty(o.text) ? "목표 물건을 찾는 중" : Game.Quest.FormatTokens(o.text).Replace("{n}", "0").Replace("{count}", o.count.ToString());
            return new Goal { kind = GoalKind.Prop, id = o.target, map = map ?? "", label = text };
        }

        Goal KillGoal(ObjectiveDef o, QuestDef quest)
        {
            string map = !string.IsNullOrEmpty(o.map) ? o.map : null;
            if (map == null && AnyEnemy(o.target)) map = Game.World.MapId;
            if (map == null) map = StoryRespawn.ScriptedMap(quest, o.target); // story monsters (1-3 attack night) come back where the scene put them
            if (map == null) map = o.target == "*" ? HuntingMap() : ZoneWith(o.target);
            if (map == null) // dungeon monsters
                return new Goal { kind = GoalKind.DungeonGuide, map = DungeonGuideMap(), label = "던전에서 잡는 몬스터입니다. 던전 안내인에게 가는 중" };
            return new Goal { kind = GoalKind.Hunt, id = o.target, map = map, label = "몬스터 사냥 중" };
        }

        Goal CollectGoal(string item, string map)
        {
            if (item == ItemIds.Wood || item == ItemIds.Stone)
                return new Goal { kind = GoalKind.Gather, id = item, map = string.IsNullOrEmpty(map) ? MapRegistry.Forest : map, label = $"{QuestManager.ItemName(item)} 모으는 중" };
            return new Goal { kind = GoalKind.Hunt, id = "*", map = string.IsNullOrEmpty(map) ? HuntingMap() : map, label = $"{QuestManager.ItemName(item)} 모으는 중" };
        }

        Goal WorkshopGoal()
        {
            var q = Game.Quest;
            var inv = Game.Session.Inventory;
            if (inv.Count(ItemIds.Wood) < q.WoodStillNeeded) return CollectGoal(ItemIds.Wood, MapRegistry.Forest);
            if (inv.Count(ItemIds.Stone) < q.StoneStillNeeded) return CollectGoal(ItemIds.Stone, MapRegistry.Forest);
            return new Goal { kind = GoalKind.Site, map = MapRegistry.Village, label = "공방 터에 재료를 전하러 가는 중" };
        }

        /// <summary>The hunting ground recommended for the hero's level (the highest one already open to it).</summary>
        static string HuntingMap()
        {
            int level = Game.Session.Progression.Level;
            HuntingZone best = null;
            foreach (var z in HuntingGrounds.All)
                if (z.minLevel <= level && (best == null || z.minLevel > best.minLevel)) best = z;
            return best != null ? best.id : MapRegistry.Forest;
        }

        /// <summary>A hunting ground where <paramref name="monsterId"/> lives, the closest to the hero's level; null if none (dungeon monsters).</summary>
        static string ZoneWith(string monsterId)
        {
            int level = Game.Session.Progression.Level;
            HuntingZone best = null;
            foreach (var z in HuntingGrounds.All)
            {
                if (System.Array.IndexOf(z.monsters, monsterId) < 0) continue;
                if (best == null || Mathf.Abs(z.monsterLevel - level) < Mathf.Abs(best.monsterLevel - level)) best = z;
            }
            return best?.id;
        }

        static string DungeonGuideMap()
        {
            foreach (var npc in NpcController.Services)
                if (npc != null && npc.Definition.service == NpcService.Dungeon) return Game.World.MapId;
            // Every town has a guide; an indoor visitor should leave to the same town first.
            return HuntingGrounds.HomeOf(Game.World.MapId);
        }

    }
}
