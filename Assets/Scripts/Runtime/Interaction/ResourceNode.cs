using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public enum ResourceKind
    {
        Tree,
        Rock,
    }

    /// <summary>
    /// A tree or rock that the player hits with the sword to collect materials. Trees leave a stump
    /// and regrow; rocks vanish and respawn later.
    /// </summary>
    public class ResourceNode : MonoBehaviour, IDamageable
    {
        ResourceKind kind;
        int maxHealth;
        int health;
        string dropItem;
        int dropAmount;
        float respawnSeconds;
        string fullSpriteKey;
        string stumpKey = "stump";
        float fxHeight = 1f;

        SpriteRenderer spriteRenderer;
        CircleCollider2D circle;
        Transform visual;
        float shakeUntil;
        bool depleted;

        /// <param name="hd">Use the 32px town/forest art ("town_chop", "town_rock_*", "town_stump").</param>
        public static ResourceNode Create(ResourceKind kind, Vector2 position, Transform parent, GameConfig config, bool fruit = false, bool hd = false, bool villageNature = false)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var sr = visual.gameObject.AddComponent<SpriteRenderer>();

            var node = go.AddComponent<ResourceNode>();
            node.kind = kind;
            node.visual = visual;
            node.spriteRenderer = sr;
            node.circle = go.AddComponent<CircleCollider2D>();
            if (kind == ResourceKind.Tree)
            {
                node.maxHealth = config.treeHealth;
                node.dropItem = ItemIds.Wood;
                node.dropAmount = config.treeWoodDrop;
                node.respawnSeconds = config.treeRegrowSeconds;
                node.fullSpriteKey = hd ? "town_chop" : fruit ? "tree_fruit" : "tree";
                node.stumpKey = hd ? "town_stump" : "stump";
                node.fxHeight = hd ? 1.9f : 1f;
                node.circle.radius = hd ? 0.36f : 0.4f;
                node.circle.offset = new Vector2(0f, 0.15f);
            }
            else
            {
                node.maxHealth = config.rockHealth;
                node.dropItem = ItemIds.Stone;
                node.dropAmount = config.rockStoneDrop;
                node.respawnSeconds = config.rockRespawnSeconds;
                node.fullSpriteKey = hd ? $"town_rock_{Mathf.Abs(Mathf.RoundToInt(position.x * 3f + position.y)) % 2}" : "rock";
                node.circle.radius = hd ? 0.5f : 0.42f;
                node.circle.offset = new Vector2(0f, hd ? 0.3f : 0.3f);
            }
            if (villageNature) node.fullSpriteKey = VillageNatureArt.Resolve(node.fullSpriteKey);
            node.health = node.maxHealth;
            sr.sprite = Game.Art.Get(node.fullSpriteKey);
            go.AddComponent<YSort>().Configure(true);
            return node;
        }

        public bool TakeDamage(DamageInfo info)
        {
            if (depleted || info.team != Team.Player) return false;
            health -= Mathf.Max(1, info.amount);
            shakeUntil = Time.time + 0.18f;
            Vector2 center = (Vector2)transform.position + new Vector2(0f, kind == ResourceKind.Tree ? 0.8f * fxHeight : 0.35f);
            if (kind == ResourceKind.Tree)
            {
                Game.Audio.PlaySfx("chop");
                Fx.Burst("fx_leaf", center + new Vector2(0f, 0.6f * fxHeight), 4, 2.5f, 0.8f);
            }
            else
            {
                Game.Audio.PlaySfx("mine");
                Fx.Burst("fx_chip", center, 4, 3f, 0.6f);
            }
            if (health <= 0) Deplete();
            return true;
        }

        void Deplete()
        {
            depleted = true;
            Vector2 dropOrigin = (Vector2)transform.position + new Vector2(0f, 0.3f);
            for (int i = 0; i < dropAmount; i++) Pickup.Create(dropItem, 1, dropOrigin, transform.parent);

            if (kind == ResourceKind.Tree)
            {
                Game.Audio.PlaySfx("tree_fall");
                spriteRenderer.sprite = Game.Art.Get(stumpKey);
                circle.radius = 0.35f;
                circle.offset = new Vector2(0f, 0.15f);
                Fx.Burst("fx_leaf", (Vector2)transform.position + new Vector2(0f, 1.2f * fxHeight), 10, 3.5f, 1f);
            }
            else
            {
                Game.Audio.PlaySfx("rock_break");
                spriteRenderer.enabled = false;
                circle.enabled = false;
                Fx.Burst("fx_chip", (Vector2)transform.position + new Vector2(0f, 0.3f), 8, 3.5f, 0.8f);
            }
            GetComponent<YSort>().Refresh();
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnSeconds);
            // Never pop back on top of the player.
            while (Game.Player != null && Vector2.Distance(Game.Player.Position, transform.position) < 1.5f)
                yield return new WaitForSeconds(1f);

            depleted = false;
            health = maxHealth;
            spriteRenderer.enabled = true;
            spriteRenderer.sprite = Game.Art.Get(fullSpriteKey);
            circle.enabled = true;
            if (kind == ResourceKind.Tree)
            {
                circle.radius = fxHeight > 1f ? 0.36f : 0.4f;
                circle.offset = new Vector2(0f, 0.15f);
            }
            GetComponent<YSort>().Refresh();

            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                float s = Mathf.Lerp(0.6f, 1f, t / 0.3f);
                visual.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            visual.localScale = Vector3.one;
        }

        void Update()
        {
            if (Time.time < shakeUntil)
                visual.localPosition = new Vector3(Mathf.Sin(Time.time * 70f) * 0.06f, 0f, 0f);
            else if (visual.localPosition != Vector3.zero)
                visual.localPosition = Vector3.zero;
        }
    }
}
