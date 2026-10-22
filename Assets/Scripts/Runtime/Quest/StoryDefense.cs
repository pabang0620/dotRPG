using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 3-7 사얍 방어전 (Docs/ch3/WORLD_AND_STORY.md 2절 c3_defense). The quest step holds a flag objective
    /// (<c>{"type":"flag","target":"c3_defense_won","map":"dry_floor"}</c>); while that step is current and the hero is on
    /// <see cref="DefenseMap"/>, four waves of 잿빛 손 scouts come from the west and 쿠크 and 카나 fight beside the hero as
    /// guests (the 수비대). Clearing the last wave sets the flag (the step completes through the normal flag signal).
    /// Falling or leaving the map ends the try; it starts again on the next visit. A small watcher object also gives 발체
    /// her three moods (<see cref="BalcheMood"/>).
    /// </summary>
    public sealed class StoryDefense : MonoBehaviour
    {
        public const string Flag = "c3_defense_won";
        public const string DefenseMap = "dry_floor";
        public const int Waves = 4;
        const float PollSeconds = 1f, StartDelay = 2.5f, WaveGap = 3f, SpawnMin = 9f, SpawnMax = 12f;
        static readonly string[][] WaveMonsters =
        {
            new[] { "grey_ranger", "grey_ranger", "grey_ranger", "grey_ranger" },
            new[] { "grey_ranger", "grey_ranger", "grey_ranger", "grey_priest", "grey_ranger" },
            new[] { "grey_ranger", "grey_priest", "grey_ranger", "grey_ranger", "grey_priest", "grey_ranger" },
            new[] { "grey_priest", "grey_ranger", "grey_ranger", "grey_priest", "grey_ranger", "grey_ranger", "grey_ranger" },
        };

        static StoryDefense instance;
        enum Phase { Idle, Starting, Fighting, Gap }

        Phase phase;
        int wave;
        float nextAt, pollAt;
        readonly List<EnemyController> alive = new List<EnemyController>();

        /// <summary>A defense is under way (guests out, waves coming).</summary>
        public static bool Running => instance != null && instance.phase != Phase.Idle;

        /// <summary>The wave in progress (1..<see cref="Waves"/>, 0 = none), for the tracker.</summary>
        public static int CurrentWave => instance != null && instance.phase != Phase.Idle ? instance.wave + 1 : 0;

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("StoryDefense");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StoryDefense>();
        }

        /// <summary>StoryCompanions.Refresh (every map entry): a defense in progress on another map ends; the watcher starts.</summary>
        public static void OnMapEntered(string mapId)
        {
            Ensure();
            if (instance.phase != Phase.Idle && mapId != DefenseMap) instance.Abort(null);
            instance.pollAt = 0f;
        }

        void Update()
        {
            if (Time.unscaledTime >= pollAt)
            {
                pollAt = Time.unscaledTime + PollSeconds;
                BalcheMood.AttachAll();
                if (phase == Phase.Idle && Wanted()) Begin();
            }
            if (phase == Phase.Idle || !Game.IsWorldRunning) return;
            var player = Game.Player;
            if (player == null || Game.World == null || Game.World.MapId != DefenseMap) { Abort(null); return; }
            if (player.IsDead) { Abort(L.T("수비가 무너졌다. 사얍 외곽으로 다시 가서 막아 내자.")); return; }
            switch (phase)
            {
                case Phase.Starting:
                case Phase.Gap:
                    if (Time.time >= nextAt) SpawnWave();
                    break;
                case Phase.Fighting:
                    alive.RemoveAll(e => e == null || e.IsDead || !e.isActiveAndEnabled);
                    if (alive.Count > 0) break;
                    wave++;
                    if (wave >= Waves) { Win(); break; }
                    phase = Phase.Gap;
                    nextAt = Time.time + WaveGap;
                    GameEvents.RaiseToast(L.F("다음 정찰대가 온다 ({0}/{1})", wave + 1, Waves));
                    break;
            }
        }

        /// <summary>The current step of an active quest waits on <see cref="Flag"/>, chapter 3 is open, and the hero is on the map.</summary>
        static bool Wanted()
        {
            var j = Game.Session?.Journal;
            if (!Chapter3.Enabled || j == null || Game.Quest == null || j.HasFlag(Flag)) return false;
            if (Game.World == null || Game.World.MapId != DefenseMap || Game.Player == null || Game.Player.IsDead) return false;
            if (Game.Dungeon != null && Game.Dungeon.InRun) return false;
            foreach (var q in Game.Quest.Database.All)
            {
                if (j.Status(q.id) != QuestStatus.Active) continue;
                var st = j.State(q.id);
                if (st.step < 0 || st.step >= q.steps.Count) continue;
                foreach (var o in q.steps[st.step].objectives)
                    if (o.type == ObjectiveTypes.Flag && o.target == Flag) return true;
            }
            return false;
        }

        void Begin()
        {
            phase = Phase.Starting;
            wave = 0;
            nextAt = Time.time + StartDelay;
            alive.Clear();
            Game.Party?.AddGuest(StoryCompanions.KukId, false);
            Game.Party?.AddGuest(StoryCompanions.KanaId, false);
            GameEvents.RaiseToast(L.T("잿빛 손 정찰대가 몰려온다! 쿠크·카나와 함께 사얍을 지켜라."));
        }

        void SpawnWave()
        {
            var world = Game.World;
            var player = Game.Player;
            var zone = HuntingGrounds.Get(DefenseMap);
            int level = zone != null ? zone.monsterLevel : Game.Session.Progression.Level;
            int xp = zone != null ? zone.KillXp : 0;
            var ids = WaveMonsters[Mathf.Clamp(wave, 0, WaveMonsters.Length - 1)];
            for (int i = 0; i < ids.Length; i++)
            {
                // From the west (the way the scouts came), spread over a half ring.
                float a = Mathf.PI * (0.65f + 0.7f * (i + 0.5f) / ids.Length);
                Vector2 at = player.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(SpawnMin, SpawnMax);
                for (int tries = 0; tries < 6 && !world.IsFree(at); tries++)
                    at = Vector2.MoveTowards(at, player.Position, 1.2f);
                var e = MonsterDatabase.SpawnField(ids[i], at, world.ObjectsRoot, level, xp);
                if (e != null) alive.Add(e);
            }
            phase = Phase.Fighting;
            if (wave == 0) GameEvents.RaiseToast(L.F("정찰대 {0}/{1}", wave + 1, Waves));
        }

        void Win()
        {
            phase = Phase.Idle;
            ReleaseGuests();
            GameEvents.RaiseToast(L.T("사얍을 지켜 냈다!"));
            Game.Quest?.SetFlag(Flag);
        }

        void Abort(string line)
        {
            phase = Phase.Idle;
            foreach (var e in alive)
                if (e != null && !e.IsDead) Destroy(e.gameObject);
            alive.Clear();
            ReleaseGuests();
            if (!string.IsNullOrEmpty(line)) GameEvents.RaiseToast(line);
        }

        static void ReleaseGuests()
        {
            Game.Party?.RemoveGuest(StoryCompanions.KanaId);
            if (Game.Dungeon == null || !Game.Dungeon.InRun) Game.Party?.RemoveGuest(StoryCompanions.KukId);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
