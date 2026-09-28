using UnityEngine;

namespace DotRPG
{
    /// <summary>Finds the interactable in front of the player each frame and triggers it on input.</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        PlayerController owner;

        public Interactable Current { get; private set; }

        public void Setup(PlayerController player) => owner = player;

        void Update()
        {
            if (owner == null || owner.IsDead || !Game.IsPlaying)
            {
                Current = null;
                return;
            }
            Current = Interactable.FindBest(owner.Position + new Vector2(0f, 0.3f), owner.Facing.ToVector(), owner.Stats.interactRange);
        }

        public void TryInteract()
        {
            if (Current == null || !Current.CanInteract) return;
            owner.FaceTowards(Current.InteractionPoint);
            Current.Interact(owner);
        }
    }
}
