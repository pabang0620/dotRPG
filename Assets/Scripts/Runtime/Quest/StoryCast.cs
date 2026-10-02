using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The story's own characters (Docs/STORY.md) and where they stand at each point of the story.
    /// A placement shows its character on a map while its flag rules hold (e.g. 브람 only before the
    /// attack, 카엘 at the graves after it). Cutscenes spawn the same characters by id.
    /// </summary>
    public static class StoryCast
    {
        public sealed class Placement
        {
            public string npcId, map;
            /// <summary>Map-text cell (column, row from the top line).</summary>
            public float col, row;
            public Facing facing = Facing.Down;
            /// <summary>Shown only while every one of these flags is set.</summary>
            public string[] requires = new string[0];
            /// <summary>Hidden as soon as any of these flags is set.</summary>
            public string[] hiddenBy = new string[0];
            /// <summary>Conversation when talked to outside of quests.</summary>
            public string dialogue = "";
        }

        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static readonly Color32 SkinLight = C(250, 205, 160);
        static readonly Color32 SkinTan = C(222, 160, 110);

        static Dictionary<string, NpcDefinition> cast;

        static NpcDefinition Def(string id, string name, CharacterLook look, NpcBehaviour behaviour = NpcBehaviour.Idle, NpcTool tool = NpcTool.None) =>
            new NpcDefinition("", id, name, look, behaviour, tool, Facing.Down, "");

        static Dictionary<string, NpcDefinition> Cast
        {
            get
            {
                if (cast != null) return cast;
                cast = new Dictionary<string, NpcDefinition>();
                void Add(NpcDefinition d) => cast[d.npcId] = d;
                Add(Def("bram", "브람", new CharacterLook("bram", HairStyle.Short, SkinTan, C(86, 70, 58), C(122, 92, 60), C(70, 56, 46))));
                Add(Def("hanna", "한나", new CharacterLook("hanna", HairStyle.Bun, SkinLight, C(150, 92, 52), C(196, 116, 92), C(118, 88, 76))));
                Add(Def("ria", "리아", new CharacterLook("ria", HairStyle.Long, SkinLight, C(214, 132, 70), C(116, 168, 104), C(90, 120, 80))));
                Add(Def("herbalist", "약초꾼 오르", new CharacterLook("herbalist", HairStyle.Curly, SkinTan, C(120, 120, 110), C(94, 132, 84), C(80, 70, 56), HatKind.Straw)));
                Add(Def("kael", "카엘", new CharacterLook("kael", HairStyle.Short, SkinLight, C(196, 196, 204), C(110, 112, 122), C(58, 60, 70)) { armor = ArmorStyle.Plate, armorColor = C(150, 154, 166) }));
                Add(Def("bargas", "흑철의 바르가스", new CharacterLook("bargas", HairStyle.Bald, C(120, 118, 130), C(30, 28, 34), C(36, 34, 42), C(28, 26, 32)) { armor = ArmorStyle.Plate, armorColor = C(40, 38, 48) }));
                Add(Def("leona", "기사단장 레오나", new CharacterLook("leona", HairStyle.Long, SkinLight, C(236, 214, 150), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Plate, armorColor = C(196, 200, 214) }));
                Add(Def("orban", "대사제 오르반", new CharacterLook("orban", HairStyle.Bald, SkinLight, C(220, 220, 220), C(236, 232, 214), C(214, 196, 120), HatKind.Wizard, C(236, 220, 150)) { robe = true }));
                Add(Def("grah", "수호자 그라흐", new CharacterLook("grah", HairStyle.Spiky, C(130, 110, 150), C(40, 30, 50), C(80, 60, 100), C(50, 40, 60)) { armor = ArmorStyle.Plate, armorColor = C(70, 64, 86) }));
                Add(Def("knight", "기사단원", new CharacterLook("knight", HairStyle.Short, SkinTan, C(70, 50, 40), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Plate, armorColor = C(170, 176, 190) }));
                return cast;
            }
        }

        public static NpcDefinition Find(string npcId) => npcId != null && Cast.TryGetValue(npcId, out var d) ? d : null;

        /// <summary>Where story characters stand outside of cutscenes. Filled with the chapter data.</summary>
        public static readonly List<Placement> Placements = new List<Placement>();

        public static bool Visible(Placement p)
        {
            var j = Game.Session.Journal;
            foreach (var f in p.requires) if (!j.HasFlag(f)) return false;
            foreach (var f in p.hiddenBy) if (j.HasFlag(f)) return false;
            return true;
        }

        /// <summary>Spawns the story characters that belong on <paramref name="mapId"/> right now (world build).</summary>
        public static void SpawnFor(string mapId, Transform parent)
        {
            if (Game.Session == null) return;
            foreach (var p in Placements)
            {
                if (p.map != mapId || !Visible(p)) continue;
                var def = Find(p.npcId);
                if (def == null) continue;
                var placed = def.Clone();
                placed.initialFacing = p.facing;
                placed.dialogueId = p.dialogue;
                NpcController.Create(placed, CutscenePlayer.CellToWorld(p.col, p.row), parent);
            }
        }
    }

    /// <summary>Story characters who fight in the party for a while (카엘 in chapters 1-2).</summary>
    public static class StoryCompanions
    {
        public static string MercIdFor(string actor) => "story_" + actor;

        public static void Join(string actor)
        {
            if (Game.Party == null) return;
            string id = MercIdFor(actor);
            if (MercenaryDatabase.Get(id) == null || Game.Party.Has(id)) return;
            var roster = Game.Session.PartyRoster;
            // A story companion always gets a seat: the last hired mercenary steps out.
            if (roster.Count >= PartyManager.MaxCompanions)
            {
                string bench = roster[roster.Count - 1];
                Game.Party.RemoveCompanion(bench);
                GameEvents.RaiseToast($"{MercenaryDatabase.Get(bench)?.name}이(가) 잠시 파티에서 빠졌다.");
            }
            Game.Party.AddCompanion(id);
        }

        public static void Leave(string actor)
        {
            Game.Party?.RemoveCompanion(MercIdFor(actor));
        }
    }
}
