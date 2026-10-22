using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Chapter 3 gate (Docs/PLAN_CHAPTER3_2026-10-21.md, Docs/ch3/IMPLEMENTATION_ORDER.md step 0). The chapter ships in the
    /// build but stays closed until the server opens it: online the server's /meta says so (chapter3_open, level_cap), offline
    /// the GameConfig flag does. Closed = level cap 40 and no chapter 3 maps, quests, raids or specializations.
    /// </summary>
    public static class Chapter3
    {
        /// <summary>Level cap while the chapter is closed (the launch content) and once it is open.</summary>
        public const int ClosedCap = 40, OpenCap = 50;

        static bool serverOpen;
        static int serverCap;

        /// <summary>True when chapter 3 is open for this session: the server's word online, the GameConfig flag offline.</summary>
        public static bool Enabled => OnlineSession.Playing ? serverOpen : Game.Config != null && Game.Config.chapter3Open;

        /// <summary>What the last /meta said, before a character is in play (the login reads it to fetch the story bundle).</summary>
        public static bool ServerOpen => serverOpen;

        /// <summary>The server's level cap (0 = not sent: derived from <see cref="Enabled"/>).</summary>
        public static int ServerLevelCap => OnlineSession.Playing ? serverCap : 0;

        /// <summary>Reads chapter3_open / level_cap from a /meta answer (called before every login).</summary>
        public static void ReadMeta(Dictionary<string, object> meta)
        {
            serverOpen = meta != null && meta.TryGetValue("chapter3_open", out var open) && open is bool b && b;
            serverCap = MiniJson.Int(meta, "level_cap");
        }
    }
}
