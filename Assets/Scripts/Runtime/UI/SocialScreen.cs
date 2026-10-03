using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [F5] 친구 · 차단 · 신고 (PLAN_ONLINE §2.3). Lists friends, recent chat partners and blocked players;
    /// choosing a name shows what can be done with that player. Reports attach the last 20 chat lines.
    /// </summary>
    public class SocialScreen : MenuScreen
    {
        public static SocialScreen Instance { get; private set; }

        enum View { Friends, Recent, Blocked, Person, Report }
        static readonly string[] ListNames = { "친구", "최근 대화", "차단 목록" };

        View view = View.Friends;
        View listView = View.Friends;
        string person;
        UnityEngine.UI.Text titleText;

        static IChatService Service => OnlineServices.Chat;

        public static SocialScreen Create(Transform canvas)
        {
            var root = CreateRoot(canvas, "Social", true);
            var screen = root.gameObject.AddComponent<SocialScreen>();
            Instance = screen;
            screen.BuildPanel(root, "친구 · 차단 · 신고", 640);
            screen.titleText = screen.panel.Find("Title").GetComponent<UnityEngine.UI.Text>();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            view = listView;
            if (Service is ServerChatService server) server.Refresh(); // [SERVER 5] SocialChanged redraws
            Rebuild();
        }

        void OnEnable() { OnlineServices.ChatChanged += Rebind; Rebind(); }
        void OnDisable() { OnlineServices.ChatChanged -= Rebind; if (bound != null) bound.SocialChanged -= OnSocial; bound = null; }

        IChatService bound;
        void Rebind()
        {
            if (bound != null) bound.SocialChanged -= OnSocial;
            bound = Service;
            bound.SocialChanged += OnSocial;
        }

        void OnSocial() { if (view != View.Report && view != View.Person && menu != null) Rebuild(); }

        void Rebuild()
        {
            menu.Clear();
            if (view == View.Person) BuildPerson();
            else if (view == View.Report) BuildReport();
            else BuildList();
            FitPanel();
            menu.Refresh();
        }

        void BuildList()
        {
            titleText.text = "친구 · 차단 · 신고";
            menu.AddOption("보기", () => ListNames[(int)view], d =>
            {
                view = (View)(((int)view + d + 3) % 3);
                listView = view;
                Rebuild();
            });
            if (view == View.Friends)
            {
                // [SERVER 5] Friend requests waiting for an answer.
                foreach (var q in Service.Requests)
                {
                    var req = q;
                    menu.AddButton($"<color=#ffe066>친구 요청</color> {req.name} - 수락", () => Service.RespondRequest(req.id, true));
                    menu.AddButton($"<color=#8c96a8>친구 요청</color> {req.name} - 거절", () => Service.RespondRequest(req.id, false));
                }
                foreach (var f in Service.Friends)
                {
                    var friend = f;
                    string state = friend.online ? $"<color=#8fe28f>● 접속 중</color> <color=#b8c4d8>{friend.where}</color>" : "<color=#8a94a8>○ 접속 안 함</color>";
                    menu.AddButton($"{friend.name}   {state}", () => OpenPerson(friend.name));
                }
                if (Service.Friends.Count == 0) menu.AddButton("<color=#8a94a8>친구가 없습니다. 최근 대화에서 추가하세요.</color>", () => { }, () => false);
            }
            else if (view == View.Recent)
            {
                var names = Service.RecentSpeakers().Take(8).ToList();
                foreach (var n in names)
                {
                    string name = n;
                    string tag = Service.IsFriend(name) ? " <color=#8fe28f>(친구)</color>" : "";
                    menu.AddButton(name + tag, () => OpenPerson(name));
                }
                if (names.Count == 0) menu.AddButton("<color=#8a94a8>아직 대화한 모험가가 없습니다.</color>", () => { }, () => false);
            }
            else
            {
                foreach (var n in Service.Blocked.ToList())
                {
                    string name = n;
                    menu.AddButton($"{name}  <color=#ff9f43>차단 중</color>", () => OpenPerson(name));
                }
                if (Service.Blocked.Count == 0) menu.AddButton("<color=#8a94a8>차단한 모험가가 없습니다.</color>", () => { }, () => false);
            }
            menu.AddButton("닫기", Close);
            menu.OnCancel = Close;
        }

        void OpenPerson(string name)
        {
            person = name;
            view = View.Person;
            Rebuild();
        }

        void BuildPerson()
        {
            titleText.text = person;
            bool friend = Service.IsFriend(person), blocked = Service.IsBlocked(person);
            if (!blocked)
            {
                menu.AddButton("귓속말", () => { Close(); ChatView.Instance?.Open($"/w {person} "); });
                menu.AddButton("파티 초대", () =>
                {
                    string id = Service.IdOf(person);
                    if (!Service.IsOnline || id == null || PartyClient.Instance == null)
                    {
                        GameEvents.RaiseToast(Service.IsOnline ? $"{person}님을 찾을 수 없습니다." : $"{person}님에게 파티 초대를 보냈습니다. (오프라인 미리보기: 응답 없음)");
                        return;
                    }
                    PartyClient.Instance.Invite(id, (ok, msg) => GameEvents.RaiseToast(ok ? $"{person}님에게 파티 초대를 보냈습니다." : msg));
                });
                menu.AddButton(friend ? "친구 삭제" : "친구 추가", () =>
                {
                    if (friend) Service.RemoveFriend(person); else Service.AddFriend(person);
                    if (!Service.IsOnline) GameEvents.RaiseToast(friend ? $"{person}님을 친구에서 삭제했습니다." : $"{person}님을 친구로 추가했습니다."); // online: the server's answer toasts
                    OnlineServices.SaveSocial();
                    Rebuild();
                });
            }
            menu.AddButton(blocked ? "차단 해제" : "차단", () =>
            {
                if (blocked) { Service.Unblock(person); OnlineServices.SaveSocial(); Rebuild(); return; }
                Game.UI.Confirm($"{person}님을 차단할까요?\n<size=18>채팅·귓속말·초대를 받지 않고 친구에서도 빠집니다.</size>", () =>
                {
                    Service.Block(person);
                    OnlineServices.SaveSocial();
                    if (!Service.IsOnline) GameEvents.RaiseToast($"{person}님을 차단했습니다.");
                    Rebuild();
                }, overlay: true);
            });
            menu.AddButton("신고", () => { view = View.Report; Rebuild(); });
            menu.AddButton("돌아가기", Back);
            menu.OnCancel = Back;
        }

        void BuildReport()
        {
            titleText.text = $"{person} 신고 · 사유 선택";
            foreach (var r in ChatRules.ReportReasons)
            {
                string reason = r;
                menu.AddButton(reason, () => Game.UI.Confirm($"{person}님을 '{reason}'(으)로 신고할까요?\n<size=18>최근 대화 {ChatRules.ReportLines}줄이 함께 전달됩니다.</size>", () =>
                {
                    GameEvents.RaiseToast(Service.Report(person, reason));
                    view = View.Person;
                    Rebuild();
                }, overlay: true));
            }
            menu.AddButton("돌아가기", () => { view = View.Person; Rebuild(); });
            menu.OnCancel = () => { view = View.Person; Rebuild(); };
        }

        void Back()
        {
            view = listView;
            Rebuild();
        }

        void Close() => Game.Flow.CloseInventory();

        /// <summary>[DevCapture] Current title for the automated check.</summary>
        public string DevTitle => titleText.text;
        public void DevOpenPerson(string name) => OpenPerson(name);
    }
}
