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
            if (npc == null) { Stop(L.F("{0}을(를) 이 지역에서 찾지 못했습니다.", Game.Quest.NpcName(g.id))); return; }
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
            if (target == null) { Stop(L.T("목표 물건이 아직 보이지 않습니다. 이야기를 먼저 진행해 주세요.")); return; }
            ApproachAndUse(p, target);
        }

        void DoSite(PlayerController p, Goal g)
        {
            foreach (var it in Interactable.All)
                if (it is ConstructionSite && it.CanInteract) { ApproachAndUse(p, it); return; }
            Stop(L.T("공방 터를 찾지 못했습니다."));
        }

        void DoDungeonGuide(PlayerController p, Goal g)
        {
            foreach (var npc in NpcController.Services)
            {
                if (npc == null || npc.Definition.service != NpcService.Dungeon) continue;
                if (ApproachAndUse(p, npc)) Stop(L.T("던전 창을 열었습니다. 던전을 골라 들어가 주세요."));
                return;
            }
            Stop(L.T("던전 안내인을 찾지 못했습니다."));
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
                if (unreachable.TryGetValue(e, out float until) && Time.time < until) continue;
                float d = Vector2.Distance(e.Position, p.Position);
                if (d < bestD) { bestD = d; best = e; }
            }
            Heal(p);
            // [RAID] 결계 의식: the circles are plain to see, so 자동 사냥 (and the measure bot) runs to a free one first
            // (before the "next monster is far, wait 3 s" rule, which stopped it halfway to a far circle).
            if (RaidSeal.SpotFor(p, out Vector2 seal))
            {
                WalkTo(p, seal, RaidSeal.Radius * .5f);
                lastProgressAt = Time.time;
                return;
            }
            if (best == null) { status = L.F("{0} · 몬스터를 찾는 중", g.label); Roam(p); return; }
            // [BALANCE] Toned down: when nothing is close, stand a moment (3 s) before walking to a far monster.
            if (bestD > HuntNear)
            {
                if (farWaitUntil <= 0f) farWaitUntil = Time.time + HuntFarWait;
                if (Time.time < farWaitUntil) { status = L.F("{0} · 다음 몬스터를 찾는 중", g.label); lastProgressAt = Time.time; lastPos = p.Position; return; }
            }
            else farWaitUntil = 0f;
            if (best != chaseTarget) { chaseTarget = best; chaseSince = Time.time; }
            Fight(p, best.Position, best.Center, bestD);
            // [MEASURE] Walking at a monster across water or a cliff for a while without getting closer: leave it for a
            // moment and take another (it stopped 자동 사냥 with "길이 막혀" after 25 s, 0 kills a minute in 붉은 돌 고개).
            if (Time.time - lastProgressAt > UnreachableSeconds && Time.time - chaseSince > UnreachableSeconds)
            {
                unreachable[best] = Time.time + UnreachableSkip;
                lastProgressAt = Time.time;
                chaseTarget = null;
                if (unreachable.Count > 64) unreachable.Clear();
            }
        }

        const float UnreachableSeconds = 5f, UnreachableSkip = 15f;
        readonly System.Collections.Generic.Dictionary<EnemyController, float> unreachable = new System.Collections.Generic.Dictionary<EnemyController, float>();
        EnemyController chaseTarget;
        float chaseSince;

        const float HuntNear = 4f, HuntFarWait = 3f;
        float farWaitUntil;

        /// <summary>Mages shoot from range instead of walking into sword reach (they took every hit standing next to the pack).</summary>
        const float MageReach = 4.5f;

        void Fight(PlayerController p, Vector2 pos, Vector2 center, float dist)
        {
            // [MEASURE] A person steps out of a ground warning (the AI companions' rule): the measure bot does too, else
            // squishy careers died to every telegraph and the raid failure rates were overstated (2026-10-13).
            if (MeasureSkilled && CompanionBrain.EscapeDanger(p.Position, out Vector2 away))
            {
                input.next.move = away;
                input.next.aim = away;
                lastProgressAt = Time.time;
                return;
            }
            input.next.aim = (center - p.Center).normalized;
            input.next.faceAim = true;
            int spam = SpamSlot(p);
            float reach = p.Class == CharacterClass.Mage ? MageReach : AttackReach;
            if (spam >= 0 && p.Skills.Numbers(spam).range > 1.5f) reach = Mathf.Max(reach, Mathf.Min(p.Skills.Numbers(spam).range * 0.85f, 6f));
            if (dist > reach) { WalkTo(p, pos, reach * 0.8f); return; }
            lastProgressAt = Time.time;
            if (MeasureSkilled) { SkilledTurn(p, spam); return; }
            AutoTurn(p, spam);
        }

        float nextSpam;

        /// <summary>
        /// [BALANCE] Auto hunt plays a little worse than a person: one ready skill every 1.6-2.2 s (in key order, skipping empty,
        /// cooling and unaffordable keys), the career's main hunting skill with a short pause after each cast, else basic attacks.
        /// Measured 2026-10-13: pressing every key in turn every 2.4-3.2 s (empty ones too) and never the main skill made auto hunt
        /// 15-50% of a practised player after the career change (Docs/PLAN_PLAYTEST_MEASURE_2026-10-12.md 4.2).
        /// </summary>
        void AutoTurn(PlayerController p, int spam)
        {
            if (p.Skills == null || p.Skills.IsCasting) return;
            if (Time.time >= nextSkill)
            {
                nextSkill = Time.time + UnityEngine.Random.Range(1.6f, 2.2f);
                for (int i = 0; i < SkillKeys.Length; i++)
                {
                    int slot = SkillKeys[(skillSlot + i) % SkillKeys.Length];
                    var gem = p.Data.Progression.Active(slot);
                    if (slot == spam || gem == null || !p.Skills.IsReady(slot) || !Affordable(p, slot)) continue;
                    // [FIGHTER] 검기 태세 is a toggle: switch it on once and never press it again while it is on.
                    if (gem.id == "f_stance" && CareerCombat.For(p).StanceOn) continue;
                    input.next.skillSlot = slot;
                    skillSlot = (skillSlot + i + 1) % SkillKeys.Length;
                    nextAttack = Mathf.Max(nextAttack, Time.time + .5f);
                    return;
                }
            }
            if (spam >= 0 && Time.time >= nextSpam && p.Skills.IsReady(spam) && Affordable(p, spam))
            {
                input.next.skillSlot = spam;
                nextSpam = Time.time + UnityEngine.Random.Range(.35f, .6f);
                return;
            }
            if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + UnityEngine.Random.Range(.5f, .7f); // was 0.25 s: auto hunt out-hit real players
                input.next.attack = true;
            }
        }

        static readonly int[] SkillKeys = { 0, 1, 2, 3, 5, 6, 7 };

        /// <summary>The key holding the career's spammable main hunting skill, or -1 (none before the career change).</summary>
        static int SpamSlot(PlayerController p)
        {
            foreach (int k in SkillKeys)
            {
                var gem = p.Data.Progression.Active(k);
                if (gem != null && System.Array.IndexOf(CareerSkill.MainHunters, gem.id) >= 0) return k;
            }
            return -1;
        }

        static bool Affordable(PlayerController p, int slot)
        {
            var n = p.Skills.Numbers(slot);
            return n.usesLife ? p.Health.Current > n.manaCost * 2 : p.Mana >= n.manaCost;
        }

        /// <summary>[MEASURE] A practised player's turn: other skills as they come ready, else the main skill, else a basic attack.</summary>
        void SkilledTurn(PlayerController p, int spam)
        {
            if (p.Skills == null || p.Skills.IsCasting) return;
            // [MEASURE] A player who knows the raid saves the cooldown skills for 망자의 심판 and bursts the cast.
            bool burst = false, hold = false;
            if (MeasureAware)
                foreach (var e in EnemyController.Active)
                    if (e != null && !e.IsDead && e.Behaviour is BossBrain bb && e.Def != null && e.Def.raid)
                    {
                        if (bb.CastingJudgment) burst = true;
                        else if (bb.JudgmentIn < 9f) hold = true;
                    }
            if (!hold && Time.time >= nextSkill)
            {
                nextSkill = Time.time + (burst ? .05f : .3f);
                for (int i = 0; i < SkillKeys.Length; i++)
                {
                    int slot = SkillKeys[(skillSlot + i) % SkillKeys.Length];
                    if (slot == spam || !p.Skills.IsReady(slot) || !Affordable(p, slot)) continue;
                    var gem = p.Data.Progression.Active(slot);
                    if (gem != null && gem.id == "f_stance" && CareerCombat.For(p).StanceOn) continue;
                    input.next.skillSlot = slot;
                    skillSlot = (skillSlot + i + 1) % SkillKeys.Length;
                    return;
                }
            }
            if (spam >= 0 && p.Skills.IsReady(spam) && Affordable(p, spam)) { input.next.skillSlot = spam; return; }
            if (Time.time >= nextAttack) { nextAttack = Time.time + .3f; input.next.attack = true; }
        }

        void Heal(PlayerController p)
        {
            var h = p.Health;
            if (AutoPotion.On) return; // [AUTO] the auto-potion setting drinks for everyone, manual play and auto-progress alike
            if (h == null || h.Max <= 0 || Time.time < nextHeal) return;
            if (h.Current < h.Max * 0.4f) { input.next.useHealing = true; nextHeal = Time.time + 3f; }
            // MP runs dry in long hunts: drink a mana potion below 20% like a player would.
            else if (p.MaxMana > 0 && p.Mana < p.MaxMana * 0.2f) { input.next.useMana = true; nextHeal = Time.time + 3f; }
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
            var dir = AvoidNpcs(p, GridPath.Steer(p.Position, to), to);
            // Stuck on something GridPath does not know (a companion, a moving NPC): sidestep for a moment.
            if (Vector2.Distance(p.Position, lastPos) > 0.6f) { lastPos = p.Position; lastProgressAt = Time.time; }
            float stuck = Time.time - lastProgressAt;
            if (stuck > GiveUpSeconds) { Stop(L.T("길이 막혀 자동 진행을 멈췄습니다.")); return; }
            if (stuck > NpcStuckSeconds) PassThroughNpcs(p);
            if (stuck > StuckSeconds) dir = (dir + new Vector2(-dir.y, dir.x) * (((int)(stuck / 1.5f) % 2 == 0) ? 1f : -1f)).normalized;
            input.next.move = dir;
            input.next.aim = dir;
        }

        // =============================== NPCs in the way ===============================
        // GridPath leaves NPCs out (they walk about), so the hero walked into one and pushed against it until the
        // 3 s sidestep, often stopping with "길이 막혀" in the crowded village. Now it bends round an NPC ahead, and
        // when still pinned against one for a second it walks through that NPC (collision back on once apart).

        const float AvoidLook = 1.5f, AvoidWidth = 0.7f, NpcStuckSeconds = 1f, NpcTouch = 0.9f, NpcRelease = 1.3f;
        NpcController avoiding;
        float avoidSign;
        readonly List<Collider2D> ghosted = new List<Collider2D>();
        Collider2D ghostBody;

        Vector2 AvoidNpcs(PlayerController p, Vector2 dir, Vector2 to)
        {
            if (dir.sqrMagnitude < 0.0001f) return dir;
            Vector2 me = p.Position, left = new Vector2(-dir.y, dir.x);
            NpcController block = null;
            float nearest = AvoidLook, across = 0f;
            foreach (var n in NpcController.All)
            {
                if (n == null || !n.isActiveAndEnabled) continue;
                Vector2 at = n.transform.position;
                if (Vector2.Distance(at, to) < TalkReach) continue; // the one being walked to
                Vector2 rel = at - me;
                float ahead = Vector2.Dot(rel, dir), side = Vector2.Dot(rel, left);
                if (ahead <= 0f || ahead >= nearest || Mathf.Abs(side) >= AvoidWidth) continue;
                if (ghosted.Count > 0 && ghosted.Contains(n.GetComponent<Collider2D>())) continue; // already walking through it
                nearest = ahead; across = side; block = n;
            }
            if (block == null) { avoiding = null; return dir; }
            // Keep the side picked for this NPC so a head-on approach does not flip left / right every frame.
            if (block != avoiding) { avoiding = block; avoidSign = across >= 0f ? -1f : 1f; }
            float bend = 0.6f + (1f - nearest / AvoidLook);
            return (dir + left * avoidSign * bend).normalized;
        }

        void PassThroughNpcs(PlayerController p)
        {
            var body = p.BodyCollider;
            if (body == null) return;
            if (ghostBody != body) RestoreNpcs(null);
            ghostBody = body;
            foreach (var n in NpcController.All)
            {
                if (n == null || !n.isActiveAndEnabled || Vector2.Distance(n.transform.position, p.Position) > NpcTouch) continue;
                var c = n.GetComponent<Collider2D>();
                if (c == null || ghosted.Contains(c)) continue;
                Physics2D.IgnoreCollision(body, c, true);
                ghosted.Add(c);
            }
        }

        /// <summary>Collision back on for NPCs the hero has left behind (all of them with <paramref name="p"/> null).</summary>
        void RestoreNpcs(PlayerController p)
        {
            for (int i = ghosted.Count - 1; i >= 0; i--)
            {
                var c = ghosted[i];
                if (c != null && p != null && ghostBody == p.BodyCollider && Vector2.Distance(c.transform.position, p.Position) < NpcRelease) continue;
                if (c != null && ghostBody != null) Physics2D.IgnoreCollision(ghostBody, c, false);
                ghosted.RemoveAt(i);
            }
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

        static string MapName(string id) => L.T(MapRegistry.Get(id)?.displayName ?? id);
    }
}
