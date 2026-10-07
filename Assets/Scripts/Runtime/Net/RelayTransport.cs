using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY 8] What a relay ticket request (T1) answered.</summary>
    public sealed class RelayTicket
    {
        public string url, ticket;
        public int seat, hostSeat, hostEpoch;
    }

    /// <summary>
    /// [PARTY 8] Binary relay frames (Docs/server/phase8_api.md §4.4): PING [0x02][u32], PONG [0x03][u32],
    /// BATCH [0x04][count u8] then count x [channel u8][addr u8][len u16 LE][payload]. Little endian like
    /// <see cref="BinaryWriter"/>.
    /// </summary>
    public static class RelayFrames
    {
        public const byte Ping = 0x02, Pong = 0x03, Batch = 0x04, Everyone = 0xFF;
        public const int MaxFrameBytes = 8192, MaxPacketBytes = 6144;

        public struct Packet { public byte channel, addr; public byte[] payload; }

        public static byte[] PingFrame(uint nonce) { var b = new byte[5]; b[0] = Ping; WriteU32(b, 1, nonce); return b; }

        static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }

        /// <summary>Packs packets into as few BATCH frames as the size limit allows.</summary>
        public static List<byte[]> Pack(List<Packet> packets)
        {
            var frames = new List<byte[]>();
            int i = 0;
            while (i < packets.Count)
            {
                using (var ms = new MemoryStream())
                {
                    ms.WriteByte(Batch);
                    ms.WriteByte(0);
                    int count = 0, size = 2;
                    while (i < packets.Count && count < 255)
                    {
                        var p = packets[i];
                        int len = p.payload.Length;
                        if (size + 4 + len > MaxFrameBytes && count > 0) break;
                        ms.WriteByte(p.channel);
                        ms.WriteByte(p.addr);
                        ms.WriteByte((byte)len);
                        ms.WriteByte((byte)(len >> 8));
                        ms.Write(p.payload, 0, len);
                        size += 4 + len;
                        count++;
                        i++;
                    }
                    var frame = ms.ToArray();
                    frame[1] = (byte)count;
                    frames.Add(frame);
                }
            }
            return frames;
        }

        public static void Unpack(byte[] frame, int length, List<Packet> into)
        {
            if (length < 2 || frame[0] != Batch) return;
            int count = frame[1], o = 2;
            for (int k = 0; k < count && o + 4 <= length; k++)
            {
                byte ch = frame[o], addr = frame[o + 1];
                int len = frame[o + 2] | frame[o + 3] << 8;
                o += 4;
                if (o + len > length) return;
                var payload = new byte[len];
                Buffer.BlockCopy(frame, o, payload, 0, len);
                o += len;
                into.Add(new Packet { channel = ch, addr = addr, payload = payload });
            }
        }
    }

    /// <summary>
    /// [PARTY 8] Combat transport through our server's /relay WebSocket (phase8_api §4). Works the same on
    /// PC, mobile and builds without Steam. Peers are party seats (0-3); the server stamps who sent each
    /// packet and only lets members talk to the host. Reliable channels go in order; snapshot-like channels
    /// keep only the newest packet per (peer, channel). Everything is flushed as one BATCH every 20 ms.
    /// A background task reads frames into a queue that <see cref="Tick"/> empties on the main thread.
    /// Reconnects with a fresh ticket (0.5-8 s backoff); after three failed tries in a row <see cref="Failed"/>.
    /// </summary>
    public sealed class RelayTransport : ITransport, ITransportHealth, IDisposable
    {
        const float FlushSeconds = 0.02f, PingSeconds = 2f;
        static readonly float[] Backoff = { 0.5f, 1f, 2f, 4f, 8f };

        readonly Action<Action<RelayTicket, string>> fetchTicket;
        readonly int wire;
        ClientWebSocket ws;
        CancellationTokenSource cts;
        readonly ConcurrentQueue<string> controlIn = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<byte[]> dataIn = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<int> closes = new ConcurrentQueue<int>();
        readonly ConcurrentQueue<byte[]> outFrames = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<string> outText = new ConcurrentQueue<string>();
        readonly Queue<NetPacket> inbox = new Queue<NetPacket>();
        readonly List<RelayFrames.Packet> reliable = new List<RelayFrames.Packet>();
        readonly Dictionary<int, RelayFrames.Packet> latest = new Dictionary<int, RelayFrames.Packet>();
        readonly HashSet<int> peerSeats = new HashSet<int>();
        readonly List<RelayFrames.Packet> unpackBuffer = new List<RelayFrames.Packet>();
        volatile bool open;
        bool ready, connecting, disposed, resume;
        int seat = -1, hostSeat = -1, failures;
        uint pingNonce;
        float flushTimer, pingTimer, retryAt, pingSentAt;

        public int LocalPeer => seat;
        public string Kind => "relay";
        public int HostPeer => hostSeat;
        public bool Failed { get; private set; }
        /// <summary>Room is over or this seat was removed: no reconnect.</summary>
        public string ClosedReason { get; private set; }
        public float RttMs { get; private set; }
        public int HostEpoch { get; private set; }
        public bool IsConnected => ready && open && (seat == hostSeat || peerSeats.Contains(hostSeat));

        public IReadOnlyList<int> Peers
        {
            get
            {
                if (!ready) return Array.Empty<int>();
                if (seat != hostSeat) return peerSeats.Contains(hostSeat) ? new[] { hostSeat } : Array.Empty<int>();
                var list = new List<int>(peerSeats);
                list.Remove(seat);
                return list;
            }
        }

        /// <summary>The server moved the host role (seat, epoch).</summary>
        public event Action<int, int> HostChanged;
        public event Action<int> PeerJoined, PeerLeft;
        /// <summary>The room's transport changed (current kind) or the room closed.</summary>
        public event Action<string> TransportChanged;

        /// <param name="ticketProvider">Calls back with a fresh ticket (or null and an error code).</param>
        public RelayTransport(Action<Action<RelayTicket, string>> ticketProvider, int wireVersion)
        {
            fetchTicket = ticketProvider;
            wire = wireVersion;
        }

        // ---------------- ITransport ----------------

        public void Send(int toPeer, byte channel, byte[] data)
        {
            if (data == null || data.Length > RelayFrames.MaxPacketBytes) return;
            var p = new RelayFrames.Packet { channel = channel, addr = (byte)toPeer, payload = data };
            if (NetChannel.IsReliable(channel)) reliable.Add(p);
            else latest[toPeer * 256 + channel] = p; // newer snapshot replaces an unsent older one
        }

        public bool TryReceive(out NetPacket packet)
        {
            if (inbox.Count > 0) { packet = inbox.Dequeue(); return true; }
            packet = default;
            return false;
        }

        /// <summary>Main thread: control frames, data, flush, ping, reconnect.</summary>
        public void Tick()
        {
            if (disposed) return;
            while (controlIn.TryDequeue(out var text)) OnControl(text);
            while (dataIn.TryDequeue(out var frame)) OnData(frame);
            while (closes.TryDequeue(out int code)) OnClosed(code);
            if (ClosedReason != null) return;
            float dt = Time.unscaledDeltaTime;
            if (!open && !connecting && Time.realtimeSinceStartup >= retryAt) Connect();
            if (!ready) return;
            flushTimer -= dt;
            if (flushTimer <= 0f) { flushTimer = FlushSeconds; Flush(); }
            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = PingSeconds;
                pingSentAt = Time.realtimeSinceStartup;
                outFrames.Enqueue(RelayFrames.PingFrame(++pingNonce));
            }
        }

        void Flush()
        {
            if (reliable.Count == 0 && latest.Count == 0) return;
            var all = new List<RelayFrames.Packet>(reliable);
            all.AddRange(latest.Values);
            reliable.Clear();
            latest.Clear();
            foreach (var f in RelayFrames.Pack(all)) outFrames.Enqueue(f);
        }

        // ---------------- incoming ----------------

        void OnData(byte[] frame)
        {
            if (frame.Length >= 5 && frame[0] == RelayFrames.Pong)
            {
                float rtt = (Time.realtimeSinceStartup - pingSentAt) * 1000f;
                RttMs = RttMs <= 0f ? rtt : RttMs * 0.8f + rtt * 0.2f;
                return;
            }
            unpackBuffer.Clear();
            RelayFrames.Unpack(frame, frame.Length, unpackBuffer);
            foreach (var p in unpackBuffer) inbox.Enqueue(new NetPacket { from = p.addr, channel = p.channel, data = p.payload });
        }

        void OnControl(string text)
        {
            Dictionary<string, object> d;
            try { d = MiniJson.Parse(text) as Dictionary<string, object>; }
            catch (FormatException) { return; }
            switch (MiniJson.Str(d, "t"))
            {
                case "ready":
                    ready = true;
                    failures = 0;
                    resume = true;
                    seat = MiniJson.Int(d, "seat", seat);
                    hostSeat = MiniJson.Int(d, "host_seat", hostSeat);
                    HostEpoch = MiniJson.Int(d, "host_epoch", HostEpoch);
                    peerSeats.Clear();
                    foreach (var o in MiniJson.Arr(d, "peers") ?? new List<object>()) peerSeats.Add((int)Math.Round((double)o));
                    break;
                case "peer.joined":
                    peerSeats.Add(MiniJson.Int(d, "seat"));
                    PeerJoined?.Invoke(MiniJson.Int(d, "seat"));
                    break;
                case "peer.left":
                    peerSeats.Remove(MiniJson.Int(d, "seat"));
                    PeerLeft?.Invoke(MiniJson.Int(d, "seat"));
                    break;
                case "host.changed":
                    hostSeat = MiniJson.Int(d, "seat", hostSeat);
                    HostEpoch = MiniJson.Int(d, "epoch", HostEpoch);
                    HostChanged?.Invoke(hostSeat, HostEpoch);
                    break;
                case "transport.changed":
                    ClosedReason = "transport";
                    TransportChanged?.Invoke(MiniJson.Str(d, "current"));
                    break;
                case "room.closed":
                    ClosedReason = "room";
                    TransportChanged?.Invoke(null);
                    break;
                case "bye":
                    if (d.TryGetValue("reconnect", out var rc) && rc is bool again && !again && ClosedReason == null) ClosedReason = MiniJson.Str(d, "reason", "closed");
                    float wait = MiniJson.Int(d, "retry_after_ms") / 1000f;
                    if (wait > 0f) retryAt = Time.realtimeSinceStartup + wait;
                    break;
            }
        }

        /// <summary>Close codes from phase8_api §2.6 (mapping).</summary>
        void OnClosed(int code)
        {
            ready = false;
            if (disposed || ClosedReason != null) return;
            switch (code)
            {
                case 4011: ClosedReason = "removed"; TransportChanged?.Invoke(null); return;
                case 4012: ClosedReason = "room"; TransportChanged?.Invoke(null); return;
                case 4014: ClosedReason = "transport"; TransportChanged?.Invoke(null); return;
                case 4015: ClosedReason = "wire"; GameEvents.RaiseToast("파티원과 게임 버전이 다릅니다."); return;
                case 4003: case 4426: ClosedReason = "account"; return;
            }
            if (++failures >= 3) Failed = true;
            if (retryAt > Time.realtimeSinceStartup) return;
            float wait = Backoff[Mathf.Min(failures - 1, Backoff.Length - 1)] * UnityEngine.Random.Range(0.8f, 1.2f);
            retryAt = Time.realtimeSinceStartup + wait;
        }

        // ---------------- socket ----------------

        void Connect()
        {
            connecting = true;
            fetchTicket((t, error) =>
            {
                if (disposed) return;
                if (t == null)
                {
                    connecting = false;
                    if (error == "ROOM_CLOSED" || error == "ROOM_NOT_FOUND") { ClosedReason = "room"; TransportChanged?.Invoke(null); return; }
                    if (error == "TRANSPORT_NOT_RELAY") { ClosedReason = "transport"; TransportChanged?.Invoke(null); return; }
                    if (++failures >= 3) Failed = true;
                    retryAt = Time.realtimeSinceStartup + Backoff[Mathf.Min(failures - 1, Backoff.Length - 1)];
                    return;
                }
                seat = t.seat;
                hostSeat = t.hostSeat;
                Open(t);
            });
        }

        async void Open(RelayTicket t)
        {
            var socket = new ClientWebSocket();
            var source = new CancellationTokenSource();
            ws = socket;
            cts = source;
            try { await socket.ConnectAsync(new Uri(t.url), source.Token); }
            catch (Exception)
            {
                connecting = false;
                closes.Enqueue(1006);
                return;
            }
            connecting = false;
            if (disposed || ws != socket) { try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (Exception) { } return; }
            open = true;
            var hello = new Dictionary<string, object>
            {
                ["t"] = "hello", ["v"] = 1, ["ticket"] = t.ticket, ["client_version"] = ApiClient.ClientVersion, ["wire"] = wire, ["resume"] = resume,
            };
            outText.Enqueue(MiniJson.Write(hello));
            _ = Task.Run(() => SendLoop(socket, source.Token));
            _ = Task.Run(() => ReceiveLoop(socket, source.Token));
        }

        async Task SendLoop(ClientWebSocket socket, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    if (outText.TryDequeue(out var text))
                        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, token);
                    else if (outFrames.TryDequeue(out var frame))
                        await socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, token);
                    else await Task.Delay(5, token);
                }
            }
            catch (Exception) { }
        }

        async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16384];
            var ms = new MemoryStream();
            int code = 1006;
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        code = socket.CloseStatus.HasValue ? (int)socket.CloseStatus.Value : 1000;
                        break;
                    }
                    ms.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var bytes = ms.ToArray();
                    ms.SetLength(0);
                    if (r.MessageType == WebSocketMessageType.Text) controlIn.Enqueue(Encoding.UTF8.GetString(bytes));
                    else dataIn.Enqueue(bytes);
                }
            }
            catch (Exception) { }
            open = false;
            closes.Enqueue(code);
        }

        /// <summary>Leaves the room on purpose (seat given back) and closes.</summary>
        public void Leave()
        {
            if (open) outText.Enqueue("{\"t\":\"leave\"}");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ready = false;
            var socket = ws;
            ws = null;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            if (socket != null && socket.State == WebSocketState.Open)
            {
                try { _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { }
            }
            open = false;
        }
    }
}
