using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UX 2026-10-14] One party window in three pages instead of three separate windows: 내 파티 (PartyLobbyScreen: people
    /// and AI in one roster, invite, ready, depart), 파티 찾기 (PartyFinderScreen: posts, apply, quick match) and AI 동료
    /// (PartyScreen: AI companions for the empty seats). Every page shows the same tab strip and the party status in its
    /// header, the side menu / P key / the dungeon window's 파티 편성 all open the hub, and Esc goes back to where the hub
    /// was opened from (the dungeon window) or to the game.
    /// </summary>
    public static class PartyHub
    {
        public enum Page { MyParty, Finder, Companions }

        /// <summary>The window the hub was opened from (null = the game). Switching pages keeps it.</summary>
        public static WindowScreen ReturnTo;

        static bool OnlineReady => OnlineSession.Playing && PartyLobbyScreen.Instance != null;

        /// <summary>Online: the party page; offline (no server party): the AI companions.</summary>
        public static Page DefaultPage => OnlineReady ? Page.MyParty : Page.Companions;

        public static MenuScreen WindowOf(Page page)
        {
            switch (page)
            {
                case Page.MyParty: return OnlineReady ? PartyLobbyScreen.Instance : (MenuScreen)Game.UI.Party;
                case Page.Finder: return PartyFinderScreen.Instance != null ? PartyFinderScreen.Instance : (MenuScreen)Game.UI.Party;
                default: return Game.UI.Party;
            }
        }

        /// <summary>Opens the hub from outside (side menu, key, another window) on <paramref name="page"/> (default page when null).</summary>
        public static void Open(Page? page = null, WindowScreen from = null)
        {
            ReturnTo = from;
            Game.Flow.OpenWindow(WindowOf(page ?? DefaultPage));
        }

        /// <summary>Switches to another page of the hub (keeps where it was opened from).</summary>
        public static void Switch(Page page)
        {
            var target = WindowOf(page);
            if (target == null || Game.UI.Top == target) return;
            Game.Audio.PlaySfx("select");
            if (Game.State.Current == GameState.Inventory) Game.UI.ShowWindow(target);
            else Game.Flow.OpenWindow(target);
        }

        public static bool IsHubPage(MenuScreen m) => m is PartyLobbyScreen || m is PartyFinderScreen || m is PartyScreen;

        /// <summary>Called by each page's Close: back to the window the hub came from. False = close to the game.</summary>
        public static bool CloseToOrigin()
        {
            var back = ReturnTo;
            ReturnTo = null;
            if (back == null || IsHubPage(back)) return false;
            Game.UI.ShowWindow(back);
            return true;
        }

        /// <summary>"혼자", "AI 2명과 함께", "재인과 파티 (방장: 원호)".</summary>
        public static string StatusLine()
        {
            int ai = Game.Session != null && Game.Session.PartyRoster != null ? Game.Session.PartyRoster.Count : 0;
            var c = PartyClient.Instance;
            if (OnlineSession.Playing && c != null && c.InParty)
            {
                var others = c.Members.Where(m => !m.me).Select(m => m.name).ToList();
                string leader = c.Members.FirstOrDefault(m => m.leader)?.name ?? "";
                string people = others.Count > 0 ? L.F("{0}과(와) 파티 (방장: {1})", string.Join(", ", others), leader) : L.T("파티 (아직 나 혼자)");
                return ai > 0 ? people + L.F("  · AI {0}명", ai) : people;
            }
            return ai > 0 ? L.F("AI {0}명과 함께", ai) : L.T("혼자");
        }

        /// <summary>Adds the tab strip and the status line to the header of a hub page.</summary>
        public static void Attach(WindowScreen window, RectTransform content, Page page)
        {
            var layout = content != null && content.parent != null ? content.parent.parent as RectTransform : null;
            if (layout == null) return;
            var title = layout.Find("Title")?.GetComponent<Text>();
            if (title != null) title.text = L.T("파티");
            var tabs = window.gameObject.AddComponent<PartyHubTabs>();
            tabs.Build(layout, page);
        }
    }

    /// <summary>[UX] The hub's header: 내 파티 | 파티 찾기 | AI 동료 tabs and the party status on the right.</summary>
    public sealed class PartyHubTabs : MonoBehaviour
    {
        const float TabX = 330f, TabW = 170f, TabGap = 8f, TabH = 46f;
        PartyHub.Page page;
        readonly Button[] tabs = new Button[3];
        Text status;
        float nextAt;

        static readonly string[] Labels = { "내 파티", "파티 찾기", "AI 동료" }; // i18n-ignore keys, shown through L.T below

        public void Build(RectTransform layout, PartyHub.Page current)
        {
            page = current;
            for (int i = 0; i < tabs.Length; i++)
            {
                var p = (PartyHub.Page)i;
                var img = UIFactory.Image(layout, "HubTab" + i, Game.Art.Get("ui_btngray"), Color.white);
                img.preserveAspect = false;
                img.raycastTarget = true;
                UIFactory.Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TabX + i * (TabW + TabGap), -15f), new Vector2(TabW, TabH));
                var b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(() => PartyHub.Switch(p));
                UiButton.Attach(b);
                var t = UIFactory.Text(img.transform, "Text", L.T(Labels[i]), UiTheme.Size(20), Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.FitInFrame(t, UIFactory.FrameRim - 2f);
                tabs[i] = b;
            }
            status = UIFactory.Text(layout, "HubStatus", "", 18, new Color32(246, 231, 200, 255), TextAnchor.MiddleRight, true);
            UIFactory.Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -12f), new Vector2(1280f - 28f - (TabX + 3f * (TabW + TabGap)) - 12f, 52f));
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = 12;
            status.resizeTextMaxSize = 18;
            if (TouchUi.Enabled) foreach (var b in tabs) MobileTouch.GrowHeight((RectTransform)b.transform, MobileTouch.WindowTapHeight, MobileTouch.Grow.Up); // [TOUCH] into the header
            Paint();
        }

        void OnEnable() => nextAt = 0f;

        void Update()
        {
            if (Time.unscaledTime < nextAt || status == null) return;
            nextAt = Time.unscaledTime + 0.5f;
            Paint();
        }

        void Paint()
        {
            bool online = OnlineSession.Playing && PartyLobbyScreen.Instance != null;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null) continue;
                bool on = (int)page == i;
                ((Image)tabs[i].targetGraphic).sprite = UiTheme.Tab(on);
                // The party and the finder are server features: offline only the AI page works.
                bool usable = online || (PartyHub.Page)i == PartyHub.Page.Companions;
                tabs[i].interactable = usable;
            }
            status.text = PartyHub.StatusLine();
        }
    }
}
