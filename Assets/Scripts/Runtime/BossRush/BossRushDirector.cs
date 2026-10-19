using System;
using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [BOSS 2026-10-20] One 3-minute attempt at the 월드보스 or 길드보스 (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 0, 2, 3):
    /// the server starts it (one try spent), the hero and the AI companions fight 심연의 거신 in the arena, and when the
    /// time runs out, the hero falls, the boss falls or the player leaves, the hero's own damage to the boss goes to the
    /// server (AI damage does not count). Then back to where the attempt started, with the result in the boss window.
    /// Online only; a person in a human party enters alone with AI (see <see cref="CanEnter"/>).
    /// </summary>
    [DefaultExecutionOrder(-640)]
    public sealed class BossRushDirector : MonoBehaviour
    {
        public bool Active { get; private set; }
        public bool Busy { get; private set; }
        public string Kind => attempt?.kind;
        public long MyDamage { get; private set; }
        public int Hits { get; private set; }
        /// <summary>Seconds left in the attempt (the full length before the boss wakes).</summary>
        public float RemainingSeconds => !Active ? 0f : !clockRunning ? BossRushClient.AttemptSeconds : Mathf.Max(0f, (float)(deadlineUtc - DateTime.UtcNow).TotalSeconds);
        public float AttemptSeconds => BossRushClient.AttemptSeconds;
        public EnemyController Boss => boss;

        /// <summary>The last finished attempt (shown at the top of the boss window).</summary>
        public static string LastResultText { get; private set; }
        /// <summary>Kind of the last finished attempt (the boss window opens on its tab).</summary>
        public static string LastKind { get; private set; }
        public event Action Changed;

        /// <summary>True while an attempt runs or is entering / leaving (story, quests and other content wait).</summary>
        public static bool InArena => Game.BossRush != null && (Game.BossRush.Active || Game.BossRush.Busy);

        internal string ReturnMapForSave => returnMap ?? MapRegistry.Village;
        internal Vector2 ReturnPositionForSave => returnPosition;
        internal Facing ReturnFacingForSave => returnFacing;

        BossRushClient.Attempt attempt;
        EnemyController boss;
        Health bossHealth;
        DateTime deadlineUtc;
        bool clockRunning, finishing;
        float startedAt;
        string returnMap;
        Vector2 returnPosition;
        Facing returnFacing;
        int generation;

        public static BossRushDirector Create(Transform parent)
        {
            var go = new GameObject("BossRush");
            go.transform.SetParent(parent, false);
            return go.AddComponent<BossRushDirector>();
        }

        public bool CanEnter(string kind, out string reason)
        {
            reason = BossRushClient.BlockReason(kind);
            if (reason != null) return false;
            if (Active || Busy || BossRushClient.Busy) reason = L.T("이미 보스에 도전하고 있습니다.");
            else if (Game.Flow != null && Game.Flow.IsTransitioning) reason = L.T("이동이 끝난 뒤 도전하세요.");
            else if (Game.Player == null || !Game.Player.gameObject.activeInHierarchy || Game.Player.IsDead) reason = L.T("캐릭터로 접속한 뒤 도전하세요.");
            else if (Game.MiniDungeon?.Active == true || Game.MiniDungeon?.Busy == true) reason = L.T("미니던전에서 나간 뒤 도전하세요.");
            else if (Game.Dungeon != null && Game.Dungeon.InRun || Game.World?.Map.instanced == true) reason = L.T("현재 던전에서 나간 뒤 도전하세요.");
            else if (PartyNet.Active || PartyRunSession.Active) reason = L.T("파티 던전을 마친 뒤 도전하세요.");
            else if (PartyClient.Instance?.InParty == true && PartyClient.Instance.Members.Count >= 2) reason = L.T("보스 도전은 혼자(AI 동료와 함께) 들어갑니다. 파티를 나간 뒤 도전하세요.");
            else if (TradeClient.Current != null || TradeClient.Busy) reason = L.T("교환을 마치거나 취소한 뒤 도전하세요.");
            else if (Game.State.Current != GameState.Playing && Game.State.Current != GameState.Inventory && Game.State.Current != GameState.Paused) reason = L.T("대화나 연출을 마친 뒤 도전하세요.");
            return reason == null;
        }

        /// <summary>Asks the server for an attempt, then goes to the arena. False (with a toast) when not allowed.</summary>
        public bool Enter(string kind)
        {
            if (!CanEnter(kind, out var why)) { GameEvents.RaiseToast(why); Game.Audio?.PlaySfx("cancel"); return false; }
            Busy = true;
            int gen = ++generation;
            QuestAutoPilot.Stop("");
            Changed?.Invoke();
            BossRushClient.Start(kind, (a, error) =>
            {
                if (gen != generation) return;
                if (a == null) { Busy = false; GameEvents.RaiseToast(error); Game.Audio?.PlaySfx("cancel"); Changed?.Invoke(); return; }
                attempt = a;
                returnMap = Game.World.MapId;
                returnPosition = Game.Player.Position;
                returnFacing = Game.Player.Facing;
                MyDamage = 0;
                Hits = 0;
                finishing = false;
                StartCoroutine(EnterArena(gen));
            });
            return true;
        }

        IEnumerator EnterArena(int gen)
        {
            if (Game.State.Current != GameState.Playing) Game.State.Set(GameState.Playing);
            yield return Game.UI.Fader.Fade(1f, .2f);
            if (gen != generation) yield break;
            FieldSession.Instance?.Leave();
            Game.Dialogue.Abort();
            Game.World.Load(BossRushCatalog.ArenaMap);
            Active = true;
            Game.Session.MapId = BossRushCatalog.ArenaMap;
            Game.Player.Spawn(Game.World.PlayerSpawn, Facing.Up, int.MaxValue, Game.Session.PlayerMaxHealth);
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            var party = Game.Party;
            if (party != null)
            {
                // The hired AI fight here as in raids (they get up by themselves; their damage is not counted).
                party.SetDungeonCompanions(true);
                party.SetCompanionAutoRevive(true);
                party.ResetCompanionFalls();
                party.ResetMeters();
            }
            Game.Camera.SetTarget(Game.Player.transform, true);
            Game.State.RefreshTimeScale();
            GameEvents.RaiseMapEntered(BossRushCatalog.ArenaMap);
            Game.Audio.PlayMusic(Game.World.Map.music);
            Game.UI.Hud.RefreshAll();
            SpawnBoss();
            clockRunning = false;
            yield return Game.UI.Fader.Fade(0f, .2f);
            Busy = false;
            GameEvents.RaiseToast(L.F("{0} · {1}초 동안 준 피해가 기록됩니다", BossRushClient.KindName(attempt.kind), Mathf.RoundToInt(AttemptSeconds)));
            Changed?.Invoke();
            yield return new WaitForSecondsRealtime(BossRushCatalog.WakeSeconds);
            if (gen != generation || !Active) yield break;
            startedAt = Time.unscaledTime;
            // The server's end time when it is sooner than the local count (a slow entry still ends on time).
            var localEnd = DateTime.UtcNow.AddSeconds(AttemptSeconds);
            var serverEnd = attempt.endsAt.ToUniversalTime();
            deadlineUtc = serverEnd > DateTime.UtcNow.AddSeconds(10) && serverEnd < localEnd ? serverEnd : localEnd;
            clockRunning = true;
            Changed?.Invoke();
        }

        void SpawnBoss()
        {
            Vector2 at = Game.World.PlayerSpawn + Vector2.up * 7f;
            foreach (var (digit, pos) in Game.World.DungeonSpawns) if (digit == 1) { at = pos; break; }
            int level = attempt.level > 0 ? attempt.level : BossRushCatalog.DefaultLevel;
            boss = DungeonMonsters.Spawn(string.IsNullOrEmpty(attempt.bossId) || MonsterDatabase.Get(attempt.bossId) == null ? BossRushCatalog.BossId : attempt.bossId,
                at, Game.World.ObjectsRoot, BossRushCatalog.BaseHpMul * attempt.hpMul, BossRushCatalog.BaseDamageMul * attempt.damageMul, level);
            if (boss == null) return;
            bossHealth = boss.GetComponent<Health>();
            if (bossHealth != null) bossHealth.Damaged += OnBossDamaged;
        }

        void OnBossDamaged(DamageInfo info)
        {
            if (!Active || Busy || finishing || !clockRunning) return;
            var m = info.AttackerMember;
            // Only my own hits count: AI companions help but their damage is not the hero's record.
            if (m == null || m != Game.Player || info.amount <= 0) return;
            MyDamage += info.amount;
            Hits++;
        }

        void Update()
        {
            if (!Active || Busy || finishing) return;
            if (Game.World == null || Game.World.MapId != BossRushCatalog.ArenaMap) { Abort(); return; }
            if (Game.Player == null || Game.Player.IsDead) { Finish("death"); return; }
            if (boss == null || boss.IsDead) { Finish("defeated"); return; }
            if (clockRunning && RemainingSeconds <= 0f) Finish("time");
        }

        /// <summary>The player leaves early (HUD button, Esc route, town scroll): the damage so far is recorded.</summary>
        public void Leave() => Finish("left");

        public bool HandleLocalDeath()
        {
            if (!Active) return false;
            Finish("death");
            return true;
        }

        void Finish(string reason)
        {
            if (!Active || Busy || finishing) return;
            finishing = true;
            Busy = true;
            QuestAutoPilot.Stop("");
            float seconds = clockRunning ? Mathf.Min(AttemptSeconds, Time.unscaledTime - startedAt) : 0f;
            clockRunning = false;
            var a = attempt;
            LastKind = a?.kind;
            long damage = MyDamage;
            int hits = Hits;
            int gen = generation;
            Changed?.Invoke();
            string head = reason == "death" ? L.T("쓰러졌습니다.") : reason == "defeated" ? L.T("보스를 쓰러뜨렸습니다!") : reason == "left" ? L.T("도전을 마쳤습니다.") : L.T("시간이 끝났습니다.");
            LastResultText = L.F("{0} 내 피해 {1:N0} · 기록 확인 중...", head, damage);
            BossRushClient.Finish(a, damage, hits, seconds, (res, error) =>
            {
                LastResultText = res == null ? L.F("{0} 내 피해 {1:N0} · {2}", head, damage, error)
                    : L.F("{0} 인정된 피해 <color=#ffe066>{1:N0}</color>{2} · 이번 주 최고 {3:N0} · {4}", head, res.accepted,
                        res.capped ? L.T(" (상한 적용)") : "", res.best, res.rank > 0 ? L.F("{0}위", res.rank) : L.T("순위 집계 중"));
                if (gen == generation) Changed?.Invoke();
                BossRushScreen.Instance?.Redraw();
            });
            StartCoroutine(ReturnRoom(reason, gen));
        }

        IEnumerator ReturnRoom(string reason, int gen)
        {
            yield return new WaitForSecondsRealtime(reason == "left" ? .3f : 1.5f);
            yield return Game.UI.Fader.Fade(1f, .2f);
            if (gen != generation) yield break;
            ClearBoss();
            Active = false;
            Game.Party?.SetDungeonCompanions(false);
            string destination = MapRegistry.Exists(returnMap) && !MapRegistry.Get(returnMap).instanced ? returnMap : MapRegistry.Village;
            Game.Session.MapId = destination;
            Game.World.Load(destination);
            Vector2 at = returnPosition.x >= 0f && returnPosition.y >= 0f && Game.World.IsFree(returnPosition) ? returnPosition : Game.World.PlayerSpawn;
            Game.Player.Spawn(at, returnFacing, int.MaxValue, Game.Session.PlayerMaxHealth);
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            Game.Camera.SetTarget(Game.Player.transform, true);
            Game.State.Set(GameState.Playing);
            Game.State.RefreshTimeScale();
            GameEvents.RaiseMapEntered(destination);
            Game.Audio.PlayMusic(Game.World.Map.music);
            Game.UI.Hud.RefreshAll();
            yield return Game.UI.Fader.Fade(0f, .2f);
            Busy = false;
            finishing = false;
            attempt = null;
            Game.Quest?.NotifyChanged();
            Changed?.Invoke();
            // The result in the boss window (Esc goes back to the game).
            BossRushScreen.Instance?.OpenResult();
        }

        /// <summary>Title / character change: the attempt ends here (the damage so far is still sent).</summary>
        public void Abort()
        {
            if (!Active && !Busy) return;
            if (attempt != null && !finishing && clockRunning)
                BossRushClient.Finish(attempt, MyDamage, Hits, Mathf.Min(AttemptSeconds, Time.unscaledTime - startedAt), (_, __) => { });
            generation++;
            StopAllCoroutines();
            ClearBoss();
            Game.Party?.SetDungeonCompanions(false);
            Active = Busy = finishing = clockRunning = false;
            attempt = null;
            Game.State?.RefreshTimeScale();
            Changed?.Invoke();
        }

        void ClearBoss()
        {
            if (bossHealth != null) bossHealth.Damaged -= OnBossDamaged;
            if (boss != null) Destroy(boss.gameObject);
            boss = null;
            bossHealth = null;
        }

        void OnApplicationQuit() => Abort();
    }
}
