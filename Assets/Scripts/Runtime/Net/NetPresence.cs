using System.IO;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [F2] Two windows on one PC see each other: each sends where its hero stands ten times a second over
    /// <see cref="UdpTransport"/> and draws the other hero as a ghost on the same map. General and party
    /// chat lines are relayed both ways, so the chat box shows the other window's messages.
    /// Started with <c>-dotrpgNet host</c> or <c>-dotrpgNet join</c> (development only).
    /// </summary>
    public class NetPresence : MonoBehaviour
    {
        public static NetPresence Instance { get; private set; }

        const float SendInterval = 0.1f;

        UdpTransport transport;
        float sendTimer;
        GameObject ghost;
        CharacterAnimator ghostAnim;
        CharacterClass ghostClass = (CharacterClass)(-1);
        Vector2 ghostTarget;
        string ghostMap;
        bool relaying, wasConnected;

        public bool IsHost => transport != null && transport.LocalPeer == 0;
        public bool Connected => transport != null && transport.IsConnected;
        public string RemoteName { get; private set; }
        public string RemoteMap => ghostMap;
        public Vector2 RemotePosition => ghostTarget;
        public bool GhostVisible => ghost != null && ghost.activeSelf;
        public int Sent => transport?.Sent ?? 0;
        public int Received => transport?.Received ?? 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-dotrpgNet") Begin(args[i + 1] == "host");
        }

        public static NetPresence Begin(bool host)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NetPresence");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<NetPresence>();
            p.transport = new UdpTransport(host);
            Instance = p;
            OnlineServices.Chat.Received += p.Relay;
            Debug.Log($"[NET] presence started as {(host ? "host" : "join")} on port {UdpTransport.HostPort + p.transport.LocalPeer}");
            return p;
        }

        void OnDestroy()
        {
            if (OnlineServices.Chat != null) OnlineServices.Chat.Received -= Relay;
            transport?.Dispose();
            if (Instance == this) Instance = null;
        }

        // Own general / party lines go to the other window.
        void Relay(ChatLine line)
        {
            if (OnlineServices.Chat.IsOnline) return; // [SERVER 5] the chat server delivers it already
            if (relaying || !line.mine || (line.channel != ChatChannel.General && line.channel != ChatChannel.Party)) return;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((byte)line.channel);
                w.Write(line.from ?? "");
                w.Write(line.text ?? "");
                transport.Send(1 - transport.LocalPeer, NetChannel.Chat, ms.ToArray());
            }
        }

        void Update()
        {
            if (transport == null) return;
            sendTimer -= Time.unscaledDeltaTime;
            var me = Game.Player;
            if (sendTimer <= 0f && me != null && Game.Session != null)
            {
                sendTimer = SendInterval;
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms, Encoding.UTF8))
                {
                    w.Write(Game.Session.MapId ?? "");
                    w.Write(me.Position.x);
                    w.Write(me.Position.y);
                    w.Write((byte)me.Facing);
                    w.Write((byte)me.Class);
                    w.Write(Game.Session.Journal?.PlayerName ?? QuestJournal.DefaultName);
                    transport.Send(1 - transport.LocalPeer, NetChannel.Presence, ms.ToArray());
                }
            }

            while (transport.TryReceive(out var p))
            {
                try
                {
                    using (var r = new BinaryReader(new MemoryStream(p.data), Encoding.UTF8))
                    {
                        if (p.channel == NetChannel.Presence) ReadPresence(r);
                        else if (p.channel == NetChannel.Chat) ReadChat(r);
                    }
                }
                catch (EndOfStreamException) { } // malformed packet from an older build: skip
            }
            MoveGhost();
            // [F4] PLAN_ONLINE §2.2: a player who drops is played by AI until the run ends.
            bool now = transport.IsConnected;
            if (now != wasConnected && !string.IsNullOrEmpty(RemoteName))
                GameEvents.RaiseToast(now ? $"{RemoteName}님이 연결되었습니다." : $"{RemoteName}님 연결 끊김 - AI가 대신 싸우는 중");
            wasConnected = now;
        }

        void ReadPresence(BinaryReader r)
        {
            ghostMap = r.ReadString();
            ghostTarget = new Vector2(r.ReadSingle(), r.ReadSingle());
            var facing = (Facing)r.ReadByte();
            var cls = (CharacterClass)r.ReadByte();
            RemoteName = r.ReadString();
            EnsureGhost(cls);
            bool moving = ((Vector2)ghost.transform.position - ghostTarget).sqrMagnitude > 0.0004f;
            ghostAnim.Play(moving ? CharacterAnim.Walk : CharacterAnim.Idle, facing);
        }

        void ReadChat(BinaryReader r)
        {
            if (OnlineServices.Chat.IsOnline) return;
            var channel = (ChatChannel)r.ReadByte();
            string from = r.ReadString();
            string text = r.ReadString();
            relaying = true;
            try { OnlineServices.Chat.Deliver(new ChatLine { channel = channel, from = from, text = text }); }
            finally { relaying = false; }
        }

        void EnsureGhost(CharacterClass cls)
        {
            if (ghost != null && cls == ghostClass) return;
            if (ghost == null)
            {
                ghost = new GameObject("RemotePlayer");
                DontDestroyOnLoad(ghost);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(ghost.transform, false);
                var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
                shadow.transform.SetParent(visual, false);
                shadow.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                shadow.sprite = Game.Art.Get("shadow");
                shadow.sortingOrder = -2;
                HdMaterial.Apply(shadow);
                var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(visual, false);
                sr.color = new Color(0.85f, 0.95f, 1f, 1f); // a faint blue tint marks the other window's hero
                ghostAnim = ghost.AddComponent<CharacterAnimator>();
                ghostAnim.Setup(CharacterClassInfo.Get(cls).Look, sr);
                ghost.AddComponent<YSort>().Configure(false);
                ghost.transform.position = ghostTarget;
            }
            else ghostAnim.SetLook(CharacterClassInfo.Get(cls).Look);
            ghostClass = cls;
        }

        void MoveGhost()
        {
            if (ghost == null) return;
            bool show = transport.IsConnected && Game.Session != null && ghostMap == Game.Session.MapId;
            if (ghost.activeSelf != show) { ghost.SetActive(show); if (show) ghost.transform.position = ghostTarget; }
            if (!show) return;
            // Smooth toward the last reported spot; snap after a teleport.
            Vector2 pos = ghost.transform.position;
            ghost.transform.position = (pos - ghostTarget).sqrMagnitude > 9f ? ghostTarget : Vector2.Lerp(pos, ghostTarget, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        }
    }
}
