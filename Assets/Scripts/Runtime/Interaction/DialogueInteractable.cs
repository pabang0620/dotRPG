using UnityEngine;

namespace DotRPG
{
    /// <summary>Generic "read / look at" object: signs, doors, notice boards. Plays one dialogue.</summary>
    public class DialogueInteractable : Interactable
    {
        [SerializeField] string prompt = "살펴보기"; // i18n-ignore Unity-serialized field default (no L.T during deserialization); translated where shown
        [SerializeField] string dialogueId = "";

        public override string Prompt => L.T(prompt);

        public void Setup(string promptText, string dialogue)
        {
            prompt = promptText;
            dialogueId = dialogue;
        }

        public override void Interact(PlayerController player)
        {
            Game.Dialogue.Play(dialogueId);
        }
    }
}
