using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // ================= High-resolution town & forest =================

        static Tile colliderTile;

        /// <summary>An invisible, solid grid cell (the painted ground shows what is there).</summary>
        static Tile ColliderTile()
        {
            if (colliderTile != null) return colliderTile;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Blank", filterMode = FilterMode.Point };
            var clear = new Color32[16];
            tex.SetPixels32(clear);
            tex.Apply(false, true);
            colliderTile = ScriptableObject.CreateInstance<Tile>();
            colliderTile.name = "Solid";
            colliderTile.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            colliderTile.colliderType = Tile.ColliderType.Grid;
            return colliderTile;
        }

        /// <summary>Water and dense forest block movement; everything else is drawn by the painted ground.</summary>
        void PaintHdCollision(Vector3Int cell)
        {
            char g = GroundAt(cell.x, cell.y);
            if (g == '~') waterMap.SetTile(cell, ColliderTile());
            else if (g == '%') cliffMap.SetTile(cell, ColliderTile());
        }

        /// <summary>The painted ground of each high-resolution map, made once and reused on every visit.</summary>
        static readonly Dictionary<string, List<(Vector2 pos, Sprite sprite)>> paintedGround = new Dictionary<string, List<(Vector2, Sprite)>>();
        static readonly Dictionary<string, WaterField> waterFields = new Dictionary<string, WaterField>();

        /// <summary>Forgets the painted ground (developer timing tests). The current map keeps its sprites until rebuilt.</summary>
        public static void ClearPaintedGroundCache() { paintedGround.Clear(); waterFields.Clear(); }

        Color32[] PaintTownGround(char[,] ground, int w, int h, out int pw, out int ph) => TownTerrain.Paint(ground, w, h, ForestMap, out pw, out ph, StoreWaterField);
        void StoreWaterField(WaterField field) => waterFields[MapId] = field;

        /// <summary>
        /// Paints the map's ground once with <paramref name="paint"/> (32 px per tile, cached per map id) and
        /// lays it out as 256px chunk sprites under everything else.
        /// </summary>
        void BuildPaintedGround(GroundPainter paint)
        {
            if (!paintedGround.TryGetValue(MapId, out var chunks))
            {
                var ground = new char[width, height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        ground[x, y] = DungeonPaintGround(x, y, GroundAt(x, y)); // [DGNTERRAIN] spawn marks inside walls look like rock
                var px = paint(ground, width, height, out int pw, out int ph);
                chunks = new List<(Vector2, Sprite)>();
                const int Chunk = 256;
                for (int cy = 0; cy < ph; cy += Chunk)
                    for (int cx = 0; cx < pw; cx += Chunk)
                    {
                        int w = Mathf.Min(Chunk, pw - cx), h = Mathf.Min(Chunk, ph - cy);
                        var block = new Color32[w * h];
                        for (int y = 0; y < h; y++) System.Array.Copy(px, (cy + y) * pw + cx, block, y * w, w);
                        var sprite = Game.Art.CreateRaw($"ground_{MapId}_{cx}_{cy}", block, w, h, 2);
                        chunks.Add((new Vector2(cx / (float)TownTerrain.Px, cy / (float)TownTerrain.Px), sprite));
                    }
                paintedGround[MapId] = chunks;
            }
            var root = new GameObject("PaintedGround").transform;
            root.SetParent(transform, false);
            foreach (var (pos, sprite) in chunks)
            {
                var go = new GameObject("Ground");
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = -30000;
                if (FxMaterials.Sharp != null) sr.sharedMaterial = FxMaterials.Sharp;
            }
            if (waterFields.TryGetValue(MapId, out var water)) LivingWater.Create(root, water, MapId);
        }

        /// <summary>
        /// Trees standing along the edge of every dense-forest area, so the painted canopy gets real
        /// trunks and crowns that overlap the player correctly. No colliders: the forest cells are solid.
        /// </summary>
        void DecorateForestEdges()
        {
            var rng = new System.Random(MapId.GetHashCode() & 0x7fff);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (cells[x, y] != '%') continue;
                    int open = 0, near = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            char n = At(x + dx, y + dy);
                            if (n == '\0' || n == '%') continue;
                            int d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                            if (d == 1 && (dx == 0 || dy == 0)) open++;
                            else near++;
                        }
                    double chance = open > 0 ? 0.85 : near > 0 ? 0.4 : 0.0;
                    if (rng.NextDouble() >= chance) continue;
                    var pos = new Vector2(x + 0.5f + (float)(rng.NextDouble() - 0.5) * 0.5f, y + 0.3f + (float)(rng.NextDouble() - 0.5) * 0.3f);
                    string key = rng.NextDouble() < 0.22 ? $"town_pine_{rng.Next(0, 2)}"
                        : ForestMap ? $"town_tree_{3 + rng.Next(0, 3)}" : $"town_tree_{rng.Next(0, 3)}";
                    StaticProp("EdgeTree", key, pos, Vector2.zero, Vector2.zero);
                }
        }

        /// <summary>Gives every high-resolution sprite the crisp-scaling material (see SpriteSharp.shader).</summary>
        void ApplySharpMaterial()
        {
            var sharp = FxMaterials.Sharp;
            if (sharp == null) return;
            float basePpu = config.pixelsPerUnit + 0.5f;
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null && !VillageNatureArt.IsGenerated(sr.sprite) && !sr.sprite.name.StartsWith(WinterVillageArt.Prefix) && sr.sprite.pixelsPerUnit > basePpu && sr.sharedMaterial != FxMaterials.Additive)
                    sr.sharedMaterial = sharp;
        }

        /// <summary>
        /// Every tree turns see-through while the player stands behind it: free-standing trees, the trees
        /// along the forest edges and the choppable ones (all named "Tree" or "EdgeTree").
        /// </summary>
        void AddTreeFades()
        {
            foreach (Transform child in objectsRoot)
                if (child.name == "Tree" || child.name == "EdgeTree") TreeFade.Attach(child.gameObject);
        }

        /// <summary>Tree with a small trunk collider; its crown overlaps whatever is behind it.</summary>
        void HdTree(string sprite, Vector2 foot, float trunkWidth)
        {
            StaticProp("Tree", sprite, foot, new Vector2(trunkWidth, 0.35f), new Vector2(0f, 0.15f));
        }

        /// <summary>A building on a w x h footprint anchored at its bottom-left cell, with a door interactable.</summary>
        GameObject HdBuilding(string name, string sprite, int x, int y, int w, int h, string prompt, string dialogue, NpcService service = NpcService.None)
        {
            var pos = new Vector2(x + w * 0.5f, y + 0.1f);
            float colH = Mathf.Max(1f, h - 0.85f);
            var go = StaticProp(name, sprite, pos, new Vector2(w - 0.15f, colH), new Vector2(0f, colH * 0.5f + 0.05f));
            var renderedSprite = go.GetComponent<SpriteRenderer>().sprite;
            var buildingArt = VillageBuildingArt.FromSprite(renderedSprite);
            float entranceX = buildingArt != null ? buildingArt.Entrance.x : 0f;
            if (buildingArt != null)
            {
                // Match the wall footprint, leaving the projecting eaves and entrance steps walkable.
                var body = go.GetComponent<BoxCollider2D>();
                body.size = buildingArt.ColliderSize;
                body.offset = buildingArt.ColliderOffset;
                // Sort at the front wall rather than the bottom step, so a visitor on the stairs stays visible.
                float wallFront = body.offset.y - body.size.y * .5f;
                go.GetComponent<YSort>().Configure(true, -Mathf.RoundToInt(wallFront * YSort.OrdersPerUnit));
            }
            if (service != NpcService.None)
            {
                var door = ServiceDoor.Attach(go, service, prompt);
                door.transform.localPosition = new Vector3(entranceX, 0f, 0f);
            }
            else if (!string.IsNullOrEmpty(dialogue))
            {
                var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                door.transform.SetParent(go.transform, false);
                door.transform.localPosition = new Vector3(entranceX, 0f, 0f);
                door.Setup(prompt, dialogue);
                door.ConfigureShape(new Vector2(0f, 0f), 0.25f, new Vector2(0f, 1.5f));
            }
            PointsOfInterest.Add(pos + new Vector2(entranceX, 1.2f));
            return go;
        }

        /// <summary>Objects of the high-resolution maps. Returns false for symbols handled by the shared code (NPCs, portals, start, spawns).</summary>
        bool SpawnTownObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            Vector2 Jitter(float amount) => new Vector2((float)(rng.NextDouble() - 0.5) * amount, (float)(rng.NextDouble() - 0.5) * amount * 0.5f);
            switch (c)
            {
                case 'T': HdTree(ForestMap ? $"town_tree_{3 + rng.Next(0, 3)}" : $"town_tree_{rng.Next(0, 3)}", foot + Jitter(0.3f), 0.7f); return true;
                case 'O': HdTree($"town_fruit_{rng.Next(0, 2)}", foot + Jitter(0.2f), 0.6f); return true;
                case 'Y': HdTree($"town_pine_{rng.Next(0, 2)}", foot + Jitter(0.3f), 0.5f); return true;
                case 'q': HdTree($"town_dead_{rng.Next(0, 2)}", foot, 0.5f); return true;
                case 't': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config, false, true, MapId == MapRegistry.Village || HuntingGrounds.Get(MapId) != null); return true;
                case 'R': ResourceNode.Create(ResourceKind.Rock, foot, objectsRoot, config, false, true, MapId == MapRegistry.Village || HuntingGrounds.Get(MapId) != null); return true;
                case 'S': StaticProp("Stump", "town_stump", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'B': StaticProp("Bush", $"town_bush_{rng.Next(0, 3)}", foot, new Vector2(0.95f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'r': Decoration($"town_pebbles_{rng.Next(0, 2)}", center + new Vector2(0f, -0.25f)); return true;
                case 'F': StaticProp("Fence", $"town_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.45f), new Vector2(0f, 0.28f)); return true;
                case 'b': StaticProp("Barrel", "town_barrel", foot, new Vector2(0.7f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'x': StaticProp("Crate", "town_crate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'c': StaticProp("CarrotCrate", "town_ccrate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 's': StaticProp("Sacks", "town_sacks", foot, new Vector2(0.95f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'h': StaticProp("Hay", "town_hay", foot, new Vector2(1f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'u': StaticProp("FlowerPot", "town_pot", foot, new Vector2(0.55f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'v': StaticProp("FlowerBox", "town_planter", foot, new Vector2(0.95f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'm': StaticProp("Mailbox", "town_mailbox", foot, new Vector2(0.45f, 0.3f), new Vector2(0f, 0.15f)); return true;
                case 'z': StaticProp("Woodpile", "town_woodpile", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'e': StaticProp("Bench", "town_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.22f)); return true;
                case 'w': StaticProp("Well", "town_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.9f), new Vector2(0f, 0.5f)); PointsOfInterest.Add(center); return true;
                case 'p':
                {
                    var lamp = StaticProp("Lamp", "town_lamp", foot, new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
                    WarmGlow.Attach(lamp.transform, new Vector2(0f, 1.95f), 1.4f, 0.28f);
                    return true;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "town_sign_0", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.5f));
                    return true;
                }
                case 'N':
                {
                    var board = StaticProp("NoticeBoard", "town_board", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = board.AddComponent<DialogueInteractable>();
                    interact.Setup("게시판 읽기", "town_board");
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.5f, new Vector2(0f, 2.1f));
                    PointsOfInterest.Add(center);
                    return true;
                }
                case '&': if (MapRegistry.IsTown(MapId)) StaticProp("Anvil", "town_anvil", foot, new Vector2(.7f, .4f), new Vector2(0, .2f)); else Anvil.Create(foot, objectsRoot, "town_anvil"); return true;
                case 'C': CropPlot.Create(foot, objectsRoot, config, true); return true;
                case '$': TreasureChest.Create($"{MapId}:{x}:{y}", foot, objectsRoot, true); return true;
                case 'g': StaticProp("Grave", $"town_grave_{rng.Next(0, 2)}", foot, new Vector2(0.75f, 0.35f), new Vector2(0f, 0.17f)); return true;
                case 'i': StaticProp("Ruin", "town_ruin", foot, new Vector2(0.7f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'j': Decoration("town_bones", center + new Vector2(0f, -0.2f)); return true;
                case 'o': Decoration("town_shroom", center + new Vector2(0f, -0.2f)); return true;
                case 'l': StaticProp("Log", "town_log", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.45f), new Vector2(0f, 0.25f)); return true;
                case 'H':
                {
                    int style = x < 20 ? 0 : y > 30 ? 1 : 2;
                    HdBuilding("House", $"town_house_{style}", x, y, 10, 8, "문 두드리기", "town_house_door");
                    return true;
                }
                case 'I': HdBuilding("ChiefHall", "town_hall", x, y, 10, 8, "문 두드리기", "town_hall_door"); return true;
                case 'M': HdBuilding("GeneralStore", "town_store", x, y, 10, 8, "잡화점 들어가기", null, NpcService.Shop); return true;
                case 'A': HdBuilding("Smithy", "town_smithy", x, y, 10, 8, "대장간 들어가기", null, NpcService.Blacksmith); return true;
                case 'Z': HdBuilding("Warehouse", "town_warehouse", x, y, 10, 8, "창고 들어가기", null, NpcService.Storage); return true;
                case 'L':
                {
                    // [STORY] 브람의 마굿간; burned from the attack night on.
                    bool burned = Game.Session != null && Game.Session.Journal.HasFlag("stable_burned");
                    string key = burned ? "town_stable_burned" : "town_stable";
                    if (Resources.Load<Sprite>(VillageBuildingArt.Find(key).ResourcePath) == null) key = "town_warehouse"; // until the art is in
                    HdBuilding("Stable", key, x, y, 10, 8, "살펴보기", burned ? "stable_burned_look" : "stable_look");
                    return true;
                }
                case 'X':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    ConstructionSite.Create(pos, objectsRoot, true);
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'Q':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    var fountain = StaticProp("Fountain", "town_fountain_0", pos, new Vector2(2.8f, 1.1f), new Vector2(0f, 0.65f));
                    fountain.AddComponent<FountainWater>().Setup();
                    var look = fountain.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "town_fountain");
                    look.ConfigureShape(new Vector2(0f, -0.1f), 0.9f, new Vector2(0f, 2.9f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
            }
            return false;
        }
    }
}
