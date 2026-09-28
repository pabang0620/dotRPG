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
    /// Frame-based 4-direction sprite animation shared by the player, NPCs and enemies.
    /// Frames come from <see cref="SpriteLibrary.GetCharacter"/>, so real sprite sheets can replace the
    /// placeholders by file name, or this component can later be swapped for an Animator controller
    /// behind the same Play() API.
    /// </summary>
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
        float timer;
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
            Refresh();
        }

        public void Play(CharacterAnim anim, Facing dir)
        {
            if (anim != current)
            {
                current = anim;
                timer = 0f;
            }
            facing = dir;
        }

        void Update()
        {
            if (look == null || target == null) return;
            int previousFrame = FrameIndex();
            timer += Time.deltaTime * (current == CharacterAnim.Walk ? SpeedMultiplier : 1f);
            int frame = FrameIndex();
            if (current == CharacterAnim.Walk && frame != previousFrame && (frame == 0 || frame == 2)) Footstep?.Invoke();
            Refresh();
        }

        int FrameIndex()
        {
            switch (current)
            {
                case CharacterAnim.Walk: return Mathf.FloorToInt(timer * walkFps) % WalkFrames.Length;
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
                case CharacterAnim.Walk: frame = WalkFrames[FrameIndex()]; break;
                case CharacterAnim.Attack: frame = "attack"; break;
                case CharacterAnim.Hurt: frame = "hurt"; break;
                default: frame = IdleFrames[FrameIndex()]; break;
            }
            target.sprite = Game.Art.GetCharacter(look, facing.SpriteKey(), frame);
            target.flipX = facing == Facing.Left;
        }
    }
}
