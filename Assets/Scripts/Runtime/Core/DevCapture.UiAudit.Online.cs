using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        [Serializable] sealed class UiAuditCredentials { public string server, loginId, password, characterId; }

        static string UiAuditArgument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : "";
        }

        // Invalid online options must still isolate tokens instead of falling back to real credentials.
        public static bool IsolatedUiAuditTokenStore
        {
            get
            {
#if DOTRPG_RELEASE
                return false;
#else
                return UiAuditArgument("-uiAudit") == "1" && !string.IsNullOrEmpty(UiAuditArgument("-dotrpgUi"));
#endif
            }
        }

        // Only a separately validated loopback QA invocation may perform API requests.
        public static bool IsolatedOnlineUiAudit
        {
            get
            {
#if DOTRPG_RELEASE
                return false;
#else
                string output = UiAuditArgument("-dotrpgUi");
                return IsolatedUiAuditTokenStore && !string.IsNullOrEmpty(UiAuditArgument("-uiAuditOnline")) &&
                    !string.IsNullOrEmpty(output) && Uri.TryCreate(UiAuditArgument("-dotrpgServer"), UriKind.Absolute, out var uri) &&
                    uri.IsLoopback && uri.Scheme == "http" && string.IsNullOrEmpty(uri.UserInfo);
#endif
            }
        }

        IEnumerator UiAuditOnlineRun()
        {
            if (!IsolatedOnlineUiAudit || OnlineSession.Current != null || string.IsNullOrEmpty(SaveSystem.DirectoryOverride) ||
                !string.Equals(Path.GetFullPath(SaveSystem.DirectoryOverride), Path.GetFullPath(Path.Combine(folder, "saves")), StringComparison.OrdinalIgnoreCase))
            { Log("ONLINE UI AUDIT ABORT: isolation validation failed"); yield break; }
            var credential = JsonUtility.FromJson<UiAuditCredentials>(File.ReadAllText(UiAuditArgument("-uiAuditOnline")));
            string explicitUrl = UiAuditArgument("-dotrpgServer").TrimEnd('/');
            if (credential == null || credential.server.TrimEnd('/') != explicitUrl || ApiClient.Instance.BaseUrl != explicitUrl)
            { Log("ONLINE UI AUDIT ABORT: QA endpoint mismatch"); yield break; }
            ApplyRequestedResolution(); yield return Wait(1.2f);
            uiAudit = new UiAuditReport { language = Game.Settings.Data.language, uiScale = Game.Settings.Data.uiScale,
                width = Screen.width, height = Screen.height, saveDirectory = SaveSystem.DirectoryOverride,
                scope = "Isolated loopback server UI capture. Check ready.json fixture results. Capture does not establish functional correctness or populated data completeness.",
                remaining = "Transactions, gameplay, additional careers, full/empty/error-state coverage and visual review remain separate." };
            ApiResult login = null;
            OnlineSession.Login(credential.loginId, credential.password, false, r => login = r);
            float deadline = Time.realtimeSinceStartup + 45f;
            while (login == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (login == null || !login.ok) { Log("ONLINE UI AUDIT login failed: " + (login?.code ?? "timeout")); UiAuditWrite(); yield break; }
            ApiResult entered = null; SaveData save = null;
            OnlineSession.Current.EnterCharacter(credential.characterId, (r, s) => { entered = r; save = s; });
            deadline = Time.realtimeSinceStartup + 30f;
            while (entered == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (entered == null || !entered.ok || save == null) { Log("ONLINE UI AUDIT enter failed: " + (entered?.code ?? "timeout")); UiAuditWrite(); yield break; }
            Game.Flow.EnterOnline(save);
            deadline = Time.realtimeSinceStartup + 60f;
            while ((Game.Flow.IsTransitioning || !Game.IsPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
            if (Game.Flow.IsTransitioning || !Game.IsPlaying) { Log("ONLINE UI AUDIT world timed out"); UiAuditWrite(); yield break; }
            uiAudit.level = Game.Session.Progression.Level;
            try
            {
                yield return Wait(2f);
                yield return UiAuditCapture("online_hud", null);
                foreach (var screen in new MenuScreen[] { Game.UI.Equipment, Game.UI.Skills, Game.UI.Cosmetics, GachaScreen.Instance,
                    CosmeticSynthScreen.Instance, ExchangeShopScreen.Instance, AuctionScreen.Instance, SocialScreen.Instance,
                    MailScreen.Instance, AchievementScreen.Instance, LevelRewardScreen.Instance, DailyQuestScreen.Instance,
                    GuildScreen.Instance, BossRushScreen.Instance, RankingScreen.Instance })
                {
                    Game.Flow.CloseInventory(); Game.Flow.OpenWindow(screen);
                    // Network-backed screens refresh asynchronously; failures remain visible in the capture.
                    yield return Wait(2f);
                    yield return UiAuditCapture("online_" + screen.name, screen);
                }
                yield return UiAuditWindow("online_daily", DailyQuestScreen.Instance);
                foreach (var tab in new[] { "TabDaily", "TabWeekly" }) yield return UiAuditTab("online_daily_" + tab, DailyQuestScreen.Instance, tab);
                yield return UiAuditWindow("online_auction", AuctionScreen.Instance);
                foreach (var tab in new[] { "Tab0", "Tab1", "Tab2", "Tab3" }) yield return UiAuditTab("online_auction_" + tab, AuctionScreen.Instance, tab);
                foreach (PartyHub.Page page in Enum.GetValues(typeof(PartyHub.Page)))
                { Game.Flow.CloseInventory(); PartyHub.Open(page); yield return Wait(2f); yield return UiAuditCapture("online_party_" + page, PartyHub.WindowOf(page)); }
                yield return UiAuditWindow("online_boss", BossRushScreen.Instance);
                foreach (var tab in new[] { "TabWorld", "TabGuild" }) yield return UiAuditTab("online_boss_" + tab, BossRushScreen.Instance, tab);
                yield return UiAuditWindow("online_rank", RankingScreen.Instance);
                foreach (var tab in new[] { "Tab0", "Tab1", "Tab2" }) { yield return UiAuditTab("online_rank_" + tab, RankingScreen.Instance, tab); yield return Wait(2f); yield return UiAuditCapture("online_rank_loaded_" + tab, RankingScreen.Instance); }
                yield return UiAuditOnlineDetails();
                uiAudit.completed = true;
            }
            finally { UiAuditWrite(); }
        }

        bool UiAuditOnlineClick(Transform scope, string id)
        {
            var button = scope == null ? null : UiAuditFind(scope, id)?.GetComponent<Button>();
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) return false;
            button.onClick.Invoke(); return true;
        }

        IEnumerator UiAuditOnlineExpect(string id, MenuScreen screen, Func<bool> ready)
        {
            float end = Time.realtimeSinceStartup + 12f;
            while (Game.UI.Top == screen && !ready() && Time.realtimeSinceStartup < end) yield return null;
            bool valid = Game.UI.Top == screen && ready();
            yield return UiAuditCapture(id, screen);
            UiAuditEvidence(valid, "expected loaded data/state for " + id);
        }

        IEnumerator UiAuditOnlinePages(MenuScreen screen, string panelName, string prefix)
        {
            var panel = UiAuditFind(screen.transform, panelName);
            var page = panel == null ? null : UiAuditFind(panel, "Page")?.GetComponent<Text>();
            Func<int, bool> at = n => page != null && page.text.Replace(" ", "") == n + "/3";
            yield return UiAuditOnlineExpect(prefix + "_page1", screen, () => at(1));
            for (int i = 2; i <= 3; i++)
            {
                bool clicked = at(i - 1) && UiAuditOnlineClick(panel, "Next");
                int expectedPage = i;
                yield return UiAuditOnlineExpect(prefix + "_page" + i, screen, () => clicked && at(expectedPage));
            }
        }

        IEnumerator UiAuditOnlineDetails()
        {
            yield return UiAuditWindow("online_mail_detail_start", MailScreen.Instance);
            yield return UiAuditOnlinePages(MailScreen.Instance, "List", "online_mail");
            var list = UiAuditFind(MailScreen.Instance.transform, "List");
            bool selected = UiAuditOnlineClick(list, "Row6");
            var detail = UiAuditFind(MailScreen.Instance.transform, "Detail");
            var detailTitle = detail == null ? null : UiAuditFind(detail, "Title")?.GetComponent<Text>();
            var row = list == null ? null : UiAuditFind(list, "Row6");
            var rowTitle = row == null ? null : UiAuditFind(row, "Title")?.GetComponent<Text>();
            yield return UiAuditOnlineExpect("online_mail_last_detail", MailScreen.Instance,
                () => selected && detailTitle != null && rowTitle != null && rowTitle.text.Length > 0 && detailTitle.text == "<b>" + rowTitle.text + "</b>");
            yield return UiAuditWindow("online_auction_pages_start", AuctionScreen.Instance);
            foreach (var tab in new[] { "Tab0", "Tab3" })
            {
                yield return UiAuditTab("online_auction_pages_" + tab, AuctionScreen.Instance, tab);
                yield return UiAuditOnlinePages(AuctionScreen.Instance, "Table", "online_auction_" + tab);
            }
            yield return UiAuditWindow("online_guild_details_start", GuildScreen.Instance);
            foreach (var tab in new[] { "Members", "Apps" })
            {
                var mine = UiAuditFind(GuildScreen.Instance.transform, "Mine");
                yield return UiAuditOnlineExpect("online_guild_ready_" + tab, GuildScreen.Instance, () => mine != null && mine.gameObject.activeInHierarchy);
                bool clicked = UiAuditOnlineClick(mine, tab);
                yield return Wait(.5f);
                var image = mine == null ? null : UiAuditFind(mine, tab)?.GetComponent<Image>();
                yield return UiAuditOnlineExpect("online_guild_" + tab, GuildScreen.Instance, () => clicked && image != null && image.sprite == Game.Art.Get("ui_btn"));
            }
            yield return UiAuditWindow("online_social_details_start", SocialScreen.Instance);
            yield return UiAuditOnlineExpect("online_social_friends_loaded", SocialScreen.Instance, () => OnlineServices.Chat.Friends.Count > 0);
            var menu = SocialScreen.Instance.GetComponentInChildren<MenuList>();
            // The fresh fixture has one friend and no pending requests: index 1 is the person row.
            if (menu != null && menu.Count == 3 && OnlineServices.Chat.Friends.Count == 1 && OnlineServices.Chat.Requests.Count == 0) menu.Activate(1, 1);
            yield return UiAuditCapture("online_social_person", SocialScreen.Instance);
            UiAuditEvidence(menu != null && menu.Count > 3, "person action menu visible; no action submitted");
            Game.Flow.CloseInventory(); Game.Flow.OpenWindow(SocialScreen.Instance);
            yield return Wait(.5f);
            for (int i = 0; i < 3; i++)
            {
                menu = SocialScreen.Instance.GetComponentInChildren<MenuList>();
                menu?.Activate(0, 1);
                yield return Wait(.5f);
                yield return UiAuditCapture("online_social_view_" + i, SocialScreen.Instance);
                var value = menu == null ? null : UiAuditFind(menu.transform, "Value")?.GetComponent<Text>();
                string expectedView = i == 0 ? L.T("최근 대화") : i == 1 ? L.T("차단 목록") : L.T("친구");
                UiAuditEvidence(value != null && value.text == expectedView, "social view selection matches expected label");
            }
            yield return UiAuditWindow("online_rates_start", GachaScreen.Instance);
            foreach (var banner in new[] { "skin", "aura", "weapon", "armor", "accessory", "sealed" })
            {
                yield return UiAuditTab("online_rates_banner_" + banner, GachaScreen.Instance, "Card_" + banner);
                bool opened = UiAuditOnlineClick(GachaScreen.Instance.transform, "Rates");
                var modal = UiAuditFind(GachaScreen.Instance.transform, "RateModal");
                var viewport = modal == null ? null : UiAuditFind(modal, "Viewport");
                var scroll = viewport == null ? null : viewport.GetComponent<ScrollRect>();
                yield return UiAuditOnlineExpect("online_rates_top_" + banner, GachaScreen.Instance,
                    () => opened && modal != null && modal.gameObject.activeInHierarchy && scroll != null && scroll.content.rect.height > 0);
                Canvas.ForceUpdateCanvases();
                if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f; }
                yield return UiAuditOnlineExpect("online_rates_bottom_" + banner, GachaScreen.Instance,
                    () => scroll != null && (scroll.content.rect.height <= scroll.viewport.rect.height || Mathf.Abs(scroll.verticalNormalizedPosition) < .01f));
                UiAuditOnlineClick(modal, "Close");
            }
        }
    }
}
