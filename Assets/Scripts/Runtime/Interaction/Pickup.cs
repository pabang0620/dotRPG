using UnityEngine;

namespace DotRPG
{
    /// <summary>Item drop that pops out, then gets magnetised to the player and collected.</summary>
    public class Pickup : MonoBehaviour
    {
        const float MagnetRadius = 2.2f;
        const float CollectRadius = 0.35f;

        string itemId;
        int amount;
        SpriteRenderer sr;
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
            // scaling material like the rest of the 32px art — otherwise they shimmer on the ground.
            if (pickup.sr.sprite != null && pickup.sr.sprite.pixelsPerUnit > 16.5f && FxMaterials.Sharp != null)
                pickup.sr.sharedMaterial = FxMaterials.Sharp;
            pickup.ground = position;
            pickup.velocity = Random.insideUnitCircle.normalized * Random.Range(0.8f, 1.8f);
            pickup.upSpeed = Random.Range(3.5f, 5f);
            pickup.height = 0.2f;
            pickup.Apply();
            return pickup;
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
            if (player != null && !player.IsDead && age > 0.45f && Game.IsPlaying)
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

        void Collect()
        {
            Game.Session.Inventory.Add(itemId, amount);
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
        }
    }
}
