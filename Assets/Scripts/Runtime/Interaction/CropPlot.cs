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

        public override string Prompt => "당근 뽑기";
        public override bool CanInteract => grown;

        public static CropPlot Create(Vector2 position, Transform parent, GameConfig config)
        {
            var go = new GameObject("Carrot");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var crop = go.AddComponent<CropPlot>();
            crop.sr = go.AddComponent<SpriteRenderer>();
            crop.sr.sprite = Game.Art.Get("crop_carrot");
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
            sr.sprite = Game.Art.Get("crop_hole");
            Game.Audio.PlaySfx("pluck");
            Fx.Burst("fx_leaf", (Vector2)transform.position + new Vector2(0f, 0.3f), 3, 1.5f, 0.5f);
            Pickup.Create(ItemIds.Carrot, 1, (Vector2)transform.position + new Vector2(0f, 0.2f), transform.parent);
        }

        void Update()
        {
            if (grown) return;
            if (Time.time >= regrowAt)
            {
                grown = true;
                sr.sprite = Game.Art.Get("crop_carrot");
            }
            else if (Time.time >= regrowAt - regrowSeconds * 0.5f && sr.sprite != Game.Art.Get("crop_sprout"))
            {
                sr.sprite = Game.Art.Get("crop_sprout");
            }
        }
    }
}
