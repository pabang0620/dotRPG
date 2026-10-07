using UnityEngine;

namespace DotRPG
{
    /// <summary>Item drop that pops out, then gets magnetised to the player and collected.</summary>
    public class Pickup : MonoBehaviour
    {
        const float MagnetRadius = 3.2f; // 2.2 -> 3.2: more monsters per field, less walking to every drop
        const float CollectRadius = 0.35f;

        static readonly System.Collections.Generic.List<Pickup> Active = new System.Collections.Generic.List<Pickup>();

        string itemId;
        int amount;
        /// <summary>[SERVER] Drop rolled by the server: collecting claims it by id instead of adding locally.</summary>
        public string DropId;
        /// <summary>[SERVER] Already granted by the server (gather, chest): collecting only shows the toast.</summary>
        public bool ServerGranted;
        SpriteRenderer sr;
        SpriteRenderer glow; // rare and better gear: a soft pulsing light in its rarity colour
        Color glowColor;
        Vector2 ground;
        Vector2 velocity;
        float height;
        float upSpeed;
        float age;
        float magnetSpeed;

        public static Pickup Create(string itemId, int amount, Vector2 position, Transform parent)
        {
            var go = new GameObject("Pickup_" + itemId);
            go.transform.SetParent(parent, false);
            var pickup = go.AddComponent<Pickup>();
            pickup.itemId = itemId;
            pickup.amount = amount;
            pickup.sr = go.AddComponent<SpriteRenderer>();
            pickup.sr.sprite = Game.Art.Get(Game.Config.GetItem(itemId).iconKey);
            // High-resolution (density-2) icons are bilinear-filtered, so draw them through the sharp
            // scaling material like the rest of the 32px art - otherwise they shimmer on the ground.
            if (pickup.sr.sprite != null && pickup.sr.sprite.pixelsPerUnit > 16.5f && FxMaterials.Sharp != null)
                pickup.sr.sharedMaterial = FxMaterials.Sharp;
            var gear = EquipmentDatabase.Get(itemId);
            if (gear != null && gear.rarity >= ItemRarity.Rare)
            {
                var g = new GameObject("Glow");
                g.transform.SetParent(go.transform, false);
                g.transform.localScale = Vector3.one * (gear.rarity >= ItemRarity.Unique ? 1.5f : gear.rarity == ItemRarity.Epic ? 1.25f : 1.05f);
                pickup.glow = g.AddComponent<SpriteRenderer>();
                pickup.glow.sprite = Game.Art.Get("fx_glow");
                if (FxMaterials.Additive != null) pickup.glow.sharedMaterial = FxMaterials.Additive;
                pickup.glowColor = EquipmentDatabase.RarityTint(gear.rarity);
            }
            pickup.ground = position;
            pickup.velocity = Random.insideUnitCircle.normalized * Random.Range(0.8f, 1.8f);
            pickup.upSpeed = Random.Range(3.5f, 5f);
            pickup.height = 0.2f;
            pickup.Apply();
            return pickup;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>Loot filter (설정 > 줍기): this kind is left on the ground. Gold and rare-or-better gear are always taken.</summary>
        public bool Skipped
        {
            get
            {
                var d = Game.Settings?.Data;
                if (d == null || itemId == ConsumableDatabase.Gold) return false;
                var gear = EquipmentDatabase.Get(itemId);
                if (gear != null)
                {
                    if (EquipmentDatabase.LevelOfKey(itemId) > 0) return false;
                    return (gear.rarity == ItemRarity.Common && d.skipCommonGear) || (gear.rarity == ItemRarity.Uncommon && d.skipUncommonGear);
                }
                var c = ConsumableDatabase.Get(itemId);
                if (c != null) return ConsumableDatabase.IsUsable(itemId) && d.skipConsumables;
                return d.skipMaterials;
            }
        }

        /// <summary>Dungeon cleared: everything still on the floor goes into the bag (no walking round to pick it up), except what the loot filter skips.</summary>
        public static void CollectAll()
        {
            int n = 0;
            foreach (var p in Active.ToArray())
                if (p != null && !p.Skipped) { p.Collect(true); n++; }
            if (n > 0) GameEvents.RaiseToast($"바닥에 남은 아이템 {n}개를 챙겼습니다.");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;

            // Hop out of the source.
            if (upSpeed > 0f || height > 0f)
            {
                upSpeed -= 16f * dt;
                height = Mathf.Max(0f, height + upSpeed * dt);
                ground += velocity * dt;
                if (height <= 0f) upSpeed = 0f;
            }

            var player = Game.Player;
            if (player != null && !player.IsDead && age > 0.45f && Game.IsPlaying && !Skipped)
            {
                Vector2 target = player.Position + new Vector2(0f, 0.3f);
                float distance = Vector2.Distance(ground, target);
                if (distance < MagnetRadius)
                {
                    magnetSpeed = Mathf.Min(magnetSpeed + 20f * dt, 10f);
                    ground = Vector2.MoveTowards(ground, target, magnetSpeed * dt);
                    if (distance < CollectRadius)
                    {
                        Collect();
                        return;
                    }
                }
                else magnetSpeed = 0f;
            }
            Apply();
        }

        void Collect(bool quiet = false)
        {
            if (DropId != null) OnlineEconomy.ClaimDrop(DropId);
            // Online, anything else on the ground is a visual only: the server already decided what the bag holds.
            else if (!ServerGranted && !OnlineEconomy.On) Game.Session.Inventory.Add(itemId, amount);
            if (quiet) { Destroy(gameObject); return; }
            Game.Audio.PlaySfx("pickup");
            var gear = EquipmentDatabase.Get(itemId);
            if (itemId == ConsumableDatabase.Gold)
                GameEvents.RaiseToast($"<color=#ffd84a>+{amount} 골드</color>");
            else if (gear != null)
            {
                int level = EquipmentDatabase.LevelOfKey(itemId);
                GameEvents.RaiseToast($"장비 획득: <color={EquipmentDatabase.RarityColor(gear.rarity)}>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.NameAt(level)}</color>  ({gear.StatLine(level)})  [{Game.Input.GetBindingLabel(GameAction.Inventory)}] 가방");
            }
            else
                GameEvents.RaiseToast($"+{amount} {Game.Config.GetItem(itemId).displayName}");
            Destroy(gameObject);
        }

        void Apply()
        {
            float bob = height <= 0f ? Mathf.Sin(age * 5f) * 0.05f + 0.05f : 0f;
            transform.position = new Vector3(ground.x, ground.y + height + bob, 0f);
            sr.sortingOrder = YSort.OrderFor(ground.y) + 2;
            if (glow != null)
            {
                float a = 0.38f + 0.22f * Mathf.Sin(age * 3.2f);
                glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, a);
                glow.sortingOrder = sr.sortingOrder - 1;
            }
            sr.color = Skipped ? new Color(1f, 1f, 1f, 0.55f) : Color.white; // filtered: shown faded, left on the ground
        }
    }
}
