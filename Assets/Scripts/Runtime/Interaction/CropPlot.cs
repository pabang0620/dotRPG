using UnityEngine;

namespace DotRPG
{
    /// <summary>A carrot that can be pulled for a healing item, then regrows.</summary>
    public class CropPlot : Interactable
    {
        SpriteRenderer sr;
        bool grown = true;
        float regrowAt;
        float regrowSeconds;
        string grownKey = "crop_carrot", sproutKey = "crop_sprout", holeKey = "crop_hole";
        /// <summary>[SERVER] "{map}:{x}:{y}" grid id, the same as maps.json nodes.</summary>
        public string NodeId { get; private set; }
        public static readonly System.Collections.Generic.List<CropPlot> Active = new System.Collections.Generic.List<CropPlot>();

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        /// <summary>[SERVER] Starts pulled: the server says it is still regrowing for this character.</summary>
        public void StartRegrowing()
        {
            if (!grown) return;
            grown = false;
            regrowAt = Time.time + regrowSeconds;
            sr.sprite = Game.Art.Get(holeKey);
        }

        public override string Prompt => "당근 뽑기";
        public override bool CanInteract => grown;

        public static CropPlot Create(Vector2 position, Transform parent, GameConfig config, bool hd = false)
        {
            var go = new GameObject("Carrot");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var crop = go.AddComponent<CropPlot>();
            crop.NodeId = $"{(Game.World != null ? Game.World.MapId : "")}:{Mathf.FloorToInt(position.x)}:{Mathf.FloorToInt(position.y)}";
            if (hd) { crop.grownKey = "town_crop_0"; crop.sproutKey = "town_crop_1"; crop.holeKey = "town_crop_2"; }
            crop.sr = go.AddComponent<SpriteRenderer>();
            crop.sr.sprite = Game.Art.Get(crop.grownKey);
            crop.regrowSeconds = config.cropRegrowSeconds;
            crop.ConfigureShape(new Vector2(0f, 0.1f), 0f, new Vector2(0f, 1.1f));
            go.AddComponent<YSort>().Configure(true);
            return crop;
        }

        public override void Interact(PlayerController player)
        {
            if (!grown) return;
            grown = false;
            regrowAt = Time.time + regrowSeconds;
            sr.sprite = Game.Art.Get(holeKey);
            Game.Audio.PlaySfx("pluck");
            Fx.Burst("fx_leaf", (Vector2)transform.position + new Vector2(0f, 0.3f), 3, 1.5f, 0.5f);
            var at = (Vector2)transform.position + new Vector2(0f, 0.2f);
            if (OnlineEconomy.On) OnlineEconomy.Gather(NodeId, at, transform.parent); // [SERVER]
            else Pickup.Create(ItemIds.Carrot, 1, at, transform.parent);
        }

        void Update()
        {
            if (grown) return;
            if (Time.time >= regrowAt)
            {
                grown = true;
                sr.sprite = Game.Art.Get(grownKey);
            }
            else if (Time.time >= regrowAt - regrowSeconds * 0.5f && sr.sprite != Game.Art.Get(sproutKey))
            {
                sr.sprite = Game.Art.Get(sproutKey);
            }
        }
    }
}
