using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Marker isolates these kills from field loot, achievements and quest-kill rewards.</summary>
    public sealed class MiniDungeonEnemy : MonoBehaviour
    {
        internal MiniDungeonDirector Owner;
        internal int Generation;
        internal string Reference;
        internal bool Credited;
    }

    /// <summary>Independent solo training instance. It never spends weekday entries or grants anything except XP.</summary>
    [DefaultExecutionOrder(-650)]
    public sealed partial class MiniDungeonDirector : MonoBehaviour
    {
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        internal static DateTime Now => UtcNow != null ? UtcNow().ToUniversalTime() : DateTime.UtcNow;
        public bool Active { get; private set; }
        public bool Busy { get; private set; }
        public bool Ready => !OnlineSession.Playing || onlineReady && onlineCharacter == OnlineSession.Current.ActiveCharacter;
        public int Tier => definition != null ? definition.Tier : 0;
        public int Kills { get; private set; }
        public int EarnedXp { get; private set; }
        /// <summary>Kill gold so far: a fixed amount per kill on both sides, so the count is exact (the HUD's counter).</summary>
        public int EarnedGold => Kills * MiniDungeonCatalog.GoldPerKill;
        public int XpPerKill { get; private set; }
        /// <summary>XP per kill while the next main quest waits on level (<see cref="MiniDungeonCatalog.KillsPerBarStuck"/>).</summary>
        public int CatchUpXpPerKill { get; private set; }
        /// <summary>Level the next main quest waits on (0 = none, normal XP). Online: the server's last answer.</summary>
        public int CatchUpGate => OnlineSession.Playing && onlineReady ? onlineGate :
            MiniDungeonCatalog.CatchUpGate(Game.Session != null ? Game.Session.Progression.Level : 0);
        public bool CatchingUp => CatchUpGate > 0;
        public float RemainingSeconds => Active ? Mathf.Max(0f, (float)(deadlineUtc - Now).TotalSeconds) :
            OnlineSession.Playing ? onlineRemaining : Game.Session != null ? Game.Session.MiniDungeonProgress.RemainingSeconds(Now) : MiniDungeonCatalog.DailySeconds;
        public event Action Changed;
        internal string ReturnMapForSave => returnMap ?? MapRegistry.Village;
        internal Vector2 ReturnPositionForSave => returnPosition;
        internal Facing ReturnFacingForSave => returnFacing;

        MiniDungeonDef definition;
        DateTime deadlineUtc;
        string returnMap;
        Vector2 returnPosition;
        Facing returnFacing;
        int generation, entryLevel;
        float nextSave, nextRecovery;
        readonly List<EnemyController> enemies = new List<EnemyController>();
        readonly List<Vector2> spawnPoints = new List<Vector2>();
        readonly List<float> respawnAt = new List<float>();

        public static MiniDungeonDirector Create(Transform parent)
        {
            var go = new GameObject("MiniDungeon");
            go.transform.SetParent(parent, false);
            return go.AddComponent<MiniDungeonDirector>();
        }

        public void Activate() => Refresh();
        public MiniDungeonStatus GetStatus() => new MiniDungeonStatus { Ready = Ready, Busy = Busy, Active = Active,
            Tier = Tier, Kills = Kills, EarnedXp = EarnedXp, RemainingSeconds = RemainingSeconds };

        public void Refresh()
        {
            if (OnlineSession.Playing) RefreshOnline();
            else { Game.Session?.MiniDungeonProgress.Refresh(Now); Changed?.Invoke(); }
        }

        public bool CanEnter(int tier, out string reason)
        {
            reason = "";
            var d = MiniDungeonCatalog.Get(tier);
            if (d == null) reason = L.T("존재하지 않는 미니던전입니다.");
            else if (Active || Busy || Game.Flow != null && Game.Flow.IsTransitioning) reason = L.T("이미 입장 중이거나 미니던전을 이용하고 있습니다.");
            else if (Game.Player == null || !Game.Player.gameObject.activeInHierarchy || Game.Player.IsDead) reason = L.T("캐릭터로 접속한 뒤 입장하세요.");
            else if (ContentUnlocks.LockedReason(ContentUnlocks.Feature.MiniDungeon) is string locked) reason = locked; // [UNLOCK]
            else if (Game.Session.Progression.Level >= Progression.LevelCap) reason = L.T("만렙 캐릭터는 경험치 미니던전에 입장할 수 없습니다.");
            else if (Game.Session.Progression.Level < d.MinLevel || Game.Session.Progression.Level > d.MaxLevel) reason = L.F("이 구간은 Lv.{0}~{1} 캐릭터만 입장할 수 있습니다.", d.MinLevel, d.MaxLevel);
            else if (Game.Dungeon != null && Game.Dungeon.InRun || Game.World?.Map.instanced == true) reason = L.T("현재 던전에서 나간 뒤 입장하세요.");
            else if (PartyNet.Active || PartyRunSession.Active || FieldSession.Active || PartyClient.Instance?.InParty == true) reason = L.T("미니던전은 1인 콘텐츠입니다. 파티를 나간 뒤 입장하세요.");
            else if (TradeClient.Current != null || TradeClient.Busy) reason = L.T("교환을 마치거나 취소한 뒤 입장하세요.");
            else if (!Ready) reason = L.T("서버에서 오늘의 이용 시간을 확인하고 있습니다.");
            else if (RemainingSeconds <= 0f) reason = L.T("오늘의 미니던전 30분을 모두 사용했습니다. 오전 6시에 초기화됩니다.");
            else if (Game.State.Current != GameState.Playing && Game.State.Current != GameState.Inventory && Game.State.Current != GameState.Paused) reason = L.T("대화나 연출을 마친 뒤 입장하세요.");
            return reason.Length == 0;
        }

        /// <returns>True when entry started; online entry waits for server authorization.</returns>
        public bool Enter(int tier)
        {
            if (!CanEnter(tier, out var why)) { GameEvents.RaiseToast(why); return false; }
            definition = MiniDungeonCatalog.Get(tier);
            returnMap = Game.World.MapId;
            returnPosition = Game.Player.Position;
            returnFacing = Game.Player.Facing;
            entryLevel = Game.Session.Progression.Level;
            Kills = EarnedXp = 0;
            XpPerKill = MiniDungeonCatalog.XpPerKill(entryLevel);
            CatchUpXpPerKill = MiniDungeonCatalog.XpPerKill(entryLevel, true);
            Busy = true;
            generation++;
            QuestAutoPilot.Stop("");
            if (OnlineSession.Playing) BeginOnline();
            else
            {
                var budget = Game.Session.MiniDungeonProgress;
                if (!budget.Begin(Now, budget.RemainingSeconds(Now), returnMap, returnPosition, returnFacing)) { Busy = false; return false; }
                deadlineUtc = budget.DeadlineUtc;
                // Persist the running clock before combat can grant the first experience point.
                SaveSnapshot();
                StartCoroutine(EnterRoom());
            }
            Changed?.Invoke();
            return true;
        }

        IEnumerator EnterRoom()
        {
            if (Game.State.Current != GameState.Playing) Game.State.Set(GameState.Playing);
            yield return Game.UI.Fader.Fade(1f, .2f);
            FieldSession.Instance?.Leave();
            Game.Party?.SetDungeonCompanions(false);
            Game.Dialogue.Abort();
            Game.World.Load(definition.MapId);
            Active = true;
            Game.Session.MapId = definition.MapId;
            Game.Player.Spawn(Game.World.PlayerSpawn, Facing.Down, int.MaxValue, Game.Session.PlayerMaxHealth);
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            Game.Camera.SetTarget(Game.Player.transform, true);
            Game.State.RefreshTimeScale();
            GameEvents.RaiseMapEntered(definition.MapId);
            Game.Audio.PlayMusic(definition.Music);
            Game.UI.Hud.RefreshAll();
            spawnPoints.Clear(); enemies.Clear(); respawnAt.Clear();
            foreach (var point in Game.World.DungeonSpawns)
                if (spawnPoints.Count < MiniDungeonCatalog.SpawnCount && Game.World.IsFree(point.pos)) spawnPoints.Add(point.pos);
            for (int i = 0; i < spawnPoints.Count; i++) { enemies.Add(null); respawnAt.Add(Time.time + 2.1f); }
            nextRecovery = Time.unscaledTime + 2f;
            nextSave = Time.unscaledTime + 5f;
            yield return Game.UI.Fader.Fade(0f, .2f);
            Busy = false;
            QuestAutoPilot.StartMiniDungeonHunt();
            GameEvents.RaiseToast(L.T("경험치 미니던전 · 자동 사냥 시작 · 하루 총 30분"));
            Changed?.Invoke();
        }

        void Update()
        {
            if (!Active || Busy) return;
            if (Game.Player == null || Game.Player.IsDead) { Leave("death"); return; }
            if (Game.World == null || !MiniDungeonCatalog.IsMap(Game.World.MapId)) { Abort(); return; }
            if (!OnlineSession.Playing) Game.Session.MiniDungeonProgress.Refresh(Now);
            if (RemainingSeconds <= 0f || !OnlineSession.Playing && !Game.Session.MiniDungeonProgress.Active)
            { Leave("expired"); return; }
            if (Game.Session.Progression.Level >= Progression.LevelCap) { Leave("max_level"); return; }
            if (OnlineSession.Playing) TickOnline();
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] == null && Time.time >= respawnAt[i]) SpawnAt(i);
            // Training fountains recover the actor directly, never drink or create inventory items.
            if (Time.unscaledTime >= nextRecovery && Game.IsWorldRunning)
            {
                nextRecovery = Time.unscaledTime + 2f;
                Game.Player.Health.Heal(Mathf.Max(1, Mathf.CeilToInt(Game.Player.Health.Max * .08f)));
                Game.Player.Data.Mana = Mathf.Min(Game.Player.MaxMana, Game.Player.Mana + Game.Player.MaxMana * .08f);
            }
            if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 5f; SaveSnapshot(); }
        }

        void SpawnAt(int slot)
        {
            var def = MonsterDatabase.Get(definition.MonsterId).Clone();
            def.noLoot = true;
            def.armor = 0f;
            def.xp = 0;
            def.name = definition.Theme == MapTheme.Canyon ? L.T("수련 암석 골렘") : definition.Theme == MapTheme.Winter ? L.T("수련 설산 예티") : L.T("수련 해골");
            if (definition.Theme != MapTheme.Forest)
                def.look = new CharacterLook { id = definition.Theme == MapTheme.Canyon ? RegionalMonsterArt.Rock : RegionalMonsterArt.Yeti, body = BodyKind.Monster, hairStyle = HairStyle.Bald };
            var stats = ScriptableObject.CreateInstance<EnemyStats>();
            stats.name = "MiniTraining_" + entryLevel;
            stats.enemyId = definition.MonsterId; stats.displayName = def.name;
            stats.maxHealth = Mathf.Max(20, Mathf.RoundToInt(def.hp * .45f * (1f + MonsterDatabase.HpPerLevel * (entryLevel - 1))));
            stats.attackDamage = Mathf.Max(1, Mathf.RoundToInt(def.damage * .20f * (1f + MonsterDatabase.DamagePerLevel * (entryLevel - 1))));
            stats.xpReward = 0; stats.attackKnockback = 1.2f;
            stats.hurtStunTime = .25f; stats.invulnerableTime = .12f; stats.knockbackSpeed = 2f;
            stats.wanderSpeed = 1f; stats.chaseSpeed = 2f; stats.wanderRadius = 1.5f;
            stats.detectRadius = 9f; stats.loseInterestRadius = 15f; stats.leashRadius = 20f;
            stats.attackRange = .95f; stats.windupTime = .65f; stats.recoverTime = 1.1f;
            var enemy = EnemyController.Create(stats, def.look, spawnPoints[slot], Game.World.ObjectsRoot);
            enemy.ApplyDefinition(def, entryLevel, .45f, .20f, generation * 1000 + slot);
            var marker = enemy.gameObject.AddComponent<MiniDungeonEnemy>();
            marker.Owner = this; marker.Generation = generation; marker.Reference = Guid.NewGuid().ToString();
            enemies[slot] = enemy;
            enemy.Died += e => { if (this == null || marker.Generation != generation) return; enemies[slot] = null; respawnAt[slot] = Time.time + MiniDungeonCatalog.RespawnSeconds; };
        }

        /// <summary>Called only from the shared XP handler, preventing both the event and loot route from granting twice.</summary>
        public void CreditKill(EnemyController enemy)
        {
            var marker = enemy != null ? enemy.GetComponent<MiniDungeonEnemy>() : null;
            if (marker == null || !enemy.IsDead || marker.Credited || marker.Owner != this || marker.Generation != generation || !Active || Busy || RemainingSeconds <= 0f) return;
            marker.Credited = true;
            if (OnlineSession.Playing) QueueOnlineKill(marker.Reference);
            else
            {
                Kills++;
                var progress = Game.Session.Progression;
                int room = -progress.Xp;
                for (int level = progress.Level; level < Progression.LevelCap; level++) room += Progression.XpToNext(level);
                // Catch-up is judged again on every kill: it stops the moment the waiting main quest's level is reached.
                int each = MiniDungeonCatalog.CatchUpGate(progress.Level) > 0 ? CatchUpXpPerKill : XpPerKill;
                int before = Mathf.Clamp(room, 0, each);
                Game.Session.Progression.AddXp(before);
                EarnedXp += before;
                Game.Session.Inventory.Add(ConsumableDatabase.Gold, MiniDungeonCatalog.GoldPerKill); // kill gold, no drops (online: the server's delta)
                nextSave = Mathf.Min(nextSave, Time.unscaledTime + .75f); // one checkpoint for an AoE burst
                Changed?.Invoke();
            }
        }

        public bool HandleLocalDeath()
        {
            if (!Active) return false;
            Leave("death");
            return true;
        }

        public void Leave() => Leave("left");
        public void Leave(string reason)
        {
            if (!Active || Busy) return;
            Busy = true;
            QuestAutoPilot.Stop("");
            if (OnlineSession.Playing) LeaveOnline(reason);
            else { Game.Session.MiniDungeonProgress.End(Now); StartCoroutine(ReturnRoom(reason)); }
            Changed?.Invoke();
        }

        IEnumerator ReturnRoom(string reason)
        {
            yield return Game.UI.Fader.Fade(1f, .2f);
            ClearEnemies();
            Active = false;
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
            SaveSnapshot();
            yield return Game.UI.Fader.Fade(0f, .2f);
            Busy = false;
            Game.Quest.NotifyChanged(); // any level objective completed during training now resumes normally
            GameEvents.RaiseToast(reason == "death" ? L.T("쓰러져서 미니던전에서 나왔습니다. 남은 시간은 보존됩니다.") :
                reason == "expired" ? L.T("미니던전 이용 시간이 끝났습니다.") : reason == "max_level" ? L.T("만렙 달성! 미니던전 수련을 마쳤습니다.") :
                reason == "network" ? L.T("연결을 확인할 수 없어 미니던전을 종료했습니다.") : L.T("미니던전에서 나왔습니다. 남은 시간으로 다시 입장할 수 있습니다."));
            Changed?.Invoke();
        }

        /// <summary>Title / character change cleanup; the caller owns the destination-world transition.</summary>
        public void Abort()
        {
            if (!Active && !Busy) return;
            QuestAutoPilot.Stop("");
            if (OnlineSession.Playing) AbortOnline();
            else Game.Session?.MiniDungeonProgress.End(Now);
            SaveSnapshot();
            StopAllCoroutines();
            ClearEnemies();
            Active = Busy = false;
            generation++;
            Game.State?.RefreshTimeScale();
            Changed?.Invoke();
        }

        void ClearEnemies()
        {
            foreach (var enemy in enemies) if (enemy != null) Destroy(enemy.gameObject);
            enemies.Clear(); respawnAt.Clear(); spawnPoints.Clear();
        }

        void SaveSnapshot()
        {
            if (Game.Session == null || Game.Saves == null || Game.Player == null) return;
            Game.Saves.Write(Game.Session.Capture(Game.Player.Position, Game.Player.Facing));
        }

        void OnApplicationQuit() { if (Active || Busy) { Abort(); } }
        void OnApplicationPause(bool paused) { if (paused && Active) SaveSnapshot(); }
    }
}
