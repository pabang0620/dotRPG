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
        [System.Serializable]
        public sealed class Placement
        {
            public string npcId = "", map = "";
            /// <summary>Map-text cell (column, row from the top line).</summary>
            public float col, row;
            /// <summary>down / up / left / right.</summary>
            public string facing = "down";
            /// <summary>Shown only while every one of these flags is set.</summary>
            public string[] requires = new string[0];
            /// <summary>Hidden as soon as any of these flags is set.</summary>
            public string[] hiddenBy = new string[0];
            /// <summary>Conversation when talked to outside of quests ("" = the character's idle line).</summary>
            public string dialogue = "";
            /// <summary>Shown only while this quest is active ("" = any time).</summary>
            public string activeQuest = "";
        }

        /// <summary>A story object: a sprite that may be used (raises <see cref="GameEvents.Interacted"/> with <see cref="interactId"/>).</summary>
        [System.Serializable]
        public sealed class Prop
        {
            public string sprite = "", map = "";
            public float col, row;
            public string[] requires = new string[0];
            public string[] hiddenBy = new string[0];
            public string activeQuest = "";
            public string interactId = "", prompt = "살펴보기", dialogue = "";
            public bool solid = true, glow, bob;
            public float scale = 1f;
        }

        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static readonly Color32 SkinLight = C(250, 205, 160);
        static readonly Color32 SkinTan = C(222, 160, 110);

        static Dictionary<string, NpcDefinition> cast;

        static NpcDefinition Def(string id, string name, CharacterLook look, NpcBehaviour behaviour = NpcBehaviour.Idle, NpcTool tool = NpcTool.None) =>
            new NpcDefinition("", id, name, look, behaviour, tool, Facing.Down, id + "_idle");

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
                Add(Def("knight_dorn", "기사 돈", new CharacterLook("knight_dorn", HairStyle.Short, SkinTan, C(60, 44, 36), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Plate, armorColor = C(170, 176, 190) }));
                Add(Def("knight_ivy", "기사 아이비", new CharacterLook("knight_ivy", HairStyle.Bun, SkinLight, C(150, 60, 40), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Plate, armorColor = C(170, 176, 190) }));
                Add(Def("knight_mo", "기사 모", new CharacterLook("knight_mo", HairStyle.Curly, SkinLight, C(200, 150, 80), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Leather, armorColor = C(120, 90, 60) }));
                Add(Def("prisoner", "붙잡힌 마족", new CharacterLook("prisoner", HairStyle.Spiky, C(150, 130, 170), C(50, 40, 60), C(70, 60, 80), C(50, 44, 56))));
                Add(Def("winter_hunter", "설원 사냥꾼 하람", new CharacterLook("winter_hunter", HairStyle.Long, SkinTan, C(70, 52, 40), C(150, 160, 176), C(84, 70, 60), HatKind.Straw, C(210, 214, 224)))); // 눈꽃 마을 사냥터 의뢰
                Add(Def("winter_elder", "눈꽃 마을 촌장 세라", new CharacterLook("winter_elder", HairStyle.Bun, SkinLight, C(214, 214, 222), C(96, 120, 168), C(70, 76, 96), HatKind.None, C(210, 214, 224)))); // 눈꽃 마을 의뢰
                Add(Def("winter_innkeeper", "여관 주인 보라", new CharacterLook("winter_innkeeper", HairStyle.Curly, SkinLight, C(140, 70, 50), C(196, 92, 84), C(110, 84, 70)))); // 눈꽃 마을 여관
                Add(Def("knight", "기사단원", new CharacterLook("knight", HairStyle.Short, SkinTan, C(70, 50, 40), C(64, 96, 168), C(48, 58, 92)) { armor = ArmorStyle.Plate, armorColor = C(170, 176, 190) }));
                return cast;
            }
        }

        public static NpcDefinition Find(string npcId) => npcId != null && Cast.TryGetValue(npcId, out var d) ? d : null;

        [System.Serializable]
        sealed class WorldFile
        {
            public List<Placement> placements = new List<Placement>();
            public List<Prop> props = new List<Prop>();
        }

        public const string ResourcePath = "Data/StoryWorld";
        static WorldFile world;

        static WorldFile World
        {
            get
            {
                if (world != null) return world;
                var asset = Resources.Load<TextAsset>(ResourcePath);
                try { world = asset != null ? JsonUtility.FromJson<WorldFile>(asset.text) : null; }
                catch (System.Exception e) { Debug.LogError("[dotRPG] StoryWorld.json could not be read: " + e.Message); }
                if (world == null) world = new WorldFile();
                return world;
            }
        }

        /// <summary>Where story characters stand outside of cutscenes (Resources/Data/StoryWorld.json).</summary>
        public static List<Placement> Placements => World.placements;

        public static List<Prop> Props => World.props;

        static bool Holds(string[] requires, string[] hiddenBy, string activeQuest)
        {
            var j = Game.Session.Journal;
            foreach (var f in requires) if (!j.HasFlag(f)) return false;
            foreach (var f in hiddenBy) if (j.HasFlag(f)) return false;
            if (!string.IsNullOrEmpty(activeQuest) && j.Status(activeQuest) != QuestStatus.Active) return false;
            return true;
        }

        public static bool Visible(Placement p) => Holds(p.requires, p.hiddenBy, p.activeQuest);
        public static bool Visible(Prop p) => Holds(p.requires, p.hiddenBy, p.activeQuest);

        /// <summary>The prologue night: villagers have fled or hidden.</summary>
        public static bool VillagersHidden => Game.Session != null && Game.Session.Journal.HasFlag(StoryIds.FlagAttackNight);

        /// <summary>Spawns the story characters that belong on <paramref name="mapId"/> right now (world build).</summary>
        public static void SpawnFor(string mapId, Transform parent, Rect bounds)
        {
            if (Game.Session == null) return;
            foreach (var p in Placements)
            {
                if (p.map != mapId || !Visible(p)) continue;
                var def = Find(p.npcId);
                if (def == null) continue;
                var placed = def.Clone();
                placed.initialFacing = CutscenePlayer.ParseFacing(p.facing, Facing.Down);
                placed.dialogueId = string.IsNullOrEmpty(p.dialogue) ? p.npcId + "_idle" : p.dialogue;
                NpcController.Create(placed, CutscenePlayer.CellToWorld(bounds, p.col, p.row), parent);
            }
            foreach (var p in Props)
                if (p.map == mapId && Visible(p)) StoryProp.Create(p, CutscenePlayer.CellToWorld(bounds, p.col, p.row), parent);
        }
    }

    /// <summary>[STORY] A story object in the world (horse, graves, the hair ribbon, festival lanterns).</summary>
    public class StoryProp : Interactable
    {
        StoryCast.Prop prop;
        SpriteRenderer sr;
        Vector3 basePos;

        public override string Prompt => prop.prompt;
        public string InteractId => prop.interactId;
        public override bool CanInteract => !string.IsNullOrEmpty(prop.interactId) || !string.IsNullOrEmpty(prop.dialogue);

        public static StoryProp Create(StoryCast.Prop p, Vector2 pos, Transform parent)
        {
            var go = new GameObject("Story_" + p.sprite);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sp = go.AddComponent<StoryProp>();
            sp.prop = p;
            sp.basePos = go.transform.position;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * p.scale;
            sp.sr = visual.AddComponent<SpriteRenderer>();
            sp.sr.sprite = Game.Art.Get(p.sprite);
            HdMaterial.Apply(sp.sr);
            if (p.solid)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.8f * p.scale, 0.4f);
                col.offset = new Vector2(0f, 0.2f);
            }
            go.AddComponent<YSort>().Configure(true);
            if (p.glow) WarmGlow.Attach(go.transform, new Vector2(0f, 1.2f * p.scale), 1.3f, 0.3f);
            sp.ConfigureShape(new Vector2(0f, 0.2f), 0.6f, new Vector2(0f, 1.6f * p.scale));
            return sp;
        }

        void Update()
        {
            if (!prop.bob || sr == null) return;
            sr.transform.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * 1.6f)) * 0.03f, 0f);
        }

        public override void Interact(PlayerController player)
        {
            string id = prop.interactId;
            if (!string.IsNullOrEmpty(prop.dialogue)) Game.Dialogue.Play(prop.dialogue, () => { if (!string.IsNullOrEmpty(id)) GameEvents.RaiseInteracted(id); });
            else if (!string.IsNullOrEmpty(id)) GameEvents.RaiseInteracted(id);
        }
    }

    /// <summary>
    /// Story characters who fight in the party for a while. 카엘 (flag <see cref="KaelFlag"/>, from quest 1-5 until
    /// the betrayal) joins outside the towns - hunting grounds, dungeons, raids - and stands in town as an NPC who
    /// gives quests. A mercenary whose seat he takes comes back when he leaves.
    /// </summary>
    public static class StoryCompanions
    {
        public const string KaelFlag = "kael_companion";
        public const string KaelGoneFlag = "kael_betrayed";
        static string benched;

        static bool KaelWanted(bool outsideTown) =>
            outsideTown && Game.Session != null && Game.Session.Journal.HasFlag(KaelFlag) && !Game.Session.Journal.HasFlag(KaelGoneFlag);

        /// <summary>Map entered (towns are safe maps) or a dungeon run starting/ending.</summary>
        public static void Refresh(bool outsideTown)
        {
            if (Game.Party == null || Game.Session == null) return;
            bool has = Game.Party.Has(MercIdFor("kael"));
            if (KaelWanted(outsideTown) && !has)
            {
                var roster = Game.Session.PartyRoster;
                if (roster.Count >= PartyManager.MaxCompanions)
                {
                    benched = roster[roster.Count - 1];
                    Game.Party.RemoveCompanion(benched);
                }
                Game.Party.AddCompanion(MercIdFor("kael"));
            }
            else if (!KaelWanted(outsideTown) && has)
            {
                Game.Party.RemoveCompanion(MercIdFor("kael"));
                if (!string.IsNullOrEmpty(benched) && !Game.Session.PartyRoster.Contains(benched)) Game.Party.AddCompanion(benched);
                benched = null;
            }
        }

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
                GameEvents.RaiseToast($"{MercenaryDatabase.Get(bench)?.name}이(가) 잠시 파티에서 빠졌습니다.");
            }
            Game.Party.AddCompanion(id);
        }

        public static void Leave(string actor)
        {
            Game.Party?.RemoveCompanion(MercIdFor(actor));
        }
    }
}
