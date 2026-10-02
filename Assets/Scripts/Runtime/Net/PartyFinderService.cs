using System;
using System.Collections.Generic;
using System.Linq;

namespace DotRPG
{
    /// <summary>[ONLINE] One 파티 모집 글.</summary>
    public sealed class PartyPost
    {
        public string id;
        public string dungeonId;
        public string dungeonName;
        public DungeonDifficulty difficulty;
        public int members, maxMembers;
        public int minPower;
        public string leaderName;
        public CharacterClass leaderClass;
        public int leaderLevel;
        public string message;
        public bool mine;
        public bool applied;
        public bool Full => members >= maxMembers;
    }

    /// <summary>[ONLINE] State of the 자동 매칭 queue.</summary>
    public sealed class MatchQueueState
    {
        public bool active;
        public string dungeonId;
        public DungeonDifficulty difficulty;
        public float elapsed;
        public int humans = 1;
        public float Remaining => Math.Max(0f, PartyFinderRules.QueueSeconds - elapsed);
    }

    public static class PartyFinderRules
    {
        /// <summary>Queue time before the party departs with AI in the empty slots.</summary>
        public const float QueueSeconds = 60f;
        public const int MaxMessage = 30;
        public static readonly string[] PresetMessages = { "빠르게 돌아요", "초보 환영합니다", "숙련자만 와 주세요", "보스 패턴 아시는 분", "카드 보상 노려요" };
        public static string DifficultyName(DungeonDifficulty d) => DungeonDatabase.Difficulty(d).name;
        public static string DifficultyColor(DungeonDifficulty d) =>
            d == DungeonDifficulty.Normal ? "#c8d2e0" : d == DungeonDifficulty.Adventure ? "#78dcff" : d == DungeonDifficulty.King ? "#ffd34a" : "#ff7a7a";
    }

    /// <summary>[ONLINE] 파티 찾기 / 자동 매칭 (PLAN_ONLINE §2.1). Server-backed later; <see cref="MockPartyFinderService"/> offline.</summary>
    public interface IPartyFinderService
    {
        bool IsOnline { get; }
        IReadOnlyList<PartyPost> List(string dungeonFilter);
        PartyPost Create(string dungeonId, DungeonDifficulty difficulty, int maxMembers, int minPower, string message);
        bool Cancel(string postId);
        /// <summary>Applies to a post; returns a Korean status line.</summary>
        string Apply(string postId, int myPower);
        MatchQueueState Queue { get; }
        void StartQueue(string dungeonId, DungeonDifficulty difficulty);
        void CancelQueue();
        /// <summary>Ends the queue now; returns how many AI mercenaries fill the empty slots.</summary>
        int DepartWithAi();
        void Tick(float deltaSeconds);
        event Action Changed;
    }

    /// <summary>[ONLINE] In-memory 파티 찾기 with generated posts. Nothing leaves this PC.</summary>
    public sealed class MockPartyFinderService : IPartyFinderService
    {
        static readonly string[] Leaders = { "검은뿔", "달빛마녀", "모리촌장팬", "카타나장인", "해골사냥꾼", "눈꽃여우", "광산왕", "불꽃베기" };
        /// <summary>Mock: one more human "joins" the queue at these seconds.</summary>
        static readonly float[] QueueJoins = { 18f, 41f };

        readonly List<PartyPost> posts = new List<PartyPost>();
        int nextId = 1;

        public bool IsOnline => false;
        public MatchQueueState Queue { get; } = new MatchQueueState();
        public event Action Changed;

        public MockPartyFinderService(int seed = 11)
        {
            var rng = new Random(seed);
            var dungeons = new List<DungeonDef>(DungeonDatabase.Weekday) { DungeonDatabase.SkeletonKing, DungeonDatabase.Get(DungeonDatabase.RaidBargas) };
            for (int i = 0; i < 9; i++)
            {
                var d = dungeons[rng.Next(dungeons.Count)];
                var diff = d.isRaid ? DungeonDifficulty.Normal : (DungeonDifficulty)rng.Next(0, DungeonDatabase.DifficultyCount);
                int max = d.isRaid ? 4 : rng.Next(3, 5);
                posts.Add(new PartyPost
                {
                    id = "P" + nextId++,
                    dungeonId = d.id,
                    dungeonName = d.name,
                    difficulty = diff,
                    maxMembers = max,
                    members = rng.Next(1, max + 1),
                    minPower = (int)diff * 900 + rng.Next(2, 9) * 100,
                    leaderName = Leaders[i % Leaders.Length],
                    leaderClass = rng.Next(2) == 0 ? CharacterClass.Warrior : CharacterClass.Mage,
                    leaderLevel = 5 + (int)diff * 7 + rng.Next(0, 6),
                    message = PartyFinderRules.PresetMessages[rng.Next(PartyFinderRules.PresetMessages.Length)],
                });
            }
        }

        public IReadOnlyList<PartyPost> List(string dungeonFilter) =>
            posts.Where(p => string.IsNullOrEmpty(dungeonFilter) || p.dungeonId == dungeonFilter)
                 .OrderByDescending(p => p.mine).ThenBy(p => p.Full).ThenBy(p => p.minPower).ToList();

        public PartyPost Create(string dungeonId, DungeonDifficulty difficulty, int maxMembers, int minPower, string message)
        {
            posts.RemoveAll(p => p.mine);
            var d = DungeonDatabase.Get(dungeonId);
            var cls = Game.Session != null ? Game.Session.PlayerClass : CharacterClass.Warrior;
            var post = new PartyPost
            {
                id = "P" + nextId++,
                dungeonId = dungeonId,
                dungeonName = d != null ? d.name : dungeonId,
                difficulty = difficulty,
                maxMembers = Math.Max(2, Math.Min(4, maxMembers)),
                members = 1,
                minPower = Math.Max(0, minPower),
                leaderName = "나",
                leaderClass = cls,
                leaderLevel = Game.Session != null ? Game.Session.Progression.Level : 1,
                message = message != null && message.Length > PartyFinderRules.MaxMessage ? message.Substring(0, PartyFinderRules.MaxMessage) : message ?? "",
                mine = true,
            };
            posts.Add(post);
            Changed?.Invoke();
            return post;
        }

        public bool Cancel(string postId)
        {
            bool ok = posts.RemoveAll(p => p.id == postId && p.mine) > 0;
            if (ok) Changed?.Invoke();
            return ok;
        }

        public string Apply(string postId, int myPower)
        {
            var p = posts.FirstOrDefault(x => x.id == postId);
            if (p == null) return "모집이 끝난 글입니다.";
            if (p.mine) return "내 모집 글입니다.";
            if (p.Full) return "모집 완료된 파티입니다.";
            if (myPower < p.minPower) return $"전투력이 부족합니다 (최소 {p.minPower:N0}).";
            p.applied = true;
            Changed?.Invoke();
            return $"{p.leaderName}님의 파티에 참가 신청했습니다. (오프라인 미리보기: 방장 응답 없음)";
        }

        public void StartQueue(string dungeonId, DungeonDifficulty difficulty)
        {
            Queue.active = true;
            Queue.dungeonId = dungeonId;
            Queue.difficulty = difficulty;
            Queue.elapsed = 0f;
            Queue.humans = 1;
            Changed?.Invoke();
        }

        public void CancelQueue()
        {
            if (!Queue.active) return;
            Queue.active = false;
            Changed?.Invoke();
        }

        public int DepartWithAi()
        {
            if (!Queue.active) return 0;
            int ai = PartyManager.MaxMembers - Queue.humans;
            Queue.active = false;
            Changed?.Invoke();
            return ai;
        }

        public void Tick(float dt)
        {
            if (!Queue.active) return;
            float before = Queue.elapsed;
            Queue.elapsed += dt;
            foreach (var t in QueueJoins) if (before < t && Queue.elapsed >= t) Queue.humans = Math.Min(PartyManager.MaxMembers, Queue.humans + 1);
            if (Queue.elapsed >= PartyFinderRules.QueueSeconds) DepartWithAi();
        }
    }

    /// <summary>[ONLINE] Services in use (mock until a server exists).</summary>
    public static class OnlineServices
    {
        static IPartyFinderService partyFinder;
        static IAuctionService auction;
        public static IPartyFinderService PartyFinder { get => partyFinder ?? (partyFinder = new MockPartyFinderService()); set => partyFinder = value; }
        public static IAuctionService Auction { get => auction ?? (auction = new MockAuctionService()); set => auction = value; }
    }
}
