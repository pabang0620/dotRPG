using System;
using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    /// <summary>
    /// [SERVER 5] Chat, friends, blocks and reports through the server (phase5_api). Lines arrive over
    /// <see cref="ChatSocket"/>; own lines show at once and are replaced by the server's filtered text on
    /// ack (or removed with a toast on refusal). The window keeps talking in names, so this service keeps
    /// a name -> character id map from every line, friend and block it sees; friends, blocks and reports
    /// are REST calls whose result comes back as a toast.
    /// </summary>
    public sealed class ServerChatService : IChatService
    {
        sealed class Pending { public ChatLine line; public Dictionary<string, object> frame; public float at; }

        readonly ChatSocket socket;
        readonly string characterId;
        readonly List<ChatLine> lines = new List<ChatLine>();
        readonly List<ChatFriend> friends = new List<ChatFriend>();
        readonly Dictionary<string, string> friendIds = new Dictionary<string, string>(); // name -> friendship id
        readonly HashSet<string> blocked = new HashSet<string>();
        readonly Dictionary<string, string> blockIds = new Dictionary<string, string>(); // name -> block id
        readonly Dictionary<string, string> ids = new Dictionary<string, string>(); // name -> character id
        readonly List<ChatRequest> requests = new List<ChatRequest>();
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(); // cid -> own line
        float clock, lastSent = -99f;

        public bool IsOnline => true;
        public IReadOnlyList<ChatLine> Lines => lines;
        public IReadOnlyList<ChatFriend> Friends => friends;
        public IReadOnlyCollection<string> Blocked => blocked;
        public IReadOnlyList<ChatRequest> Requests => requests;
        public event Action<ChatLine> Received;
        public event Action SocialChanged;

        static ApiClient Api => ApiClient.Instance;
        static string MyName => Game.Session?.Journal?.PlayerName ?? QuestJournal.DefaultName;

        public ServerChatService(string character)
        {
            characterId = character;
            socket = new ChatSocket(character);
            socket.Frame += OnFrame;
            socket.ReadyChanged += OnReadyChanged;
            socket.Start();
            Refresh();
        }

        public void Detach() => socket.Stop();

        public bool Connected => socket.Ready;

        // ---------------- sending ----------------

        public string Send(ChatChannel channel, string text, string whisperTo = null)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return "";
            if (channel == ChatChannel.System) return "시스템 채널에는 쓸 수 없습니다.";
            if (!socket.Ready) return socket.StopReason ?? "채팅 서버에 다시 연결하는 중입니다.";
            if (text.Length > ChatRules.MaxLength) text = text.Substring(0, ChatRules.MaxLength);
            if (clock - lastSent < ChatRules.MinInterval) return "채팅은 1초에 한 번만 보낼 수 있습니다.";
            if (channel == ChatChannel.Whisper)
            {
                if (string.IsNullOrEmpty(whisperTo)) return "귓속말 상대를 적어 주세요. (/w 이름 내용)";
                if (blocked.Contains(whisperTo)) return $"{whisperTo}님은 차단 중입니다.";
            }
            lastSent = clock;
            string cid = ApiClient.NewRequestId();
            var frame = new Dictionary<string, object> { ["cid"] = cid, ["channel"] = ChannelCode(channel), ["text"] = text };
            if (channel == ChatChannel.Whisper)
            {
                if (ids.TryGetValue(whisperTo, out var id)) frame["to"] = id;
                else frame["to_name"] = whisperTo;
            }
            var line = new ChatLine { channel = channel, from = MyName, to = whisperTo, text = ChatRules.Filter(text), time = clock, mine = true };
            pending[cid] = new Pending { line = line, frame = frame, at = clock };
            socket.Send("chat.send", frame);
            Add(line);
            return null;
        }

        static string ChannelCode(ChatChannel c) => c == ChatChannel.Party ? "party" : c == ChatChannel.Whisper ? "whisper" : "general";

        static ChatChannel ChannelOf(string code) => code == "party" ? ChatChannel.Party : code == "whisper" ? ChatChannel.Whisper : ChatChannel.General;

        public void PostSystem(string text) => Add(new ChatLine { channel = ChatChannel.System, from = "", text = text, time = clock });

        public void Deliver(ChatLine line) { } // the server pushes lines; the dev UDP relay stays off online

        void Add(ChatLine line)
        {
            if (!line.mine && line.channel != ChatChannel.System && blocked.Contains(line.from)) return;
            lines.Add(line);
            if (lines.Count > ChatRules.Keep) lines.RemoveAt(0);
            Received?.Invoke(line);
        }

        // ---------------- receiving ----------------

        void OnReadyChanged(bool ready)
        {
            if (!ready) return;
            // Lines sent just before a drop: same cid again (the server answers a replay, never twice).
            foreach (var p in pending.Values.ToList())
                if (clock - p.at < 30f) socket.Send("chat.send", p.frame);
        }

        void OnFrame(string t, Dictionary<string, object> d)
        {
            switch (t)
            {
                case "backlog":
                    if (d.TryGetValue("gap", out var g) && g is bool gap && gap) PostSystem("일부 메시지를 놓쳤습니다.");
                    foreach (var o in MiniJson.Arr(d, "lines") ?? new List<object>()) OnMessage(o as Dictionary<string, object>);
                    long cursor = (long)MiniJson.Num(d, "cursor", socket.LastSeq);
                    if (cursor > socket.LastSeq) socket.LastSeq = cursor;
                    break;
                case "chat.msg":
                    OnMessage(d);
                    break;
                case "chat.ack":
                {
                    string cid = MiniJson.Str(d, "cid");
                    if (pending.TryGetValue(cid, out var p))
                    {
                        p.line.text = MiniJson.Str(d, "text", p.line.text);
                        pending.Remove(cid);
                        Received?.Invoke(p.line); // redraw with the server's text
                    }
                    long seq = (long)MiniJson.Num(d, "seq");
                    if (seq > socket.LastSeq) socket.LastSeq = seq;
                    break;
                }
                case "chat.err":
                {
                    string cid = MiniJson.Str(d, "cid");
                    if (pending.TryGetValue(cid, out var p))
                    {
                        pending.Remove(cid);
                        lines.Remove(p.line);
                        Received?.Invoke(null);
                    }
                    GameEvents.RaiseToast(MiniJson.Str(d, "message", "채팅을 보낼 수 없습니다."));
                    break;
                }
                case "chat.sys":
                    PostSystem(MiniJson.Str(d, "text", ""));
                    break;
                case "friends.presence":
                    foreach (var o in MiniJson.Arr(d, "list") ?? new List<object>()) ApplyPresence(o as Dictionary<string, object>);
                    SocialChanged?.Invoke();
                    break;
                case "friends.changed":
                    if (MiniJson.Str(d, "reason") == "request") GameEvents.RaiseToast("친구 요청이 왔습니다. (친구 창)");
                    if (MiniJson.Str(d, "reason") == "accepted") GameEvents.RaiseToast("친구 요청이 수락되었습니다.");
                    Refresh();
                    break;
                case "party.changed":
                    if (MiniJson.Str(d, "scope") == "run") PartyRunSession.Instance?.FetchNow();
                    else PartyClient.Instance?.PollNow();
                    break;
                case "field.changed":
                    FieldSession.Instance?.FetchNow(); // [PARTY 8]
                    break;
                case "party.invite":
                    PartyClient.Instance?.OnInvite(d);
                    break;
                case "party.invite.closed":
                    PartyClient.Instance?.PollNow();
                    break;
                case "sanction":
                    GameEvents.RaiseToast(MiniJson.Str(d, "message", "운영 정책에 따른 조치가 적용되었습니다."));
                    break;
                case "error":
                    UnityEngine.Debug.LogWarning("[CHAT] " + MiniJson.Str(d, "code") + " " + MiniJson.Str(d, "message"));
                    break;
            }
        }

        void OnMessage(Dictionary<string, object> m)
        {
            if (m == null) return;
            long seq = (long)MiniJson.Num(m, "seq");
            if (seq > 0 && seq <= socket.LastSeq) return; // already shown
            if (seq > socket.LastSeq) socket.LastSeq = seq;
            var from = MiniJson.Obj(m, "from");
            var to = MiniJson.Obj(m, "to");
            string fromName = MiniJson.Str(from, "name", "");
            Remember(fromName, MiniJson.Str(from, "id"));
            Remember(MiniJson.Str(to, "name"), MiniJson.Str(to, "id"));
            Add(new ChatLine { channel = ChannelOf(MiniJson.Str(m, "channel")), from = fromName, to = MiniJson.Str(to, "name"), text = MiniJson.Str(m, "text", ""), time = clock });
        }

        void Remember(string name, string id)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id)) ids[name] = id;
        }

        public string IdOf(string name) => name != null && ids.TryGetValue(name, out var id) ? id : null;

        void ApplyPresence(Dictionary<string, object> p)
        {
            if (p == null) return;
            string fid = MiniJson.Str(p, "id");
            var f = friends.FirstOrDefault(x => friendIds.TryGetValue(x.name, out var v) && v == fid);
            var c = MiniJson.Obj(p, "character");
            if (f == null)
            {
                if (c == null) return;
                f = new ChatFriend { name = MiniJson.Str(c, "name", "") };
                friends.Add(f);
                friendIds[f.name] = fid;
            }
            f.online = p.TryGetValue("online", out var on) && on is bool b && b;
            f.where = Where(MiniJson.Obj(p, "where"));
            if (c != null) Remember(MiniJson.Str(c, "name"), MiniJson.Str(c, "id"));
        }

        static string Where(Dictionary<string, object> w)
        {
            if (w == null) return "";
            string activity = MiniJson.Str(w, "activity");
            if (activity == "dungeon") return "던전 공략 중";
            if (activity == "raid") return "레이드 중";
            var map = MapRegistry.Get(MiniJson.Str(w, "map_id"));
            return map != null ? map.displayName : "";
        }

        // ---------------- friends, blocks, reports (REST) ----------------

        /// <summary>Friends, requests and blocks again (window opened, or the server said they changed).</summary>
        public void Refresh()
        {
            Api.Get("/friends", r =>
            {
                if (!r.ok) return;
                friends.Clear();
                friendIds.Clear();
                foreach (var o in MiniJson.Arr(r.data, "friends") ?? new List<object>())
                {
                    var c = MiniJson.Obj(o, "character");
                    string name = MiniJson.Str(c, "name", "");
                    Remember(name, MiniJson.Str(c, "id"));
                    friendIds[name] = MiniJson.Str(o, "id");
                    friends.Add(new ChatFriend { name = name, online = o is Dictionary<string, object> od && od.TryGetValue("online", out var on) && on is bool b && b, where = Where(MiniJson.Obj(o, "where")) });
                }
                requests.Clear();
                foreach (var o in MiniJson.Arr(r.data, "incoming") ?? new List<object>())
                {
                    var from = MiniJson.Obj(o, "from");
                    Remember(MiniJson.Str(from, "name"), MiniJson.Str(from, "id"));
                    requests.Add(new ChatRequest { id = MiniJson.Str(o, "id"), name = MiniJson.Str(from, "name", "") });
                }
                SocialChanged?.Invoke();
            });
            Api.Get("/blocks", r =>
            {
                if (!r.ok) return;
                blocked.Clear();
                blockIds.Clear();
                foreach (var o in MiniJson.Arr(r.data, "blocks") ?? new List<object>())
                {
                    string name = MiniJson.Str(o, "name", "");
                    blocked.Add(name);
                    blockIds[name] = MiniJson.Str(o, "id");
                }
                SocialChanged?.Invoke();
            });
        }

        public bool IsFriend(string name) => friends.Any(f => f.name == name);

        public void AddFriend(string name)
        {
            string id = IdOf(name);
            if (id == null) { GameEvents.RaiseToast($"{name}님을 찾을 수 없습니다. 채팅에서 만난 모험가만 추가할 수 있습니다."); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["character_id"] = characterId, ["target"] = id };
            Api.Post("/friends/requests", body, r =>
            {
                if (!r.ok) { GameEvents.RaiseToast(r.message); return; }
                GameEvents.RaiseToast(MiniJson.Str(r.data, "status") == "accepted" ? $"{name}님과 친구가 되었습니다." : $"{name}님에게 친구 요청을 보냈습니다.");
                Refresh();
            });
        }

        public void RemoveFriend(string name)
        {
            if (!friendIds.TryGetValue(name, out var fid)) return;
            Api.Delete($"/friends/{fid}", r =>
            {
                GameEvents.RaiseToast(r.ok ? $"{name}님을 친구에서 삭제했습니다." : r.message);
                Refresh();
            });
        }

        public void RespondRequest(string requestId, bool accept)
        {
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["accept"] = accept };
            Api.Post($"/friends/requests/{requestId}/respond", body, r =>
            {
                GameEvents.RaiseToast(!r.ok ? r.message : accept ? "친구 요청을 수락했습니다." : "친구 요청을 거절했습니다.");
                Refresh();
            });
        }

        public bool IsBlocked(string name) => blocked.Contains(name);

        public void Block(string name)
        {
            string id = IdOf(name);
            if (id == null) { GameEvents.RaiseToast($"{name}님을 찾을 수 없습니다."); return; }
            blocked.Add(name); // hide at once; the server confirms
            Api.Put($"/blocks/{id}", new Dictionary<string, object>(), r =>
            {
                if (!r.ok) { blocked.Remove(name); GameEvents.RaiseToast(r.message); }
                Refresh();
            });
            SocialChanged?.Invoke();
        }

        public void Unblock(string name)
        {
            if (!blockIds.TryGetValue(name, out var bid)) { blocked.Remove(name); SocialChanged?.Invoke(); return; }
            Api.Delete($"/blocks/{bid}", r =>
            {
                GameEvents.RaiseToast(r.ok ? $"{name}님의 차단을 풀었습니다." : r.message);
                Refresh();
            });
        }

        public IEnumerable<string> RecentSpeakers()
        {
            var seen = new HashSet<string>();
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                string who = l.mine ? l.to : l.from;
                if (string.IsNullOrEmpty(who) || l.channel == ChatChannel.System || !seen.Add(who)) continue;
                yield return who;
            }
        }

        public string Report(string name, string reason)
        {
            string id = IdOf(name);
            if (id == null) return $"{name}님을 찾을 수 없습니다.";
            int index = Array.IndexOf(ChatRules.ReportReasons, reason);
            string code = index >= 0 ? ChatRules.ReportReasonCodes[index] : "other";
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["character_id"] = characterId, ["target"] = id, ["reason"] = code };
            Api.Post("/reports", body, r =>
            {
                if (!r.ok) { GameEvents.RaiseToast(r.message); return; }
                if (r.data.TryGetValue("duplicate", out var dup) && dup is bool isDup && isDup) { GameEvents.RaiseToast("이미 같은 사유로 신고한 모험가입니다."); return; }
                int n = MiniJson.Int(MiniJson.Obj(r.data, "report"), "line_count");
                GameEvents.RaiseToast($"{name}님을 '{reason}'(으)로 신고했습니다. 최근 대화 {n}줄을 첨부했습니다.");
            });
            return $"{name}님 신고를 접수하는 중입니다.";
        }

        public void Tick(float dt)
        {
            clock += dt;
            socket.Pump(dt);
            // Unanswered sends older than the resend window are dropped (shown as failed).
            foreach (var kv in pending.Where(p => clock - p.Value.at > 35f).ToList())
            {
                pending.Remove(kv.Key);
                lines.Remove(kv.Value.line);
                Received?.Invoke(null);
                GameEvents.RaiseToast("채팅을 보내지 못했습니다.");
            }
        }
    }
}
