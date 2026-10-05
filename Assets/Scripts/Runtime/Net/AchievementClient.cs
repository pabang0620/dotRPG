using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>One achievement as the server judged it (the server owns the list, the goals and the progress).</summary>
    public sealed class AchievementView
    {
        public string id, title, description, category;
        public int goal, progress;
        public bool achieved;
    }

    /// <summary>
    /// 업적·칭호. The server judges every achievement from its own records (enhance log, kills, clears, level...), so
    /// this only asks: after enhancing, a level up, a dungeon clear, a finished quest and once a minute. Newly unlocked
    /// ones are announced; the equipped title is shown in front of the name in chat ([칭호][이름]).
    /// </summary>
    public static class AchievementClient
    {
        public static readonly List<AchievementView> All = new List<AchievementView>();
        /// <summary>Equipped title's name ("" = none).</summary>
        public static string MyTitle { get; private set; } = "";
        public static string MyTitleId { get; private set; } = "";
        public static event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;
        static float nextCheck, lastAsk = -10f;
        static bool hooked, asking;

        /// <summary>Called when an online character enters the world: hook the moments worth checking, then ask once.</summary>
        public static void OnEnteredWorld()
        {
            if (!hooked)
            {
                hooked = true;
                GameEvents.QuestCompleted += _ => Check();
                if (Game.Dungeon != null) Game.Dungeon.RunEnded += _ => Check();
            }
            if (Game.Session != null) { Game.Session.Progression.LeveledUp -= OnLevel; Game.Session.Progression.LeveledUp += OnLevel; }
            All.Clear();
            MyTitle = MyTitleId = "";
            Check(true);
        }

        static void OnLevel(int level) => Check();

        /// <summary>Ask the server again (throttled to once every 3 s; quiet = no announcement, the first load).</summary>
        public static void Check(bool quiet = false)
        {
            if (!OnlineSession.Playing || Api == null || asking || Time.unscaledTime - lastAsk < 3f) return;
            asking = true;
            lastAsk = Time.unscaledTime;
            nextCheck = Time.unscaledTime + 60f;
            Api.Get(Char + "/achievements", r =>
            {
                asking = false;
                if (!r.ok || r.data == null) return;
                Read(r.data);
                if (quiet) return;
                foreach (var o in MiniJson.Arr(r.data, "newly") ?? new List<object>())
                {
                    var a = All.Find(x => x.id == o as string);
                    if (a == null) continue;
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast($"<color=#ffd34a>업적 달성!</color> {a.title}  ·  칭호를 얻었습니다 (메뉴 > 업적)");
                }
            });
        }

        /// <summary>Kills add up quietly: ask once a minute while playing.</summary>
        public static void Tick()
        {
            if (OnlineSession.Playing && Time.unscaledTime >= nextCheck && nextCheck > 0f) Check();
        }

        public static void Equip(string achievementId, Action<bool, string> done)
        {
            if (!OnlineSession.Playing || Api == null) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            var body = new Dictionary<string, object> { ["achievement_id"] = string.IsNullOrEmpty(achievementId) ? null : achievementId };
            Api.Post(Char + "/title", body, r =>
            {
                if (r.ok) ReadTitle(MiniJson.Obj(r.data, "title"));
                Changed?.Invoke();
                done?.Invoke(r.ok, r.ok ? "" : (string.IsNullOrEmpty(r.message) ? "칭호를 바꾸지 못했습니다." : r.message));
            });
        }

        static void Read(Dictionary<string, object> data)
        {
            All.Clear();
            foreach (var o in MiniJson.Arr(data, "achievements") ?? new List<object>())
            {
                var a = o as Dictionary<string, object>;
                All.Add(new AchievementView
                {
                    id = MiniJson.Str(a, "id"), title = MiniJson.Str(a, "title", ""), description = MiniJson.Str(a, "description", ""), category = MiniJson.Str(a, "category", "growth"),
                    goal = MiniJson.Int(a, "goal", 1), progress = MiniJson.Int(a, "progress"),
                    achieved = !string.IsNullOrEmpty(MiniJson.Str(a, "achieved_at")),
                });
            }
            ReadTitle(MiniJson.Obj(data, "title"));
            Changed?.Invoke();
        }

        static void ReadTitle(Dictionary<string, object> t)
        {
            MyTitleId = t != null ? MiniJson.Str(t, "id", "") : "";
            MyTitle = t != null ? MiniJson.Str(t, "name", "") : "";
        }
    }
}
