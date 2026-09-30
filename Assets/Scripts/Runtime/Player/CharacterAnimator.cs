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
                case CharacterAnim.Walk: frame = SilverWarriorArt.Supports(look.id) ? "walk" + FrameIndex() : WalkFrames[FrameIndex()]; break;
                case CharacterAnim.Attack:
                    if (combat == null) combat = GetComponent<PlayerCombat>();
                    frame = SilverWarriorArt.Supports(look.id) ? WarriorAttackMotion.Frame(combat != null && combat.IsAttacking ? combat.AttackProgress : Mathf.Clamp01(timer / WarriorAttackMotion.Duration)) : "attack";
                    break;
                case CharacterAnim.Hurt: frame = "hurt"; break;
                default: frame = IdleFrames[FrameIndex()]; break;
            }
            FrameKey = frame;
            target.sprite = Game.Art.GetCharacter(look, SilverWarriorArt.Supports(look.id) ? SilverWarriorArt.ViewKey(facing) : facing.SpriteKey(), frame);
            target.flipX = !SilverWarriorArt.Supports(look.id) && facing.IsLeft();
            if (SilverWarriorArt.Supports(look.id)) SilverWarriorPresentation.ApplyMaterial(target);
            else HdMaterial.Apply(target);
        }

        /// <summary>Swaps the look (e.g. new clothes) keeping the current animation.</summary>
        public void SetLook(CharacterLook characterLook)
        {
            look = characterLook;
            Refresh();
        }
    }
}
