using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [UNLOCK 2026-10-18] Content opened by the main quest line (Docs/PLAN_WEAKNESS_2026-10-18.md section 2). The basics
    /// (bag, shop, storage, world map, auto quest, cash shop) are open from the start; each feature below opens when its
    /// main quest is finished (the raid when 1-20 is accepted, like the skeleton king's own unlock). A character past that quest has it open already: the check
    /// reads the quest journal, so online and offline agree. The moment a feature opens, a toast and a tip say so and its
    /// guide quest (안내 의뢰) is accepted and tracked, so the next thing to do is right there.
    /// Client guidance only: the server does not lock these.
    /// </summary>
    public static class ContentUnlocks
    {
        public enum Feature { Enhance, Gem, AutoHunt, WeekdayDungeon, NormalDungeon, PartyFinder, Auction, Trade, Daily, SkillStyle, Raid, Promote, MiniDungeon }

        sealed class Rule
        {
            public string quest;
            /// <summary>True: open once the quest is accepted (the raid), not finished.</summary>
            public bool onAccept;
            /// <summary>Quest that teaches the feature (accepted when it opens; null = a tip only).</summary>
            public string guide;
        }

        static readonly Dictionary<Feature, Rule> Rules = new Dictionary<Feature, Rule>
        {
            [Feature.Enhance] = new Rule { quest = "c1_trail", guide = "g_enhance" },
            [Feature.Gem] = new Rule { quest = "c1s_patrol", guide = "g_gem" },
            [Feature.AutoHunt] = new Rule { quest = "c1s_patrol", guide = "g_autohunt" },
            [Feature.WeekdayDungeon] = new Rule { quest = "c1s_ruins", guide = "c1s_firstdungeon" },
            [Feature.NormalDungeon] = new Rule { quest = "c1s_firstdungeon", guide = "g_party" },
            [Feature.PartyFinder] = new Rule { quest = "c1s_firstdungeon", guide = "g_party" },
            [Feature.Auction] = new Rule { quest = "c1s_depths", guide = "g_auction" },
            [Feature.Trade] = new Rule { quest = "c1s_depths", guide = "g_auction" },
            [Feature.MiniDungeon] = new Rule { quest = "c1s_depths", guide = null }, // Lv.10 band opens right after (c1s_depths needs Lv.9)
            [Feature.Daily] = new Rule { quest = "career_path", guide = "g_daily" },
            [Feature.SkillStyle] = new Rule { quest = "career_path", guide = "g_style" },
            [Feature.Raid] = new Rule { quest = "c1_fortress", onAccept = true, guide = "c1_fortress" }, // same quest as the skeleton king raid's unlockQuest (RaidLockReason)
            [Feature.Promote] = new Rule { quest = "c1_fortress", guide = null },
        };

        /// <summary>A feature has just opened (raised once, when its main quest is finished or accepted).</summary>
        public static event Action<Feature> Opened;

        static readonly List<Feature> pending = new List<Feature>();
        static bool bound;
        static bool devRun;
        static float devCheckedAt = -10f;

        /// <summary>Automated runs (captures, measurements) and story-off sessions see everything open.</summary>
        static bool Bypass
        {
            get
            {
                if (!QuestManager.StoryEnabled) return true;
                if (Time.unscaledTime - devCheckedAt > 2f)
                {
                    devCheckedAt = Time.unscaledTime;
                    devRun = UnityEngine.Object.FindAnyObjectByType<DevCapture>() != null;
                }
                return devRun;
            }
        }

        public static bool IsOpen(Feature f)
        {
            if (Game.Quest == null || Game.Session == null || Bypass) return true;
            var r = Rules[f];
            var st = Game.Quest.StatusOf(r.quest);
            return st == QuestStatus.Completed || (r.onAccept && (st == QuestStatus.Active || st == QuestStatus.ReadyToTurnIn));
        }

        /// <summary>"메인 퀘스트 1-7 해골 숲의 흔적 완료 후 열려요" (null when open).</summary>
        public static string LockedReason(Feature f)
        {
            if (IsOpen(f)) return null;
            var r = Rules[f];
            var q = Game.Quest.Database.Get(r.quest);
            string title = q != null ? L.T(q.title) : r.quest;
            string code = q != null ? q.code : "";
            return r.onAccept
                ? L.F("메인 퀘스트 {0} {1}을 받으면 열려요", code, title)
                : L.F("메인 퀘스트 {0} {1} 완료 후 열려요", code, title);
        }

        /// <summary>True when open; otherwise shows <see cref="LockedReason"/> as a toast with the cancel sound.</summary>
        public static bool Check(Feature f)
        {
            if (IsOpen(f)) return true;
            GameEvents.RaiseToast(LockedReason(f));
            Game.Audio?.PlaySfx("cancel");
            return false;
        }

        /// <summary>The feature a window belongs to (null = always open): Game.Flow.OpenWindow checks it.</summary>
        public static Feature? FeatureOf(MenuScreen window) => window switch
        {
            EnhanceScreen _ => Feature.Enhance,
            AuctionScreen _ => Feature.Auction,
            PartyFinderScreen _ => Feature.PartyFinder,
            DailyQuestScreen _ => Feature.Daily,
            MiniDungeonScreen _ => Feature.MiniDungeon,
            _ => (Feature?)null,
        };

        /// <summary>The feature a menu icon opens (side menu, phone menu sheet): dimmed while locked. Null = always open.</summary>
        public static Feature? FeatureOfMenu(string icon, bool daily) =>
            daily ? Feature.Daily
            : icon == "menuicon_dungeon" ? Feature.WeekdayDungeon
            : icon == "menuicon_raid" ? Feature.Raid
            : icon == "menuicon_finder" ? Feature.PartyFinder
            : icon == "menuicon_auction" ? Feature.Auction
            : (Feature?)null;

        /// <summary>Open features whose guide quest is not finished yet (the "할 일" list shows them first).</summary>
        public static IEnumerable<Feature> RecentlyOpened
        {
            get
            {
                if (Game.Quest == null || Game.Session == null) yield break;
                foreach (var kv in Rules)
                {
                    if (kv.Value.guide == null || !IsOpen(kv.Key)) continue;
                    var st = Game.Quest.StatusOf(kv.Value.guide);
                    if (st == QuestStatus.Available || st == QuestStatus.Active || st == QuestStatus.ReadyToTurnIn) yield return kv.Key;
                }
            }
        }

        /// <summary>Display name of a feature ("강화").</summary>
        public static string NameOf(Feature f) => f switch
        {
            Feature.Enhance => L.T("강화"),
            Feature.Gem => L.T("보조 젬"),
            Feature.AutoHunt => L.T("자동 사냥"),
            Feature.WeekdayDungeon => L.T("요일 던전"),
            Feature.NormalDungeon => L.T("일반 던전"),
            Feature.PartyFinder => L.T("파티 찾기"),
            Feature.Auction => L.T("경매장"),
            Feature.Trade => L.T("1:1 교환"),
            Feature.Daily => L.T("일일·주간 의뢰"),
            Feature.SkillStyle => L.T("스킬 스타일"),
            Feature.Raid => L.T("레이드"),
            Feature.MiniDungeon => L.T("미니던전"),
            _ => L.T("승급"),
        };

        /// <summary>One or two lines on what the feature is for (the tip shown when it opens).</summary>
        public static string DescriptionOf(Feature f) => f switch
        {
            Feature.Enhance => L.T("대장장이에게 강화석과 골드를 주면 장비가 단단해져요. +10부터는 실패하면 단계가 떨어지거나 부서질 수 있어요."),
            Feature.Gem => L.T("스킬 창 젬 탭에서 스킬 옆 칸에 보조 젬을 끼우면 그 스킬이 강해져요."),
            Feature.AutoHunt => L.F("사냥터에서 [{0}]를 누르면 알아서 사냥해요. 움직이면 멈춰요.", Game.Input != null ? Game.Input.GetBindingLabel(GameAction.AutoHunt) : "F7"),
            Feature.WeekdayDungeon => L.T("요일마다 다른 던전이 열려요. 하루 3번 들어가 재료·골드·장비를 받아요."),
            Feature.NormalDungeon => L.T("일반 던전은 매일 열리고 입장 횟수가 없어요. 사냥과 비슷한 경험치를 줘요."),
            Feature.PartyFinder => L.T("함께 갈 사람을 구해요. 같이 사냥하면 경험치가 늘어나요(2인 +15%, 4인 +50%)."),
            Feature.Auction => L.T("다른 사람과 장비·재료를 사고팔아요. 등록은 Lv.10부터예요."),
            Feature.Trade => L.T("친구와 아이템을 직접 주고받아요."),
            Feature.Daily => L.T("매일 3개, 매주 3개 의뢰를 받아 경험치와 골드를 모아요."),
            Feature.SkillStyle => L.T("스킬 창 전직 탭에서 스킬 배분을 두 벌 만들어 바꿔 쓸 수 있어요."),
            Feature.Raid => L.T("해골왕 레이드가 열렸어요. 수·토·일에 열리고 혼자서도 보상을 받아요."),
            Feature.MiniDungeon => L.T("Lv.10부터 혼자 들어가는 경험치 수련장이에요(하루 30분). 다음 메인 퀘스트가 레벨 때문에 막혔을 때 오면 경험치가 10배예요."),
            _ => L.T("대장장이 창의 승급으로 장비 등급을 올려요. 레이드에서 얻는 고대의 핵과 골드가 들어요."),
        };

        /// <summary>Hooks the quest events (QuestManager.Bind).</summary>
        public static void Bind()
        {
            if (bound) return;
            bound = true;
            GameEvents.QuestCompleted += OnQuestCompleted;
            GameEvents.QuestAccepted += OnQuestAccepted;
        }

        /// <summary>Drops queued announcements (a new session starts).</summary>
        public static void ClearPending() => pending.Clear();

        static void OnQuestCompleted(string questId) => Queue(questId, false);
        static void OnQuestAccepted(string questId) => Queue(questId, true);

        static void Queue(string questId, bool accepted)
        {
            if (Bypass) return;
            foreach (var kv in Rules)
                if (kv.Value.quest == questId && kv.Value.onAccept == accepted && !pending.Contains(kv.Key)) pending.Add(kv.Key);
        }

        /// <summary>
        /// End of <see cref="QuestManager.Refresh"/>: the features opened since the last pass announce themselves and their
        /// guide quests (unlocked by the same pass) are accepted and tracked.
        /// </summary>
        public static void ProcessPending(QuestManager quests)
        {
            if (pending.Count == 0 || quests == null) return;
            var opened = new List<Feature>(pending);
            pending.Clear();
            var names = new List<string>();
            foreach (var f in opened) names.Add(NameOf(f));
            GameEvents.RaiseToast(L.F("새 콘텐츠: {0}", string.Join(" · ", names)));
            foreach (var f in opened)
            {
                // Side guides are taken at once; a main quest guide keeps its giver's story talk.
                string guide = Rules[f].guide;
                var def = guide != null ? quests.Database.Get(guide) : null;
                if (def != null && def.Kind == QuestKind.Sub) quests.AcceptNow(guide);
                Opened?.Invoke(f);
            }
        }
    }
}
