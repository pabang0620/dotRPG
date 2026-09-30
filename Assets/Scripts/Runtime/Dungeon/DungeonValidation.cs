using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CONTENT] Startup data check of every dungeon room: each <see cref="SpawnGroup"/> digit must have at least one
    /// cell in the room's map (otherwise the group falls back to a spot near the entry), and each map needs its
    /// 'P' entry (otherwise the party starts in the middle of the room, on top of the monsters).
    /// Problems are logged with Debug.LogError once at startup and kept in <see cref="Errors"/>.
    /// </summary>
    public static class DungeonValidation
    {
        static List<string> errors;

        /// <summary>Every problem found (empty when the data is consistent). Runs the check on first use.</summary>
        public static IReadOnlyList<string> Errors => errors ?? Validate();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RunAtStartup()
        {
            foreach (var e in Validate()) Debug.LogError($"[dotRPG] Dungeon data: {e}");
        }

        /// <summary>Re-checks all weekday dungeons and the raid.</summary>
        public static List<string> Validate()
        {
            errors = new List<string>();
            var all = new List<DungeonDef>(DungeonDatabase.Weekday) { DungeonDatabase.SkeletonKing };
            foreach (var d in all)
                for (int r = 0; r < d.RoomCount; r++)
                {
                    var room = d.rooms[r];
                    var info = MapRegistry.Get(room.mapId);
                    var asset = info != null ? Resources.Load<TextAsset>(info.resource) : null;
                    if (asset == null)
                    {
                        errors.Add($"{d.id} room {r}: map '{room.mapId}' not found");
                        continue;
                    }
                    var digits = new HashSet<char>();
                    bool entry = false;
                    foreach (var raw in asset.text.Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        if (line.StartsWith("//")) continue;
                        foreach (char c in line)
                        {
                            if (c >= '1' && c <= '9') digits.Add(c);
                            else if (c == 'P') entry = true;
                        }
                    }
                    if (!entry) errors.Add($"{d.id} room {r}: map '{room.mapId}' has no 'P' entry");
                    foreach (var g in room.groups)
                        if (!digits.Contains((char)('0' + g.digit)))
                            errors.Add($"{d.id} room {r}: group {g.digit} ({g.monsterId}) has no cells in map '{room.mapId}'");
                }
            return errors;
        }
    }
}
