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
        enum GoalKind { None, Npc, Prop, Hunt, Map, Site, DungeonGuide, Wait }

        struct Goal
        {
            public GoalKind kind;
            public string id;     // npc id, interact id, enemy id ("*" any), item id
            public string map;    // map the goal is on ("" = here)
            public string label;  // what the HUD says
            public bool soft;     // Wait only: the story moves on by itself (a scene, the next quest unlocking), so hold instead of stopping
        }

        const float TalkReach = 1.25f, AttackReach = 1.05f, StuckSeconds = 3f, GiveUpSeconds = 25f, SoftWaitSeconds = 8f;

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
        /// <summary>The hunting ground 자동 사냥 belongs to: a knockback through a portal walks it back there.</summary>
        string huntMap;

        static bool InHuntingGround => Game.MiniDungeon?.Active == true || Game.BossRush?.Active == true || MeasureAnywhere || (Game.World != null && HuntingGrounds.Get(Game.World.MapId) != null && (Game.Dungeon == null || !Game.Dungeon.InRun));
        /// <summary>[MEASURE] Dev-only (-dotrpgMeasure): 자동 사냥 also fights in dungeons and raids, to time them.</summary>
        internal static bool MeasureAnywhere;
        /// <summary>[MEASURE] Dev-only: fight like a practised player (spam the main hunting skill, cast skills as they come
        /// ready) instead of auto hunt's deliberately slower pace, to time manual play.</summary>
        internal static bool MeasureSkilled;
        /// <summary>[MEASURE] Knows the raid: holds cooldown skills when 망자의 심판 is near and dumps them into the cast.</summary>
        internal static bool MeasureAware;
        /// <summary>[AUTO] For the HUD button state: 자동 사냥 can start on this map.</summary>
        public static bool CanHuntHere => InHuntingGround;

        public static void StartMiniDungeonHunt()
        {
            if (Game.MiniDungeon?.Active != true) return;
            Stop("");
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = true; Instance.huntForQuest = false; Instance.huntTarget = "*";
            Instance.huntMap = Game.World != null ? Game.World.MapId : null;
            Instance.autoStarted = false; Instance.quietStart = false; Instance.subMode = false;
            Instance.Begin();
        }

        public static void ToggleHunt()
        {
            if (Hunting) { Stop(L.T("자동 사냥을 멈췄습니다.")); return; }
            if (!ContentUnlocks.Check(ContentUnlocks.Feature.AutoHunt)) return; // [UNLOCK] the player's own switch; quest hunting is not blocked
            if (!InHuntingGround) { GameEvents.RaiseToast(L.T("자동 사냥은 사냥터에서만 켤 수 있습니다. (던전·요일던전·레이드는 직접 플레이)")); Game.Audio.PlaySfx("cancel"); return; }
            if (Active) Stop("");
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = true; Instance.huntForQuest = false; Instance.huntTarget = "*"; Instance.huntMap = Game.World != null ? Game.World.MapId : null;
            Instance.autoStarted = false; Instance.quietStart = false; Instance.subMode = false; Instance.growth = false;
            GuideFlags.Mark(GuideFlags.AutoHunt); // [GUIDE]
            Instance.Begin();
        }

        sealed class AutoInput : IActorInput
        {
            public ActorCommand next = ActorCommand.None;
            readonly LocalInput local = new LocalInput();
            public IActorInput Manual;

            public ActorCommand Read(PlayerController self)
            {
                // Keys pressed inside a window (online, the world keeps running) or on the frame one closes are menu input,
                // not the player taking over. A mini dungeon reads its own manual input (Manual) while it plays.
                var mine = Game.MiniDungeon?.Active == true && !Game.IsPlaying ? ActorCommand.None
                    : Game.IsPlaying && !Game.State.ChangedThisFrame && !BossTrailer.Blocking ? (Manual ?? local).Read(self) : ActorCommand.None;
                bool touched = mine.move.sqrMagnitude > 0.04f || mine.attack || mine.mobility || mine.interact || mine.skillSlot >= 0
                    || mine.useHealing || mine.useMana || mine.townScroll;
                if (touched)
                {
                    NotePlayerTookOver();
                    Stop(Instance != null && Instance.autoStarted ? L.T("직접 조작 중입니다. 손을 떼면 1장 자동 진행이 다시 이어집니다.") : L.T("직접 조작으로 자동 진행을 멈췄습니다."));
                    return mine;
                }
                var cmd = next;
                next.attack = false;
                next.skillSlot = -1;
                next.useHealing = false;
                next.useMana = false;
                return cmd;
            }
        }

        bool on;
        string status = "";
        AutoInput input;
        IActorInput previous;
        PlayerController driven;

        /// <summary>A window (not the pause menu, not a conversation) is open while the online world keeps running.</summary>
        public static bool WindowOpenInRunningWorld => Game.State != null && Game.State.Current == GameState.Inventory && Game.IsWorldRunning;

        /// <summary>True while auto-progress / auto-hunting is steering this character (its commands are not menu input).</summary>
        public static bool Drives(PlayerController p) => Instance != null && Instance.on && Instance.driven == p && p != null;
        Vector2 lastPos;
        float lastProgressAt, nextAttack, nextSkill, nextHeal, interactCooldown;
        int skillSlot;
        string goalKey = "";
        static Dictionary<string, string> npcMaps;

        public static void Toggle()
        {
            if (Game.MiniDungeon?.Active == true || Game.MiniDungeon?.Busy == true)
            { GameEvents.RaiseToast(L.T("미니던전에서는 자동 사냥 버튼을 이용하세요.")); return; }
            if (Active) { Stop(Hunting ? L.T("자동 사냥을 멈췄습니다.") : L.T("자동 진행을 멈췄습니다.")); return; }
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.huntMode = false;
            Instance.autoStarted = false; Instance.quietStart = false; Instance.subMode = false; Instance.growth = false;
            GrowthGuide.Invalidate();
            if (GrowthGuide.Active) Instance.growth = true; // [GROWTH] no quest to take yet: hunt for the 성장 추천
            else if (MainStoryDone && !StartSubQuests()) return; // [AUTO] 서브퀘스트 자동진행
            GuideFlags.Mark(GuideFlags.AutoQuest); // [GUIDE]
            Instance.Begin();
        }

        public static void Stop(string why)
        {
            if (Instance == null || !Instance.on) return;
            Instance.on = false;
            Instance.huntMode = false;
            Instance.growth = false;
            if (Instance.subMode) { Instance.subMode = false; Targets.Clear(); }
            Instance.status = "";
            Instance.lastStopAt = Time.unscaledTime;
            Instance.stopKey = InChapterOne() && Game.Session != null ? ChapterOneProgressKey() : null;
            Instance.Release();
            if (!string.IsNullOrEmpty(why)) GameEvents.RaiseToast(why);
        }

        void Begin()
        {
            var p = Game.Player;
            if (p == null || p.IsDead) return;
            if ((Game.MiniDungeon?.Active == true || Game.MiniDungeon?.Busy == true || BossRushDirector.InArena) && !huntMode) return; // [BOSS] auto progress would walk out of the attempt
            if (Game.Dungeon != null && Game.Dungeon.InRun && !MeasureAnywhere) { GameEvents.RaiseToast(L.T("던전 안에서는 자동 진행을 쓸 수 없습니다.")); return; }
            on = true;
            goalKey = "";
            Acquire(p);
            lastPos = p.Position;
            lastProgressAt = Time.time;
            if (huntMode) { GameEvents.RaiseToast(L.T("자동 사냥을 시작합니다. 아무 키나 누르면 멈춥니다.")); return; }
            if (growth) { GameEvents.RaiseToast(L.T("성장 추천 사냥터로 갑니다. 퀘스트를 받을 수 있게 되면 퀘스트로 돌아갑니다.")); return; }
            followTargets = Targets.Count > 0;
            chapter = Game.Quest?.CurrentMain()?.chapter ?? 0;
            bool quiet = quietStart;
            quietStart = false;
            if (quiet) return;
            GameEvents.RaiseToast(followTargets ? L.F("체크한 퀘스트 {0}개를 자동 진행합니다. 아무 키나 누르면 멈춥니다.", Targets.Count)
                : chapter > 0 ? L.F("{0}장 퀘스트를 끝까지 자동 진행합니다. 아무 키나 누르면 멈춥니다.", chapter)
                : L.T("퀘스트 자동 진행을 시작합니다. 퀘스트 창에서 진행할 퀘스트를 체크할 수 있습니다."));
        }

        void Acquire(PlayerController p)
        {
            if (driven == p && p.Input == input) return;
            Release();
            previous = p.Input;
            input = new AutoInput { Manual = previous };
            driven = p;
            p.Input = input;
        }

        void Release()
        {
            RestoreNpcs(null);
            if (driven != null && driven.Input == input) driven.Input = previous ?? new LocalInput();
            driven = null;
            previous = null;
        }

        void OnDestroy() { Release(); if (Instance == this) Instance = null; }

        // Auto-progress runs a whole chapter (1-1 to 1-끝) without stopping at each quest; it stops at the end of the
        // chapter, at steps the player must do by hand (dungeons, raids, the career choice) and on any key.
        // With quests checked in the quest log it runs until those are done instead.
        int chapter;
        bool followTargets;

        void OnEnable() { GameEvents.QuestCompleted += OnQuestCompleted; }
        void OnDisable() { GameEvents.QuestCompleted -= OnQuestCompleted; }

        void OnQuestCompleted(string id)
        {
            if (!on || (huntMode && !huntForQuest)) return;
            var q = Game.Quest;
            if (subMode) { SubQuestCompleted(); return; }
            if (followTargets)
            {
                if (!Targets.Exists(t => q.StatusOf(t) != QuestStatus.Completed)) Stop(L.T("체크한 퀘스트를 모두 마쳤습니다."));
                return;
            }
            var def = q.Database.Get(id);
            if (def == null || def.Kind != QuestKind.Main) return;
            if (chapter == 0) chapter = def.chapter;
            if (def.chapter == chapter && !ChapterLeft(chapter))
                Stop(L.F("{0}장 퀘스트를 모두 마쳤습니다. 다음 장은 [자동 진행]을 다시 누르세요.", chapter));
        }

        static bool ChapterLeft(int number)
        {
            var q = Game.Quest;
            foreach (var d in q.Database.All)
                if (d.Kind == QuestKind.Main && d.chapter == number && q.StatusOf(d.id) != QuestStatus.Completed) return true;
            return false;
        }

        void Update()
        {
            if (!on) return;
            var p = Game.Player;
            if (p == null || Game.Quest == null || Game.World == null) { Stop(""); return; }
            if (p.IsDead) { Stop(L.T("쓰러져서 자동 진행을 멈췄습니다.")); return; }
            if (Game.Dungeon != null && Game.Dungeon.InRun && !MeasureAnywhere) { Stop(""); return; }
            if (driven != p) Acquire(p); // the map was rebuilt with a new player body
            if (ghosted.Count > 0) RestoreNpcs(p);
            input.next = ActorCommand.None;
            // Conversations, the pause menu and map changes pause the walk; it carries on afterwards. An open window (quests,
            // shop, bag...) does not: online the world keeps running behind it, so auto-progress and auto-hunting go on.
            // Hunting in a mini dungeon also goes on while its own panel is up.
            bool miniHunt = huntMode && Game.MiniDungeon?.Active == true;
            if (!(Game.IsPlaying || WindowOpenInRunningWorld || (miniHunt && Game.IsWorldRunning)) || (Game.Flow != null && Game.Flow.IsTransitioning)) { lastProgressAt = Time.time; return; }

            // [AUTO] 자동 사냥: stays in this hunting ground; a quest hunt hands back to the story once its step is done.
            if (huntMode)
            {
                // Pushed out of the hunting ground (a monster's knockback through a portal): a quest hunt hands back to the
                // story, which walks to the quest's map again; plain 자동 사냥 walks back to its own hunting ground.
                string hereMap = Game.World.MapId;
                if (!InHuntingGround || (!MeasureAnywhere && !string.IsNullOrEmpty(huntMap) && hereMap != huntMap))
                {
                    if (huntForQuest) { huntMode = false; return; }
                    string back = string.IsNullOrEmpty(huntMap) || huntMap == hereMap ? null : NextMapTowards(hereMap, huntMap);
                    if (back == null || !Game.World.PortalTowards(back, out var way)) { Stop(L.T("사냥터를 벗어나 자동 사냥을 멈췄습니다.")); return; }
                    status = L.F("{0}(으)로 돌아가는 중", MapName(huntMap));
                    WalkTo(p, way, 0f);
                    return;
                }
                if (GrowthHuntLeaves(true)) return; // [GROWTH]
                if (huntForQuest)
                {
                    var q = Resolve();
                    if (q.kind != GoalKind.Hunt) { huntMode = false; GameEvents.RaiseToast(L.T("사냥을 마쳐서 이야기를 이어 갑니다.")); return; }
                    huntTarget = q.id;
                }
                var hunt = new Goal { kind = GoalKind.Hunt, id = huntTarget, map = "", label = huntForQuest ? L.T("자동 사냥 중 (퀘스트)") : L.T("자동 사냥 중") };
                status = hunt.label;
                DoHunt(p, hunt);
                return;
            }

            var goal = growth ? GrowthGoal() : Resolve(); // [GROWTH] 성장 추천 사냥 (QuestAutoPilot.Growth.cs)
            if (growth && GrowthHuntLeaves(false)) return;
            if (goal.kind == GoalKind.None && !growth) goal = NoQuestGoal();
            string key = goal.kind + ":" + goal.id + ":" + goal.map;
            if (key != goalKey) { goalKey = key; lastProgressAt = Time.time; lastPos = p.Position; }
            status = goal.label;
            switch (goal.kind)
            {
                case GoalKind.None: Stop(L.T("따라갈 퀘스트가 없습니다.")); return;
                case GoalKind.Wait:
                    if (goal.soft && Time.time - lastProgressAt < SoftWaitSeconds) return;
                    Stop(goal.label); return;
            }

            // Another map first: walk to the portal on the way there.
            string here = Game.World.MapId;
            if (!string.IsNullOrEmpty(goal.map) && goal.map != here)
            {
                string step = NextMapTowards(here, goal.map);
                if (step == null || !Game.World.PortalTowards(step, out var portal)) { Stop(L.F("{0}(으)로 가는 길을 찾지 못했습니다.", MapName(goal.map))); return; }
                status = L.F("{0}(으)로 이동 중 · {1}", MapName(goal.map), goal.label);
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
                    if (InHuntingGround) { huntMode = true; huntForQuest = !growth; huntTarget = goal.id; huntMap = here; GameEvents.RaiseToast(L.T("사냥터에 도착해 자동 사냥을 켰습니다.")); }
                    else DoHunt(p, goal);
                    break;
                case GoalKind.DungeonGuide: DoDungeonGuide(p, goal); break;
                case GoalKind.Map:
                    // Arrived: the step (or the scene waiting here) usually finishes on its own a moment later.
                    if (Time.time - lastProgressAt >= SoftWaitSeconds) Stop(L.F("{0}에 도착했습니다.", MapName(goal.map)));
                    break;
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

        /// <summary>[MAP] Where the quests send the player next (the same pick as auto-progress), for the map markers.</summary>
        public bool PeekGoal(out string kind, out string id, out string map, out string label)
        {
            var g = Resolve();
            kind = g.kind.ToString();
            id = g.id ?? "";
            map = g.map ?? "";
            label = g.label ?? "";
            return g.kind != GoalKind.None && g.kind != GoalKind.Wait;
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
                    if (waiting.kind == GoalKind.None) waiting = Wait(L.F("'{0}'은(는) 아직 받을 수 없는 퀘스트입니다.", L.T(picked.title)));
                    continue;
                }
                var pg = GoalFor(picked);
                if (pg.kind != GoalKind.None && pg.kind != GoalKind.Wait) return pg;
                if (waiting.kind == GoalKind.None) waiting = pg;
            }
            if (Targets.Count > 0) return waiting.kind != GoalKind.None ? waiting : Wait(L.T("체크한 퀘스트에 지금 할 일이 없습니다."));
            var main = q.CurrentMain();
            var g = main != null ? GoalFor(main) : default;
            if (g.kind == GoalKind.None || (g.kind == GoalKind.Wait && !g.soft)) // a soft wait is the story moving on: stay
            {
                var sub = q.Database.Get(Game.Session.Journal.Tracked);
                if (sub != null && q.StatusOf(sub.id) != QuestStatus.Completed && q.StatusOf(sub.id) != QuestStatus.Locked)
                {
                    var sg = GoalFor(sub);
                    if (sg.kind != GoalKind.None && sg.kind != GoalKind.Wait) return sg;
                }
            }
            if (g.kind == GoalKind.None) return LevelGate();
            return g;
        }

        /// <summary>
        /// The next main quest of the chapter waits only on the hero's level (1-8 needs Lv 2, 1-9 Lv 5 ...): hunt up to it
        /// in the recommended ground. The level-up unlocks the quest and auto-progress walks to its giver.
        /// </summary>
        Goal LevelGate()
        {
            var q = Game.Quest;
            var j = Game.Session.Journal;
            foreach (var d in q.Database.All)
            {
                if (d.Kind != QuestKind.Main || (chapter > 0 && d.chapter != chapter) || q.StatusOf(d.id) != QuestStatus.Locked) continue;
                if (!d.requires.TrueForAll(j.IsCompleted) || !d.requiresFlags.TrueForAll(j.HasFlag)) continue;
                if (Game.Session.Progression.Level >= d.minLevel) continue;
                return new Goal { kind = GoalKind.Hunt, id = "*", map = HuntingMap(), label = L.F("레벨 {0}까지 사냥 중 (다음 퀘스트 {1})", d.minLevel, d.code) };
            }
            return default;
        }

        Goal GoalFor(QuestDef def)
        {
            var q = Game.Quest;
            var st = q.StatusOf(def.id);
            if (st == QuestStatus.Available)
                return string.IsNullOrEmpty(def.giver) ? Wait(L.T("퀘스트가 시작되기를 기다립니다."), true) : NpcGoal(def.giver, L.T("{0}에게 퀘스트 받으러 가는 중"));
            if (st == QuestStatus.ReadyToTurnIn)
                return string.IsNullOrEmpty(def.turnIn) ? Wait("", true) : NpcGoal(def.turnIn, L.T("{0}에게 보고하러 가는 중"));
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
                    case ObjectiveTypes.Talk: return NpcGoal(o.target, L.T("{0}에게 가는 중"));
                    case ObjectiveTypes.Quests:
                        // An errand board: carry on with the first listed errand that is not finished yet.
                        foreach (var id in o.target.Split(','))
                        {
                            var sub = q.Database.Get(id.Trim());
                            if (sub == null || q.StatusOf(sub.id) == QuestStatus.Completed || q.StatusOf(sub.id) == QuestStatus.Locked) continue;
                            var g = GoalFor(sub);
                            if (g.kind != GoalKind.None && g.kind != GoalKind.Wait) return g;
                        }
                        return Wait(L.T("남은 부탁을 직접 골라 진행해 주세요."));
                    case ObjectiveTypes.Interact: return PropGoal(o);
                    case ObjectiveTypes.Kill: return KillGoal(o, def);
                    case ObjectiveTypes.Collect: return CollectGoal(o.target, o.map);
                    case ObjectiveTypes.Reach: return new Goal { kind = GoalKind.Map, map = o.target, label = L.F("{0}(으)로 이동 중", MapName(o.target)) };
                    case ObjectiveTypes.Flag:
                        if (o.target == QuestManager.WorkshopBuiltFlag) return WorkshopGoal();
                        // [GUIDE] 안내 의뢰: the player does it by hand; say exactly what to do.
                        return Wait(string.IsNullOrEmpty(o.text) ? L.T("이 단계는 직접 진행해 주세요.") : L.F("{0} (직접 해 보세요)", o.text));
                    case ObjectiveTypes.Dungeon:
                    case ObjectiveTypes.Raid:
                        return new Goal { kind = GoalKind.DungeonGuide, map = DungeonGuideMap(), label = o.type == ObjectiveTypes.Raid ? L.T("레이드 안내인에게 가는 중") : L.T("던전 안내인에게 가는 중") };
                    case ObjectiveTypes.Level:
                        return new Goal { kind = GoalKind.Hunt, id = "*", map = HuntingMap(), label = L.F("레벨 {0}까지 사냥 중", o.count) };
                    case ObjectiveTypes.Cutscene:
                        if (!string.IsNullOrEmpty(o.map) && o.map != Game.World.MapId)
                            return new Goal { kind = GoalKind.Map, map = o.map, label = L.T("이야기가 이어지는 곳으로 이동 중") };
                        return Wait(L.T("이야기가 이어지기를 기다립니다."), true);
                }
            }
            if (outOfEntries)
                return new Goal { kind = GoalKind.Hunt, id = "*", map = HuntingMap(), label = L.T("오늘 던전 횟수를 다 써서 레벨에 맞는 사냥터에서 사냥 중") };
            return Wait("", true); // every objective done: the step finishes this frame
        }

        static Goal Wait(string why, bool soft = false) => new Goal { kind = GoalKind.Wait, label = why, soft = soft };

        Goal NpcGoal(string npcId, string what)
        {
            string map = FindNpc(npcId) != null ? Game.World.MapId : NpcMap(npcId);
            foreach (var it in Interactable.All)
                if (it is ServiceDoor door && WorldBuilder.ServiceNpc(door.Service)?.npcId == npcId) { map = Game.World.MapId; break; }
            return new Goal { kind = GoalKind.Npc, id = npcId, map = map ?? "", label = string.Format(what, Game.Quest.NpcName(npcId)) };
        }

        Goal PropGoal(ObjectiveDef o)
        {
            string map = o.map;
            if (string.IsNullOrEmpty(map))
                foreach (var pr in StoryCast.Props)
                    if (pr.interactId == o.target && StoryCast.Visible(pr)) { map = pr.map; break; }
            string text = string.IsNullOrEmpty(o.text) ? L.T("목표 물건을 찾는 중") : Game.Quest.FormatTokens(o.text).Replace("{n}", "0").Replace("{count}", o.count.ToString());
            return new Goal { kind = GoalKind.Prop, id = o.target, map = map ?? "", label = text };
        }

        Goal KillGoal(ObjectiveDef o, QuestDef quest)
        {
            string map = !string.IsNullOrEmpty(o.map) ? o.map : null;
            if (map == null && AnyEnemy(o.target)) map = Game.World.MapId;
            if (map == null) map = StoryRespawn.ScriptedMap(quest, o.target); // story monsters (1-3 attack night) come back where the scene put them
            if (map == null) map = o.target == "*" ? HuntingMap() : ZoneWith(o.target);
            if (map == null) // dungeon monsters
                return new Goal { kind = GoalKind.DungeonGuide, map = DungeonGuideMap(), label = L.T("던전에서 잡는 몬스터입니다. 던전 안내인에게 가는 중") };
            return new Goal { kind = GoalKind.Hunt, id = o.target, map = map, label = L.T("몬스터 사냥 중") };
        }

        Goal CollectGoal(string item, string map)
        {
            return new Goal { kind = GoalKind.Hunt, id = "*", map = string.IsNullOrEmpty(map) ? HuntingMap() : map, label = L.F("{0} 모으는 중", L.T(QuestManager.ItemName(item))) };
        }

        Goal WorkshopGoal() => new Goal { kind = GoalKind.Site, map = MapRegistry.Village, label = L.T("공방 터로 재건을 도우러 가는 중") };

        /// <summary>The hunting ground recommended for the hero's level (the highest one already open to it).</summary>
        static string HuntingMap() => GrowthGuide.BestZone(Game.Session.Progression.Level)?.id ?? MapRegistry.Forest;

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
