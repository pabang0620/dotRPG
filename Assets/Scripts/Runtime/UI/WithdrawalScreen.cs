using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] 회원 탈퇴 (Docs/server/phase12_withdrawal.md W1, W2): shows what the server says is lost (W1), asks for the confirm
    /// phrase and for proof of the owner (account password, or a Steam ticket for Steam accounts), then sends W2 and leaves the session.
    /// The cancel side lives on the login screen (W3).
    /// </summary>
    public class WithdrawalScreen : OnlineMenuScreen
    {
        const float Width = 760f, BodyTop = 86f, RowHeight = 50f;
        const int BodySize = 18;

        Text body;
        InputField confirmField, pwField;
        WithdrawalClient.Info info;
        string requestId, requestFor;

        protected override bool AnyFieldFocused => confirmField.isFocused || pwField.isFocused;

        public static WithdrawalScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Withdrawal", true);
            var screen = root.gameObject.AddComponent<WithdrawalScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("회원 탈퇴"), Width, "\n", BodySize);
            screen.body = screen.panel.Find("Body").GetComponent<Text>();
            screen.confirmField = screen.Field(L.T("확인 문구"), 0f, "", 40, false);
            screen.pwField = screen.Field(L.T("비밀번호"), 0f, L.T("계정 비밀번호"), 64, true);
            screen.Status(0f);
            screen.menu.AddButton(L.T("탈퇴 요청"), () => screen.Submit(), () => screen.info != null && screen.info.canRequest);
            screen.menu.AddButton(L.T("돌아가기"), () => screen.Back());
            screen.menu.OnCancel = screen.Back;
            screen.Layout("", false);
            return screen;
        }

        void Back()
        {
            if (!busy) ui.Pop();
        }

        public override void Show()
        {
            base.Show();
            info = null;
            requestId = null;
            confirmField.text = "";
            pwField.text = "";
            Say("");
            Layout(L.T("안내를 불러오는 중..."), false);
            LoadInfo();
        }

        void LoadInfo()
        {
            busy = true;
            WithdrawalClient.LoadInfo((r, loaded) =>
            {
                busy = false;
                if (!r.ok) { info = null; Layout(WithdrawalClient.Explain(r), false); menu.Refresh(); return; }
                info = loaded;
                ((Text)confirmField.placeholder).text = info.confirmPhrase;
                Layout(BuildText(info), info.reauth != "steam");
                menu.Refresh();
            });
        }

        protected override void Update()
        {
            base.Update();
            if (AnyFieldFocused && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Submit();
        }

        // ---------------- text and layout ----------------

        static string BuildText(WithdrawalClient.Info i)
        {
            var lines = new List<string>
            {
                L.T("<color=#ff8a70>탈퇴하면 캐릭터와 재화가 모두 사라집니다.</color>"),
                L.F("요청한 뒤 {0}일 동안은 접속할 수 없고,", i.graceDays),
                L.F("{0}까지 로그인 화면에서 철회할 수 있습니다.", WithdrawalClient.LocalTime(i.dueAtIfNow)),
                L.F("캐릭터 {0}명 · 골드 {1:N0} · 별조각 {2:N0} (유료 {3:N0})", i.characters, i.goldTotal, i.freeStars, i.paidStars),
            };
            if (i.starDebt > 0) lines.Add(L.F("갚아야 할 별조각 {0:N0}개가 있습니다.", i.starDebt));
            if (i.unclaimedMails + i.activeListings + i.topBids > 0)
                lines.Add(L.F("받지 않은 우편 {0}통 · 판매 중 경매 {1}건 · 최고 입찰 {2}건", i.unclaimedMails, i.activeListings, i.topBids));
            foreach (var n in i.notices)
            {
                switch (n)
                {
                    case "paid_stars_forfeited": lines.Add(L.T("유료 별조각은 탈퇴하면 사라집니다.")); break;
                    case "refund_follows_steam_policy": lines.Add(L.T("환불은 Steam 정책을 따릅니다.")); break;
                    case "auction_and_mail_lost_after_grace": lines.Add(L.T("경매·우편은 유예가 끝나면 사라집니다.")); break;
                    case "friends_party_not_restored": lines.Add(L.T("친구·파티는 철회해도 복구되지 않습니다.")); break;
                }
            }
            foreach (var b in i.blockers)
                if (b == "payment_open") lines.Add(L.T("<color=#ff8a70>결제 처리 중인 주문이 있어 지금은 탈퇴할 수 없습니다.</color>"));
            if (i.canRequest)
            {
                lines.Add("");
                lines.Add(L.F("아래에 <b>{0}</b> 를 입력하세요.", i.confirmPhrase) + (i.reauth == "steam" ? L.T(" (Steam 계정으로 본인 확인)") : ""));
            }
            return string.Join("\n", lines);
        }

        /// <summary>Body text, then the fields, status line and menu under it; the panel grows with the content.</summary>
        void Layout(string text, bool needPassword)
        {
            body.text = text;
            int lines = Mathf.Max(1, text.Split('\n').Length);
            float h = lines * BodySize * 1.45f + 8f;
            body.rectTransform.sizeDelta = new Vector2(Width - 70f, h);
            float y = -(BodyTop + h + 10f);
            bool fields = info != null && info.canRequest;
            SetRow(L.T("확인 문구"), fields, ref y);
            SetRow(L.T("비밀번호"), fields && needPassword, ref y);
            UIFactory.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Width - 60f, 34f));
            y -= 40f;
            menu.RectTransform.anchoredPosition = new Vector2(0f, y);
            panel.sizeDelta = new Vector2(Width, -y + menu.Height + 34f);
            FitToScreen();
        }

        void SetRow(string label, bool visible, ref float y)
        {
            var caption = (RectTransform)panel.Find(label + "Label");
            var box = (RectTransform)panel.Find(label + "Box");
            caption.gameObject.SetActive(visible);
            box.gameObject.SetActive(visible);
            if (!visible) return;
            UIFactory.Place(caption, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-190f, y), new Vector2(110f, 40f));
            UIFactory.Place(box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, y), new Vector2(320f, 40f));
            y -= RowHeight;
        }

        // ---------------- request ----------------

        void Submit()
        {
            if (busy || info == null || !info.canRequest) return;
            string phrase = confirmField.text.Trim();
            if (phrase.Length == 0) { Say(L.T("확인 문구를 입력해 주세요."), true); return; }
            bool dev = info.reauth != "steam";
            if (dev && pwField.text.Length < 8) { Say(L.T("계정 비밀번호를 입력해 주세요."), true); return; }
            bool paid = info.paidStars > 0;
            void Final() => ui.Confirm(L.T("정말 탈퇴를 요청할까요?\n<size=18>요청하면 바로 로그아웃되고, 유예 기간이 끝나면 되돌릴 수 없습니다.</size>"), () => Send(phrase, paid), overlay: true);
            if (paid) ui.Confirm(L.F("유료 별조각 {0:N0}개가 사라지는 것에 동의하시나요?", info.paidStars), Final, overlay: true);
            else Final();
        }

        void Send(string phrase, bool ackPaid)
        {
            // The same request_id while the same question is retried (a lost answer must not become a second request).
            string key = phrase + "|" + ackPaid;
            if (requestId == null || requestFor != key) { requestId = ApiClient.NewRequestId(); requestFor = key; }
            busy = true;
            Say(L.T("탈퇴를 요청하는 중..."));
            bool dev = info.reauth != "steam";
            WithdrawalClient.Request(requestId, phrase, ackPaid, OnlineSession.Current?.LoginId, dev ? pwField.text : null, r =>
            {
                busy = false;
                if (!r.ok)
                {
                    if (r.code != "NETWORK") requestId = null; // a refused request can be asked again with a new id
                    Say(WithdrawalClient.Explain(r), true);
                    Game.Audio.PlaySfx("cancel");
                    if (r.code == "ACK_REQUIRED" || r.code == "WITHDRAW_BLOCKED") LoadInfo(); // the notice changed
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                string due = WithdrawalClient.LocalTime(MiniJson.Str(MiniJson.Obj(r.data, "withdrawal"), "due_at") ?? info.dueAtIfNow);
                pwField.text = "";
                OnlineSession.EndByWithdrawal(openLogin: false);
                ui.Pop(); // character list (empty now)
                ui.Pop(); // title
                GameEvents.RaiseToast(L.F("탈퇴 요청이 접수되었습니다. {0}까지 로그인 화면에서 철회할 수 있습니다.", due));
            });
        }
    }
}
