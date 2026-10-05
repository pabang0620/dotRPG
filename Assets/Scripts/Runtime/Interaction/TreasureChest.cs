using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Treasure chest placed with '$'. Opens once per playthrough (remembered in the save) and gives
    /// the epic "철 흉갑" chest plate.
    /// </summary>
    public class TreasureChest : Interactable
    {
        string chestId;
        SpriteRenderer sr;
        bool hd;

        /// <summary>What every chest gives (also exported for the server, GameDataExport).</summary>
        public const string Reward = "eq_neck_10_r"; // shared by both classes (GearCatalog)

        public override string Prompt => "상자 열기";

        public override bool CanInteract => !Game.Session.OpenedChests.Contains(chestId);

        public static TreasureChest Create(string id, Vector2 position, Transform parent, bool hd = false)
        {
            var go = new GameObject("Chest_" + id);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var chest = go.AddComponent<TreasureChest>();
            chest.chestId = id;
            chest.hd = hd;
            chest.sr = go.AddComponent<SpriteRenderer>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.85f, 0.5f);
            col.offset = new Vector2(0f, 0.25f);
            go.AddComponent<YSort>().Configure(true);
            chest.ConfigureShape(new Vector2(0f, 0.2f), 0.2f, new Vector2(0f, 1.2f));
            chest.Refresh();
            return chest;
        }

        void Refresh() => sr.sprite = Game.Art.Get(hd ? (CanInteract ? "town_chest_0" : "town_chest_1") : CanInteract ? "chest_closed" : "chest_open");

        public override void Interact(PlayerController player)
        {
            if (!CanInteract) return;
            Game.Session.OpenedChests.Add(chestId);
            Refresh();
            var at = (Vector2)transform.position + new Vector2(0f, 0.3f);
            // [SERVER] Online the server opens it once per character and grants the reward.
            if (OnlineEconomy.On) OnlineEconomy.OpenChest(chestId, at, transform.parent, _ => { });
            // Fixed reward: the epic chest plate (any class can wear it).
            else Pickup.Create(Reward, 1, at, transform.parent);
            Game.Audio.PlaySfx("quest");
            Fx.Sparkle((Vector2)transform.position + Vector2.up * 0.5f, 5, 0.6f);
            GameEvents.RaiseToast("보물상자를 열었다!");
            Game.Flow.Autosave();
        }
    }
}
