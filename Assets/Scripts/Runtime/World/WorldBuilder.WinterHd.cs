using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // ================= Winter village =================

        /// <summary>
        /// Winter ground: one seamless snow field, cobble paths whose edges are buried in snow, low
        /// cliffs with snowy lips, the river with snowy banks, a waterfall where it crosses the cliff,
        /// wooden bridge/deck and stone stairs.
        /// </summary>
        void PaintWinterGround(Vector3Int cell, char c, System.Random rng)
        {
            int x = cell.x, y = cell.y;
            char ground = GroundAt(x, y);
            if (ground == 'V')
            {
                int part = GroundAt(x, y + 1) != 'V' ? 0 : GroundAt(x, y - 1) != 'V' ? 2 : 1;
                int sides = (GroundAt(x - 1, y) != 'V' ? 1 : 0) | (GroundAt(x + 1, y) != 'V' ? 2 : 0);
                waterMap.SetTile(cell, GetTile($"snow_fall_{part}_{x % 3}_{sides}", true));
                return;
            }
            if (ground == '~')
            {
                char above = GroundAt(x, y + 1), below = GroundAt(x, y - 1);
                int mask = 0;
                if (above == 'W') mask |= 8;
                else if (above == 'd') mask |= 32;
                else if (above == 'V') mask |= 64;
                else if (above != '\0' && !IsWater(above)) mask |= 1;
                if (IsLand(GroundAt(x + 1, y))) mask |= 2;
                if (IsLand(GroundAt(x - 1, y))) mask |= 4;
                if (IsLand(below) && below != 'W') mask |= 16;
                int bend = WinterBend(x, y);
                if (bend != 0) mask |= bend << 7;
                else
                {
                    // A neighbour's S-curve reaching into this cell.
                    if (WinterBend(x - 1, y) == 1) mask |= 1 << 11;
                    if (WinterBend(x + 1, y) == 2) mask |= 2 << 11;
                    if (WinterBend(x - 1, y) == 4) mask |= 4 << 11;
                    if (WinterBend(x + 1, y) == 8) mask |= 8 << 11;
                }
                waterMap.SetTile(cell, GetTile($"snow_water_{mask}_{(x * 7 + y * 3) % 4}", true));
                return;
            }
            if (ground == 'W')
            {
                int d = 1;
                while (d < 4 && GroundAt(x, y - d) == 'W') d++;
                char belowWall = GroundAt(x, y - d);
                if (belowWall == '\0' || belowWall == 'W') d = 4;
                int flags = 0;
                char up = GroundAt(x, y + 1);
                if (up != 'W' && up != '\0' && !IsWater(up)) flags |= 1;
                char l = GroundAt(x - 1, y), r = GroundAt(x + 1, y);
                if (l != 'W' && l != '\0') flags |= 2;
                if (r != 'W' && r != '\0') flags |= 4;
                cliffMap.SetTile(cell, GetTile($"snow_cliff_{d}_{flags}_{(x * 5 + y * 3) % 3}", true));
                return;
            }
            if (ground == 'L')
            {
                int sides = (GroundAt(x - 1, y) != 'L' ? 1 : 0) | (GroundAt(x + 1, y) != 'L' ? 2 : 0);
                groundMap.SetTile(cell, GetTile($"snow_stairs_{sides}"));
                return;
            }
            if (ground == 'd')
            {
                int part = (GroundAt(x, y + 1) != 'd' ? 1 : 0) | (GroundAt(x, y - 1) != 'd' ? 2 : 0);
                groundMap.SetTile(cell, GetTile($"snow_bridge_{part}"));
                return;
            }
            // Snow and path both sample 128px seamless patterns, so no tile border shows.
            int sx = ((x % 8) + 8) % 8, sy = 7 - ((y % 8) + 8) % 8;
            if (ground == ',')
            {
                groundMap.SetTile(cell, GetTile($"snow_path_{sx}_{sy}"));
                bool Snow(int dx, int dy) => GroundAt(x + dx, y + dy) == '.';
                int mask = 0;
                if (Snow(0, 1)) mask |= 1;
                if (Snow(1, 0)) mask |= 2;
                if (Snow(0, -1)) mask |= 4;
                if (Snow(-1, 0)) mask |= 8;
                if ((mask & 3) == 0 && Snow(1, 1)) mask |= 16;
                if ((mask & 6) == 0 && Snow(1, -1)) mask |= 32;
                if ((mask & 12) == 0 && Snow(-1, -1)) mask |= 64;
                if ((mask & 9) == 0 && Snow(-1, 1)) mask |= 128;
                if (mask != 0) edgeMap.SetTile(cell, GetTile($"snow_pathedge_{mask}"));
                return;
            }
            groundMap.SetTile(cell, GetTile($"snow_ground_{sx}_{sy}"));
            // Rare small marks of varied kinds (drift bumps, ice pellets, tracks, sparkles...).
            if (c == '.' && rng.NextDouble() < 0.045) decoMap.SetTile(cell, GetTile($"snow_speck_{rng.Next(0, 12)}"));
        }

        /// <summary>
        /// Smooth river bends: a water cell at the corner of a one-tile step in the bank (land on two sides,
        /// the bank carrying on one column over above or below) is drawn as one S-shaped bank, so the river
        /// reads as a curve instead of a staircase. Returns 1 (land N+W), 2 (N+E), 4 (S+W), 8 (S+E) or 0.
        /// </summary>
        int WinterBend(int x, int y)
        {
            if (GroundAt(x, y) != '~') return 0;
            bool Solid(int dx, int dy) { char ch = GroundAt(x + dx, y + dy); return ch != '\0' && !IsWater(ch) && ch != 'd'; }
            bool Wet(int dx, int dy) => IsWater(GroundAt(x + dx, y + dy));
            bool n = Solid(0, 1), s = Solid(0, -1), w = Solid(-1, 0), e = Solid(1, 0);
            if (n && w && Wet(1, 0) && Wet(0, -1) && Wet(1, 1) && Solid(-1, -1)) return 1;
            if (n && e && Wet(-1, 0) && Wet(0, -1) && Wet(-1, 1) && Solid(1, -1)) return 2;
            if (s && w && Wet(1, 0) && Wet(0, 1) && Wet(1, -1) && Solid(-1, 1)) return 4;
            if (s && e && Wet(-1, 0) && Wet(0, 1) && Wet(-1, -1) && Solid(1, 1)) return 8;
            return 0;
        }

        /// <summary>Objects of the winter village. Returns false for symbols shared with the other maps.</summary>
        bool SpawnWinterObject(char c, int x, int y, System.Random rng)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            var foot = new Vector2(x + 0.5f, y + 0.2f);
            switch (c)
            {
                case 'Q': WinterTree($"snow_tree_0_{rng.Next(0, 3)}", foot, 0.7f); return true;
                case 'q': WinterTree($"snow_tree_{(rng.NextDouble() < 0.5 ? 1 : 2)}_{rng.Next(0, 3)}", foot, 0.55f); return true;
                case 'Y': WinterTree($"snow_pine_0_{rng.Next(0, 3)}", foot, 0.5f); return true;
                case 'y': WinterTree($"snow_pine_1_{rng.Next(0, 3)}", foot, 0.4f); return true;
                case 'B': StaticProp("Bush", $"snow_bush_{rng.Next(0, 3)}", foot, new Vector2(0.9f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'g': Decoration($"snow_grass_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-4, 5) / 16f, -0.25f)); return true;
                case 'R': StaticProp("Stone", $"snow_rock_{rng.Next(0, 2)}", foot, new Vector2(0.8f, 0.45f), new Vector2(0f, 0.2f)); return true;
                case 'i': Decoration($"snow_ice_{rng.Next(0, 3)}", center + new Vector2(rng.Next(-3, 4) / 16f, rng.Next(-3, 4) / 16f), -20500); return true;
                case 'F': StaticProp("Fence", $"snow_fence_{FenceMask(x, y)}", new Vector2(x + 0.5f, y + 0.05f), new Vector2(1f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'v': StaticProp("GardenBed", $"snow_bed_{rng.Next(0, 2)}", new Vector2(x + 0.5f, y + 0.1f), new Vector2(0.9f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'h': StaticProp("Hay", "snow_hay", foot, new Vector2(1f, 0.55f), new Vector2(0f, 0.3f)); return true;
                case 'o': StaticProp("Well", "snow_well", new Vector2(x + 1f, y + 0.1f), new Vector2(1.6f, 1.0f), new Vector2(0f, 0.55f)); return true;
                case 'e': StaticProp("Bench", "snow_bench", new Vector2(x + 1f, y + 0.1f), new Vector2(1.8f, 0.35f), new Vector2(0f, 0.25f)); return true;
                case 'l': StaticProp("LogSeat", "snow_log", foot, new Vector2(1.2f, 0.4f), new Vector2(0f, 0.2f)); return true;
                case 'w': StaticProp("Workbench", "snow_workbench", new Vector2(x + 1f, y + 0.1f), new Vector2(2f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'm': StaticProp("Mailbox", "snow_mailbox", foot, new Vector2(0.5f, 0.3f), new Vector2(0f, 0.15f)); return true;
                case 'u': StaticProp("FlowerPot", "snow_pot", foot, new Vector2(0.6f, 0.35f), new Vector2(0f, 0.18f)); return true;
                case 'z': StaticProp("Woodpile", "snow_woodpile", new Vector2(x + 1f, y + 0.1f), new Vector2(1.7f, 0.5f), new Vector2(0f, 0.3f)); return true;
                case 'b': StaticProp("Barrel", "snow_barrel", foot, new Vector2(0.7f, 0.5f), new Vector2(0f, 0.25f)); return true;
                case 'x': StaticProp("Crate", "snow_crate", foot, new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f)); return true;
                case 'p':
                {
                    var lamp = StaticProp("Lamp", "snow_lamp", foot, new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
                    WarmGlow.Attach(lamp.transform, new Vector2(0f, 2.35f), 1.5f, 0.3f);
                    return true;
                }
                case 'j':
                {
                    var fire = StaticProp("Campfire", "snow_fire_0", foot, new Vector2(1.1f, 0.5f), new Vector2(0f, 0.25f));
                    fire.AddComponent<CampfireFx>();
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
                    var house = StaticProp("House", "snow_house", pos, new Vector2(2.8f, 1.9f), new Vector2(0f, 1.2f));
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
                    var barn = StaticProp("Barn", "snow_barn", pos, new Vector2(4.7f, 2.4f), new Vector2(0f, 1.3f));
                    var door = new GameObject("Door").AddComponent<DialogueInteractable>();
                    door.transform.SetParent(barn.transform, false);
                    door.Setup("살펴보기", "winter_barn");
                    door.ConfigureShape(new Vector2(0f, 0f), 0.4f, new Vector2(0f, 1.9f));
                    PointsOfInterest.Add(pos + Vector2.up);
                    return true;
                }
                case 'n':
                {
                    var sign = StaticProp("Sign", "snow_sign", foot, new Vector2(0.8f, 0.4f), new Vector2(0f, 0.2f));
                    var interact = sign.AddComponent<DialogueInteractable>();
                    interact.Setup("읽기", SignDialogueFor(x, y));
                    interact.ConfigureShape(new Vector2(0f, 0.2f), 0.1f, new Vector2(0f, 1.4f));
                    return true;
                }
                case 'A':
                {
                    var gate = StaticProp("Gate", "snow_gate", new Vector2(x + 2f, y + 0.1f), new Vector2(3.9f, 1.3f), new Vector2(0f, 0.65f));
                    var look = gate.AddComponent<DialogueInteractable>();
                    look.Setup("살펴보기", "winter_gate");
                    look.ConfigureShape(new Vector2(0f, -0.2f), 0.6f, new Vector2(0f, 3f));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Snowy tree: only the trunk blocks movement, the crown overlaps what is behind it.</summary>
        void WinterTree(string sprite, Vector2 foot, float trunkWidth)
        {
            StaticProp("Tree", sprite, foot, new Vector2(trunkWidth, 0.35f), new Vector2(0f, 0.15f));
        }

        Color32 WinterMinimapColor(int x, int y)
        {
            switch (cells[x, y])
            {
                case 'Q': case 'q': return new Color32(95, 165, 150, 255);
                case 'Y': case 'y': return new Color32(64, 130, 112, 255);
                case 'R': return new Color32(163, 169, 186, 255);
                case 'F': return new Color32(168, 112, 74, 255);
                case 'B': return new Color32(111, 188, 171, 255);
            }
            switch (GroundAt(x, y))
            {
                case '~': case 'V': return new Color32(79, 159, 220, 255);
                case ',': return new Color32(183, 187, 200, 255);
                case 'd': return new Color32(201, 144, 104, 255);
                case 'W': return new Color32(154, 142, 166, 255);
                case 'L': return new Color32(201, 198, 210, 255);
                default: return new Color32(236, 242, 244, 255);
            }
        }

        void CreateBoundaryWalls()
        {
            var walls = new GameObject("Bounds");
            walls.transform.SetParent(transform, false);
            void Wall(Vector2 center, Vector2 size)
            {
                var box = walls.AddComponent<BoxCollider2D>();
                box.offset = center;
                box.size = size;
            }
            Wall(new Vector2(width * 0.5f, -0.5f), new Vector2(width + 2, 1));
            Wall(new Vector2(width * 0.5f, height + 0.5f), new Vector2(width + 2, 1));
            Wall(new Vector2(-0.5f, height * 0.5f), new Vector2(1, height + 2));
            Wall(new Vector2(width + 0.5f, height * 0.5f), new Vector2(1, height + 2));
        }

        const string FallbackMap =
            "TTTTTTTTTTTT\n" +
            "T....1.....T\n" +
            "T..P.......T\n" +
            "T....R..k..T\n" +
            "T~~~~......T\n" +
            "TTTTTTTTTTTT\n";
    }
}
