using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Runs a dungeon (Game.Dungeon), plan §6.0: entry from the select window → room by room (monsters never
    /// respawn; a cleared room opens its gate; walking in fades to the next room with the party re-placed at
    /// its entry) → boss → 1 s slow motion + CLEAR → leftover monsters die → result screen (rank, cards) →
    /// retry / dungeon select / village. Local death in a run shows the revive-coin countdown instead of
    /// game over (routed from <see cref="GameFlow.OnPlayerDied"/>); downed companions stay down for the rest of
    /// the room and rejoin at the next entry with half HP. Nothing is saved inside a dungeon.
    /// </summary>
    public sealed partial class DungeonDirector : MonoBehaviour
    {
        // ---------- Tuning ----------
        /// <summary>Coin countdown after the local player goes down.</summary>
        public const float ReviveSeconds = 10f;
        /// <summary>Invulnerability after a coin revive.</summary>
        public const float ReviveInvulnerable = 3f;
        /// <summary>Companions downed in a room rejoin at the next entry with this share of their HP.</summary>
        public const float CompanionRejoinHp = 0.5f;
        /// <summary>Boss down: time scale and (real) duration of the slow motion.</summary>
        public const float SlowMotionScale = 0.25f, SlowMotionSeconds = 1f;
        /// <summary>CLEAR banner stays this long after the slow motion before the result opens.</summary>
        public const float ClearHoldSeconds = 1.3f;
        /// <summary>Delay between the fatal blow / giving up and the failed result.</summary>
        public const float FailDelay = 1.2f;
        const float FadeSeconds = 0.35f;
        /// <summary>Companions walk in with the player before they start fighting.</summary>
        const float RoomRegroupSeconds = 0.8f;

        DungeonRun run;
        DungeonDoor door;
        bool busy, roomReady, roomCleared;
        readonly List<EnemyController> bosses = new List<EnemyController>();
        float reviveUntil;
        Health hookedLocal;

        /// <summary>The run in progress (also while its result is shown); null outside dungeons.</summary>
        public DungeonRun Run => run;
        /// <summary>True from entering until the party is back in the village (saving is blocked meanwhile).</summary>
        public bool InRun => run != null;
        /// <summary>A fade / room change is going on.</summary>
        public bool IsBusy => busy;
        /// <summary>The coin countdown is showing (local player down).</summary>
        public bool ReviveOpen { get; private set; }
        public float ReviveRemaining => ReviveOpen ? Mathf.Max(0f, reviveUntil - reviveClock) : 0f;
        public DungeonDoor Door => door;

        // [PARTY NET] What the sync layer needs to know.
        public DungeonDef CurrentDungeon => run?.Dungeon;
        public DungeonDifficulty CurrentDifficulty => run != null ? run.Difficulty : DungeonDifficulty.Normal;
        public int CurrentRoom => run != null ? run.RoomIndex : 0;
        public bool RoomIsReady => roomReady && !busy;
        /// <summary>A member PC in a party run: the host decides rooms, clears and the end.</summary>
        public bool Follower => run != null && PartyNet.IsMember;
        static bool NetHost => PartyNet.IsHost;

        /// <summary>Raised after every room load (HUD refresh), on clear banner and when the run ends.</summary>
        public event Action RoomChanged;
        public event Action ClearBanner;
        public event Action<DungeonRun> RunEnded;

        float reviveClock;

        public static DungeonDirector Create(Transform parent)
        {
            var go = new GameObject("Dungeon");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<DungeonDirector>();
            EnemyController.Killed += d.OnEnemyKilled;
            return d;
        }

        void OnDestroy() => EnemyController.Killed -= OnEnemyKilled;

        static DungeonProgress Progress => Game.Session.Dungeons;

        // =============================== Entry ===============================

        /// <summary>Why the party cannot enter (null = it can).</summary>
        public string CannotEnterReason(DungeonDef dungeon, DungeonDifficulty difficulty, DateTime now)
        {
            if (dungeon == null) return "던전을 고르세요.";
            if (InRun) return "이미 던전 안에 있습니다.";
            if (busy || (Game.Flow != null && Game.Flow.IsTransitioning)) return "잠시 후에 다시 시도하세요.";
            if (Game.Player == null || Game.Player.IsDead) return "쓰러진 상태로는 입장할 수 없습니다.";
            if (!ResetClock.IsOpen(dungeon, now)) return $"오늘({DungeonDatabase.DayName(ResetClock.GameDay(now))})은 열리지 않는 던전입니다.";
            if (!Progress.IsUnlocked(dungeon, difficulty)) return $"{DungeonDatabase.Difficulty(difficulty - 1).name} 난이도를 먼저 클리어해야 합니다.";
            if (dungeon.isRaid)
            {
                // [RAID] The story opens each raid; a final raid needs the seal key fragments in the bag.
                string locked = RaidLockReason(dungeon);
                if (locked != null) return locked;
                int need = DungeonDatabase.DifficultyFor(dungeon, difficulty).recommendedLevel;
                if (Game.Session.Progression.Level < need) return $"Lv.{need}부터 입장할 수 있는 레이드입니다.";
                // A final raid without the seal keys is still entered, as a practice run (no reward): solo story can finish.
            }
            if (!dungeon.isRaid && Progress.EntriesLeft(now) <= 0) return "오늘 입장 횟수를 모두 사용했습니다. (06:00 초기화)";
            if (Game.Party != null && Game.Party.Count > dungeon.maxParty) return $"최대 {dungeon.maxParty}명까지 입장할 수 있습니다.";
            return null;
        }

        /// <summary>[RAID] Why the story has not opened this raid yet (null = open).</summary>
        public static string RaidLockReason(DungeonDef raid)
        {
            if (raid == null || string.IsNullOrEmpty(raid.unlockQuest) || Game.Quest == null) return null;
            var st = Game.Quest.StatusOf(raid.unlockQuest);
            if (st == QuestStatus.Active || st == QuestStatus.ReadyToTurnIn || st == QuestStatus.Completed) return null;
            var q = Game.Quest.Database.Get(raid.unlockQuest);
            return q != null ? $"메인 퀘스트 '{q.DisplayTitle}'를 받으면 열립니다." : "아직 열리지 않았습니다.";
        }

        /// <summary>
        /// Enters <paramref name="dungeon"/> at <paramref name="difficulty"/> with the current party (spends one
        /// of today's weekday entries). Closes an open window first. False (with a toast) when not allowed.
        /// </summary>
        public bool Enter(DungeonDef dungeon, DungeonDifficulty difficulty)
        {
            // One party: with other people in my online party, entering takes all of them (and AI for the free seats).
            var pc = PartyClient.Instance;
            if (OnlineEconomy.On && pc != null && pc.InParty && pc.Members.Count >= 2) return EnterAsParty(pc, dungeon, difficulty);
            var now = ResetClock.Now;
            string reason = CannotEnterReason(dungeon, difficulty, now);
            if (reason != null)
            {
                GameEvents.RaiseToast(reason);
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            if (OnlineEconomy.On) return EnterOnline(dungeon, difficulty, false);
            if (!dungeon.isRaid) Progress.UseEntry(now);
            // Continue after quitting mid-run starts in the village, with this entry already spent.
            SaveBeforeEntering();
            StartRun(dungeon, difficulty, now);
            return true;
        }

    }
}
