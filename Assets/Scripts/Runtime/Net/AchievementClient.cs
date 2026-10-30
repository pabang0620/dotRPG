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
        /// <summary>[2026-10-30] Name colour of each ranking title I own ('gold' / 'orange' / null): the name takes the colour of the equipped one.</summary>
        public static readonly Dictionary<string, string> RankColors = new Dictionary<string, string>();
        /// <summary>The colour my name shows now (the equipped ranking title's, else null).</summary>
        public static string MyNameColor => !string.IsNullOrEmpty(MyTitleId) && RankColors.TryGetValue(MyTitleId, out var c) ? c : null;
        /// <summary>Category of the ranking titles in <see cref="All"/> (not achievements: left out of the achieved count).</summary>
        public const string RankCategory = "rank";
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
            RankColors.Clear();
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
                    GameEvents.RaiseToast(L.F("<color=#ffd34a>업적 달성!</color> {0}  ·  칭호를 얻었습니다 (메뉴 > 업적)", L.T(a.title)));
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
            if (!OnlineSession.Playing || Api == null) { done?.Invoke(false, L.T("온라인 캐릭터로 접속해야 합니다.")); return; }
            var body = new Dictionary<string, object> { ["achievement_id"] = string.IsNullOrEmpty(achievementId) ? null : achievementId };
            Api.Post(Char + "/title", body, r =>
            {
                if (r.ok) ReadTitle(MiniJson.Obj(r.data, "title"));
                Changed?.Invoke();
                done?.Invoke(r.ok, r.ok ? "" : (string.IsNullOrEmpty(r.message) ? L.T("칭호를 바꾸지 못했습니다.") : r.message));
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
            // Ranking titles: world boss ones are permanent (expires_at null, 2026-10-30), the guild boss one lasts until the next
            // weekly settlement. Equipped like any title; the name colour follows the equipped one.
            RankColors.Clear();
            foreach (var o in MiniJson.Arr(data, "rank_titles") ?? new List<object>())
            {
                var t = o as Dictionary<string, object>;
                string id = MiniJson.Str(t, "id");
                string color = MiniJson.Str(t, "name_color");
                if (!string.IsNullOrEmpty(id)) RankColors[id] = color;
                DateTime.TryParse(MiniJson.Str(t, "expires_at", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out var until);
                string kind = until > DateTime.MinValue ? L.F("랭킹 칭호 · {0:M/d HH:mm}까지", until.ToLocalTime()) : L.T("랭킹 칭호 · 영구");
                All.Add(new AchievementView
                {
                    id = id, title = MiniJson.Str(t, "title", ""), category = RankCategory, goal = 1, progress = 1, achieved = true,
                    description = string.IsNullOrEmpty(color) ? kind : kind + " · " + (color == "gold" ? L.T("장착하면 이름이 금색") : L.T("장착하면 이름이 주황색")),
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
