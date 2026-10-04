using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Base class for everything the player can use with the Interact button (NPCs, signs, crops,
    /// the construction site). Instances register themselves so the player can find the best target
    /// without physics queries.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        static readonly List<Interactable> Active = new List<Interactable>();

        [Tooltip("Offset of the point the player must be near.")]
        [SerializeField] protected Vector2 interactionOffset = new Vector2(0f, 0.3f);
        [Tooltip("Extra reach for large objects.")]
        [SerializeField] protected float extraRange;
        [Tooltip("Where the key prompt floats, relative to the object.")]
        [SerializeField] protected Vector2 promptOffset = new Vector2(0f, 1.5f);

        /// <summary>Verb shown in the prompt, e.g. "대화하기".</summary>
        public abstract string Prompt { get; }

        public virtual bool CanInteract => true;

        public Vector2 InteractionPoint => (Vector2)transform.position + interactionOffset;

        public Vector3 PromptWorldPosition => transform.position + (Vector3)promptOffset;

        public abstract void Interact(PlayerController player);

        /// <summary>Every enabled interactable (quest auto-walk looks up story objects).</summary>
        public static IReadOnlyList<Interactable> All => Active;

        public void ConfigureShape(Vector2 offset, float range, Vector2 prompt)
        {
            interactionOffset = offset;
            extraRange = range;
            promptOffset = prompt;
        }

        protected virtual void OnEnable() => Active.Add(this);
        protected virtual void OnDisable() => Active.Remove(this);

        /// <summary>Nearest usable interactable in range, preferring the one the player faces.</summary>
        public static Interactable FindBest(Vector2 from, Vector2 facing, float range)
        {
            Interactable best = null;
            float bestScore = float.MaxValue;
            foreach (var candidate in Active)
            {
                if (candidate == null || !candidate.CanInteract) continue;
                Vector2 delta = candidate.InteractionPoint - from;
                float distance = delta.magnitude;
                if (distance > range + candidate.extraRange) continue;
                float facingBonus = distance > 0.01f ? Vector2.Dot(delta / distance, facing) : 1f;
                float score = distance - candidate.extraRange - facingBonus * 0.45f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }
    }
}
