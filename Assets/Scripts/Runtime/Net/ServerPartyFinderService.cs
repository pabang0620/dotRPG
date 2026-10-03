using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY] The 파티 찾기 window's service backed by the server (phase4_api §4-5). The window asks
    /// synchronously, so the board is a cache refreshed in the background (<c>GET /parties</c>) and every
    /// action answers right away with "sent" while the real answer arrives through <see cref="Changed"/>.
    /// Joining a party (created, accepted, matched) opens the party lobby.
    /// </summary>
    public sealed class ServerPartyFinderService : IPartyFinderService
    {
        const float BoardRefreshSeconds = 4f;

        readonly List<PartyPost> posts = new List<PartyPost>();
        float boardTimer;
        bool loading, wasInParty;
        string lastFilter;

        public bool IsOnline => true;
        public MatchQueueState Queue { get; } = new MatchQueueState();
        public event Action Changed;
        public event Action<MatchQueueState, int> Departed;

        public ServerPartyFinderService()
        {
            PartyClient.Ensure();
            PartyClient.Changed += OnClientChanged;
        }

        public void Detach() => PartyClient.Changed -= OnClientChanged;

        void OnClientChanged()
        {
            var c = PartyClient.Instance;
            Queue.active = c.Queued;
            if (c.Queued)
            {
                Queue.dungeonId = c.QueueDungeon;
                Queue.difficulty = c.QueueDifficulty;
                Queue.elapsed = (float)(DateTime.UtcNow - c.QueuedAt).TotalSeconds;
                Queue.humans = c.HumansWaiting;
            }
            // Became a party member (my post, an accepted application, a match): show the lobby.
            if (c.InParty && !wasInParty && PartyLobbyScreen.Instance != null && Game.Flow != null && Game.State.Current == GameState.Playing)
                Game.Flow.OpenWindow(PartyLobbyScreen.Instance);
            wasInParty = c.InParty;
            foreach (var p in posts) p.applied = c.MyApplications.ContainsKey(p.id);
            Changed?.Invoke();
        }

        public IReadOnlyList<PartyPost> List(string dungeonFilter)
        {
            if (dungeonFilter != lastFilter) { lastFilter = dungeonFilter; boardTimer = 0f; }
            if (boardTimer <= 0f) RefreshBoard();
            return posts.Where(p => string.IsNullOrEmpty(dungeonFilter) || p.dungeonId == dungeonFilter).ToList();
        }

        void RefreshBoard()
        {
            if (loading || !OnlineSession.Playing) return;
            loading = true;
            boardTimer = BoardRefreshSeconds;
            string q = string.IsNullOrEmpty(lastFilter) ? "?limit=50" : $"?limit=50&dungeon_id={lastFilter}";
            ApiClient.Instance.Get($"/characters/{OnlineSession.Current.ActiveCharacter}/parties{q}", r =>
            {
                loading = false;
                if (!r.ok) return;
                posts.Clear();
                var mine = PartyClient.Instance;
                foreach (var o in MiniJson.Arr(r.data, "posts") ?? new List<object>())
                {
                    var leader = MiniJson.Obj(o, "leader");
                    string id = MiniJson.Str(o, "id");
                    var def = DungeonDatabase.Get(MiniJson.Str(o, "dungeon_id"));
                    posts.Add(new PartyPost
                    {
                        id = id,
                        dungeonId = MiniJson.Str(o, "dungeon_id"),
                        dungeonName = def != null ? def.name : MiniJson.Str(o, "dungeon_id"),
                        difficulty = (DungeonDifficulty)MiniJson.Int(o, "difficulty"),
                        members = MiniJson.Int(o, "members", 1),
                        maxMembers = MiniJson.Int(o, "max_members", 4),
                        minPower = MiniJson.Int(o, "min_power"),
                        leaderName = MiniJson.Str(leader, "name", ""),
                        leaderClass = MiniJson.Str(leader, "class") == "mage" ? CharacterClass.Mage : CharacterClass.Warrior,
                        leaderLevel = MiniJson.Int(leader, "level", 1),
                        message = MiniJson.Str(o, "message", ""),
                        mine = Flag(o, "mine"),
                        applied = Flag(o, "applied") || (mine != null && mine.MyApplications.ContainsKey(id)),
                    });
                }
                Changed?.Invoke();
            });
        }

        static bool Flag(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;

        public PartyPost Create(string dungeonId, DungeonDifficulty difficulty, int maxMembers, int minPower, string message)
        {
            PartyClient.Ensure().CreateParty(dungeonId, difficulty, maxMembers, minPower, message, (ok, msg) =>
            {
                GameEvents.RaiseToast(ok ? "모집 글을 올렸다." : msg);
                boardTimer = 0f;
            });
            return null;
        }

        public bool Cancel(string postId)
        {
            var c = PartyClient.Instance;
            if (c == null || !c.InParty || c.PartyId != postId) return false;
            c.Leave((ok, msg) => { GameEvents.RaiseToast(ok ? "모집을 취소했다." : msg); boardTimer = 0f; });
            return true;
        }

        public string Apply(string postId, int myPower)
        {
            PartyClient.Ensure().Apply(postId, (ok, msg) =>
            {
                GameEvents.RaiseToast(ok ? "참가 신청을 보냈다. 방장이 수락하면 파티 창이 열린다." : msg);
                var p = posts.FirstOrDefault(x => x.id == postId);
                if (p != null) p.applied = ok;
                Changed?.Invoke();
            });
            var post = posts.FirstOrDefault(x => x.id == postId);
            if (post != null) post.applied = true;
            return "참가 신청을 보내는 중...";
        }

        public void StartQueue(string dungeonId, DungeonDifficulty difficulty)
        {
            Queue.active = true;
            Queue.dungeonId = dungeonId;
            Queue.difficulty = difficulty;
            Queue.elapsed = 0f;
            Queue.humans = 1;
            PartyClient.Ensure().Queue(dungeonId, difficulty, (ok, msg) => { if (!ok) { Queue.active = false; GameEvents.RaiseToast(msg); Changed?.Invoke(); } });
            Changed?.Invoke();
        }

        public void CancelQueue()
        {
            Queue.active = false;
            PartyClient.Ensure().CancelQueue((ok, msg) => { if (!ok) GameEvents.RaiseToast(msg); });
            Changed?.Invoke();
        }

        /// <summary>Server: the matched people (and this PC) become a party now; AI seats are set in the lobby.</summary>
        public int DepartWithAi()
        {
            if (!Queue.active) return 0;
            int ai = PartyManager.MaxMembers - Queue.humans;
            Queue.active = false;
            PartyClient.Ensure().FillAi((ok, msg) => GameEvents.RaiseToast(ok ? "파티를 만들었다. 파티 창에서 출발하자." : msg));
            Changed?.Invoke();
            return ai;
        }

        public void Tick(float deltaSeconds)
        {
            boardTimer -= deltaSeconds;
            if (Queue.active) Queue.elapsed += deltaSeconds;
        }
    }
}
