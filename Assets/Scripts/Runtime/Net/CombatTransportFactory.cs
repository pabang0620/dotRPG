using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>[PARTY 8] Where a combat room lives and how to reach it (from a RunView or a field session view).</summary>
    public sealed class RoomTransportInfo
    {
        /// <summary>"run" (party dungeon) or "field" (field session).</summary>
        public string kind;
        public string roomId;
        /// <summary>Transport the server picked for the room: "relay", "steam" or "dev".</summary>
        public string current = "relay";
        public int epoch;
        public int seat;
        public bool amHost;
        public string hostSteamId, entryToken;
        public byte[] hostKey;
        /// <summary>The host's seat, and each seat's SteamID64 (Steam transport maps connections to seats).</summary>
        public int hostSeat;
        public readonly Dictionary<int, ulong> seatSteam = new Dictionary<int, ulong>();
        /// <summary>Every member's seat (people only): the host's AI fill the other seats.</summary>
        public readonly HashSet<int> seats = new HashSet<int>();

        /// <summary>Reads the server's member list (seat or slot, steam_id).</summary>
        public void ReadMembers(List<object> members, string hostCharacterId)
        {
            seatSteam.Clear();
            seats.Clear();
            foreach (var m in members ?? new List<object>())
            {
                int s = MiniJson.Has(m, "seat") ? MiniJson.Int(m, "seat") : MiniJson.Int(m, "slot");
                seats.Add(s);
                if (ulong.TryParse(MiniJson.Str(m, "steam_id"), out var id)) seatSteam[s] = id;
                if (MiniJson.Str(m, "character_id") == hostCharacterId) hostSeat = s;
            }
        }
    }

    /// <summary>
    /// [PARTY 8] Builds the combat transport the server chose for a room (phase8_api §5): our relay by
    /// default (PC, mobile, no Steam needed), Steam P2P when the server says so and Steam is running,
    /// the two-window UDP pipe only in development builds. When the chosen one cannot connect the
    /// session asks the server to switch (T2) and builds the next one.
    /// </summary>
    public static class CombatTransportFactory
    {
        static ApiClient Api => ApiClient.Instance;

        public static ITransport Create(RoomTransportInfo info)
        {
            switch (info.current)
            {
                case "steam":
                    var steam = SteamBridge.Current;
                    if (steam != null && steam.Ready) return steam.CreateTransport(info);
                    return null; // the session asks the server for the next transport
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                case "dev":
                    return new UdpTransport(info.amHost, UdpTransport.PartyPort);
#endif
                default:
                    return new RelayTransport(done => Ticket(info, done), PartyNet.WireVersion);
            }
        }

        /// <summary>T1: a 60-second single-use ticket for this room (asked again on every reconnect).</summary>
        static void Ticket(RoomTransportInfo info, Action<RelayTicket, string> done)
        {
            var session = OnlineSession.Current;
            if (session == null || string.IsNullOrEmpty(session.ActiveCharacter)) { done(null, "OFFLINE"); return; }
            Api.Post($"/characters/{session.ActiveCharacter}/rooms/{info.kind}/{info.roomId}/relay-ticket", new Dictionary<string, object>(), r =>
            {
                if (!r.ok) { done(null, r.code); return; }
                var room = MiniJson.Obj(r.data, "room");
                done(new RelayTicket
                {
                    url = MiniJson.Str(r.data, "relay_url"),
                    ticket = MiniJson.Str(r.data, "ticket"),
                    seat = MiniJson.Int(room, "seat", info.seat),
                    hostSeat = MiniJson.Int(room, "host_seat", 0),
                    hostEpoch = MiniJson.Int(room, "host_epoch", 1),
                }, null);
            });
        }

        /// <summary>T2: the current transport failed; the server answers with the next one (or the one another member already switched to).</summary>
        public static void RequestSwitch(RoomTransportInfo info, string failed, Action<string, int> done)
        {
            var session = OnlineSession.Current;
            if (session == null) { done?.Invoke(null, 0); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["failed"] = failed, ["epoch"] = info.epoch };
            Api.Post($"/characters/{session.ActiveCharacter}/rooms/{info.kind}/{info.roomId}/transport", body, r =>
            {
                var t = r.ok ? MiniJson.Obj(r.data, "transport") : MiniJson.Obj(r.errors, "current") ?? r.errors;
                string current = MiniJson.Str(t, "current");
                done?.Invoke(current, MiniJson.Int(t, "epoch", info.epoch));
            });
        }
    }

    /// <summary>
    /// [PARTY 8] Steam features live in the separate DotRPG.Steam assembly (Standalone builds with
    /// STEAMWORKS_NET only); it registers itself here. Null = no Steam, the relay is used.
    /// </summary>
    public interface ISteamBridge
    {
        bool Ready { get; }
        ulong SteamId { get; }
        uint AppId { get; }
        void GetAuthTicket(string identity, Action<string> done);
        ITransport CreateTransport(RoomTransportInfo info);
    }

    public static class SteamBridge
    {
        public static ISteamBridge Current;
    }
}
