using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotRPG
{
    /// <summary>
    /// [F2] Development transport between two game windows on one PC: plain UDP on 127.0.0.1.
    /// Peer 0 (host) listens on <see cref="HostPort"/>, peer 1 (join) on <see cref="HostPort"/>+1.
    /// Wire format: [channel byte][payload]. Non-blocking; nothing leaves the machine.
    /// The release build swaps this for Steam P2P (PLAN_ONLINE O3) behind the same <see cref="ITransport"/>.
    /// </summary>
    public sealed class UdpTransport : ITransport, IDisposable
    {
        public const int HostPort = 47777;
        /// <summary>[PARTY] Dev party runs (PARTY_TRANSPORT=dev), separate from the village presence pipe.</summary>
        public const int PartyPort = 47787;
        /// <summary>No packet for this long = the other window is gone.</summary>
        const double TimeoutSeconds = 3.0;

        readonly UdpClient socket;
        readonly IPEndPoint remote;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Queue<NetPacket> inbox = new Queue<NetPacket>();
        double lastHeard = -99.0;

        public int LocalPeer { get; }
        public bool IsConnected => clock.Elapsed.TotalSeconds - lastHeard < TimeoutSeconds;
        public IReadOnlyList<int> Peers => IsConnected ? new[] { 1 - LocalPeer } : Array.Empty<int>();
        public string Kind => "dev";
        public int HostPeer => 0;
        public int Sent { get; private set; }
        public int Received { get; private set; }

        /// <param name="basePort">Host port (the joiner uses +1). Village presence uses <see cref="HostPort"/>, party runs <see cref="PartyPort"/>.</param>
        public UdpTransport(bool host, int basePort = HostPort)
        {
            LocalPeer = host ? 0 : 1;
            int localPort = basePort + LocalPeer;
            int remotePort = basePort + (1 - LocalPeer);
            socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, localPort));
            socket.Client.Blocking = false;
            // Windows: a send to a closed port makes the next receive throw (WSAECONNRESET). Turn that off.
            try { socket.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch (Exception) { }
            remote = new IPEndPoint(IPAddress.Loopback, remotePort);
        }

        public void Send(int toPeer, byte channel, byte[] data)
        {
            if (toPeer == LocalPeer) return;
            var buf = new byte[data.Length + 1];
            buf[0] = channel;
            Buffer.BlockCopy(data, 0, buf, 1, data.Length);
            try { socket.Send(buf, buf.Length, remote); Sent++; }
            catch (SocketException) { } // the other window is not up yet: UDP just drops it
        }

        public bool TryReceive(out NetPacket packet)
        {
            Tick();
            if (inbox.Count > 0) { packet = inbox.Dequeue(); return true; }
            packet = default;
            return false;
        }

        /// <summary>Drains the socket into the inbox.</summary>
        public void Tick()
        {
            try
            {
                while (socket.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = socket.Receive(ref from);
                    if (buf == null || buf.Length < 1) continue;
                    var data = new byte[buf.Length - 1];
                    Buffer.BlockCopy(buf, 1, data, 0, data.Length);
                    inbox.Enqueue(new NetPacket { from = 1 - LocalPeer, channel = buf[0], data = data });
                    lastHeard = clock.Elapsed.TotalSeconds;
                    Received++;
                }
            }
            catch (SocketException) { } // Windows reports "port unreachable" for an earlier send; ignore
        }

        public void Dispose() => socket?.Close();
    }
}
