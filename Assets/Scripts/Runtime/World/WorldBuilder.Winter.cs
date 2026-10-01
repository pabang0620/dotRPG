using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The winter village in 32px art: painted ground (<see cref="WinterTerrain"/>) plus "wnt_*" sprites.
    /// The four hooks below route the Winter map through the high-resolution path; cliffs and water are
    /// solid, stairs and the bridge stay walkable exactly as in the old 16px map, and every object keeps
    /// its footprint, collider, dialogue id and points-of-interest just as <c>SpawnWinterObject</c> had them.
    /// </summary>
    public partial class WorldBuilder
    {
        /// <summary>True: the 32px winter village is implemented; the Winter map runs through these hooks.</summary>
        bool WinterHdReady => true;

        /// <summary>Per cell, before its object spawns: invisible colliders for the solid cliffs, river and waterfall.</summary>
        void PaintWinterHdCell(Vector3Int cell, char c)
        {
            char g = GroundAt(cell.x, cell.y);
            if (g == '~' || g == 'V') waterMap.SetTile(cell, ColliderTile());
            else if (g == 'W') cliffMap.SetTile(cell, ColliderTile());
        }

        /// <summary>After every cell: paint the whole ground once (snow, paths, cliffs, river, waterfall, deck, stairs).</summary>
        void BuildWinterHd()
        {
            BuildPaintedGround(PaintWinterSurface);
        }

        Color32[] PaintWinterSurface(char[,] ground, int w, int h, out int pw, out int ph)
            => WinterTerrain.Paint(ground, w, h, out pw, out ph, StoreWaterField);

        /// <summary>Winter objects in 32px art. Returns false for symbols the shared code handles (NPCs, portals, P, k).</summary>
        bool SpawnWinterHdObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            switch (c)
            {
                case 'Q': WinterTree($"wnt_tree_0_{rng.Next(0, 3)}", foot, 0.7f); return true;
                case 'q': WinterTree($"wnt_tree_{(rng.NextDouble() < 0.5 ? 1 : 2)}_{rng.Next(0, 3)}", foot, 0.55f); return true;
                case 'Y': WinterTree($"wnt_pine_0_{rng.Next(0, 3)}", foot, 0.5f); return true;
                case 'y': WinterTree($"wnt_pine_1_{rng.Next(0, 3)}", foot, 0.4f); return true;
                case 'B': StaticProp("Bush", $"wnt_bush_{rng.Next(0, 3)}", foot, new Vector2(0.9f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'g': Decoration($"wnt_grass_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-4, 5) / 16f, -0.25f)); return true;
                case 'R': StaticProp("Stone", $"wnt_rock_{rng.Next(0, 2)}", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'i': Decoration($"wnt_ice_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-3, 4) / 16f, rng.Next(-3, 4) / 16f), -20500); return true;
                case 'F': StaticProp("Fence", $"wnt_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'v': StaticProp("GardenBed", $"wnt_bed_{rng.Next(0, 2)}", new Vector2(x + 0.5f, y + 0.1f), new Vector2(0.9f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'h': StaticProp("Hay", "wnt_hay", foot, new Vector2(1f, 0.55f), new Vector2(0f, 0.3f)); return true;
                case 'o': StaticProp("Well", "wnt_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.6f, 1.0f), new Vector2(0f, 0.55f)); PointsOfInterest.Add(new Vector2(x + 1f, y + 0.5f)); return true;
                case 'e': StaticProp("Bench", "wnt_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.25f)); return true;
                case 'l': StaticProp("LogSeat", "wnt_log", foot, new Vector2(1.2f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'w': StaticProp("Workbench", "wnt_workbench", new Vector2(x + 1f, y + 0.1f), new Vector2(2f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'm': StaticProp("Mailbox", "wnt_mailbox", foot, new Vector2(0.5f, 0.3f), new Vector2(0f, 0.15f)); return true;
                case 'u': StaticProp("FlowerPot", "wnt_pot", foot, new Vector2(0.6f, 0.35f), new Vector2(0f, 0.18f)); return true;
                case 'z': StaticProp("Woodpile", "wnt_woodpile", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'b': StaticProp("Barrel", "wnt_barrel", foot, new Vector2(0.7f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'x': StaticProp("Crate", "wnt_crate", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f)); return true;
                case 'p':
                {
                    var lamp = StaticProp("Lamp", "wnt_lamp", foot, new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
                    WarmGlow.Attach(lamp.transform, new Vector2(0f, 2.35f), 1.5f, 0.3f);
                    return true;
                }
                case 'j':
                {
                    var fire = StaticProp("Campfire", "wnt_fire_0", foot, new Vector2(1.1f, 0.5f), new Vector2(0f, 0.25f));
                    fire.AddComponent<CampfireFx>().SetHd(true);
                    var warm = fire.AddComponent<DialogueInteractable>();
                    warm.Setup("불 쬐기", "winter_fire");
                    warm.ConfigureShape(new Vector2(0f, 0.2f), 0.3f, new Vector2(0f, 1.3f));
                    PointsOfInterest.Add(center);
                    return true;
                }
                case 'H':
                {
                    // Anchor is the bottom-left cell of a 3x3 footprint.
                    var pos = new Vector2(x + 1.5f, y + 0.1f);
                    var house = StaticProp("House", "wnt_house", pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
                    WarmGlow.Attach(house.transform, new Vector2(-0.75f, 1.4f), 1.1f, 0.22f);
                    WarmGlow.Attach(house.transform, new Vector2(0.75f, 1.4f), 1.1f, 0.22f);
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(house.transform, false);
                    door.Setup("문 두드리기", "winter_house_door");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.2f, new Vector2(0f, 1.4f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'N':
                {
                    // Farm building: anchor = bottom-left of a 5x4 footprint.
                    var pos = new Vector2(x + 2.5f, y + 0.1f);
                    var barn = StaticProp("Barn", "wnt_barn", pos, new Vector2(4.7f, 2.4f), new Vector2(0f, 1.3f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(barn.transform, false);
                    door.Setup("살펴보기", "winter_barn");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.4f, new Vector2(0f, 1.9f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "wnt_sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    return true;
                }
                case 'A':
                {
                    var gate = StaticProp("Gate", "wnt_gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "winter_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 3f));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Ground code of a winter symbol on the 32px map, or <see cref="NoGroundOverride"/> for the shared rules.</summary>
        char WinterHdRawGround(char c) => NoGroundOverride;
    }
}
