using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        void SpawnObject(char c, int x, int y, List<Vector2> skeletonSpawns, System.Random rng)
        {
            if (SpawnDungeonSymbol(c, x, y)) return; // [DUNGEON] '@' doors and 1-9 spawn groups in dungeon rooms
            if (CanyonHd && SpawnCanyonHdObject(c, x, y, rng)) return;
            if (WinterHd && SpawnWinterHdObject(c, x, y, rng)) return;
            if (Winter && !WinterHd && SpawnWinterObject(c, x, y, rng)) return;
            if (Hd && SpawnTownObject(c, x, y, rng)) return;
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            switch (c)
            {
                case 'T': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config); break;
                case 'O': ResourceNode.Create(ResourceKind.Tree, foot, objectsRoot, config, true); break;
                case 'R': ResourceNode.Create(ResourceKind.Rock, foot, objectsRoot, config); break;
                case 'S': StaticProp("Stump", "stump", foot, new Vector2(0.8f, 0.5f), new Vector2(0f, 0.2f)); break;
                case 'B': StaticProp("Bush", "bush", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f)); break;
                case 'F': StaticProp("Fence", $"fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.5f), new Vector2(0f, 0.3f)); break;
                case 'f': Decoration($"deco_flower_{rng.Next(0, 2)}", center + new Vector2(rng.Next(-3, 4) / 16f, -0.3f)); break;
                case 'r': Decoration("deco_pebble", center + new Vector2(rng.Next(-3, 4) / 16f, -0.2f)); break;
                case 't': Decoration("deco_tuft", center); break;
                case 'C': CropPlot.Create(foot, objectsRoot, config); break;
                case 'c':
                    StaticProp("CarrotCrate", "crate", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f));
                    break;
                case 'x':
                    StaticProp("Box", "box", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f));
                    break;
                case 'b':
                    StaticProp("Barrel", "barrel", foot, new Vector2(0.7f, 0.5f), new Vector2(0f, 0.25f));
                    break;
                case 'I':
                {
                    // Inn landmark: anchor = bottom-left of a 5x4 footprint.
                    var pos = new Vector2(x + 2.5f, y + 0.1f);
                    var inn = StaticProp("Inn", "inn", pos, new Vector2(4.6f, 2.3f), new Vector2(0f, 1.3f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(inn.transform, false);
                    door.Setup("여관 둘러보기", "canyon_inn");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.3f, new Vector2(0f, 1.6f));
                    PointsOfInterest.Add(pos + Vector2.up * 1.5f);
                    break;
                }
                case 'M':
                    StaticProp("Stall", "stall", new Vector2(x + 1.5f, y + 0.1f), new Vector2(2.7f, 0.9f), new Vector2(0f, 0.5f));
                    break;
                case 'o':
                    StaticProp("Well", "well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.6f, 1.0f), new Vector2(0f, 0.55f));
                    break;
                case 'e':
                    StaticProp("Bench", "bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.25f));
                    break;
                case '$':
                    TreasureChest.Create($"{MapId}:{x}:{y}", foot, objectsRoot);
                    break;
                case '&':
                    Anvil.Create(foot, objectsRoot);
                    PointsOfInterest.Add(center);
                    break;
                case 'A':
                {
                    var gate = StaticProp("Gate", "gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "canyon_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 2.6f));
                    break;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    break;
                }
                case 'H':
                case 'G':
                {
                    // Anchor is the bottom-left cell of a 3x3 footprint.
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    string sprite = c == 'G' ? "house_green" : Canyon ? "house_red" : "house";
                    var house = StaticProp("House", sprite, pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(house.transform, false);
                    door.Setup("문 두드리기", Canyon ? "canyon_house_door" : "house_door");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.2f, new Vector2(0f, 1.4f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    break;
                }
                case 'X':
                {
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    ConstructionSite.Create(pos, objectsRoot);
                    PointsOfInterest.Add(pos + Vector2.up);
                    break;
                }
                case '>':
                case '<':
                case '[':
                case ']':
                {
                    string target = WorldRoutes.Target(map,c);
                    if (string.IsNullOrEmpty(target) || !MapRegistry.Exists(target))
                    {
                        Debug.LogWarning($"[dotRPG] Portal '{c}' on map '{MapId}' has no target map.");
                        break;
                    }
                    MapPortal.Create(center, target, objectsRoot);
                    if (!portalCells.TryGetValue(target, out var list)) portalCells[target] = list = new List<Vector2>();
                    list.Add(center);
                    // One arrow marker per portal group (on its first cell in reading order).
                    if (At(x - 1, y) != c && At(x, y + 1) != c) Decoration(PortalArrowKey(x, y), center, -19990);
                    break;
                }
                case 'P':
                    PlayerSpawn = center;
                    break;
                case 'k':
                    skeletonSpawns.Add(center);
                    break;
                default:
                    if ((c >= '1' && c <= '9') || (c >= 'A' && c <= 'Z'))
                    {
                        var def = config.GetNpcBySymbol(c) ?? FindDefaultNpc(c);
                        if (def != null && StoryCast.VillagersHidden && MapId == MapRegistry.Village) def = null; // [STORY] attack night
                        else if (def != null)
                        {
                            if (MapRegistry.IsTown(MapId) && MapRegistry.IsIndoorService(def.service)) break;
                            NpcController.Create(def, center, objectsRoot);
                            PointsOfInterest.Add(center);
                        }
                        else if (c <= '9') Debug.LogWarning($"[dotRPG] Map uses NPC symbol '{c}' but no NpcDefinition has it.");
                    }
                    break;
            }
        }

        static List<NpcDefinition> defaultNpcs;

        /// <summary>NPCs added after GameConfig.asset was saved are still found via the code defaults.</summary>
        static NpcDefinition FindDefaultNpc(char symbol)
        {
            if (defaultNpcs == null) defaultNpcs = GameConfig.DefaultNpcs();
            foreach (var npc in defaultNpcs)
                if (!string.IsNullOrEmpty(npc.mapSymbol) && npc.mapSymbol[0] == symbol) return npc;
            return null;
        }

        /// <summary>The NPC that runs a town service (for shop doors that open the same window).</summary>
        public static NpcDefinition ServiceNpc(NpcService service)
        {
            if (Game.Config != null)
                foreach (var npc in Game.Config.npcs) if (npc.service == service) return npc;
            if (defaultNpcs == null) defaultNpcs = GameConfig.DefaultNpcs();
            foreach (var npc in defaultNpcs) if (npc.service == service) return npc;
            return null;
        }

        string PortalArrowKey(int x, int y)
        {
            float left = x, right = width - 1 - x, bottom = y, top = height - 1 - y;
            float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            if (min == right) return "arrow_right";
            if (min == left) return "arrow_left";
            if (min == top) return "arrow_up";
            return "arrow_down";
        }

        int FenceMask(int x, int y)
        {
            int mask = 0;
            if (At(x - 1, y) == 'F') mask |= 1;
            if (At(x + 1, y) == 'F') mask |= 2;
            if (At(x, y + 1) == 'F') mask |= 4;
            if (At(x, y - 1) == 'F') mask |= 8;
            return mask;
        }

        string SignDialogueFor(int x, int y)
        {
            // Signs are identified by their order on the map (top-left first) so text stays in JSON.
            // The first map keeps the original ids (sign_0...), other maps are prefixed: canyon_sign_0...
            string prefix = MapId == MapRegistry.Village ? "sign_" : MapId + "_sign_";
            int index = 0;
            for (int yy = height - 1; yy >= 0; yy--)
                for (int xx = 0; xx < width; xx++)
                {
                    if (cells[xx, yy] != 'n') continue;
                    if (xx == x && yy == y) return prefix + index;
                    index++;
                }
            return prefix + "0";
        }

        GameObject StaticProp(string name, string spriteKey, Vector2 position, Vector2 colliderSize, Vector2 colliderOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get(NatureKey(spriteKey, position, name == "EdgeTree"));
            if (colliderSize != Vector2.zero)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = colliderSize;
                col.offset = colliderOffset;
            }
            go.AddComponent<YSort>().Configure(true);
            return go;
        }

        void Decoration(string spriteKey, Vector2 position, int order = -20000)
        {
            var go = new GameObject(spriteKey);
            go.transform.SetParent(objectsRoot, false);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get(NatureKey(spriteKey, position));
            sr.sortingOrder = order; // always under characters
        }
    }
}
