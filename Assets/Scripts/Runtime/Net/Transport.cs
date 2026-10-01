using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>[ONLINE] One received message.</summary>
    public struct NetPacket
    {
        public int from;
        public byte channel;
        public byte[] data;
    }

    /// <summary>[ONLINE] Message channels.</summary>
    public static class NetChannel
    {
        /// <summary>Payload: party slot byte + <see cref="NetCommand"/>.</summary>
        public const byte Input = 1;
        public const byte Chat = 2;
    }

    /// <summary>
    /// [ONLINE] Unreliable-or-reliable message pipe between peers. The real build plugs Steam P2P in here
    /// (PLAN_ONLINE O3); development uses <see cref="LoopbackTransport"/>.
    /// </summary>
    public interface ITransport
    {
        int LocalPeer { get; }
        bool IsConnected { get; }
        void Send(int toPeer, byte channel, byte[] data);
        bool TryReceive(out NetPacket packet);
        /// <summary>Advances the transport one network tick (delivers delayed packets).</summary>
        void Tick();
    }

    /// <summary>
    /// [ONLINE] In-process transport: <see cref="CreatePair"/> returns two connected peers that deliver to
    /// each other after <c>latencyTicks</c> calls of <see cref="Tick"/>. No sockets, no threads.
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        struct Pending { public long deliverAt; public NetPacket packet; }

        readonly Queue<Pending> inbox = new Queue<Pending>();
        LoopbackTransport other;
        long now;
        readonly int latencyTicks;

        public int LocalPeer { get; }
        public bool IsConnected => other != null;
        public int Sent { get; private set; }
        public int Received { get; private set; }

        LoopbackTransport(int peer, int latency)
        {
            LocalPeer = peer;
            latencyTicks = latency;
        }

        public static (LoopbackTransport host, LoopbackTransport client) CreatePair(int latencyTicks = 0)
        {
            var a = new LoopbackTransport(0, latencyTicks);
            var b = new LoopbackTransport(1, latencyTicks);
            a.other = b;
            b.other = a;
            return (a, b);
        }

        public void Send(int toPeer, byte channel, byte[] data)
        {
            if (other == null || toPeer != other.LocalPeer) return;
            var copy = (byte[])data.Clone();
            other.inbox.Enqueue(new Pending { deliverAt = other.now + latencyTicks, packet = new NetPacket { from = LocalPeer, channel = channel, data = copy } });
            Sent++;
        }

        public bool TryReceive(out NetPacket packet)
        {
            if (inbox.Count > 0 && inbox.Peek().deliverAt <= now)
            {
                packet = inbox.Dequeue().packet;
                Received++;
                return true;
            }
            packet = default;
            return false;
        }

        public void Tick() => now++;

        public void Disconnect()
        {
            if (other != null) other.other = null;
            other = null;
        }
    }

    /// <summary>
    /// [ONLINE] Host side: reads input packets from a transport and feeds each member's <see cref="NetworkInput"/>.
    /// Client side: <see cref="SendCommand"/> packs and sends the local command.
    /// </summary>
    public sealed class NetCommandRouter
    {
        readonly ITransport transport;
        readonly Dictionary<int, NetworkInput> inputs = new Dictionary<int, NetworkInput>();
        public int Routed { get; private set; }

        public NetCommandRouter(ITransport transport) { this.transport = transport; }

        public void Register(int slot, NetworkInput input) => inputs[slot] = input;

        public static void SendCommand(ITransport transport, int toPeer, int slot, in ActorCommand cmd, uint tick)
        {
            var data = new byte[1 + NetCommand.Size];
            data[0] = (byte)slot;
            NetCommand.Pack(cmd, tick).Write(data, 1);
            transport.Send(toPeer, NetChannel.Input, data);
        }

        /// <summary>Drains the transport; returns the number of commands routed.</summary>
        public int Pump()
        {
            int n = 0;
            while (transport.TryReceive(out var p))
            {
                if (p.channel != NetChannel.Input || p.data == null || p.data.Length < 1 + NetCommand.Size) continue;
                if (!inputs.TryGetValue(p.data[0], out var input)) continue;
                input.Enqueue(NetCommand.Read(p.data, 1));
                n++;
            }
            Routed += n;
            return n;
        }
    }
}
