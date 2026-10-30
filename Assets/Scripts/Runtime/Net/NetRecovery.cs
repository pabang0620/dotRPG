using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [NET 2026-10-30] Connection recovery (Docs/PLAN_NET_STABILITY_2026-10-30.md). The automatic parts live where the
    /// connection lives (ChatSocket and RelayTransport reconnect by themselves, FieldSession re-enters when a partner on
    /// the map is missing from the sync, PartyRunSession rebuilds a member's fight connection). This class holds what
    /// the player sees: <see cref="Reconnecting"/> for the status line, and <see cref="ResyncAll"/> behind the pause
    /// menu's "연결 다시 맞추기", which rebuilds chat, field and party connections without logging out or leaving the party.
    /// </summary>
    public static class NetRecovery
    {
        const float Cooldown = 5f;
        static float lastResyncAt = -99f;

        /// <summary>Something online is down and coming back by itself (chat / town socket, or the fight connection).</summary>
        public static bool Reconnecting
        {
            get
            {
                if (!OnlineSession.Playing) return false;
                if (OnlineServices.Chat is ServerChatService chat && chat.Reconnecting) return true;
                if (!PartyNet.Active || RunOver) return false;
                var t = PartyNet.Current.Transport;
                if (t is RelayTransport relay && relay.ClosedReason != null) return false; // over for good: the session takes care of it
                return !t.IsConnected;
            }
        }

        static bool RunOver
        {
            get
            {
                var run = Game.Dungeon != null ? Game.Dungeon.Run : null;
                return run != null && (run.IsOver || run.State == DungeonRunState.Clearing);
            }
        }

        /// <summary>
        /// Tear down and rebuild every online connection: chat / town socket, party poll, field session, party run
        /// connection (a run the server still has is joined again). The account stays logged in and the party stays.
        /// </summary>
        public static void ResyncAll()
        {
            if (!OnlineSession.Playing) return;
            if (Time.unscaledTime - lastResyncAt < Cooldown) { GameEvents.RaiseToast(L.T("잠시 후 다시 눌러 주세요.")); return; }
            lastResyncAt = Time.unscaledTime;
            Debug.Log("[NET] manual resync");
            if (OnlineServices.Chat is ServerChatService chat) chat.Reconnect();
            if (PartyRunSession.Active) PartyRunSession.Instance.Resync();
            else PartyRunSession.AllowRejoin();
            FieldSession.Instance?.Resync();
            PartyClient.Instance?.PollNow(true);
            GameEvents.RaiseToast(L.T("연결을 다시 맞추는 중입니다."));
        }
    }
}
