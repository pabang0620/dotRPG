using System;
using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    /// <summary>[ONLINE] Chat channels (PLAN_ONLINE §2.3).</summary>
    public enum ChatChannel { General, Party, Whisper, System }

    public sealed class ChatLine
    {
        public ChatChannel channel;
        public string from;
        /// <summary>Whisper target (own whispers only).</summary>
        public string to;
        public string text;
        public float time;
        public bool mine;
        /// <summary>Sender's title ("" = none): shown as [칭호][이름].</summary>
        public string title = "";
    }

    public sealed class ChatFriend
    {
        public string name;
        public bool online;
        public string where;
    }

    /// <summary>[SERVER 5] A friend request someone sent me.</summary>
    public sealed class ChatRequest
    {
        public string id;
        public string name;
    }

    public sealed class ChatReport
    {
        public string target;
        public string reason;
        public List<string> log;
    }

    public static class ChatRules
    {
        public const int MaxLength = 60;
        /// <summary>One message per second; the same text three times in a row mutes for ten seconds.</summary>
        public const float MinInterval = 1f;
        public const int RepeatLimit = 3;
        public const float MuteSeconds = 10f;
        /// <summary>Lines attached to a report.</summary>
        public const int ReportLines = 20;
        public const int Keep = 120;

        public static readonly string[] QuickSignals = { "모여!", "부활해 줘!", "기믹 위치 여기!", "준비 완료!", "고마워!" };
        public static readonly string[] ReportReasons = { "욕설·비하", "도배", "광고·사기", "불법 프로그램 의심", "기타" };
        /// <summary>[SERVER] Codes the server stores for <see cref="ReportReasons"/> (same order).</summary>
        public static readonly string[] ReportReasonCodes = { "abuse", "spam", "scam_ad", "cheat", "other" };

        // Client-side preview only; the server makes the final call (PLAN_ONLINE §2.3).
        static readonly string[] Banned = { "시발", "씨발", "병신", "개새", "좆", "꺼져" };

        public static string Filter(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var b in Banned) text = text.Replace(b, new string('*', b.Length));
            return text;
        }

        public static string ChannelName(ChatChannel c) =>
            c == ChatChannel.General ? "일반" : c == ChatChannel.Party ? "파티" : c == ChatChannel.Whisper ? "귓속말" : "시스템";

        public static string ChannelColor(ChatChannel c) =>
            c == ChatChannel.General ? "#f0f0f0" : c == ChatChannel.Party ? "#78dcff" : c == ChatChannel.Whisper ? "#ff9ee0" : "#ffd84a";
    }

    /// <summary>[ONLINE] 채팅·친구·차단·신고. Server-backed later; <see cref="MockChatService"/> offline.</summary>
    public interface IChatService
    {
        bool IsOnline { get; }
        IReadOnlyList<ChatLine> Lines { get; }
        /// <summary>Sends a line; returns null when sent or a Korean reason when refused.</summary>
        string Send(ChatChannel channel, string text, string whisperTo = null);
        void PostSystem(string text);
        /// <summary>A line from another player arrived (server push, or the [F2] two-window transport).</summary>
        void Deliver(ChatLine line);
        IReadOnlyList<ChatFriend> Friends { get; }
        bool IsFriend(string name);
        void AddFriend(string name);
        void RemoveFriend(string name);
        IReadOnlyCollection<string> Blocked { get; }
        bool IsBlocked(string name);
        void Block(string name);
        void Unblock(string name);
        /// <summary>Other players seen in chat, newest first.</summary>
        IEnumerable<string> RecentSpeakers();
        /// <summary>Files a report with the last <see cref="ChatRules.ReportLines"/> lines; returns a Korean status line.</summary>
        string Report(string name, string reason);
        void Tick(float deltaSeconds);
        /// <summary>[SERVER 5] Friend requests waiting for my answer (none offline).</summary>
        IReadOnlyList<ChatRequest> Requests { get; }
        void RespondRequest(string requestId, bool accept);
        /// <summary>[SERVER 5] Character id of a name seen in chat (null offline or unknown).</summary>
        string IdOf(string name);
        /// <summary>A line was added, changed or removed (null = removed / redraw).</summary>
        event Action<ChatLine> Received;
        event Action SocialChanged;
    }

    /// <summary>[ONLINE] In-memory chat: own lines, a few generated village voices, system notices. Nothing leaves this PC.</summary>
    public sealed class MockChatService : IChatService
    {
        static readonly string[] Voices = { "검은뿔", "달빛마녀", "모리촌장팬", "카타나장인", "해골사냥꾼", "눈꽃여우" };
        static readonly string[] Chatter =
        {
            "해골 왕 수요일에 같이 가실 분?", "강화 +10 드디어 성공했다", "협곡 쪽 몬스터 너무 세요", "철광석 시세 얼마예요?",
            "파티 찾기에 글 올렸어요", "보호권 아끼세요 진짜로", "겨울 마을 가는 길 어디예요?", "카엘 수상하지 않아요?",
        };

        readonly List<ChatLine> lines = new List<ChatLine>();
        readonly List<ChatFriend> friends = new List<ChatFriend>();
        readonly HashSet<string> blocked = new HashSet<string>();
        public readonly List<ChatReport> Reports = new List<ChatReport>();
        readonly Random rng = new Random(7);
        float clock, lastSent = -99f, mutedUntil, nextChatter = 20f;
        string lastText;
        int repeats;

        public bool IsOnline => false;
        public IReadOnlyList<ChatRequest> Requests => Array.Empty<ChatRequest>();
        public void RespondRequest(string requestId, bool accept) { }
        public string IdOf(string name) => null;
        static string MyName => Game.Session?.Journal?.PlayerName ?? QuestJournal.DefaultName;
        public IReadOnlyList<ChatLine> Lines => lines;
        public IReadOnlyList<ChatFriend> Friends => friends;
        public IReadOnlyCollection<string> Blocked => blocked;
        public event Action<ChatLine> Received;
        public event Action SocialChanged;

        public MockChatService(string savedFriends = null, string savedBlocked = null)
        {
            foreach (var n in Split(savedFriends)) friends.Add(new ChatFriend { name = n });
            foreach (var n in Split(savedBlocked)) blocked.Add(n);
            for (int i = 0; i < friends.Count; i++) { friends[i].online = i % 2 == 0; friends[i].where = friends[i].online ? "해골 숲 옆 작은 마을" : ""; }
        }

        static IEnumerable<string> Split(string s) => string.IsNullOrEmpty(s) ? Enumerable.Empty<string>() : s.Split(',').Where(x => x.Length > 0);
        public string SaveFriends() => string.Join(",", friends.Select(f => f.name));
        public string SaveBlocked() => string.Join(",", blocked);

        public string Send(ChatChannel channel, string text, string whisperTo = null)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return "";
            if (channel == ChatChannel.System) return "시스템 채널에는 쓸 수 없습니다.";
            if (text.Length > ChatRules.MaxLength) text = text.Substring(0, ChatRules.MaxLength);
            if (clock < mutedUntil) return $"같은 말을 반복해 {Math.Ceiling(mutedUntil - clock)}초 동안 채팅이 막혔습니다.";
            if (clock - lastSent < ChatRules.MinInterval) return "채팅은 1초에 한 번만 보낼 수 있습니다.";
            if (channel == ChatChannel.Whisper)
            {
                if (string.IsNullOrEmpty(whisperTo)) return "귓속말 상대를 적어 주세요. (/w 이름 내용)";
                if (blocked.Contains(whisperTo)) return $"{whisperTo}님은 차단 중입니다.";
            }
            repeats = text == lastText ? repeats + 1 : 1;
            lastText = text;
            lastSent = clock;
            if (repeats >= ChatRules.RepeatLimit) { mutedUntil = clock + ChatRules.MuteSeconds; repeats = 0; }
            Add(new ChatLine { channel = channel, from = MyName, to = whisperTo, text = ChatRules.Filter(text), time = clock, mine = true });
            return null;
        }

        public void PostSystem(string text) => Add(new ChatLine { channel = ChatChannel.System, from = "", text = text, time = clock });

        public void Deliver(ChatLine line)
        {
            line.mine = false;
            line.time = clock;
            line.text = ChatRules.Filter(line.text);
            Add(line);
        }

        void Add(ChatLine line)
        {
            if (!line.mine && line.channel != ChatChannel.System && blocked.Contains(line.from)) return;
            lines.Add(line);
            if (lines.Count > ChatRules.Keep) lines.RemoveAt(0);
            Received?.Invoke(line);
        }

        public bool IsFriend(string name) => friends.Any(f => f.name == name);

        public void AddFriend(string name)
        {
            if (string.IsNullOrEmpty(name) || IsFriend(name)) return;
            friends.Add(new ChatFriend { name = name, online = true, where = "해골 숲 옆 작은 마을" });
            SocialChanged?.Invoke();
        }

        public void RemoveFriend(string name)
        {
            if (friends.RemoveAll(f => f.name == name) > 0) SocialChanged?.Invoke();
        }

        public bool IsBlocked(string name) => blocked.Contains(name);

        public void Block(string name)
        {
            if (string.IsNullOrEmpty(name) || !blocked.Add(name)) return;
            friends.RemoveAll(f => f.name == name);
            SocialChanged?.Invoke();
        }

        public void Unblock(string name)
        {
            if (blocked.Remove(name)) SocialChanged?.Invoke();
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
            var log = lines.Skip(Math.Max(0, lines.Count - ChatRules.ReportLines))
                .Select(l => $"[{ChatRules.ChannelName(l.channel)}] {l.from}: {l.text}").ToList();
            Reports.Add(new ChatReport { target = name, reason = reason, log = log });
            return $"{name}님을 '{reason}'(으)로 신고했습니다. 최근 대화 {log.Count}줄을 첨부했습니다. (오프라인 모드)";
        }

        public void Tick(float dt)
        {
            clock += dt;
            if (clock < nextChatter) return;
            nextChatter = clock + 25f + (float)rng.NextDouble() * 20f;
            Add(new ChatLine { channel = ChatChannel.General, from = Voices[rng.Next(Voices.Length)], text = Chatter[rng.Next(Chatter.Length)], time = clock });
        }
    }
}
