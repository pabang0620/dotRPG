using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY 8] Combat over Steam networking sockets (ISteamNetworkingSockets P2P through Valve's relay,
    /// phase8_api §5). Star: the host listens, members connect to the host only. Every connection is
    /// mapped to a party seat with the server's member list (seat -> SteamID); a connection from a
    /// SteamID not on the list is refused. Packets carry the channel in their first byte. Reliable
    /// channels use Steam's reliable flag. Connecting longer than 12 s marks the transport failed so the
    /// session switches to our relay.
    /// </summary>
    public sealed class SteamP2PTransport : ITransport, ITransportHealth, IDisposable
    {
        const int VirtualPort = 7;
        const float ConnectTimeoutSeconds = 12f;
        /// <summary>Host side: most packets / bytes one member may send per second, and the inbox size.</summary>
        const int MemberPacketsPerSecond = 120, MemberBytesPerSecond = 32 * 1024, MaxInbox = 2048;

        readonly Dictionary<int, (float windowStart, int packets, int bytes)> budget = new Dictionary<int, (float, int, int)>();

        readonly RoomTransportInfo info;
        readonly ulong mySteam;
        readonly bool host;
        readonly Dictionary<uint, int> connSeat = new Dictionary<uint, int>();
        readonly Dictionary<int, HSteamNetConnection> seatConn = new Dictionary<int, HSteamNetConnection>();
        readonly Queue<NetPacket> inbox = new Queue<NetPacket>();
        readonly IntPtr[] messages = new IntPtr[64];
        HSteamListenSocket listen = HSteamListenSocket.Invalid;
        HSteamNetPollGroup poll = HSteamNetPollGroup.Invalid;
        Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        float startedAt;
        bool disposed;

        public int LocalPeer => info.seat;
        public string Kind => "steam";
        public int HostPeer => info.hostSeat;
        public bool Failed { get; private set; }
        public bool IsConnected => host || seatConn.ContainsKey(info.hostSeat);
        public IReadOnlyList<int> Peers => new List<int>(seatConn.Keys);

        public SteamP2PTransport(RoomTransportInfo roomInfo, ulong localSteamId)
        {
            info = roomInfo;
            mySteam = localSteamId;
            host = roomInfo.amHost;
            startedAt = Time.realtimeSinceStartup;
            statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
            poll = SteamNetworkingSockets.CreatePollGroup();
            if (host)
            {
                listen = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);
                return;
            }
            if (!info.seatSteam.TryGetValue(info.hostSeat, out var hostSteam)) { Failed = true; return; }
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(hostSteam));
            var conn = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, null);
            connSeat[conn.m_HSteamNetConnection] = info.hostSeat;
            SteamNetworkingSockets.SetConnectionPollGroup(conn, poll);
        }

        int SeatOf(ulong steam)
        {
            foreach (var kv in info.seatSteam) if (kv.Value == steam) return kv.Key;
            return -1;
        }

        void OnStatus(SteamNetConnectionStatusChangedCallback_t s)
        {
            if (disposed) return;
            var conn = s.m_hConn;
            ulong remote = s.m_info.m_identityRemote.GetSteamID().m_SteamID;
            switch (s.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (!host) return; // our own outgoing connection
                    int seat = SeatOf(remote);
                    // Only party members the server listed for this room, never anyone else.
                    if (seat < 0 || seat == info.seat) { SteamNetworkingSockets.CloseConnection(conn, 0, "not in this party", false); return; }
                    if (SteamNetworkingSockets.AcceptConnection(conn) != EResult.k_EResultOK) return;
                    connSeat[conn.m_HSteamNetConnection] = seat;
                    SteamNetworkingSockets.SetConnectionPollGroup(conn, poll);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (connSeat.TryGetValue(conn.m_HSteamNetConnection, out int s2)) seatConn[s2] = conn;
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    if (connSeat.TryGetValue(conn.m_HSteamNetConnection, out int s3))
                    {
                        seatConn.Remove(s3);
                        connSeat.Remove(conn.m_HSteamNetConnection);
                        if (!host) Failed = true; // lost the host: the session asks for the next transport
                    }
                    SteamNetworkingSockets.CloseConnection(conn, 0, "closed", false);
                    break;
            }
        }

        public void Send(int toPeer, byte channel, byte[] data)
        {
            if (!seatConn.TryGetValue(toPeer, out var conn) || data == null) return;
            var buf = new byte[data.Length + 1];
            buf[0] = channel;
            Buffer.BlockCopy(data, 0, buf, 1, data.Length);
            int flags = NetChannel.IsReliable(channel) ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable;
            var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { SteamNetworkingSockets.SendMessageToConnection(conn, handle.AddrOfPinnedObject(), (uint)buf.Length, flags, out _); }
            finally { handle.Free(); }
        }

        public bool TryReceive(out NetPacket packet)
        {
            if (inbox.Count > 0) { packet = inbox.Dequeue(); return true; }
            packet = default;
            return false;
        }

        public void Tick()
        {
            if (disposed) return;
            if (!host && !seatConn.ContainsKey(info.hostSeat) && Time.realtimeSinceStartup - startedAt > ConnectTimeoutSeconds) Failed = true;
            PruneRemoved(); // the session keeps info.seatSteam current from heartbeats and party pushes
            int n = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(poll, messages, messages.Length);
            for (int i = 0; i < n; i++)
            {
                var msg = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                if (msg.m_cbSize >= 1 && connSeat.TryGetValue(msg.m_conn.m_HSteamNetConnection, out int seat) && Allowed(seat, msg.m_cbSize))
                {
                    var bytes = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);
                    var data = new byte[bytes.Length - 1];
                    Buffer.BlockCopy(bytes, 1, data, 0, data.Length);
                    // Same rules as the relay: a member may only send member channels to the host,
                    // and a member only accepts what the host sends.
                    bool fromHostSeat = seat == info.hostSeat;
                    bool ok = host ? IsMemberChannel(bytes[0]) : fromHostSeat;
                    if (ok && inbox.Count < MaxInbox) inbox.Enqueue(new NetPacket { from = seat, channel = bytes[0], data = data });
                }
                SteamNetworkingMessage_t.Release(messages[i]);
            }
        }

        static bool IsMemberChannel(byte ch) =>
            ch == NetChannel.Input || ch == NetChannel.MemberState || ch == NetChannel.Event || ch == NetChannel.Control;

        /// <summary>Per-member packet and byte budget per second (host side); members trust only the host.</summary>
        bool Allowed(int seat, int size)
        {
            if (!host) return true;
            float now = Time.realtimeSinceStartup;
            (float windowStart, int packets, int bytes) b = budget.TryGetValue(seat, out var cur) && now - cur.windowStart < 1f ? cur : (now, 0, 0);
            b.packets++;
            b.bytes += size;
            budget[seat] = b;
            return b.packets <= MemberPacketsPerSecond && b.bytes <= MemberBytesPerSecond;
        }

        /// <summary>The server's member list changed (kick, leave): drop connections of SteamIDs no longer on it.</summary>
        void PruneRemoved()
        {
            if (!host) return;
            var drop = new List<int>();
            foreach (var kv in seatConn)
                if (!info.seatSteam.ContainsKey(kv.Key)) drop.Add(kv.Key);
            foreach (int s in drop)
            {
                SteamNetworkingSockets.CloseConnection(seatConn[s], 0, "removed from party", false);
                seatConn.Remove(s);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var conn in seatConn.Values) SteamNetworkingSockets.CloseConnection(conn, 0, "bye", true);
            seatConn.Clear();
            if (listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
            if (poll != HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(poll);
            statusCallback?.Dispose();
        }
    }
}
