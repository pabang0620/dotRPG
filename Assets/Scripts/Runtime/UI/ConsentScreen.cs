using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [POLICY] 약관 동의 before online play (PolicyClient.EnsureConsent): terms, personal data and "14 or older" are all
    /// required. It cannot be skipped: Esc does nothing, "동의하지 않음" logs out and goes back to the title.
    /// </summary>
    public class ConsentScreen : MenuScreen
    {
        UIRoot ui;
        Text status;
        bool terms, privacy, age, busy;
        Action<Action<ApiResult>> onAgree;
        Action onDecline;

        bool All => terms && privacy && age;

        public static ConsentScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Consent", true);
            var screen = root.gameObject.AddComponent<ConsentScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("약관 동의"), 700, L.T("온라인 플레이를 시작하려면 아래 항목에 모두 동의해 주세요.\n항목을 바꾸려면 ◀ ▶ 또는 클릭, 내용은 '보기'로 확인합니다.") + "\n" + PrivacySummary, 18);
            var menu = screen.menu;
            menu.AddOption(L.T("모두 동의"), () => screen.All ? L.T("동의함") : L.T("동의 안 함"), d =>
            {
                bool v = !screen.All;
                screen.terms = screen.privacy = screen.age = v;
            });
            menu.AddOption(L.T("[필수] 이용약관 동의"), () => Mark(screen.terms), d => screen.terms = !screen.terms);
            menu.AddButton(L.T("이용약관 보기"), () => PolicyClient.Open(PolicyClient.Terms));
            menu.AddOption(L.T("[필수] 개인정보 수집·이용 동의"), () => Mark(screen.privacy), d => screen.privacy = !screen.privacy);
            menu.AddButton(L.T("개인정보처리방침 보기"), () => PolicyClient.Open(PolicyClient.Privacy));
            menu.AddOption(L.T("[필수] 만 14세 이상입니다"), () => Mark(screen.age), d => screen.age = !screen.age);
            menu.AddButton(L.T("동의하고 시작"), screen.Agree, () => screen.All && !screen.busy);
            menu.AddButton(L.T("동의하지 않음"), screen.Decline, () => !screen.busy);
            menu.OnCancel = () => { }; // required: Esc does not skip it
            screen.FitPanel();
            // One status line under the menu (POST errors).
            screen.status = UIFactory.Text(screen.panel, "Status", "", UiTheme.Size(16), UiTheme.TextSecondary, TextAnchor.LowerCenter, true);
            UIFactory.Place(screen.status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(640f, 24f));
            return screen;
        }

        // The four things a consent request must state (개인정보 보호법 제15조 제2항): purpose, items, retention, the right to refuse.
        static string PrivacySummary =>
            L.T("<size=15><color=#9aa4b5>개인정보 수집·이용: 목적은 계정 관리, 게임 진행, 부정 이용 방지, 결제입니다. 항목은 Steam ID, 기기 식별값(해시), 접속 IP, 게임 기록입니다. 탈퇴 후 30일이 지나면 파기하며, 법으로 보관해야 하는 결제 기록은 5년 보관합니다. 동의하지 않을 수 있으나 그러면 온라인 플레이를 할 수 없습니다.</color></size>");

        static string Mark(bool on) => on ? L.T("동의함") : L.T("동의 안 함");

        /// <summary>Shows the screen with nothing checked. <paramref name="agree"/> sends the agreement and reports back.</summary>
        public void Open(Action<Action<ApiResult>> agree, Action decline)
        {
            onAgree = agree;
            onDecline = decline;
            terms = privacy = age = busy = false;
            status.text = "";
            if (ui.Top != this) ui.Push(this);
            menu.Refresh();
        }

        void Agree()
        {
            if (!All || busy) return;
            busy = true;
            status.text = L.T("동의 내용을 보내는 중...");
            menu.Refresh();
            onAgree?.Invoke(r =>
            {
                busy = false;
                if (!r.ok)
                {
                    Game.Audio.PlaySfx("cancel");
                    status.text = string.IsNullOrEmpty(r.message) ? L.T("보내지 못했습니다. 다시 시도해 주세요.") : r.message;
                    menu.Refresh();
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                if (ui.Top == this) ui.Pop();
            });
        }

        void Decline()
        {
            if (busy) return;
            if (ui.Top == this) ui.Pop();
            if (ui.Top == ui.OnlineLogin) ui.Pop(); // back to the title
            onDecline?.Invoke();
        }
    }
}
