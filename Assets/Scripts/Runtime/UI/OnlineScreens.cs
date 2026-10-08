using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Shared bits of the online screens: text fields on a menu panel and a status line.</summary>
    public abstract class OnlineMenuScreen : MenuScreen
    {
        protected UIRoot ui;
        protected Text status;
        protected bool busy;

        protected InputField Field(string label, float y, string placeholder, int limit, bool password)
        {
            var caption = UIFactory.Text(panel, label + "Label", label, 20, UIColors.Cream, TextAnchor.MiddleRight, true);
            UIFactory.Place(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-190f, y), new Vector2(110f, 40f));
            var bg = UIFactory.Panel(panel, label + "Box", true);
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, y), new Vector2(320f, 40f));
            bg.raycastTarget = true;
            var text = UIFactory.Text(bg.rectTransform, "Text", "", 20, Color.white, TextAnchor.MiddleLeft, false);
            UIFactory.Stretch(text.rectTransform, 12f, 2f, 12f, 2f);
            text.supportRichText = false;
            var hint = UIFactory.Text(bg.rectTransform, "Placeholder", placeholder, 18, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft, false);
            UIFactory.Stretch(hint.rectTransform, 12f, 2f, 12f, 2f);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = limit;
            field.lineType = InputField.LineType.SingleLine;
            field.targetGraphic = bg;
            if (password) { field.contentType = InputField.ContentType.Password; field.asteriskChar = '*'; }
            return field;
        }

        protected void Status(float y)
        {
            status = UIFactory.Text(panel, "Status", "", 18, UiTheme.TextSecondary, TextAnchor.MiddleCenter, true);
            UIFactory.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 34f));
        }

        protected void Say(string message, bool error = false)
        {
            if (status == null) return;
            status.text = message ?? "";
            status.color = error ? new Color32(255, 128, 110, 255) : UiTheme.TextSecondary;
        }

        /// <summary>Korean line for a failed call: the server's message, or what to do for the special codes.</summary>
        protected static string Explain(ApiResult r)
        {
            switch (r.code)
            {
                case "NETWORK": return "서버에 연결할 수 없습니다. 서버가 켜져 있는지 확인하세요.";
                case "CLIENT_OUTDATED":
                case "DATA_OUTDATED": return "게임을 업데이트해 주세요. (서버와 버전이 다릅니다)";
                default: return string.IsNullOrEmpty(r.message) ? $"요청이 실패했습니다. ({r.status})" : r.message;
            }
        }

        /// <summary>Typing in a field must not drive the menu (arrows, Enter, Esc).</summary>
        protected abstract bool AnyFieldFocused { get; }

        protected virtual void Update()
        {
            bool typing = AnyFieldFocused;
            InputReader.TextInputActive = typing;
            if (menu != null) menu.enabled = !typing && !busy;
        }

        public override void Hide()
        {
            InputReader.TextInputActive = false;
            base.Hide();
        }
    }

    /// <summary>[SERVER] 온라인 접속: development login (id + password) or a new account.</summary>
    public class OnlineLoginScreen : OnlineMenuScreen
    {
        InputField idField, pwField;
        // [W3] The last login was refused because the account waits for withdrawal: offer the cancel button.
        bool cancelOffered, cancelSteam;

        protected override bool AnyFieldFocused => idField.isFocused || pwField.isFocused;

        public static OnlineLoginScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineLogin", true);
            var screen = root.gameObject.AddComponent<OnlineLoginScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "온라인 접속", 640, "\n\n\n\n\n", 20);
            screen.idField = screen.Field("아이디", -100f, "영문 소문자·숫자 4~20자", 20, false);
            screen.pwField = screen.Field("비밀번호", -150f, "8자 이상", 64, true);
            screen.Status(-200f);
            screen.BuildMenu();
            return screen;
        }

        void BuildMenu()
        {
            menu.Clear();
            // [PARTY 8] Steam players log in with their Steam account (shown only while Steam is running).
            menu.AddButton("Steam으로 접속", () => SubmitSteam(), () => SteamBridge.Current != null && SteamBridge.Current.Ready);
            menu.AddButton("로그인", () => Submit(false));
            menu.AddButton("새 계정 만들기", () => Submit(true));
            if (cancelOffered)
                menu.AddButton("탈퇴 철회", () => ui.Confirm("탈퇴 요청을 철회할까요?\n<size=18>철회하면 계정과 캐릭터가 그대로 돌아옵니다.</size>", CancelWithdrawal, overlay: true));
            menu.AddButton("돌아가기", () => ui.Pop());
            menu.OnCancel = () => ui.Pop();
            FitPanel();
            menu.Refresh();
        }

        /// <summary>[W3] 403 ACCOUNT_WITHDRAWAL_PENDING: say so, show the cancel button and ask once.</summary>
        void OfferCancel(bool steam, ApiResult r)
        {
            bool allowed = r.errors != null && r.errors.TryGetValue("cancel_allowed", out var a) && a is bool b && b;
            if (!allowed)
            {
                Say("탈퇴 처리 중인 계정입니다. 고객 지원에 문의하세요.", true);
                return;
            }
            cancelOffered = true;
            cancelSteam = steam;
            BuildMenu();
            Say("탈퇴 대기 중인 계정입니다.", true);
            string due = WithdrawalClient.LocalTime(MiniJson.Str(r.errors, "due_at"));
            ui.Confirm($"탈퇴 대기 중인 계정입니다.\n{(due.Length > 0 ? due + "까지 철회할 수 있습니다." : "기한 안에는 철회할 수 있습니다.")}\n\n지금 탈퇴를 철회할까요?", CancelWithdrawal, overlay: true);
        }

        void CancelWithdrawal()
        {
            if (busy) return;
            string id = idField.text.Trim().ToLowerInvariant(), pw = pwField.text;
            if (!cancelSteam && (id.Length < 4 || pw.Length < 8)) { Say("아이디와 비밀번호를 입력해 주세요.", true); return; }
            busy = true;
            Say("탈퇴를 철회하는 중...");
            WithdrawalClient.Cancel(cancelSteam ? null : id, cancelSteam ? null : pw, r =>
            {
                busy = false;
                if (!r.ok)
                {
                    Say(WithdrawalClient.Explain(r), true);
                    Game.Audio.PlaySfx("cancel");
                    if (r.code == "NO_PENDING_WITHDRAWAL" || r.code == "WITHDRAWAL_DUE" || r.code == "CANCEL_NOT_ALLOWED") { cancelOffered = false; BuildMenu(); }
                    return;
                }
                cancelOffered = false;
                BuildMenu();
                Say("탈퇴를 철회했습니다. 접속하는 중...");
                if (cancelSteam) SubmitSteam(); else Submit(false); // the cancel used the ticket / password up: log in again
            });
        }

        public override void Show()
        {
            base.Show();
            pwField.text = "";
            if (cancelOffered) { cancelOffered = false; BuildMenu(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Say($"서버: {ApiClient.Instance.BaseUrl}");
#else
            Say("");
#endif
            // [SERVER 7] Planned or running maintenance shows before anyone tries to log in.
            ApiClient.Instance.Get("/meta", r =>
            {
                string m = r.ok ? OnlineSession.MaintenanceText(MiniJson.Obj(r.data, "maintenance")) : null;
                if (m != null && gameObject.activeInHierarchy) Say(m.Replace("\n", "  "), MiniJson.Str(MiniJson.Obj(r.data, "maintenance"), "phase") != "scheduled");
            }, auth: false);
        }

        protected override void Update()
        {
            base.Update();
            if (!AnyFieldFocused) return;
            if (Input.GetKeyDown(KeyCode.Tab)) (idField.isFocused ? pwField : idField).ActivateInputField();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit(false);
        }

        void SubmitSteam()
        {
            if (busy) return;
            busy = true;
            Say("Steam 계정으로 접속하는 중...");
            OnlineSession.LoginSteam(r =>
            {
                busy = false;
                if (!r.ok)
                {
                    Game.Audio.PlaySfx("cancel");
                    if (r.code == "ACCOUNT_WITHDRAWAL_PENDING") OfferCancel(true, r); else Say(Explain(r), true);
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                ui.Pop();
                ui.Push(ui.OnlineCharacters);
            });
        }

        void Submit(bool register)
        {
            if (busy) return;
            string id = idField.text.Trim().ToLowerInvariant(), pw = pwField.text;
            if (id.Length < 4 || pw.Length < 8) { Say("아이디는 4자 이상, 비밀번호는 8자 이상이어야 합니다.", true); return; }
            busy = true;
            Say(register ? "계정을 만드는 중..." : "접속하는 중...");
            OnlineSession.Login(id, pw, register, r =>
            {
                busy = false;
                if (!r.ok)
                {
                    Game.Audio.PlaySfx("cancel");
                    if (r.code == "ACCOUNT_WITHDRAWAL_PENDING") OfferCancel(false, r); else Say(Explain(r), true);
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                ui.Pop();
                ui.Push(ui.OnlineCharacters);
            });
        }
    }

    /// <summary>[SERVER] Online character list: enter, create (another screen) or delete.</summary>
    public class OnlineCharacterScreen : OnlineMenuScreen
    {
        Text titleText;
        bool deleting;

        protected override bool AnyFieldFocused => false;

        public static OnlineCharacterScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineCharacters", true);
            var screen = root.gameObject.AddComponent<OnlineCharacterScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "온라인 캐릭터", 700, "\n", 20);
            screen.titleText = screen.panel.Find("Title").GetComponent<Text>();
            screen.Status(-100f);
            return screen;
        }

        public override void Show()
        {
            base.Show();
            deleting = false;
            Rebuild();
            var session = OnlineSession.Current;
            if (session == null) return;
            busy = true;
            Say("캐릭터 목록을 불러오는 중...");
            session.LoadCharacters(r =>
            {
                busy = false;
                Say(r.ok ? $"{OnlineSession.Current?.LoginId} 계정" : Explain(r), !r.ok);
                Rebuild();
            });
        }

        void Rebuild()
        {
            var session = OnlineSession.Current;
            menu.Clear();
            titleText.text = deleting ? "삭제할 캐릭터 고르기" : "온라인 캐릭터";
            if (session != null)
                foreach (var c in session.Characters)
                {
                    var ch = c;
                    string cls = ch.cls == CharacterClass.Mage ? "마법사" : "전사";
                    menu.AddButton($"{ch.name}   <color=#b8c4d8>{cls} Lv.{ch.level}</color>", () => Pick(ch));
                }
            if (!deleting)
            {
                menu.AddButton("새 캐릭터 만들기", () => ui.Push(ui.OnlineCreate), () => session != null && session.Characters.Count < session.CharacterLimit);
                menu.AddButton("캐릭터 삭제", () => { deleting = true; Rebuild(); }, () => session != null && session.Characters.Count > 0);
                menu.AddButton("회원 탈퇴", () => ui.Push(ui.Withdrawal)); // [W2]
                menu.AddButton("로그아웃", () => { OnlineSession.Logout(); ui.Pop(); });
                menu.OnCancel = () => { OnlineSession.Logout(); ui.Pop(); };
            }
            else
            {
                menu.AddButton("취소", () => { deleting = false; Rebuild(); });
                menu.OnCancel = () => { deleting = false; Rebuild(); };
            }
            FitPanel();
            menu.Refresh();
        }

        void Pick(OnlineSession.CharacterSummary c)
        {
            if (busy) return;
            var session = OnlineSession.Current;
            if (session == null) return;
            if (deleting)
            {
                ui.Confirm($"{c.name} 캐릭터를 삭제할까요?\n<size=18>아이템과 진행이 모두 사라집니다.</size>", () =>
                {
                    busy = true;
                    session.DeleteCharacter(c.id, r =>
                    {
                        busy = false;
                        deleting = false;
                        Say(r.ok ? $"{c.name} 캐릭터를 삭제했습니다." : Explain(r), !r.ok);
                        Rebuild();
                    });
                }, overlay: true);
                return;
            }
            busy = true;
            Say($"{c.name} 접속 중...");
            session.EnterCharacter(c.id, (r, data) =>
            {
                busy = false;
                if (!r.ok || data == null) { Say(Explain(r), true); Game.Audio.PlaySfx("cancel"); return; }
                Game.Audio.PlaySfx("confirm");
                Game.Flow.EnterOnline(data);
            });
        }
    }

    /// <summary>[SERVER] New online character: name (server rules) and class.</summary>
    public class OnlineCreateScreen : OnlineMenuScreen
    {
        InputField nameField;
        CharacterClass cls = CharacterClass.Warrior;
        string requestId, requestFor;

        protected override bool AnyFieldFocused => nameField.isFocused;

        public static OnlineCreateScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineCreate", true);
            var screen = root.gameObject.AddComponent<OnlineCreateScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "새 온라인 캐릭터", 640, "\n\n\n", 20);
            screen.nameField = screen.Field("이름", -100f, "한글·영문·숫자 2~8자", 8, false);
            screen.Status(-150f);
            screen.menu.AddOption("직업", () => screen.cls == CharacterClass.Mage ? "마법사" : "전사",
                d => { screen.cls = screen.cls == CharacterClass.Mage ? CharacterClass.Warrior : CharacterClass.Mage; screen.requestId = null; });
            screen.menu.AddButton("만들기", () => screen.Submit());
            screen.menu.AddButton("돌아가기", () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            nameField.text = "";
            requestId = null;
            Say("이름은 다른 모험가와 겹칠 수 없습니다.");
        }

        protected override void Update()
        {
            base.Update();
            if (AnyFieldFocused && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Submit();
        }

        void Submit()
        {
            if (busy || OnlineSession.Current == null) return;
            string name = nameField.text;
            if (name.Length < 2) { Say("이름은 2자 이상이어야 합니다.", true); return; }
            // Same request_id while retrying the same name and class (a lost answer must not create twice).
            string body = name + "|" + cls;
            if (requestId == null || requestFor != body) { requestId = ApiClient.NewRequestId(); requestFor = body; }
            busy = true;
            Say("만드는 중...");
            OnlineSession.Current.CreateCharacter(name, cls, requestId, r =>
            {
                busy = false;
                if (!r.ok)
                {
                    if (r.code != "NETWORK") requestId = null; // a refused name can be retried with a new id
                    Say(Explain(r), true);
                    Game.Audio.PlaySfx("cancel");
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                ui.Pop(); // back to the list, which reloads
            });
        }
    }
}
