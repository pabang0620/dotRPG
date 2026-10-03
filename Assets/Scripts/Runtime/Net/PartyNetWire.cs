using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY NET] Message kinds inside the Control and Event channels (first byte of the payload).</summary>
    public static class PartyMsg
    {
        // Control (reliable): the handshake only
        public const byte Hello = 1, Welcome = 2;
        // Event (reliable, one ordered stream so a room change always precedes that room's spawns)
        public const byte EnemySpawn = 1, EnemyDie = 2, Damage = 3, Act = 4, RoomCleared = 5, Heal = 6,
            RoomLoad = 7, RunEnd = 8, MemberJoined = 9, MemberLeft = 10, RunStart = 11;
    }

    /// <summary>
    /// [PARTY NET] Who a party member is: enough to build the same body on another PC (class, level, gear,
    /// passives, gems, base HP). AI seats carry their mercenary id instead.
    /// </summary>
    public sealed class MemberCard
    {
        public int slot;
        public string name = "";
        public CharacterClass cls;
        public int level = 1;
        /// <summary>AI seat: the mercenary id (empty for a human).</summary>
        public string mercId = "";
        /// <summary>Server character uuid of a human (empty for AI).</summary>
        public string characterId = "";
        public int baseMaxHp;
        public string[] gear = new string[Equipment.SlotCount];
        public List<string> passives = new List<string>();
        public List<string> gemSlots = new List<string>();

        public bool IsAi => !string.IsNullOrEmpty(mercId);

        /// <summary>The card of a member on this PC (local player or companion).</summary>
        public static MemberCard Of(PlayerController m, int slot, string characterId)
        {
            var d = m.Data;
            var card = new MemberCard
            {
                slot = slot,
                name = d.IsLocal ? (Game.Session.Journal?.PlayerName ?? QuestJournal.DefaultName) : d.DisplayName,
                cls = d.Class,
                level = d.Level,
                mercId = d.IsLocal ? "" : d.MercenaryId ?? "",
                characterId = characterId ?? "",
                baseMaxHp = d.BaseMaxHp,
            };
            for (int i = 0; i < Equipment.SlotCount; i++) card.gear[i] = d.Equipment[(EquipSlot)i] ?? "";
            var save = new SaveData();
            d.Progression.Capture(save);
            card.passives = save.passives ?? new List<string>();
            card.gemSlots = save.gemSlots ?? new List<string>();
            return card;
        }

        /// <summary>Builds the member's data on this PC (an AI seat comes from the mercenary table).</summary>
        public CharacterData ToData()
        {
            var def = IsAi ? MercenaryDatabase.Get(mercId) : null;
            if (def != null) return MercenaryDatabase.CreateData(def, level);
            var data = CharacterData.CreateCompanion("net_" + slot, name, cls, Mathf.Max(1, baseMaxHp));
            data.Progression.Restore(new SaveData { level = level, xp = 0, passives = passives, gemSlots = gemSlots }, cls);
            for (int i = 0; i < Equipment.SlotCount; i++)
                if (!string.IsNullOrEmpty(gear[i])) data.Equipment.Set((EquipSlot)i, gear[i]);
            data.DisplayName = name;
            return data;
        }

        public void Write(BinaryWriter w)
        {
            w.Write((byte)slot);
            w.Write(name ?? "");
            w.Write((byte)cls);
            w.Write((short)level);
            w.Write(mercId ?? "");
            w.Write(characterId ?? "");
            w.Write(baseMaxHp);
            for (int i = 0; i < Equipment.SlotCount; i++) w.Write(gear[i] ?? "");
            WriteList(w, passives);
            WriteList(w, gemSlots);
        }

        public static MemberCard Read(BinaryReader r)
        {
            var c = new MemberCard
            {
                slot = r.ReadByte(),
                name = r.ReadString(),
                cls = (CharacterClass)r.ReadByte(),
                level = r.ReadInt16(),
                mercId = r.ReadString(),
                characterId = r.ReadString(),
                baseMaxHp = r.ReadInt32(),
            };
            for (int i = 0; i < Equipment.SlotCount; i++) c.gear[i] = r.ReadString();
            c.passives = ReadList(r);
            c.gemSlots = ReadList(r);
            return c;
        }

        static void WriteList(BinaryWriter w, List<string> list)
        {
            w.Write((short)(list?.Count ?? 0));
            if (list != null) foreach (var s in list) w.Write(s ?? "");
        }

        static List<string> ReadList(BinaryReader r)
        {
            int n = r.ReadInt16();
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadString());
            return list;
        }
    }

    /// <summary>[PARTY NET] Per-member numbers the host counted, sent with the run's end.</summary>
    public struct MemberRunStats
    {
        public int slot, hitsTaken, maxCombo, revives, damage;
    }

    /// <summary>[PARTY NET] Small helpers for building and reading payloads.</summary>
    public static class PartyWire
    {
        public static byte[] Build(Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static BinaryReader Reader(byte[] data) => new BinaryReader(new MemoryStream(data), Encoding.UTF8);

        public static void WriteVec(BinaryWriter w, Vector2 v) { w.Write(v.x); w.Write(v.y); }
        public static Vector2 ReadVec(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());
    }
}
