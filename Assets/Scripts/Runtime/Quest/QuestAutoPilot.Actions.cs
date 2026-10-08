using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class QuestAutoPilot
    {
        // =============================== Doing it ===============================

        void DoNpc(PlayerController p, Goal g)
        {
            var npc = FindNpc(g.id);
            if (npc == null)
                foreach (var it in Interactable.All)
                    if (it is ServiceDoor door && WorldBuilder.ServiceNpc(door.Service)?.npcId == g.id)
                    { ApproachAndUse(p, door); return; }
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

        /// <summary>The neighbouring map on the way from <paramref name="from"/> to <paramref name="to"/> (branch-aware breadth-first routing).</summary>
        static string NextMapTowards(string from, string to)
        {
            var cur = MapRegistry.Get(from);
            if (cur == null) return null;
            if (cur.IsInterior) return cur.exteriorMap;
            var queue = new Queue<(string map, string first)>();
            var seen = new HashSet<string> { from };
            foreach(var next in WorldRoutes.Neighbors(from))queue.Enqueue((next,next));
            while(queue.Count>0){
                var node=queue.Dequeue();if(!seen.Add(node.map))continue;
                if(node.map==to)return node.first;
                foreach(var next in WorldRoutes.Neighbors(node.map))queue.Enqueue((next,node.first));
            }
            return null;
        }

        static string MapName(string id) => MapRegistry.Get(id)?.displayName ?? id;
    }
}
