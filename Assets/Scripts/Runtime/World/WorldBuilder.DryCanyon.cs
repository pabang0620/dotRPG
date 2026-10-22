using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 메말라 가는 협곡 (MapTheme.DryCanyon): the canyon ground art graded dry and violet, then cracked earth, black stone
    /// shards, dead trees and (in 공허의 틈) the field void rifts. Generated ART_LIST sprites are used when present
    /// (dry_crack_*, dry_dead_tree_*, dry_stone_*, rift_deco_*, grey_tent / grey_flag); until then the same spots get small
    /// code-drawn shapes (the rift decorations and Grey Hand camp pieces are skipped). Each field leans on its own ground
    /// variant (0 갈라진 땅, 1 검은 돌 잔해, 2 보랏빛 균열).
    /// </summary>
    public partial class WorldBuilder
    {
        static readonly Color DryGroundTint = new Color(.86f, .78f, .84f);
        static readonly Color DryPropTint = new Color(.9f, .84f, .92f);

        void DressDryCanyon()
        {
            if (map == null || map.theme != MapTheme.DryCanyon) return;
            var painted = transform.Find("PaintedGround");
            if (painted != null)
                foreach (var sr in painted.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = DryGroundTint;
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sharedMaterial != FxMaterials.Additive && sr.GetComponentInParent<EnemyController>() == null) sr.color = sr.color * DryPropTint;

            var zone = HuntingGrounds.Get(MapId);
            int variant = zone != null ? Mathf.Clamp(zone.variant, 0, 2) : 0;
            var rng = new System.Random(4021 + variant * 131);
            var used = new List<Vector2>();
            for (int tries = 0; tries < 900 && used.Count < 70; tries++)
            {
                int x = rng.Next(3, width - 3), y = rng.Next(3, height - 3);
                var p = new Vector2(x + .5f, y + .5f);
                if (!IsFree(p) || Vector2.Distance(p, PlayerSpawn) < 3f) continue;
                bool crowded = false;
                foreach (var q in used) if ((q - p).sqrMagnitude < 4f) { crowded = true; break; }
                if (crowded) continue;
                used.Add(p);
                int kind = rng.Next(10);
                if (map.instanced && kind >= 6) continue; // [CH3] raid rooms: ground cracks only, nothing standing in the fight
                if (kind < 6) Decal(p, DryPiece("dry_crack", rng.Next(10) < 7 ? variant : rng.Next(3), Crack), -19500);
                else if (kind < 8) Upright(p, DryPiece("dry_stone", rng.Next(3), Shard), "DryStone", .5f);
                else if (kind < 9 || variant == 0) Upright(p, DryPiece("dry_dead_tree", rng.Next(2), DeadTree), "DryTree", .35f);
                else if (variant == 1 || rng.Next(3) == 0) GreyCamp(p, rng); // the Grey Hand scout 검은 수호석 잔해 and 공허의 틈
                else RiftDeco(p, rng);
            }
            if (MapId == MapRegistry.DryRift) FieldRift.Create(objectsRoot, RiftSpots());
        }

        IEnumerable<Vector2> RiftSpots()
        {
            var list = new List<Vector2>();
            foreach (Transform t in objectsRoot)
            {
                var spawner = t.GetComponent<EnemySpawner>();
                if (spawner != null) list.AddRange(spawner.Points);
            }
            return list;
        }

        /// <summary>A standing piece (stone, dead tree, tent, flag): sorted by its feet, with a small trunk collider.</summary>
        void Upright(Vector2 at, Sprite sprite, string name, float trunk)
        {
            if (sprite == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = at;
            go.AddComponent<SpriteRenderer>().sprite = sprite;
            if (trunk > 0f)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(trunk, .3f);
                col.offset = new Vector2(0f, .15f);
            }
            go.AddComponent<YSort>().Configure(true);
        }

        /// <summary>공허의 틈: a violet crack on the ground or a void crystal (generated art only).</summary>
        void RiftDeco(Vector2 at, System.Random rng)
        {
            if (rng.Next(2) == 0) Decal(at, Game.Art.Optional("rift_deco_0"), -19450);
            else Upright(at, Game.Art.Optional("rift_deco_1"), "RiftCrystal", 0f);
        }

        /// <summary>A Grey Hand tent or banner (generated art only).</summary>
        void GreyCamp(Vector2 at, System.Random rng)
        {
            if (rng.Next(3) == 0) Upright(at, Game.Art.Optional("grey_tent"), "GreyTent", 1.2f);
            else Upright(at, Game.Art.Optional("grey_flag"), "GreyFlag", .2f);
        }

        void Decal(Vector2 at, Sprite sprite, int order)
        {
            if (sprite == null) return;
            var go = new GameObject("DryDecal");
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
        }

        static readonly Dictionary<string, Sprite> dryArt = new Dictionary<string, Sprite>();

        static Sprite DryPiece(string id, int variant, System.Func<int, PixelCanvas> draw)
        {
            string key = id + "_" + variant;
            if (dryArt.TryGetValue(key, out var cached)) return cached;
            var sprite = Game.Art.Optional(key);
            if (sprite == null)
            {
                var p = draw(variant);
                var t = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false) { name = key + "_code", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels32(p.ToTexturePixels());
                t.Apply();
                sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .2f), 32, 0, SpriteMeshType.FullRect);
            }
            return dryArt[key] = sprite;
        }

        static PixelCanvas Crack(int v)
        {
            var p = new PixelCanvas(32, 20);
            var c = new Color32(58, 40, 62, 200);
            int x = 2, y = 8 + v * 2;
            var r = new System.Random(77 + v);
            while (x < 30)
            {
                p.Set(x, y, c);
                if (r.Next(3) == 0) p.Set(x, y + 1, c);
                x++;
                y = Mathf.Clamp(y + r.Next(-1, 2), 2, 17);
                if (r.Next(6) == 0) { int bx = x, by = y; for (int k = 0; k < 4; k++) p.Set(bx, by + k * (v == 1 ? 1 : -1), c); }
            }
            return p;
        }

        static PixelCanvas Shard(int v)
        {
            var p = new PixelCanvas(20, 18);
            Color32 dark = new Color32(22, 20, 30, 255), mid = new Color32(46, 42, 58, 255), lit = new Color32(96, 82, 120, 255);
            int h = 10 + v * 2;
            for (int y = 2; y < h; y++)
            {
                int half = Mathf.Max(1, (h - y) / 2);
                for (int x = 10 - half; x <= 10 + half; x++) p.Set(x, y, x == 10 - half ? lit : x > 10 ? dark : mid);
            }
            p.HLine(4, 15, 1, new Color32(30, 24, 36, 160));
            return p;
        }

        static PixelCanvas DeadTree(int v)
        {
            var p = new PixelCanvas(24, 40);
            Color32 bark = new Color32(70, 56, 58, 255), dark = new Color32(42, 32, 38, 255);
            for (int y = 2; y < 26; y++) { p.Set(11, y, bark); p.Set(12, y, dark); }
            for (int k = 0; k < 8; k++) { p.Set(11 - k, 18 + k, bark); p.Set(13 + k, 22 + k / 2, dark); }
            if (v == 1) for (int k = 0; k < 6; k++) p.Set(12 + k, 12 + k, bark);
            p.HLine(8, 15, 1, new Color32(30, 24, 30, 140));
            return p;
        }
    }
}
