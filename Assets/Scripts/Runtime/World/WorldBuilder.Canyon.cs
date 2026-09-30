using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The canyon town in 32px art: painted ground (<see cref="CanyonTerrain"/>) plus "cyn_*" and reused
    /// "town_*" sprites. With <see cref="CanyonHdReady"/> true the canyon is routed through these hooks
    /// instead of the original 16px tile map (PaintGround / SpawnObject).
    ///
    /// World sizes, colliders and gameplay are unchanged from the 16px canyon: cliffs (W) and water (~)
    /// are solid; stairs (L) and the wooden bridge (d) stay walkable; every object keeps its footprint
    /// and interaction.
    /// </summary>
    public partial class WorldBuilder
    {
        /// <summary>The 32px canyon is implemented; route the canyon through the hooks below.</summary>
        bool CanyonHdReady => true;

        /// <summary>Per cell, before its object spawns: invisible colliders for the solid ground (cliffs, water).</summary>
        void PaintCanyonHdCell(Vector3Int cell, char c)
        {
            char g = GroundAt(cell.x, cell.y);
            if (g == '~') waterMap.SetTile(cell, ColliderTile());
            else if (g == 'W') cliffMap.SetTile(cell, ColliderTile());
        }

        /// <summary>After every cell is done: paint the whole canyon ground as one 32px picture.</summary>
        void BuildCanyonHd()
        {
            BuildPaintedGround(PaintCanyonGround);
        }

        Color32[] PaintCanyonGround(char[,] ground, int w, int h, out int pw, out int ph)
            => CanyonTerrain.Paint(ground, w, h, out pw, out ph);

        /// <summary>
        /// Canyon objects in 32px art. Reuses the shared "town_*" set for the pieces that fit (trees,
        /// rocks, bushes, barrel, box, sign, fences, well, bench, chest, anvil) and the canyon-specific
        /// "cyn_*" set for the inn, stall, houses and gate. Returns false for symbols the shared code
        /// handles (NPCs 1-9/A-Z NPCs, portals &lt; &gt;, P start, k spawns).
        /// </summary>
        bool SpawnCanyonHdObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            Vector2 Jitter(float amount) => new Vector2((float)(rng.NextDouble() - 0.5) * amount, (float)(rng.NextDouble() - 0.5) * amount * 0.5f);
            switch (c)
            {
                // ---- Reused town_* nature and props ----
                case 'T': HdTree($"town_tree_{rng.Next(0, 3)}", foot + Jitter(0.3f), 0.7f); return true;
                case 'O': HdTree($"town_fruit_{rng.Next(0, 2)}", foot + Jitter(0.2f), 0.6f); return true;
                case 'R': ResourceNode.Create(ResourceKind.Rock, foot, objectsRoot, config, false, true); return true;
                case 'S': StaticProp("Stump", "town_stump", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'B': StaticProp("Bush", $"town_bush_{rng.Next(0, 3)}", foot, new Vector2(0.95f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'f': Decoration($"town_pebbles_{rng.Next(0, 2)}", center + new Vector2(0f, -0.25f)); return true;
                case 'r': Decoration($"town_pebbles_{rng.Next(0, 2)}", center + new Vector2(0f, -0.25f)); return true;
                case 't': Decoration("deco_tuft", center); return true;
                case 'F': StaticProp("Railing", $"town_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.45f), new Vector2(0f, 0.28f)); return true;
                case 'b': StaticProp("Barrel", "town_barrel", foot, new Vector2(0.7f, 0.45f), new Vector2(0f, 0.22f)); return true;
                case 'x': StaticProp("Box", "town_crate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'c': StaticProp("CarrotCrate", "town_ccrate", foot, new Vector2(0.85f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'e': StaticProp("Bench", "town_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.22f)); return true;
                case 'o': StaticProp("Well", "town_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.9f), new Vector2(0f, 0.5f)); PointsOfInterest.Add(center); return true;
                case '$': TreasureChest.Create($"{MapId}:{x}:{y}", foot, objectsRoot, true); return true;
                case '&': Anvil.Create(foot, objectsRoot, "town_anvil"); PointsOfInterest.Add(center); return true;
                case 'n':
                {
                    var sign = StaticProp("Sign", "town_sign_0", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.5f));
                    return true;
                }

                // ---- Canyon-specific cyn_* buildings ----
                case 'H':
                case 'G':
                {
                    // 3x3 footprint anchored at its bottom-left cell.
                    HdBuilding("House", c == 'G' ? "cyn_house_green" : "cyn_house_red", x, y, 3, 3, "문 두드리기", "canyon_house_door");
                    return true;
                }
                case 'I':
                {
                    // Inn landmark: 5x4 footprint.
                    HdBuilding("Inn", "cyn_inn", x, y, 5, 4, "여관 둘러보기", "canyon_inn");
                    return true;
                }
                case 'M':
                {
                    // Market stall: 3 wide, anchored bottom-left; light collider like the old stall.
                    StaticProp("Stall", "cyn_stall", new Vector2(x + 1.5f, y + 0.1f), new Vector2(2.7f, 0.9f), new Vector2(0f, 0.5f));
                    return true;
                }
                case 'A':
                {
                    // North gate: 4 wide, anchored bottom-left; solid, with a "look" interaction.
                    var gate = StaticProp("Gate", "cyn_gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "canyon_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 2.6f));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Ground code of a map symbol on the 32px canyon. The shared rules already resolve the
        /// canyon terrain (flagstone, moss grass, water, cliffs, stairs, bridge) and stand objects on
        /// flagstone, so nothing needs overriding here.</summary>
        char CanyonHdRawGround(char c) => NoGroundOverride;
    }
}
