using UnityEngine;

namespace DotRPG
{
    public enum CharacterAnim
    {
        Idle,
        Walk,
        Attack,
        Hurt,
    }

    /// <summary>
    /// Frame-based 8-direction sprite animation shared by the player, NPCs and enemies.
    /// Frames come from <see cref="SpriteLibrary.GetCharacter"/>, so real sprite sheets can replace the
    /// placeholders by file name, or this component can later be swapped for an Animator controller
    /// behind the same Play() API.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class CharacterAnimator : MonoBehaviour
    {
        static readonly string[] IdleFrames = { "idle0", "idle1" };
        static readonly string[] WalkFrames = { "walk0", "walk1", "walk2", "walk3" };

        [SerializeField] float walkFps = 9f;
        [SerializeField] float idleFps = 1.8f;
        [SerializeField] SpriteRenderer target;

        CharacterLook look;
        CharacterAnim current = CharacterAnim.Idle;
        Facing facing = Facing.Down;
        float timer, walkDistance;
        float careerPoseStart,careerPoseEnd,careerPoseFrom,careerPoseTo=1,careerFacingEnd;int careerPoseStage;Facing careerFacing;
        public Facing PresentationFacing(Facing fallback)=>GetComponent<CareerSkillMotion>() is CareerSkillMotion motion&&motion.Active?motion.ViewFacing(fallback):player!=null&&!player.IsDead&&player.Data.Progression.Career!=Career.None&&Time.time<careerFacingEnd&&(combat==null||!combat.IsAttacking)?careerFacing:fallback;
        public void SetCareerFacing(Facing direction){careerFacing=direction;careerFacingEnd=Time.time+.12f;}
        public void BeginCareerPose(float seconds,int stage=0,float from=0,float to=1)
        {careerPoseStart=Time.time;careerPoseEnd=Time.time+seconds;careerPoseStage=stage;careerPoseFrom=from;careerPoseTo=to;}
        public void EndCareerPose(){careerPoseEnd=careerFacingEnd=0;}

        Vector2 previousPosition;
        PlayerController player;
        PlayerCombat combat;
        public string FrameKey { get; private set; } = "idle0";
        public CharacterLook Look => look;
        /// <summary>Scales walk speed so footsteps match movement speed.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public SpriteRenderer Renderer => target;
        public Facing Facing => facing;
        public CharacterAnim Current => current;

        /// <summary>Raised when a walk cycle plants a foot (frame 0 and 2) — used for dust/footstep sounds.</summary>
        public event System.Action Footstep;

        public void Setup(CharacterLook characterLook, SpriteRenderer renderer)
        {
            look = characterLook;
            target = renderer;
            timer = Random.value;
            player = GetComponent<PlayerController>();
            previousPosition = transform.position;
            Refresh();
        }

        public void Play(CharacterAnim anim, Facing dir)
        {
            if (anim != current)
            {
                current = anim;
                timer = 0f;
                if (anim == CharacterAnim.Walk) walkDistance = 0f;
            }
            facing = dir;
        }

        void Update()
        {
            if (look == null || target == null) return;
            int previousFrame = FrameIndex();
            Vector2 position = player != null ? player.Position : (Vector2)transform.position;
            float distance = Vector2.Distance(position, previousPosition);
            previousPosition = position;
            if (current == CharacterAnim.Walk && distance < .5f) walkDistance += distance;
            timer += Time.deltaTime * (current == CharacterAnim.Walk ? SpeedMultiplier : 1f);
            int frame = FrameIndex();
            int secondContact = SilverWarriorArt.Supports(look.id) ? 4 : 2;
            if (current == CharacterAnim.Walk && frame != previousFrame && (frame == 0 || frame == secondContact)) Footstep?.Invoke();
            Refresh();
        }

        int FrameIndex()
        {
            switch (current)
            {
                case CharacterAnim.Walk: return player != null && SilverWarriorArt.Supports(look.id) ? Mathf.FloorToInt(walkDistance / WarriorGait.CycleDistance * WarriorGait.Frames) % WarriorGait.Frames : Mathf.FloorToInt(timer * walkFps) % WalkFrames.Length;
                case CharacterAnim.Idle: return Mathf.FloorToInt(timer * idleFps) % IdleFrames.Length;
                default: return 0;
            }
        }

        void Refresh()
        {
            if (look == null || target == null || Game.Art == null) return;
            string frame;
            switch (current)
            {
                case CharacterAnim.Walk: frame = SilverWarriorArt.Supports(look.id) ? SilverWalk(FrameIndex()) : WalkFrames[FrameIndex()]; break;
                case CharacterAnim.Attack:
                    if (combat == null) combat = GetComponent<PlayerCombat>();
                    frame = SilverWarriorArt.Supports(look.id) ? (combat != null && combat.IsAttacking ? combat.AttackFrame : WarriorAttackMotion.Frame(Mathf.Clamp01(timer / WarriorAttackMotion.Duration))) : "attack";
                    break;
                case CharacterAnim.Hurt: frame = "hurt"; break;
                default: frame = IdleFrames[FrameIndex()]; break;
            }
            if(player!=null&&!player.IsDead&&player.Data.Progression.Career!=Career.None&&Time.time<careerPoseEnd&&SilverWarriorArt.Supports(look.id)){
                if(combat==null)combat=GetComponent<PlayerCombat>();
                if(combat==null||!combat.IsAttacking)frame=WarriorAttackMotion.Frame(Mathf.Lerp(careerPoseFrom,careerPoseTo,Mathf.Clamp01((Time.time-careerPoseStart)/Mathf.Max(.01f,careerPoseEnd-careerPoseStart))),careerPoseStage);
            }
            var motion=player!=null?GetComponent<CareerSkillMotion>():null;
            if(motion!=null&&motion.Active&&(combat==null||!combat.IsAttacking))frame=motion.Frame(SilverWarriorArt.Supports(look.id))??frame;
            FrameKey = frame;
            var visibleFacing=PresentationFacing(facing);
            // [P5] Same look, facing and frame as last time: nothing to do (the sprite lookup builds a key string).
            if (ReferenceEquals(look, shownLook) && visibleFacing == shownFacing && ReferenceEquals(frame, shownFrame) && target.sprite != null) return;
            shownLook = look; shownFacing = visibleFacing; shownFrame = frame;
            target.sprite = Game.Art.GetCharacter(look, SilverWarriorArt.Supports(look.id) ? SilverWarriorArt.ViewKey(visibleFacing) : visibleFacing.SpriteKey(), frame);
            target.flipX = !SilverWarriorArt.Supports(look.id) && visibleFacing.IsLeft();
            if (SilverWarriorArt.Supports(look.id)) SilverWarriorPresentation.ApplyMaterial(target);
            else HdMaterial.Apply(target);
            // The mage's front view sits 2 art pixels right of the feet ring: nudge it back so it stands centred.
            bool nudge = player != null && !SilverWarriorArt.Supports(look.id) && visibleFacing == Facing.Down && target.sprite != null;
            target.transform.localPosition = new Vector3(nudge ? -FrontNudgePixels / target.sprite.pixelsPerUnit : 0f, 0f, 0f);
        }

        const float FrontNudgePixels = 2f;

        CharacterLook shownLook;
        Facing shownFacing;
        string shownFrame;
        static readonly string[] SilverWalkKeys = new string[16];
        static string SilverWalk(int i) => i >= 0 && i < SilverWalkKeys.Length ? (SilverWalkKeys[i] ?? (SilverWalkKeys[i] = "walk" + i)) : "walk" + i;

        /// <summary>Swaps the look (e.g. new clothes) keeping the current animation.</summary>
        public void SetLook(CharacterLook characterLook)
        {
            look = characterLook;
            Refresh();
        }
    }
}
