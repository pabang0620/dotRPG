using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class DungeonDirector
    {
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
                lockReason = run.RewardsLocked ? MiniJson.Str(serverRun, "lock_reason") : null; // shown once the room loads
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

        /// <summary>Why this raid run pays nothing (server code or the local check), shown when the first room loads.</summary>
        string lockReason;

        static string RaidLockText(string reason)
        {
            switch (reason)
            {
                case "TOO_FEW_HUMANS": return "레이드 보상은 2명 이상의 파티만 받습니다. (연습 입장)";
                case "LOW_CONTRIBUTION": return "전투 기여가 부족해 클리어 보상이 없습니다.";
                case "ALREADY_CLAIMED": return "이번 기간 레이드 보상을 이미 받았습니다. (연습 입장)";
                case "KEYS_MISSING": return "봉인 열쇠 조각이 모자라 연습 입장입니다. 클리어하면 이야기는 이어지지만 보상은 없습니다.";
                default: return "레이드 보상이 없는 연습 입장입니다.";
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
                GameEvents.RaiseToast("오늘 입장 횟수를 모두 사용했습니다.");
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
            lockReason = run.RewardsLocked ? "ALREADY_CLAIMED" : null;
            // A final raid without the seal keys is a practice run: the clear still moves the story on.
            if (dungeon.isRaid && dungeon.keyCost > 0 && !run.RewardsLocked && Game.Session.Inventory.Count(DungeonDatabase.SealKey) < dungeon.keyCost)
            {
                run.RewardsLocked = true;
                lockReason = "KEYS_MISSING";
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
                if (run.RewardsLocked) GameEvents.RaiseToast(RaidLockText(lockReason));
            }));
        }
    }
}
