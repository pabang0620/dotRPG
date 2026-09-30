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
            return false;
        }
    }
}
