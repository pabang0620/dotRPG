using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Dungeon room symbols (only on <see cref="MapInfo.instanced"/> maps): <c>@</c> = door to the next room,
    /// <c>1</c>-<c>9</c> = monster spawn group of the room's <see cref="RoomDef"/>, <c>P</c> = entry point (shared code).
    /// The builder only records where they are; <see cref="DungeonRun"/> places doors and monsters.
    /// </summary>
    public partial class WorldBuilder
    {
        /// <summary>Spawn cells of the current room: (group digit, cell centre).</summary>
        public readonly List<(int digit, Vector2 pos)> DungeonSpawns = new List<(int, Vector2)>();
        /// <summary>Centres of the '@' cells (one door made of all of them).</summary>
        public readonly List<Vector2> DungeonDoorCells = new List<Vector2>();

        void ClearDungeonMarks()
        {
            DungeonSpawns.Clear();
            DungeonDoorCells.Clear();
        }

        // ---------- Village dungeon guide (던전 안내원) ----------

        /// <summary>Preferred cell of the guide: east edge of the plaza (text column, text row from the top of Village.txt).</summary>
        const int GuideColumn = 33, GuideRowFromTop = 34; // plaza, east of the fountain (near the start point)

        static NpcDefinition guide;

        /// <summary>The 던전 안내원 (opens the dungeon select window).</summary>
        public static NpcDefinition DungeonGuide => guide ?? (guide = new NpcDefinition("", "dungeon_guide", "던전 안내원 세나",
            new CharacterLook("dungeon_guide", HairStyle.Short, new Color32(246, 204, 164, 255), new Color32(58, 44, 70, 255),
                new Color32(120, 60, 150, 255), new Color32(52, 46, 70, 255), HatKind.Cap, new Color32(210, 170, 70, 255)),
            NpcBehaviour.Idle, NpcTool.None, Facing.Down, "", "", default, NpcService.Dungeon, "오늘 열린 던전을 안내해 드릴게요. 준비되면 말씀하세요!"));

        /// <summary>Where the guide stood when the village was last built (tests).</summary>
        public Vector2? DungeonGuidePosition { get; private set; }

        /// <summary>Places the guide on a free cobblestone cell near the plaza (village only).</summary>
        void SpawnDungeonGuide()
        {
            DungeonGuidePosition = null;
            if (!MapRegistry.IsTown(MapId)) return;
            int cx = MapId == MapRegistry.Village ? GuideColumn : (int)PlayerSpawn.x;
            int cy = MapId == MapRegistry.Village ? height - 1 - GuideRowFromTop : (int)PlayerSpawn.y + 3;
            for (int r = 0; r <= 6; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (!OpenCobble(x, y)) continue;
                        var pos = new Vector2(x + 0.5f, y + 0.5f);
                        if (!IsFree(pos)) continue;
                        NpcController.Create(DungeonGuide, pos, objectsRoot);
                        PointsOfInterest.Add(pos);
                        DungeonGuidePosition = pos;
                        return;
                    }
        }

        /// <summary>A cobblestone cell whose eight neighbours are cobblestone too (nothing stands there).</summary>
        bool OpenCobble(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (At(x + dx, y + dy) != ',') return false;
            return true;
        }

        /// <summary>True when the symbol was a dungeon mark (nothing else is spawned for it).</summary>
        bool SpawnDungeonSymbol(char c, int x, int y)
        {
            if (map == null || !map.instanced) return false;
            var center = new Vector2(x + 0.5f, y + 0.5f);
            if (c == '@')
            {
                DungeonDoorCells.Add(center);
                return true;
            }
            if (c >= '1' && c <= '9')
            {
                DungeonSpawns.Add((c - '0', center));
                return true;
            }
            return SpawnDungeonLookProp(c, x, y); // [DGNTERRAIN] mine lamps
        }
    }
}
