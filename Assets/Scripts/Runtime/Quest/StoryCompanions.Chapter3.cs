using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Chapter 3 story companions (Docs/ch3/WORLD_AND_STORY.md 2·4절).
    /// 쿠크 is offered, never forced into the saved party: from 3-3 (c3_sayab) to 3-끝 (c3_end) he can be picked in the
    /// AI 동료 window like a mercenary (the 2026-10-14 rule: story companions do not join by themselves). Two scenes do
    /// put him beside the hero for a while as a guest (PartyManager.Guests): the first two rooms of the 시바크 raid while
    /// 3-6 is open (<see cref="ForcedSlot"/>), and the 사얍 방어전 with 카나 (<see cref="StoryDefense"/>).
    /// </summary>
    public static partial class StoryCompanions
    {
        public const string KukId = "story_kukum", KanaId = "story_kana";
        public const string QuestSayab = "c3_sayab", QuestXibak = "c3_xibak", QuestEnd = "c3_end";
        public const string RaidXibak = "raid_xibak";

        static bool chapterGuestLineShown;

        static QuestJournal Journal => Game.Session?.Journal;

        /// <summary>쿠크 can be picked in the AI 동료 window (chapter 3 open, after 3-3 and before 3-끝).</summary>
        public static bool KukAvailable =>
            Chapter3.Enabled && Journal != null && Journal.IsCompleted(QuestSayab) && !Journal.IsCompleted(QuestEnd);

        /// <summary>Map entered: 쿠크 leaves the saved party once his time is over (3-끝 or chapter 3 closed again).</summary>
        static void RefreshChapter3()
        {
            if (Game.Party == null || Game.Session == null) return;
            if (!KukAvailable && Game.Session.PartyRoster.Contains(KukId) && !Game.Party.IsGuest(KukId)) Game.Party.RemoveCompanion(KukId);
            StoryDefense.OnMapEntered(Game.World != null ? Game.World.MapId : null);
        }

        /// <summary>
        /// The story guest a dungeon room asks for (null: none): 쿠크 in rooms 1·2 of the 시바크 raid until 3-6 is done.
        /// He stays out of the boss room (그라흐's old soldier does not stand before what grew from the broken stone).
        /// </summary>
        public static string ForcedSlot(string dungeonId, int room)
        {
            if (dungeonId != RaidXibak || room > 1 || !Chapter3.Enabled || Journal == null) return null;
            return Journal.IsCompleted(QuestSayab) && !Journal.IsCompleted(QuestXibak) ? KukId : null;
        }

        /// <summary>DungeonDirector.LoadRoom: brings the room's guest in or sends it away. Solo runs only (a shared run keeps its seats).</summary>
        public static void OnRoomLoaded(string dungeonId, int room, bool solo)
        {
            var party = Game.Party;
            if (party == null) return;
            string want = solo ? ForcedSlot(dungeonId, room) : null;
            if (want != null)
            {
                bool fresh = !party.IsGuest(want);
                if (party.AddGuest(want, true) != null && fresh && !chapterGuestLineShown)
                {
                    chapterGuestLineShown = true;
                    GameEvents.RaiseToast(L.T("쿠크: 이 앞은 내 창이 연다. 따라와라."));
                }
            }
            else if (party.IsGuest(KukId))
            {
                party.RemoveGuest(KukId);
                if (dungeonId == RaidXibak) GameEvents.RaiseToast(L.T("쿠크: 그라흐 님의 옛 부하가 저 앞에 설 수는 없다. 문은 내가 지키지."));
            }
        }

        /// <summary>A run ended: a room guest goes home with it.</summary>
        public static void OnRunEnded()
        {
            chapterGuestLineShown = false;
            if (Game.Party != null && Game.Party.IsGuest(KukId) && !StoryDefense.Running) Game.Party.RemoveGuest(KukId);
        }

        /// <summary>The AI 동료 window's 쿠크 button: in or out of the saved party (a mercenary seat).</summary>
        public static bool ToggleKuk()
        {
            var party = Game.Party;
            if (party == null || Game.Session == null) return false;
            if (Game.Session.PartyRoster.Contains(KukId)) return party.RemoveCompanion(KukId);
            if (!KukAvailable) return false;
            return party.AddCompanion(KukId) != null || Game.Session.PartyRoster.Contains(KukId);
        }
    }
}
