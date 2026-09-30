using UnityEngine;

namespace DotRPG
{
    /// <summary>Blacksmith anvil ('&amp;' on the map): interact to open the equipment enhancement window.</summary>
    public class Anvil : Interactable
    {
        public override string Prompt => "장비 강화";

        public static Anvil Create(Vector2 position, Transform parent, string sprite = "anvil")
        {
            var go = new GameObject("Anvil");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<SpriteRenderer>().sprite = Game.Art.Get(sprite);
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.2f, 0.5f);
            col.offset = new Vector2(0f, 0.3f);
            go.AddComponent<YSort>().Configure(true);
            var anvil = go.AddComponent<Anvil>();
            anvil.ConfigureShape(new Vector2(0f, 0.2f), 0.4f, new Vector2(0f, 1.8f));
            return anvil;
        }

        public override void Interact(PlayerController player)
        {
            Game.Audio.PlaySfx("hammer");
            Game.UI.Enhance.SetKeeper(null, null);
            Game.Flow.OpenWindow(Game.UI.Enhance);
        }
    }
}
