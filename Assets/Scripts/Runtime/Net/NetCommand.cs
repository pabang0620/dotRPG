using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ONLINE] Network identity of a party member (PLAN_ONLINE §3.4): a human is account id + character id,
    /// an AI mercenary is its slot in the party. Offline every member is identified by its party slot.
    /// </summary>
    public readonly struct ActorId : IEquatable<ActorId>
    {
        public readonly long account;
        public readonly long character;
        /// <summary>Party slot 0-3 (always set; the wire format addresses members by slot).</summary>
        public readonly byte slot;
        public bool IsAi => account == 0;

        ActorId(long account, long character, byte slot)
        {
            this.account = account;
            this.character = character;
            this.slot = slot;
        }

        public static ActorId Account(long accountId, long characterId, int slot) => new ActorId(accountId, characterId, (byte)slot);
        public static ActorId Ai(int slot) => new ActorId(0, 0, (byte)slot);
        /// <summary>The offline local player (slot 0, no account).</summary>
        public static ActorId LocalOffline => new ActorId(0, 0, 0);

        public bool Equals(ActorId o) => account == o.account && character == o.character && slot == o.slot;
        public override bool Equals(object obj) => obj is ActorId o && Equals(o);
        public override int GetHashCode() => (account.GetHashCode() * 397) ^ (character.GetHashCode() * 31) ^ slot;
        public override string ToString() => IsAi ? $"ai#{slot}" : $"{account}:{character}#{slot}";
    }

    /// <summary>[ONLINE] Actor ids of the current party members (humans can be assigned once a lobby exists).</summary>
    public static class ActorIds
    {
        static readonly Dictionary<PlayerController, ActorId> assigned = new Dictionary<PlayerController, ActorId>();

        /// <summary>Id of a member: the assigned human id, otherwise its party slot (AI / offline).</summary>
        public static ActorId Of(PlayerController member)
        {
            if (member != null && assigned.TryGetValue(member, out var id)) return id;
            int slot = SlotOf(member);
            return slot <= 0 ? ActorId.LocalOffline : ActorId.Ai(slot);
        }

        public static int SlotOf(PlayerController member)
        {
            var party = Game.Party;
            if (party == null || member == null) return 0;
            for (int i = 0; i < party.Members.Count; i++) if (party.Members[i] == member) return i;
            return -1;
        }

        public static PlayerController Find(ActorId id)
        {
            var party = Game.Party;
            if (party == null) return null;
            foreach (var kv in assigned) if (kv.Value.Equals(id) && kv.Key != null) return kv.Key;
            return id.slot < party.Members.Count ? party.Members[id.slot] : null;
        }

        public static void Assign(PlayerController member, ActorId id) { if (member != null) assigned[member] = id; }
        public static void Clear() => assigned.Clear();
    }

    /// <summary>
    /// [ONLINE] Compact wire form of an <see cref="ActorCommand"/> (9 bytes): tick, movement quantised to
    /// -127..127 per axis, aim angle in 256 steps, button bits and the skill slot.
    /// </summary>
    public struct NetCommand
    {
        public const int Size = 9;
        public const int Attack = 1, Interact = 2, Healing = 4, Mana = 8, Town = 16, FaceAim = 32, HasAim = 64;

        public uint tick;
        public sbyte moveX, moveY;
        public byte aim;
        public byte buttons;
        public sbyte skill;

        public static NetCommand Pack(in ActorCommand c, uint tick)
        {
            var move = Vector2.ClampMagnitude(c.move, 1f);
            var n = new NetCommand
            {
                tick = tick,
                moveX = (sbyte)Mathf.Clamp(Mathf.RoundToInt(move.x * 127f), -127, 127),
                moveY = (sbyte)Mathf.Clamp(Mathf.RoundToInt(move.y * 127f), -127, 127),
                skill = (sbyte)Mathf.Clamp(c.skillSlot, -1, 127),
            };
            if (c.aim.sqrMagnitude > 0.0001f)
            {
                float deg = Mathf.Atan2(c.aim.y, c.aim.x) * Mathf.Rad2Deg;
                if (deg < 0f) deg += 360f;
                n.aim = (byte)(Mathf.RoundToInt(deg / 360f * 256f) & 0xFF);
                n.buttons |= HasAim;
            }
            if (c.attack) n.buttons |= Attack;
            if (c.interact) n.buttons |= Interact;
            if (c.useHealing) n.buttons |= Healing;
            if (c.useMana) n.buttons |= Mana;
            if (c.townScroll) n.buttons |= Town;
            if (c.faceAim) n.buttons |= FaceAim;
            return n;
        }

        public ActorCommand Unpack()
        {
            var c = ActorCommand.None;
            c.move = new Vector2(moveX / 127f, moveY / 127f);
            if ((buttons & HasAim) != 0)
            {
                float rad = aim / 256f * Mathf.PI * 2f;
                c.aim = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            }
            c.attack = (buttons & Attack) != 0;
            c.interact = (buttons & Interact) != 0;
            c.useHealing = (buttons & Healing) != 0;
            c.useMana = (buttons & Mana) != 0;
            c.townScroll = (buttons & Town) != 0;
            c.faceAim = (buttons & FaceAim) != 0;
            c.skillSlot = skill;
            return c;
        }

        public void Write(byte[] buffer, int offset)
        {
            buffer[offset] = (byte)tick;
            buffer[offset + 1] = (byte)(tick >> 8);
            buffer[offset + 2] = (byte)(tick >> 16);
            buffer[offset + 3] = (byte)(tick >> 24);
            buffer[offset + 4] = (byte)moveX;
            buffer[offset + 5] = (byte)moveY;
            buffer[offset + 6] = aim;
            buffer[offset + 7] = buttons;
            buffer[offset + 8] = (byte)skill;
        }

        public static NetCommand Read(byte[] buffer, int offset) => new NetCommand
        {
            tick = (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24),
            moveX = (sbyte)buffer[offset + 4],
            moveY = (sbyte)buffer[offset + 5],
            aim = buffer[offset + 6],
            buttons = buffer[offset + 7],
            skill = (sbyte)buffer[offset + 8],
        };

        public byte[] ToBytes()
        {
            var b = new byte[Size];
            Write(b, 0);
            return b;
        }
    }

    /// <summary>
    /// [ONLINE] Replays a remote member's command stream. Commands are applied in tick order, one per frame;
    /// late or duplicate ticks are dropped. When the buffer runs dry the last movement is held for
    /// <see cref="HoldSeconds"/> (one-shot buttons are never repeated), then the member stops.
    /// </summary>
    public sealed class NetworkInput : IActorInput
    {
        public const float HoldSeconds = 0.25f;
        readonly SortedList<uint, NetCommand> buffer = new SortedList<uint, NetCommand>();
        uint lastTick;
        bool any;
        Vector2 lastMove, lastAim;
        bool lastFaceAim;
        float lastAt;

        public int Buffered => buffer.Count;
        public uint LastTick => lastTick;
        public int Applied { get; private set; }

        public void Enqueue(NetCommand cmd)
        {
            if (any && cmd.tick <= lastTick) return;
            buffer[cmd.tick] = cmd;
        }

        public ActorCommand Read(PlayerController self)
        {
            if (buffer.Count > 0)
            {
                var n = buffer.Values[0];
                buffer.RemoveAt(0);
                lastTick = n.tick;
                any = true;
                Applied++;
                var c = n.Unpack();
                lastMove = c.move;
                lastAim = c.aim;
                lastFaceAim = c.faceAim;
                lastAt = Time.time;
                return c;
            }
            var hold = ActorCommand.None;
            if (Time.time - lastAt < HoldSeconds)
            {
                hold.move = lastMove;
                hold.aim = lastAim;
                hold.faceAim = lastFaceAim;
            }
            return hold;
        }
    }
}
