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
    public sealed class DungeonDirector : MonoBehaviour
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
        /// <summary>Monster numbers per level offset of a spawn group (until MonsterDatabase owns levels).</summary>
        const float LevelStep = 0.08f;

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
            if (InRun) return "이미 던전 안에 있다.";
            if (busy || (Game.Flow != null && Game.Flow.IsTransitioning)) return "잠시 후에 다시 시도하세요.";
            if (Game.Player == null || Game.Player.IsDead) return "쓰러진 상태로는 입장할 수 없다.";
            if (!ResetClock.IsOpen(dungeon, now)) return $"오늘({DungeonDatabase.DayName(ResetClock.GameDay(now))})은 열리지 않는 던전이다.";
            if (!Progress.IsUnlocked(dungeon, difficulty)) return $"{DungeonDatabase.Difficulty(difficulty - 1).name} 난이도를 먼저 클리어해야 한다.";
            if (dungeon.isRaid)
            {
                // [RAID] The story opens each raid; a final raid needs the seal key fragments in the bag.
                string locked = RaidLockReason(dungeon);
                if (locked != null) return locked;
                int need = DungeonDatabase.DifficultyFor(dungeon, difficulty).recommendedLevel;
                if (Game.Session.Progression.Level < need) return $"Lv.{need}부터 입장할 수 있는 레이드다.";
                // A final raid without the seal keys is still entered, as a practice run (no reward): solo story can finish.
            }
            if (!dungeon.isRaid && Progress.EntriesLeft(now) <= 0) return "오늘 입장 횟수를 모두 사용했다. (06:00 초기화)";
            if (Game.Party != null && Game.Party.Count > dungeon.maxParty) return $"최대 {dungeon.maxParty}명까지 입장할 수 있다.";
            return null;
        }

        /// <summary>[RAID] Why the story has not opened this raid yet (null = open).</summary>
        public static string RaidLockReason(DungeonDef raid)
        {
            if (raid == null || string.IsNullOrEmpty(raid.unlockQuest) || Game.Quest == null) return null;
            var st = Game.Quest.StatusOf(raid.unlockQuest);
            if (st == QuestStatus.Active || st == QuestStatus.ReadyToTurnIn || st == QuestStatus.Completed) return null;
            var q = Game.Quest.Database.Get(raid.unlockQuest);
            return q != null ? $"메인 퀘스트 '{q.DisplayTitle}'를 받으면 열린다." : "아직 열리지 않았다.";
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

        /// <summary>
        /// [SERVER] Online solo run (phase 3, phase 4 adds raids and hired AI companions). The server spends
        /// the entry and opens the run; the run starts when it answers (its refusal is shown as a toast).
        /// True = request sent.
        /// </summary>
        bool EnterOnline(DungeonDef dungeon, DungeonDifficulty difficulty, bool retry)
        {
            if (enteringOnline) return false;
            enteringOnline = true;
            int maxAi = Mathf.Max(0, Mathf.Min(PartyManager.MaxCompanions, (dungeon.maxParty > 0 ? dungeon.maxParty : PartyManager.MaxMembers) - 1));
            int ai = Mathf.Min(Game.Session.PartyRoster.Count, maxAi);
            OnlineEconomy.EnterDungeon(dungeon.id, (int)difficulty, ai, serverRun =>
            {
                enteringOnline = false;
                if (serverRun == null) { Game.Audio.PlaySfx("cancel"); return; }
                var now = ResetClock.Now;
                if (!dungeon.isRaid) Progress.UseEntry(now); // the local counter follows the server's
                if (retry) EndRunState();
                else SaveBeforeEntering();
                StartRun(dungeon, difficulty, now);
                // The server decides whether this raid clear pays (once per period, enough people).
                run.RewardsLocked = serverRun.TryGetValue("reward_locked", out var v) && v is bool b && b;
                if (run.RewardsLocked && dungeon.isRaid) GameEvents.RaiseToast(RaidLockText(MiniJson.Str(serverRun, "lock_reason")));
            });
            return true;
        }

        /// <summary>
        /// The leader enters for the whole party: the party's target becomes this dungeon and difficulty, then the run
        /// departs (members online are pulled in, free seats get the leader's AI companions). A member is told to wait.
        /// </summary>
        bool EnterAsParty(PartyClient pc, DungeonDef dungeon, DungeonDifficulty difficulty)
        {
            if (!pc.IsLeader)
            {
                GameEvents.RaiseToast("파티 중에는 방장이 입장하면 함께 들어갑니다.");
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            if (PartyRunSession.Active) { GameEvents.RaiseToast("이미 파티가 출발하는 중입니다."); return false; }
            var diff = dungeon.isRaid ? DungeonDifficulty.Normal : difficulty;
            int ai = Mathf.Min(Game.Session.PartyRoster.Count, PartyManager.CompanionLimit);
            void Depart() => pc.StartRun(ai, (ok, msg) =>
            {
                Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
                GameEvents.RaiseToast(ok ? "파티원과 함께 출발합니다. 파티원이 연결되는 중..." : msg);
            });
            if (pc.DungeonId == dungeon.id && pc.Difficulty == diff) Depart();
            else pc.SetTarget(dungeon.id, diff, (ok, msg) => { if (ok) Depart(); else { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(msg); } });
            return true;
        }

        static string RaidLockText(string reason)
        {
            switch (reason)
            {
                case "TOO_FEW_HUMANS": return "레이드 보상은 2명 이상의 파티만 받는다. (연습 입장)";
                case "ALREADY_CLAIMED": return "이번 기간 레이드 보상을 이미 받았다. (연습 입장)";
                case "KEYS_MISSING": return "봉인 열쇠 조각이 모자라다. (연습 입장)";
                default: return "레이드 보상이 없는 연습 입장이다.";
            }
        }

        bool enteringOnline;

        /// <summary>
        /// [PARTY] The party run began on the server (begin created every member's dungeon_runs row and spent
        /// the entries): the host PC starts the fight, the members follow its RunStart.
        /// </summary>
        public void StartPartyRun(DungeonDef dungeon, DungeonDifficulty difficulty)
        {
            if (run != null && !run.IsOver) return;
            if (run != null) EndRunState();
            var now = ResetClock.Now;
            if (!dungeon.isRaid) Progress.UseEntry(now);
            SaveBeforeEntering();
            StartRun(dungeon, difficulty, now);
        }

        /// <summary>Same dungeon and difficulty again from the result screen (spends an entry).</summary>
        public bool Retry()
        {
            if (run == null || !run.IsOver) return false;
            var dungeon = run.Dungeon;
            var difficulty = run.Difficulty;
            var now = ResetClock.Now;
            if (!dungeon.isRaid && Progress.EntriesLeft(now) <= 0)
            {
                GameEvents.RaiseToast("오늘 입장 횟수를 모두 사용했다.");
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            if (OnlineEconomy.On) return EnterOnline(dungeon, difficulty, true);
            if (!dungeon.isRaid) Progress.UseEntry(now);
            EndRunState();
            StartRun(dungeon, difficulty, now);
            return true;
        }

        /// <summary>True when the result screen may offer 다시 도전.</summary>
        public bool CanRetry => run != null && run.IsOver && !PartyNet.Active && (run.Dungeon.isRaid || Progress.EntriesLeft(ResetClock.Now) > 0);

        void SaveBeforeEntering()
        {
            if (Game.Config == null || !Game.Config.autosave || Game.Player == null) return;
            string map = Game.Session.MapId;
            Game.Session.MapId = MapRegistry.Village;
            // Negative coordinates = the village start point (see GameSession.Restore).
            bool ok = Game.Saves.Write(Game.Session.Capture(new Vector2(-1f, -1f), Facing.Down));
            Game.Session.MapId = map;
            if (ok) GameEvents.RaiseToast("자동 저장됨");
        }

        void StartRun(DungeonDef dungeon, DungeonDifficulty difficulty, DateTime now, int startRoom = 0)
        {
            FieldSession.Instance?.Leave(); // [PARTY 8] the field party session ends at the dungeon gate
            Game.Party?.SetDungeonCompanions(true); // AI companions are dungeon and raid only: they join here
            if (NetHost) PartyNet.Current.HostRunStarted(dungeon.id, difficulty); // [PARTY NET] members follow
            StoryCompanions.Refresh(true); // [STORY] 카엘 fights in dungeons and raids too
            var party = Game.Party;
            run = new DungeonRun(dungeon, difficulty, party != null ? party.Count : 1);
            run.RewardsLocked = dungeon.isRaid && !Progress.RaidRewardAvailable(dungeon, now);
            if (dungeon.isRaid && dungeon.keyCost > 0 && !run.RewardsLocked && Game.Session.Inventory.Count(DungeonDatabase.SealKey) < dungeon.keyCost)
            {
                run.RewardsLocked = true;
                GameEvents.RaiseToast($"봉인 열쇠 조각이 모자라 연습 입장이다. 클리어하면 이야기는 이어지지만 보상은 없다. ({Game.Session.Inventory.Count(DungeonDatabase.SealKey)}/{dungeon.keyCost})");
            }
            if (party != null)
            {
                party.SetCompanionAutoRevive(false);
                party.ResetMeters();
            }
            HookLocal(Game.Player);
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            // [E4] Loading card on the black screen: the dungeon's banner art, its name and a tip.
            var banner = Resources.Load<Sprite>("Art/banner_" + dungeon.id);
            Game.UI.Fader.ShowCard(banner, $"{dungeon.name} · {run.Numbers.name}");
            holdBlack = LoadingCardSeconds;
            StartCoroutine(Transition(() =>
            {
                Game.Session.MapId = MapRegistry.Village; // where the run returns to
                LoadRoom(Mathf.Clamp(startRoom, 0, run.RoomCount - 1), true);
                GameEvents.RaiseToast($"{dungeon.name} · {run.Numbers.name}");
                if (run.RewardsLocked) GameEvents.RaiseToast(dungeon.raidTier == RaidTier.Mid ? "오늘 레이드 보상을 이미 받았다. (연습 입장)" : "이번 주 레이드 보상을 이미 받았다. (연습 입장)");
            }));
        }

        // =============================== [PARTY NET] Member PC follows the host ===============================

        int followLoading = -1;

        /// <summary>The host started (or is already in) a run: same dungeon here, at the host's room.</summary>
        public void EnterFollower(DungeonDef dungeon, DungeonDifficulty difficulty, int room)
        {
            if (run != null && !run.IsOver && run.Dungeon == dungeon) return;
            if (run != null) EndRunState();
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            followLoading = room;
            StartRun(dungeon, difficulty, ResetClock.Now, room);
        }

        /// <summary>The host moved to room <paramref name="index"/>.</summary>
        public void FollowRoom(int index)
        {
            if (run == null || run.IsOver) return;
            if (index == followLoading || (index == run.RoomIndex && (roomReady || busy))) return;
            followLoading = index;
            StartCoroutine(Transition(() => LoadRoom(index, false)));
        }

        public void FollowRoomCleared()
        {
            if (run == null || run.State != DungeonRunState.Playing || roomCleared) return;
            roomCleared = true;
            run.ClearedRooms.Add(run.RoomIndex);
            if (door != null) door.Open();
            Game.Audio.PlaySfx("build_complete");
            GameEvents.RaiseToast("방을 정리했다! 방장이 문으로 이동하면 함께 넘어간다.");
            RoomChanged?.Invoke();
        }

        /// <summary>The host ended the run; this PC reports its own result with the host's counts.</summary>
        public void FollowEnd(bool cleared, string reason, float elapsedSeconds, MemberRunStats mine)
        {
            if (run == null || run.IsOver || run.State == DungeonRunState.Clearing) return;
            if (cleared) Pickup.CollectAll(); // [DUNGEON] leftover drops go into the bag
            run.Elapsed = elapsedSeconds;
            run.HitsTaken = mine.hitsTaken;
            run.MaxCombo = mine.maxCombo;
            run.RevivesUsed = mine.revives;
            if (cleared) StartCoroutine(ClearRoutine());
            else Fail(string.IsNullOrEmpty(reason) ? "파티가 던전 공략에 실패했다." : reason);
        }

        // =============================== Rooms ===============================

        void LoadRoom(int index, bool first)
        {
            roomReady = false;
            roomCleared = false;
            bosses.Clear();
            door = null;
            run.RoomIndex = index;
            OnlineEconomy.RoomIndex = index; // [SERVER] kill reports carry the room
            var room = run.Room;
            Game.Dialogue.Abort();
            CompanionBrain.DangerZones.Clear();
            CompanionBrain.PriorityTarget = null;
            CompanionBrain.PriorityTargetAlt = null;
            Game.World.Load(room.mapId);

            // Party at the entry: the local player on 'P', companions around it (downed ones rejoin at half HP).
            var local = Game.Player;
            Vector2 entry = Game.World.PlayerSpawn;
            var party = Game.Party;
            if (first && local.IsDead)
            {
                // 다시 도전 after a failed run: back on the feet at the entry.
                if (party != null) party.ReviveMember(local, 1f);
                else local.Revive(1f);
                local.Data.Mana = local.MaxMana;
            }
            local.Place(entry, Facing.Up);
            if (party != null)
                for (int i = 1; i < party.Members.Count; i++)
                {
                    var m = party.Members[i];
                    if (m == null) continue;
                    if (m.IsDead) party.ReviveMember(m, first ? 1f : CompanionRejoinHp);
                    m.Place(party.SpotFor(i - 1), Facing.Up);
                }
            party?.Regroup(RoomRegroupSeconds);

            if (!Follower) SpawnMonsters(room); // [PARTY NET] member PCs get the host's monsters as puppets
            if (!room.isBoss) door = DungeonDoor.Create(Game.World.DungeonDoorCells, Game.World.ObjectsRoot);
            if (door != null) door.Entered += OnDoorEntered;

            Game.Camera.SetTarget(local.transform, true);
            Game.UI.Hud.RefreshAll();
            Game.Audio.PlayMusic(Game.World.Map.music);
            if (room.isBoss) GameEvents.RaiseToast($"보스 방 · {run.Dungeon.bossName}");
            roomReady = true;
            if (NetHost) PartyNet.Current.HostRoomLoaded(index);
            RoomChanged?.Invoke();
        }

        void SpawnMonsters(RoomDef room)
        {
            var cells = Game.World.DungeonSpawns;
            foreach (var g in room.groups)
            {
                var spots = new List<Vector2>();
                foreach (var (digit, pos) in cells) if (digit == g.digit) spots.Add(pos);
                if (spots.Count == 0) spots.Add(Game.World.PlayerSpawn + Vector2.up * 5f);
                int level = DungeonMonsters.LevelFor(run.Numbers, g);
                for (int i = 0; i < g.count; i++)
                {
                    Vector2 p = spots[i % spots.Count];
                    // More monsters than cells: spread them around the cell.
                    if (i >= spots.Count)
                    {
                        float a = i * 2.39996f;
                        Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.8f;
                        if (Game.World.IsFree(q)) p = q;
                    }
                    var e = DungeonMonsters.Spawn(g.monsterId, p, Game.World.ObjectsRoot, run.HpMul, run.DamageMul, level);
                    if (e == null) continue;
                    run.Monsters++;
                    if (g.isBoss) bosses.Add(e);
                    var h = e.GetComponent<Health>();
                    if (h != null) h.Damaged += OnMonsterDamaged;
                }
            }
        }

        void OnMonsterDamaged(DamageInfo info)
        {
            if (run == null || run.State != DungeonRunState.Playing) return;
            var m = info.AttackerMember;
            if (m != null && m.IsLocal) run.RegisterHit(Time.time);
            if (m != null && NetHost) PartyNet.Current.HostRegisterHit(m, info.amount);
        }

        void OnEnemyKilled(EnemyController e, int xp)
        {
            if (run != null && run.State != DungeonRunState.Failed && roomReady) run.Kills++;
        }

        static bool Alive(EnemyController e) => e != null && !e.IsDead && e.isActiveAndEnabled;

        void Update()
        {
            if (run == null) return;
            if (run.State == DungeonRunState.Playing && Game.IsWorldRunning && !busy) run.Elapsed += Time.deltaTime;
            if (ReviveOpen) UpdateRevive();
            if (!roomReady || busy || run.State != DungeonRunState.Playing) return;
            if (Follower) return; // [PARTY NET] the host says when a room or the run is cleared

            if (run.InBossRoom)
            {
                bool bossAlive = false;
                foreach (var b in bosses) if (Alive(b)) bossAlive = true;
                if (!bossAlive && bosses.Count > 0) StartCoroutine(ClearRoutine());
                else if (bosses.Count == 0 && !AnyAlive()) StartCoroutine(ClearRoutine());
                return;
            }
            if (!roomCleared && !AnyAlive()) OnRoomCleared();
        }

        static bool AnyAlive()
        {
            foreach (var e in EnemyController.Active) if (Alive(e)) return true;
            return false;
        }

        void OnRoomCleared()
        {
            if (NetHost) PartyNet.Current.HostRoomCleared();
            roomCleared = true;
            run.ClearedRooms.Add(run.RoomIndex);
            if (door != null) door.Open();
            Game.Audio.PlaySfx("build_complete");
            GameEvents.RaiseToast("방을 정리했다! 문이 열렸다.");
            RoomChanged?.Invoke();
        }

        void OnDoorEntered()
        {
            if (run == null || busy || run.State != DungeonRunState.Playing || Follower) return;
            int next = run.Room.next != null && run.Room.next.Length > 0 ? run.Room.next[0] : run.RoomIndex + 1;
            if (next >= run.RoomCount) return;
            Game.Audio.PlaySfx("confirm");
            StartCoroutine(Transition(() => LoadRoom(next, false)));
        }

        /// <summary>Automated checks: go through the open gate as if the player walked in.</summary>
        public bool DevEnterDoor()
        {
            if (door == null || !door.IsOpen) return false;
            OnDoorEntered();
            return true;
        }

        // =============================== Clear ===============================

        IEnumerator ClearRoutine()
        {
            run.State = DungeonRunState.Clearing;
            run.ClearedRooms.Add(run.RoomIndex);
            // Nobody dies to a stray hit during the celebration.
            if (Game.Party != null)
                foreach (var m in Game.Party.AliveMembers) m.Health.SetInvulnerable(SlowMotionSeconds + ClearHoldSeconds + 3f);
            ClearBanner?.Invoke();
            Game.Audio.PlaySfx("dungeon_clear"); // [H3]
            Game.Camera?.Shake(0.15f, 0.3f);
            if (!Game.IsOnlineWorld && Game.IsPlaying) Time.timeScale = SlowMotionScale;
            yield return new WaitForSecondsRealtime(SlowMotionSeconds);
            Game.State.RefreshTimeScale();
            // The rest of the room falls with its master (retried: a monster hit a moment ago is briefly invulnerable).
            float holdEnd = Time.realtimeSinceStartup + ClearHoldSeconds;
            while (AnyAlive() && Time.realtimeSinceStartup < holdEnd)
            {
                foreach (var e in new List<EnemyController>(EnemyController.Active))
                    if (Alive(e)) e.TakeDamage(new DamageInfo(e.GetComponent<Health>().Current + 99999, e.Position, 0f, Team.Player));
                yield return null;
            }
            float rest = holdEnd - Time.realtimeSinceStartup;
            if (rest > 0f) yield return new WaitForSecondsRealtime(rest);
            while (!Game.IsWorldRunning || busy) yield return null;
            FinishCleared();
        }

        void FinishCleared()
        {
            Pickup.CollectAll(); // [DUNGEON] leftover drops go into the bag
            if (NetHost)
            {
                PartyNet.Current.HostRunEnded(true, null, run.Elapsed);
                PartyRunSession.Instance?.SendHostReport(true, run.Elapsed);
            }
            if (OnlineEconomy.On && OnlineEconomy.RunId != null) { StartCoroutine(FinishClearedOnline()); return; }
            var auth = DungeonAuthority.Current;
            run.State = DungeonRunState.Cleared;
            SnapshotDamage();
            run.Score = auth.ScoreRun(run);
            run.Rank = run.Score.Rank;
            var now = ResetClock.Now;
            Progress.RecordClear(run.Dungeon.id, run.Difficulty, run.Rank);
            if (!run.RewardsLocked)
            {
                run.XpGained = auth.ClearXp(run, run.Rank);
                if (run.XpGained > 0) Game.Session.Progression.AddXp(run.XpGained);
                run.Cards = auth.DealCards(run, Game.Player.Class);
                if (run.Dungeon.isRaid)
                {
                    Progress.ClaimRaid(run.Dungeon, now);
                    PayRaidKeys(run.Dungeon);
                }
            }
            EndRunState();
            RunEnded?.Invoke(run);
            ShowResult();
        }

        /// <summary>
        /// [SERVER] The server checks the run, decides rank and clear XP (already in its delta) and keeps the four
        /// cards hidden until one is picked. The local score is shown only if the server has none.
        /// </summary>
        IEnumerator FinishClearedOnline()
        {
            run.State = DungeonRunState.Cleared;
            SnapshotDamage();
            run.Score = DungeonAuthority.Current.ScoreRun(run);
            run.Rank = run.Score.Rank;
            busy = true;
            bool answered = false;
            Dictionary<string, object> data = null;
            OnlineEconomy.FinishDungeon(true, run.Elapsed, run.HitsTaken, run.MaxCombo, run.RevivesUsed, d => { data = d; answered = true; });
            float waited = 0f, limit = PartyNet.Active ? OnlineEconomy.PartyResultWaitSeconds + 15f : 15f;
            if (PartyNet.Active) GameEvents.RaiseToast("다른 파티원의 결과를 확인하는 중...");
            while (!answered && waited < limit) { waited += Time.unscaledDeltaTime; yield return null; }
            busy = false;
            string result = MiniJson.Str(data, "result");
            if (result == "cleared")
            {
                var score = MiniJson.Obj(data, "score");
                if (score != null)
                    run.Score = new RankScore
                    {
                        time = MiniJson.Int(score, "time"), hits = MiniJson.Int(score, "hits"), kills = MiniJson.Int(score, "kills"),
                        combo = MiniJson.Int(score, "combo"), revivePenalty = MiniJson.Int(score, "revive_penalty"),
                    };
                if (Enum.TryParse(MiniJson.Str(data, "rank", ""), out DungeonRank rank)) run.Rank = rank;
                run.XpGained = MiniJson.Int(data, "granted_xp");
                // Face-down placeholders: the server reveals them on the pick.
                int count = MiniJson.Int(data, "card_count");
                run.Cards = count > 0 ? new List<RewardCard>(new RewardCard[count]) : null;
                Progress.RecordClear(run.Dungeon.id, run.Difficulty, run.Rank);
                var raid = MiniJson.Obj(data, "raid");
                if (raid != null && raid.TryGetValue("reward_locked", out var rl) && rl is bool locked && locked)
                    GameEvents.RaiseToast(RaidLockText(MiniJson.Str(raid, "lock_reason")));
                else if (run.Dungeon.isRaid)
                {
                    Progress.ClaimRaid(run.Dungeon, ResetClock.Now); // the server paid this period's reward
                    int cores = MiniJson.Int(raid, "core_gain");
                    if (cores > 0) GameEvents.RaiseToast($"고대의 핵 +{cores} (대장간에서 장비 승급에 쓴다)");
                }
            }
            else
            {
                // Held for review or not answered: no reward on this screen.
                run.XpGained = 0;
                run.Cards = null;
                run.NoRewardNote = result == "held" ? "held" : "noanswer";
                GameEvents.RaiseToast(result == "held" ? "결과를 확인하는 중이다. 보상은 확인 후 지급된다." : "서버에 결과를 보내지 못했다.");
            }
            EndRunState();
            RunEnded?.Invoke(run);
            ShowResult();
        }

        /// <summary>[SERVER] Flips card <paramref name="index"/> on the server; done(own card or null). All four cards are filled in.</summary>
        public void TakeCardOnline(int index, Action<RewardCard?> done)
        {
            var current = run;
            OnlineEconomy.PickCard(index, (own, all) =>
            {
                if (current != null && all != null && current.Cards != null)
                    for (int i = 0; i < all.Count && i < current.Cards.Count; i++) current.Cards[i] = all[i];
                done?.Invoke(own);
            });
        }

        /// <summary>[RAID] Mid raids drop seal key fragments; final raids take the fragments they cost.</summary>
        void PayRaidKeys(DungeonDef raid)
        {
            var bag = Game.Session.Inventory;
            if (raid.keyCost > 0)
            {
                bag.Remove(DungeonDatabase.SealKey, Mathf.Min(raid.keyCost, bag.Count(DungeonDatabase.SealKey)));
                GameEvents.RaiseToast($"봉인 열쇠 조각 {raid.keyCost}개가 빛을 잃었다.");
            }
            if (raid.keyMax > 0)
            {
                int keys = UnityEngine.Random.Range(raid.keyMin, raid.keyMax + 1);
                bag.Add(DungeonDatabase.SealKey, keys);
                GameEvents.RaiseToast($"봉인 열쇠 조각 +{keys} (보유 {bag.Count(DungeonDatabase.SealKey)})");
            }
            int cores = PromoteRules.CoreGain(raid, run != null ? run.Difficulty : DungeonDifficulty.Normal);
            if (cores > 0)
            {
                bag.Add(DungeonDatabase.RaidCore, cores);
                GameEvents.RaiseToast($"고대의 핵 +{cores} (대장간에서 장비 승급에 쓴다)");
            }
        }

        // =============================== Death / revive / failure ===============================

        /// <summary>
        /// Called by <see cref="GameFlow.OnPlayerDied"/>. True when the dungeon takes over (coin countdown or
        /// failure) instead of the normal game over.
        /// </summary>
        public bool HandleLocalDeath()
        {
            if (run == null) return false;
            if (run.State != DungeonRunState.Playing) return true;
            if (Follower)
            {
                // [PARTY NET] Downed members get up at the next room (the host's rule for every member).
                GameEvents.RaiseToast("쓰러졌다. 다음 방에서 다시 일어난다.");
                return true;
            }
            if (run.RevivesLeft <= 0)
            {
                StartCoroutine(FailLater("부활 횟수를 모두 사용했다."));
                return true;
            }
            ReviveOpen = true;
            reviveClock = 0f;
            reviveUntil = ReviveSeconds;
            Game.Audio.PlaySfx("cancel");
            return true;
        }

        void UpdateRevive()
        {
            if (run == null || run.State != DungeonRunState.Playing) { ReviveOpen = false; return; }
            if (Game.Player != null && !Game.Player.IsDead) { ReviveOpen = false; return; }
            if (!Game.IsWorldRunning) return;
            reviveClock += Time.deltaTime;
            var input = Game.Input;
            if (Game.IsPlaying && !Game.State.ChangedThisFrame && input != null)
            {
                if (input.SubmitPressed || input.InteractPressed || input.AttackPressed) { AcceptRevive(); return; }
                if (input.CancelPressed) { GiveUp(); return; }
            }
            if (reviveClock >= reviveUntil) { ReviveOpen = false; Fail("부활하지 않았다. (시간 초과)"); }
        }

        /// <summary>부활: spends one revive; full HP / MP where the player fell and 3 s of invulnerability.</summary>
        public bool AcceptRevive()
        {
            var local = Game.Player;
            if (!ReviveOpen || run == null || local == null || !local.IsDead || run.RevivesLeft <= 0) return false;
            ReviveOpen = false;
            run.RevivesUsed++;
            if (NetHost) PartyNet.Current.HostRevived(local);
            if (Game.Party != null) Game.Party.ReviveMember(local, 1f, ReviveInvulnerable);
            else local.Revive(1f, ReviveInvulnerable);
            local.Data.Mana = local.MaxMana;
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast($"부활했다! (남은 부활 {run.RevivesLeft})");
            RoomChanged?.Invoke();
            return true;
        }

        bool leaving;

        public bool RunOver => run != null && run.IsOver;

        /// <summary>마을로 (dungeon HUD): an ended run goes straight home; a running one fails first, without the result window.</summary>
        public void LeaveToVillage()
        {
            if (run == null || busy) return;
            if (!run.IsOver)
            {
                leaving = true;
                ReviveOpen = false;
                Fail("던전에서 나왔다.");
                if (!run.IsOver) { leaving = false; return; } // could not end it right now (clearing)
            }
            leaving = false;
            ExitToVillage(false);
        }

        /// <summary>포기: the run fails.</summary>
        public void GiveUp()
        {
            if (!ReviveOpen) return;
            ReviveOpen = false;
            Fail("던전 공략을 포기했다.");
        }

        IEnumerator FailLater(string reason)
        {
            yield return new WaitForSecondsRealtime(FailDelay);
            Fail(reason);
        }

        void Fail(string reason)
        {
            if (run == null || run.IsOver || run.State == DungeonRunState.Clearing) return;
            if (NetHost)
            {
                PartyNet.Current.HostRunEnded(false, reason, run.Elapsed);
                PartyRunSession.Instance?.SendHostReport(false, run.Elapsed);
            }
            run.State = DungeonRunState.Failed;
            run.FailReason = reason;
            SnapshotDamage();
            run.Rank = DungeonRank.F;
            if (OnlineEconomy.On) OnlineEconomy.FinishDungeon(false, run.Elapsed, run.HitsTaken, run.MaxCombo, run.RevivesUsed, null); // [SERVER]
            EndRunState();
            Game.Audio.PlaySfx("player_down");
            RunEnded?.Invoke(run);
            ShowResult();
        }

        void SnapshotDamage()
        {
            run.MemberDamage.Clear();
            var party = Game.Party;
            if (party == null) return;
            foreach (var m in party.Members)
                if (m != null) run.MemberDamage.Add((m.DisplayName, party.DamageOf(m), m.IsLocal));
        }

        void ShowResult()
        {
            if (leaving) return; // 마을로: going home instead of the result window
            StartCoroutine(ShowResultRoutine());
        }

        IEnumerator ShowResultRoutine()
        {
            while (!Game.IsPlaying || busy) yield return null;
            // [BGM] Result jingle; 다시 도전 (LoadRoom) / leaving (ExitToVillage) switch back to the map's music.
            Game.Audio.PlayMusic(run.State == DungeonRunState.Cleared ? MapRegistry.MusicClear : MapRegistry.MusicFail);
            Game.UI.DungeonResult.Setup(run);
            Game.Flow.OpenWindow(Game.UI.DungeonResult);
        }

        // =============================== Result actions ===============================

        /// <summary>Flips card <paramref name="index"/> for the local player: its reward goes into the bag. Null if not allowed.</summary>
        public RewardCard? TakeCard(int index)
        {
            if (run == null || run.Cards == null || index < 0 || index >= run.Cards.Count) return null;
            var card = run.Cards[index];
            if (card.count > 0 && !string.IsNullOrEmpty(card.itemId)) Game.Session.Inventory.Add(card.itemId, card.count);
            return card;
        }

        /// <summary>Back to the village (and optionally straight into the select window).</summary>
        public void ExitToVillage(bool openSelect)
        {
            if (busy) return;
            if (Game.State.Current == GameState.Inventory) Game.Flow.CloseInventory();
            StartCoroutine(Transition(() =>
            {
                EndRunState();
                run = null;
                Game.Party?.SetDungeonCompanions(false); // back on the map: the mercenaries stay behind
                OnlineEconomy.LeaveDungeon(); // [SERVER]
                PartyRunSession.OnBackInVillage(); // [PARTY] the fight connection closes
                Game.Session.MapId = MapRegistry.Village;
                Game.World.Load(MapRegistry.Village);
                var local = Game.Player;
                local.Spawn(Game.World.PlayerSpawn, Facing.Down, int.MaxValue, Game.Session.PlayerMaxHealth);
                local.Data.Mana = local.MaxMana;
                Game.Camera.SetTarget(local.transform, true);
                GameEvents.RaiseMapEntered(MapRegistry.Village); // [STORY] back in town (story companion leaves, scenes queued there start)
                Game.Quest.NotifyChanged();
                Game.UI.Hud.RefreshAll();
                Game.Audio.PlayMusic(Game.World.Map.music);
                GameEvents.RaiseToast($"{Game.World.Map.displayName}");
                RoomChanged?.Invoke();
            }, openSelect ? (Action)(() => Game.UI.Dungeon.Open(false)) : null));
        }

        /// <summary>Title screen / new game while in a run: drop it without any transition.</summary>
        public void AbortRun()
        {
            StopAllCoroutines();
            busy = false;
            holdBlack = 0f;
            Game.UI?.Fader?.HideCard();
            ReviveOpen = false;
            EndRunState();
            run = null;
            Game.Party?.SetDungeonCompanions(false);
            OnlineEconomy.LeaveDungeon(); // [SERVER] an abandoned run is closed by the server later
            if (PartyRunSession.Active) PartyRunSession.Instance.Leave(); // [PARTY]
            PartyNet.End();
            if (Game.State.Current == GameState.Playing) Time.timeScale = 1f;
            RoomChanged?.Invoke();
        }

        /// <summary>Stops the run's live hooks (field rules back for the companions); the run data stays for the result.</summary>
        void EndRunState()
        {
            ReviveOpen = false;
            roomReady = false;
            if (door != null) door.Entered -= OnDoorEntered;
            Game.Party?.SetCompanionAutoRevive(true);
            HookLocal(null);
        }

        void HookLocal(PlayerController local)
        {
            if (hookedLocal != null) hookedLocal.Damaged -= OnLocalDamaged;
            hookedLocal = local != null ? local.Health : null;
            if (hookedLocal != null) hookedLocal.Damaged += OnLocalDamaged;
        }

        void OnLocalDamaged(DamageInfo info)
        {
            if (run != null && run.State == DungeonRunState.Playing) run.HitsTaken++;
        }

        /// <summary>[E4] How long the loading card stays on the black screen when a run starts.</summary>
        const float LoadingCardSeconds = 1.0f;
        float holdBlack;

        IEnumerator Transition(Action action, Action after = null)
        {
            if (busy) yield break;
            busy = true;
            var fader = Game.UI.Fader;
            yield return fader.Fade(1f, FadeSeconds);
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
            yield return null;
            if (holdBlack > 0f) { yield return new WaitForSecondsRealtime(holdBlack); holdBlack = 0f; }
            yield return fader.Fade(0f, FadeSeconds);
            fader.HideCard();
            busy = false;
            try { after?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
