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
    public sealed class QuestAutoPilot : MonoBehaviour
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
            if (Active) { Stop("자동 진행을 멈췄습니다."); return; }
            if (Instance == null) Instance = new GameObject("QuestAutoPilot").AddComponent<QuestAutoPilot>();
            Instance.Begin();
        }

        public static void Stop(string why)
        {
            if (Instance == null || !Instance.on) return;
            Instance.on = false;
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
            GameEvents.RaiseToast("퀘스트 자동 진행을 시작합니다. 아무 키나 누르면 멈춥니다.");
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

        void OnQuestCompleted(string id) { if (on) Stop("퀘스트 완료! 다음 퀘스트도 진행하려면 [자동 진행]을 다시 누르세요."); }
        void OnQuestAccepted(string id) { if (on) Stop("새 퀘스트를 받았습니다. 이어서 하려면 [자동 진행]을 다시 누르세요."); }

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
                case GoalKind.Hunt: DoHunt(p, goal); break;
                case GoalKind.Gather: DoGather(p, goal); break;
                case GoalKind.DungeonGuide: DoDungeonGuide(p, goal); break;
                case GoalKind.Map: Stop($"{MapName(goal.map)}에 도착했습니다."); break;
            }
        }

        // =============================== What to do next ===============================

        /// <summary>The quest the player picked in the quest log ("" = main first, then the pinned side quest).</summary>
        public static string TargetQuestId = "";

        Goal Resolve()
        {
            var q = Game.Quest;
            var picked = string.IsNullOrEmpty(TargetQuestId) ? null : q.Database.Get(TargetQuestId);
            if (picked != null)
            {
                var st = q.StatusOf(picked.id);
                if (st == QuestStatus.Completed) TargetQuestId = ""; // done: back to the default order
                else if (st == QuestStatus.Locked) return Wait($"'{picked.title}'은(는) 아직 받을 수 없는 퀘스트입니다.");
                else return GoalFor(picked);
            }
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
            var guide = WorldBuilder.ServiceNpc(NpcService.Dungeon);
            return guide != null ? NpcMap(guide.npcId) ?? MapRegistry.Village : MapRegistry.Village;
        }

        // =============================== Doing it ===============================

        void DoNpc(PlayerController p, Goal g)
        {
            var npc = FindNpc(g.id);
            if (npc == null) { Stop($"{Game.Quest.NpcName(g.id)}을(를) 이 지역에서 찾지 못했습니다."); return; }
            ApproachAndUse(p, npc);
        }

        void DoProp(PlayerController p, Goal g)
        {
            Interactable target = null;
            float best = float.MaxValue;
            foreach (var it in Interactable.All)
            {
                if (!(it is StoryProp sp) || sp.InteractId != g.id || !it.CanInteract) continue;
                float d = Vector2.Distance(p.Position, it.InteractionPoint);
                if (d < best) { best = d; target = it; }
            }
            if (target == null) { Stop("목표 물건이 아직 보이지 않습니다. 이야기를 먼저 진행해 주세요."); return; }
            ApproachAndUse(p, target);
        }

        void DoSite(PlayerController p, Goal g)
        {
            foreach (var it in Interactable.All)
                if (it is ConstructionSite && it.CanInteract) { ApproachAndUse(p, it); return; }
            Stop("공방 터를 찾지 못했습니다.");
        }

        void DoDungeonGuide(PlayerController p, Goal g)
        {
            foreach (var npc in NpcController.Services)
            {
                if (npc == null || npc.Definition.service != NpcService.Dungeon) continue;
                if (ApproachAndUse(p, npc)) Stop("던전 창을 열었습니다. 던전을 골라 들어가 주세요.");
                return;
            }
            Stop("던전 안내인을 찾지 못했습니다.");
        }

        /// <summary>Walks into reach and uses it; true on the frame it was used.</summary>
        bool ApproachAndUse(PlayerController p, Interactable target)
        {
            Vector2 point = target.InteractionPoint;
            if (Vector2.Distance(p.Position, point) > TalkReach)
            {
                WalkTo(p, point, TalkReach * 0.6f);
                return false;
            }
            if (Time.time < interactCooldown) return false;
            interactCooldown = Time.time + 1.2f;
            input.next.aim = (point - p.Position).normalized;
            target.Interact(p);
            lastProgressAt = Time.time;
            return true;
        }

        void DoHunt(PlayerController p, Goal g)
        {
            EnemyController best = null;
            float bestD = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled || !Matches(e, g.id)) continue;
                float d = Vector2.Distance(e.Position, p.Position);
                if (d < bestD) { bestD = d; best = e; }
            }
            Heal(p);
            if (best == null) { status = g.label + " · 몬스터를 찾는 중"; Roam(p); return; }
            Fight(p, best.Position, best.Center, bestD);
        }

        void DoGather(PlayerController p, Goal g)
        {
            ResourceNode best = null;
            float bestD = float.MaxValue;
            var kind = g.id == ItemIds.Stone ? ResourceKind.Rock : ResourceKind.Tree;
            foreach (var n in ResourceNode.Active)
            {
                if (n == null || !n.Available || n.Kind != kind) continue;
                float d = Vector2.Distance(n.transform.position, p.Position);
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best == null) { status = g.label + " · 자원이 다시 자라기를 기다리는 중"; Roam(p); return; }
            Vector2 pos = best.transform.position;
            Fight(p, pos, pos + new Vector2(0f, 0.4f), bestD);
        }

        void Fight(PlayerController p, Vector2 pos, Vector2 center, float dist)
        {
            input.next.aim = (center - p.Center).normalized;
            input.next.faceAim = true;
            if (dist > AttackReach) { WalkTo(p, pos, AttackReach * 0.8f); return; }
            lastProgressAt = Time.time;
            if (Time.time >= nextSkill)
            {
                nextSkill = Time.time + 1.4f;
                input.next.skillSlot = skillSlot;
                skillSlot = (skillSlot + 1) % 4;
            }
            else if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + 0.25f;
                input.next.attack = true;
            }
        }

        void Heal(PlayerController p)
        {
            var h = p.Health;
            if (h == null || h.Max <= 0 || Time.time < nextHeal) return;
            if (h.Current < h.Max * 0.4f) { input.next.useHealing = true; nextHeal = Time.time + 3f; }
        }

        Vector2 roamTarget;
        float roamUntil;

        /// <summary>Nothing to hit in sight: wander between the map's points of interest until something spawns.</summary>
        void Roam(PlayerController p)
        {
            var pois = Game.World.PointsOfInterest;
            if (Time.time > roamUntil || Vector2.Distance(p.Position, roamTarget) < 1f)
            {
                roamUntil = Time.time + 8f;
                roamTarget = pois.Count > 0 ? pois[Random.Range(0, pois.Count)] : (Vector2)Game.World.Bounds.center;
            }
            lastProgressAt = Time.time; // roaming is progress
            WalkTo(p, roamTarget, 0.5f);
        }

        void WalkTo(PlayerController p, Vector2 to, float stopAt)
        {
            if (Vector2.Distance(p.Position, to) <= stopAt) return;
            var dir = GridPath.Steer(p.Position, to);
            // Stuck on something GridPath does not know (a companion, a moving NPC): sidestep for a moment.
            if (Vector2.Distance(p.Position, lastPos) > 0.6f) { lastPos = p.Position; lastProgressAt = Time.time; }
            float stuck = Time.time - lastProgressAt;
            if (stuck > GiveUpSeconds) { Stop("길이 막혀 자동 진행을 멈췄습니다."); return; }
            if (stuck > StuckSeconds) dir = (dir + new Vector2(-dir.y, dir.x) * (((int)(stuck / 1.5f) % 2 == 0) ? 1f : -1f)).normalized;
            input.next.move = dir;
            input.next.aim = dir;
        }

        // =============================== Lookups ===============================

        static bool Matches(EnemyController e, string id)
        {
            if (string.IsNullOrEmpty(id) || id == "*") return true;
            return (e.Stats != null && e.Stats.enemyId == id) || (e.Def != null && e.Def.id == id);
        }

        static bool AnyEnemy(string id)
        {
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && Matches(e, id)) return true;
            return false;
        }

        static NpcController FindNpc(string npcId)
        {
            foreach (var n in NpcController.All)
                if (n != null && n.isActiveAndEnabled && n.Definition != null && n.Definition.npcId == npcId) return n;
            return null;
        }

        /// <summary>Map an NPC stands on: story placements (visible now) first, then the map files' NPC symbols.</summary>
        static string NpcMap(string npcId)
        {
            foreach (var pl in StoryCast.Placements)
                if (pl.npcId == npcId && StoryCast.Visible(pl)) return pl.map;
            if (npcMaps == null) BuildNpcMaps();
            return npcMaps.TryGetValue(npcId, out var m) ? m : null;
        }

        static void BuildNpcMaps()
        {
            npcMaps = new Dictionary<string, string>();
            var defs = new List<NpcDefinition>();
            if (Game.Config != null) defs.AddRange(Game.Config.npcs);
            defs.AddRange(GameConfig.DefaultNpcs());
            foreach (var map in MapRegistry.All)
            {
                var text = Resources.Load<TextAsset>(map.resource);
                if (text == null) continue;
                foreach (var d in defs)
                    if (!npcMaps.ContainsKey(d.npcId) && !string.IsNullOrEmpty(d.mapSymbol) && text.text.IndexOf(d.mapSymbol[0]) >= 0)
                        npcMaps[d.npcId] = map.id;
            }
        }

        /// <summary>The neighbouring map on the way from <paramref name="from"/> to <paramref name="to"/> (maps form a line).</summary>
        static string NextMapTowards(string from, string to)
        {
            var cur = MapRegistry.Get(from);
            if (cur == null) return null;
            foreach (string dir in new[] { cur.nextMap, cur.previousMap })
            {
                string step = dir;
                var seen = new HashSet<string> { from };
                while (!string.IsNullOrEmpty(step) && seen.Add(step))
                {
                    if (step == to) return dir;
                    var m = MapRegistry.Get(step);
                    if (m == null) break;
                    step = seen.Contains(m.nextMap ?? "") ? m.previousMap : m.nextMap;
                }
            }
            return null;
        }

        static string MapName(string id) => MapRegistry.Get(id)?.displayName ?? id;
    }
}
