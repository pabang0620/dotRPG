using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER 5] The chat / push WebSocket (Docs/server/phase5_api.md §3). JSON text frames; the first
    /// frame authenticates (hello with the access token, character and the last seq seen). A background
    /// task reads frames into a queue that <see cref="Pump"/> empties on the main thread; sends go through
    /// one ordered queue. Reconnects with backoff (1-30 s, jittered); some close codes mean "stop".
    /// Desktop only (ClientWebSocket does not exist in WebGL).
    /// </summary>
    public sealed class ChatSocket
    {
        const int PingMs = 15000;
        static readonly float[] Backoff = { 1f, 2f, 4f, 8f, 16f, 30f };

        readonly string characterId;
        ClientWebSocket ws;
        CancellationTokenSource cts;
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<int> closes = new ConcurrentQueue<int>();
        volatile bool open;
        bool stopped, connecting, ready;
        int attempt, pingN;
        float retryAt, pingTimer, connectedSince;

        /// <summary>Last chat seq received (sent as since on reconnect).</summary>
        public long LastSeq;
        public bool Ready => ready && open;
        /// <summary>Set when the server said not to come back (another login, ban, outdated client).</summary>
        public string StopReason { get; private set; }

        /// <summary>One parsed server frame (t = type).</summary>
        public event Action<string, Dictionary<string, object>> Frame;
        public event Action<bool> ReadyChanged;

        public ChatSocket(string character) { characterId = character; }

        static string WsUrl
        {
            get
            {
                string b = ApiClient.Instance.BaseUrl;
                if (b.StartsWith("https://")) return "wss://" + b.Substring(8) + "/ws";
                if (b.StartsWith("http://")) return "ws://" + b.Substring(7) + "/ws";
                return b + "/ws";
            }
        }

        public void Start()
        {
            stopped = false;
            retryAt = 0f;
        }

        public void Stop()
        {
            stopped = true;
            SetReady(false);
            var socket = ws;
            ws = null;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            if (socket != null && socket.State == WebSocketState.Open)
            {
                try { _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { }
            }
            open = false;
        }

        public void Send(string type, Dictionary<string, object> fields)
        {
            var frame = new Dictionary<string, object> { ["t"] = type };
            if (fields != null) foreach (var kv in fields) frame[kv.Key] = kv.Value;
            outbox.Enqueue(MiniJson.Write(frame));
        }

        void SetReady(bool value)
        {
            if (ready == value) return;
            ready = value;
            ReadyChanged?.Invoke(value);
        }

        /// <summary>Main thread, every frame: connects, pings, and hands received frames to <see cref="Frame"/>.</summary>
        public void Pump(float dt)
        {
            // Frames first: a bye (with its wait) is read before the close it announces.
            while (inbox.TryDequeue(out var text))
            {
                Dictionary<string, object> d;
                try { d = MiniJson.Parse(text) as Dictionary<string, object>; }
                catch (FormatException) { continue; }
                if (d == null) continue;
                string t = MiniJson.Str(d, "t");
                if (t == "ready") { SetReady(true); connectedSince = Time.realtimeSinceStartup; }
                else if (t == "token.expiring") RenewToken();
                else if (t == "bye" && d.TryGetValue("reconnect", out var rc) && rc is bool again && !again) StopReason = MiniJson.Str(d, "reason", "");
                else if (t == "bye")
                {
                    // [SERVER 7] Maintenance or an operator kick: come back after the server's suggested wait.
                    string reason = MiniJson.Str(d, "reason");
                    if (reason == "MAINTENANCE") GameEvents.RaiseToast("서버 점검이 시작되었습니다. 점검이 끝나면 다시 연결합니다.");
                    float wait = MiniJson.Int(d, "retry_after_ms") / 1000f;
                    if (wait > 0f) retryAt = Time.realtimeSinceStartup + wait;
                }
                Frame?.Invoke(t, d);
            }
            while (closes.TryDequeue(out int code)) OnClosed(code);
            if (stopped || StopReason != null) return;
            if (!open && !connecting && Time.realtimeSinceStartup >= retryAt) Connect();
            if (!Ready) return;
            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = PingMs / 1000f;
                Send("ping", new Dictionary<string, object> { ["n"] = ++pingN });
            }
            // Stable for a minute: the next drop starts the backoff from the beginning.
            if (attempt > 0 && Time.realtimeSinceStartup - connectedSince > 60f) attempt = 0;
        }

        void RenewToken()
        {
            var api = ApiClient.Instance;
            api.StartCoroutine(api.RefreshOnce(ok =>
            {
                if (ok) Send("auth", new Dictionary<string, object> { ["token"] = api.AccessToken });
            }));
        }

        async void Connect()
        {
            connecting = true;
            var socket = new ClientWebSocket();
            var source = new CancellationTokenSource();
            ws = socket;
            cts = source;
            try
            {
                await socket.ConnectAsync(new Uri(WsUrl), source.Token);
            }
            catch (Exception)
            {
                connecting = false;
                ScheduleRetry();
                return;
            }
            connecting = false;
            if (stopped || ws != socket) { try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (Exception) { } return; }
            open = true;
            pingTimer = PingMs / 1000f;
            // hello first (nothing else is accepted before it); queued sends follow after ready.
            var hello = new Dictionary<string, object>
            {
                ["t"] = "hello", ["v"] = 1, ["token"] = ApiClient.Instance.AccessToken ?? "",
                ["client_version"] = ApiClient.ClientVersion, ["character_id"] = characterId,
                ["since"] = LastSeq > 0 ? (object)LastSeq : null,
            };
            _ = Task.Run(() => SendLoop(socket, MiniJson.Write(hello), source.Token));
            _ = Task.Run(() => ReceiveLoop(socket, source.Token));
        }

        async Task SendLoop(ClientWebSocket socket, string first, CancellationToken token)
        {
            try
            {
                await SendText(socket, first, token);
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    if (outbox.TryDequeue(out var text)) await SendText(socket, text, token);
                    else await Task.Delay(20, token);
                }
            }
            catch (Exception) { /* the receive loop reports the close */ }
        }

        static Task SendText(ClientWebSocket socket, string text, CancellationToken token) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, token);

        async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
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
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    inbox.Enqueue(sb.ToString());
                    sb.Clear();
                }
            }
            catch (Exception) { }
            open = false;
            closes.Enqueue(code);
        }

        /// <summary>Close codes from phase5_api §3.7.</summary>
        void OnClosed(int code)
        {
            SetReady(false);
            if (stopped) return;
            switch (code)
            {
                case 4001: StopReason = StopReason ?? "다른 곳에서 접속했습니다."; break;
                case 4003: StopReason = StopReason ?? "이용이 정지되었습니다."; break;
                case 4004: case 4005: case 4010: StopReason = StopReason ?? "채팅 서버 인증에 실패했습니다."; break;
                case 4426: StopReason = StopReason ?? "게임을 업데이트해 주세요."; break;
                case 4002: // token expired: refresh, then come back right away
                    var api = ApiClient.Instance;
                    api.StartCoroutine(api.RefreshOnce(ok => { retryAt = 0f; if (!ok) StopReason = "다시 로그인해 주세요."; }));
                    return;
            }
            if (StopReason != null) { GameEvents.RaiseToast(StopReason); return; }
            if (retryAt > Time.realtimeSinceStartup) return; // the bye frame already set the wait
            ScheduleRetry();
        }

        void ScheduleRetry()
        {
            float wait = Backoff[Mathf.Min(attempt, Backoff.Length - 1)] * UnityEngine.Random.Range(0.8f, 1.2f);
            attempt++;
            retryAt = Time.realtimeSinceStartup + wait;
        }
    }
}
