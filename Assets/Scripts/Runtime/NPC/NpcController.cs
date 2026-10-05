using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Villager: idles, wanders, patrols or "works" with a tool (the busy village life from the
    /// reference), and talks to the player. Which dialogue is used is decided by the QuestManager
    /// so quest NPCs react to progress.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcController : Interactable
    {
        NpcDefinition def;
        Rigidbody2D body;
        CharacterAnimator animator;
        SpriteRenderer tool;
        SpriteRenderer bobber;
        Vector2 home;
        Vector2 moveTarget;
        Facing facing;
        float nextActionTime;
        float workSwingStart = -10f;
        bool moving;
        bool patrolOut = true;
        bool talking;
        YSort ySort;

        const float WorkSwingDuration = 0.28f;

        public NpcDefinition Definition => def;
        public Facing CurrentFacing => facing;

        // [STORY] Cutscene control: while scripted the npc only walks where the scene sends it.
        bool scripted;
        float scriptSpeed = 2f;
        TextMesh markText, markShadow;
        float nextMarkCheck;

        public override string Prompt
        {
            get
            {
                switch (def.service)
                {
                    case NpcService.Shop: return "상점 열기";
                    case NpcService.Blacksmith: return "장비 강화";
                    case NpcService.Storage: return "창고 열기";
                    case NpcService.Dungeon: return "던전 입장"; // [DUNGEON]
                    default: return "대화하기";
                }
            }
        }

        /// <summary>Town service NPCs on the current map (the minimap marks them with icons).</summary>
        public static readonly System.Collections.Generic.List<NpcController> Services = new System.Collections.Generic.List<NpcController>();
        /// <summary>Every NPC in the loaded map (minimap quest marks, quest auto-walk targets).</summary>
        public static readonly System.Collections.Generic.List<NpcController> All = new System.Collections.Generic.List<NpcController>();
        /// <summary>The quest mark this NPC shows right now (minimap reads it).</summary>
        public QuestMark ShownMark { get; private set; }
        public bool ShownMarkMain { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (def != null && def.service != NpcService.None && !Services.Contains(this)) Services.Add(this);
            if (def != null && !All.Contains(this)) All.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Services.Remove(this);
            All.Remove(this);
            ShownMark = QuestMark.None;
        }

        void OnDestroy() { Services.Remove(this); All.Remove(this); }

        public static NpcController Create(NpcDefinition def, Vector2 position, Transform parent)
        {
            var go = new GameObject("NPC_" + def.npcId);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var body = PhysicsCompat.AddTopDownBody(go, RigidbodyType2D.Kinematic);
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.3f;
            col.offset = new Vector2(0f, 0.22f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(visual, false);
            shadow.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            shadow.sprite = Game.Art.Get("shadow");
            shadow.sortingOrder = -2;
            HdMaterial.Apply(shadow);
            var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(visual, false);

            var npc = go.AddComponent<NpcController>();
            npc.def = def;
            npc.body = body;
            npc.home = position;
            npc.moveTarget = position;
            npc.facing = def.initialFacing;
            npc.animator = go.AddComponent<CharacterAnimator>();
            npc.animator.Setup(def.look, sr);
            npc.animator.Play(CharacterAnim.Idle, npc.facing);
            npc.ConfigureShape(new Vector2(0f, 0.3f), 0.1f, new Vector2(0f, 1.6f));

            string toolKey = ToolSprite(def.tool);
            // [ART] Generated villagers who carry their load in the picture (the porter's crate) need no extra prop.
            if (def.tool == NpcTool.Crate && Resources.Load<Sprite>($"Art/char_{def.look.id}_down_idle0") != null) toolKey = null;
            if (toolKey != null)
            {
                npc.tool = new GameObject("Tool").AddComponent<SpriteRenderer>();
                npc.tool.transform.SetParent(visual, false);
                npc.tool.sprite = Game.Art.Get(toolKey);
                npc.tool.sortingOrder = 1;
                HdMaterial.Apply(npc.tool);
            }
            if (def.tool == NpcTool.FishingRod)
            {
                npc.bobber = new GameObject("Bobber").AddComponent<SpriteRenderer>();
                npc.bobber.transform.SetParent(go.transform, false);
                npc.bobber.sprite = Game.Art.Get("fx_water");
                npc.bobber.sortingOrder = 2;
            }
            npc.ySort = go.AddComponent<YSort>();
            npc.ySort.Configure(false);
            npc.nextActionTime = Time.time + Random.Range(0.5f, 2f);
            npc.PoseTool(0f);
            if (def.service != NpcService.None && !Services.Contains(npc)) Services.Add(npc);
            if (!All.Contains(npc)) All.Add(npc);
            return npc;
        }

        static string ToolSprite(NpcTool t)
        {
            switch (t)
            {
                case NpcTool.Axe: return "tool_axe";
                case NpcTool.Pickaxe: return "tool_pickaxe";
                case NpcTool.WateringCan: return "tool_can";
                case NpcTool.FishingRod: return "tool_rod";
                case NpcTool.Hammer: return "tool_hammer";
                case NpcTool.Crate: return "tool_crate";
                default: return null;
            }
        }

        const float MarkHeight = 1.62f;
        /// <summary>Set when the player picks a career (the 전직의 길 quest's objective).</summary>
        public const string CareerFlag = "career_promoted";

        public override void Interact(PlayerController player)
        {
            if (def.npcId == "career_trainer")
            {
                // 전직의 길: a character promoted before the quest existed counts as done.
                if (Game.Session.Progression.IsPromoted && !Game.Quest.HasFlag(CareerFlag)) Game.Quest.SetFlag(CareerFlag);
                // No quest talk waiting: straight to the 전직 tab of the skill window.
                if (!Game.Quest.HasQuestTalk(def.npcId)) { Game.UI.Skills.ShowAwakening(); return; }
            }
            // A shop NPC with a quest conversation waiting (errand, report, offer) talks first; the shop opens on the next talk.
            if (def.service != NpcService.None && !Game.Quest.HasQuestTalk(def.npcId))
            {
                moving = false;
                facing = FacingExtensions.FromVector(player.Position - (Vector2)transform.position, facing);
                animator.Play(CharacterAnim.Idle, facing);
                PoseTool(0f);
                Game.UI.OpenService(def);
                return;
            }
            talking = true;
            moving = false;
            facing = FacingExtensions.FromVector(player.Position - (Vector2)transform.position, facing);
            animator.Play(CharacterAnim.Idle, facing);
            PoseTool(0f);

            string dialogueId = Game.Quest.DialogueFor(def.npcId, def.dialogueId, def.dialogueIdAfterQuest);
            Game.Dialogue.Play(dialogueId, () =>
            {
                talking = false;
                facing = def.initialFacing;
                Game.Quest.OnDialogueFinished(dialogueId, def.npcId);
                // After the 전직 안내원's offer, the choice opens right away.
                if (def.npcId == "career_trainer" && !Game.Session.Progression.IsPromoted && Game.Session.Progression.Level >= 15) Game.UI.Skills.ShowAwakening();
            }, def.displayName);
        }

        void Update()
        {
            UpdateQuestMark();
            if (scripted)
            {
                animator.Play(moving ? CharacterAnim.Walk : CharacterAnim.Idle, facing);
                PoseTool(0f);
                return;
            }
            bool worldRunning = Game.IsWorldRunning || Game.State.Current == GameState.Title;
            if (talking || !worldRunning)
            {
                moving = false;
                return;
            }

            switch (def.behaviour)
            {
                case NpcBehaviour.Idle: UpdateIdle(); break;
                case NpcBehaviour.Wander: UpdateWander(); break;
                case NpcBehaviour.Patrol: UpdatePatrol(); break;
                case NpcBehaviour.Work: UpdateWork(); break;
            }

            animator.Play(moving ? CharacterAnim.Walk : (IsSwinging ? CharacterAnim.Attack : CharacterAnim.Idle), facing);
            PoseTool(IsSwinging ? (Time.time - workSwingStart) / WorkSwingDuration : 0f);
        }

        void FixedUpdate()
        {
            if (!moving) return;
            Vector2 pos = body.position;
            float speed = scripted ? scriptSpeed : def.behaviour == NpcBehaviour.Patrol ? 1.6f : 1.1f;
            Vector2 next = Vector2.MoveTowards(pos, moveTarget, speed * Time.fixedDeltaTime);
            body.MovePosition(next);
            if (Vector2.Distance(next, moveTarget) < 0.02f) moving = false;
        }

        bool IsSwinging => Time.time - workSwingStart < WorkSwingDuration;

        void UpdateIdle()
        {
            if (Time.time < nextActionTime) return;
            // Glance around now and then.
            facing = Random.value < 0.6f ? def.initialFacing : (Facing)Random.Range(0, 4);
            nextActionTime = Time.time + Random.Range(2f, 4f);
        }

        void UpdateWander()
        {
            if (moving || Time.time < nextActionTime) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Vector2 candidate = home + Random.insideUnitCircle * 2f;
                if (Physics2D.OverlapCircle(candidate + new Vector2(0f, 0.22f), 0.35f) != null) continue;
                StartMove(candidate);
                break;
            }
            nextActionTime = Time.time + Random.Range(1.5f, 3.5f);
        }

        void UpdatePatrol()
        {
            if (moving || Time.time < nextActionTime) return;
            StartMove(patrolOut ? home + def.patrolOffset : home);
            patrolOut = !patrolOut;
            nextActionTime = Time.time + 1.2f;
        }

        void UpdateWork()
        {
            facing = def.initialFacing;
            if (Time.time < nextActionTime) return;
            nextActionTime = Time.time + Random.Range(1.1f, 1.8f);
            if (def.tool == NpcTool.FishingRod) return; // fishing is a calm, static pose
            workSwingStart = Time.time;
            Invoke(nameof(WorkImpact), WorkSwingDuration * 0.8f);
        }

        void WorkImpact()
        {
            if (!Game.IsWorldRunning && Game.State.Current != GameState.Title) return;
            Vector2 front = (Vector2)transform.position + new Vector2(0f, 0.3f) + facing.ToVector() * 0.7f;
            bool nearPlayer = Game.Player != null && Vector2.Distance(Game.Player.Position, transform.position) < 7f;
            switch (def.tool)
            {
                case NpcTool.Axe:
                    Fx.Burst("fx_leaf", front + Vector2.up * 0.4f, 2, 1.5f, 0.5f);
                    if (nearPlayer) Game.Audio.PlaySfx("chop", 0.35f);
                    break;
                case NpcTool.Pickaxe:
                    Fx.Burst("fx_chip", front, 2, 2f, 0.5f);
                    if (nearPlayer) Game.Audio.PlaySfx("mine", 0.35f);
                    break;
                case NpcTool.WateringCan:
                    Fx.Burst("fx_water", front, 3, 1f, 0.4f);
                    break;
                case NpcTool.Hammer:
                    Fx.Sparkle(front + Vector2.up * 0.3f, 1, 0.2f);
                    if (nearPlayer) Game.Audio.PlaySfx("hammer", 0.35f);
                    break;
            }
        }

        // =============================== [STORY] Cutscene control ===============================

        public void ScriptPlace(Vector2 pos, Facing f)
        {
            moving = false;
            body.position = pos;
            transform.position = pos;
            home = pos;
            moveTarget = pos;
            facing = f;
            animator.Play(CharacterAnim.Idle, facing);
        }

        public void ScriptFace(Facing f)
        {
            facing = f;
            animator.Play(moving ? CharacterAnim.Walk : CharacterAnim.Idle, facing);
        }

        /// <summary>Walks to <paramref name="target"/> (cutscene); <paramref name="skip"/> jumps there at once.</summary>
        public System.Collections.IEnumerator ScriptWalk(Vector2 target, float speed, System.Func<bool> skip)
        {
            scripted = true;
            scriptSpeed = speed;
            StartMove(target);
            while (moving && (skip == null || !skip())) yield return null;
            if (moving) ScriptPlace(target, facing);
            home = target;
            scripted = false;
        }

        void UpdateQuestMark()
        {
            if (Game.Quest == null || def == null || Time.unscaledTime < nextMarkCheck) return;
            nextMarkCheck = Time.unscaledTime + 0.25f;
            var mark = Game.Quest.MarkFor(def.npcId); // shop NPCs carry quest marks too (the errand at the general store)
            ShownMark = mark;
            ShownMarkMain = mark != QuestMark.None && Game.Quest.MarkIsMain(def.npcId);
            if (mark == QuestMark.None)
            {
                if (markText != null) markText.gameObject.SetActive(false);
                return;
            }
            if (markText == null)
            {
                // Just above the head (64 px body at 36 px per unit is about 1.8 units tall).
                markShadow = MakeMark("QuestMarkShadow", new Vector3(0.04f, MarkHeight - 0.04f, 0f), 29998);
                markText = MakeMark("QuestMark", new Vector3(0f, MarkHeight, 0f), 29999);
                markShadow.transform.SetParent(markText.transform, true);
            }
            markText.gameObject.SetActive(true);
            string glyph = mark == QuestMark.Available ? "!" : "?";
            bool main = ShownMarkMain;
            markText.text = glyph;
            markShadow.text = glyph;
            markText.color = main ? new Color32(255, 214, 64, 255) : new Color32(120, 214, 255, 255);
            markShadow.color = new Color32(20, 16, 24, 220);
        }

        TextMesh MakeMark(string name, Vector3 local, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var tm = go.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.1f;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            var font = UIFont.Get();
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null)
            {
                tm.font = font;
                mr.sharedMaterial = font.material;
            }
            mr.sortingOrder = order;
            return tm;
        }

        void LateUpdate()
        {
            if (markText != null && markText.gameObject.activeSelf)
                markText.transform.localPosition = new Vector3(0f, MarkHeight + Mathf.Sin(Time.unscaledTime * 4f) * 0.06f, 0f);
        }

        void StartMove(Vector2 target)
        {
            moveTarget = target;
            moving = true;
            facing = FacingExtensions.FromVector(target - body.position, facing);
        }

        /// <summary>Positions the held tool. t in [0,1] is the swing progress (0 = resting).</summary>
        void PoseTool(float t)
        {
            if (tool == null) return;
            Vector2 dir = facing.ToVector();
            if (def.tool == NpcTool.Crate)
            {
                // Carried over the head like the villager in the reference.
                tool.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                tool.transform.localRotation = Quaternion.identity;
                tool.transform.localScale = Vector3.one * 0.8f;
                ySort?.SetLocalOrder(tool, 2);
                return;
            }
            if (def.tool == NpcTool.FishingRod)
            {
                tool.transform.localPosition = new Vector3(dir.x * 0.25f + 0.15f, 0.4f, 0f);
                tool.transform.localRotation = Quaternion.Euler(0f, 0f, facing.IsLeft() ? 30f : -30f);
                if (bobber != null)
                {
                    var p = (Vector2)transform.position + dir * 1.6f + new Vector2(0.4f, 0.2f + Mathf.Sin(Time.time * 3f) * 0.05f);
                    bobber.transform.position = p;
                }
                return;
            }
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float swing = t <= 0f ? 60f : Mathf.Lerp(80f, -40f, Mathf.Clamp01(t));
            tool.transform.localPosition = new Vector3(0f, 0.4f, 0f) + (Vector3)(dir * 0.15f);
            tool.transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle + swing - 90f);
            ySort?.SetLocalOrder(tool, facing.IsUp() ? -1 : 1);
        }
    }
}
