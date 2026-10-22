using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] Online character list as one grid of cards (3 x 2 = the account's slots): a click picks a character
    /// (it walks), a second click or 접속하기 enters, an empty slot opens 새 캐릭터. Left / right arrows move the pick.
    /// The menu under the grid only holds actions (enter, create, delete, withdraw, log out).
    /// </summary>
    public class OnlineCharacterScreen : OnlineMenuScreen
    {
        const int Cols = 3;
        const float CardW = 180f, CardH = 160f, GapX = 16f, GapY = 10f, GridTop = -92f;

        Text titleText;
        bool deleting;
        // [0045] Kakao / Google linking in the browser: the menu shows only the cancel row meanwhile
        OnlineSession.OauthFlow linkFlow;
        int pick;
        RectTransform grid;
        readonly System.Collections.Generic.List<CharacterPreviewCard> cards = new System.Collections.Generic.List<CharacterPreviewCard>();
        readonly System.Collections.Generic.List<GameObject> empties = new System.Collections.Generic.List<GameObject>();
        float animTimer;
        /// <summary>[0038] Enter this character once the list has reloaded after its rename.</summary>
        string pendingEnterId;

        protected override bool AnyFieldFocused => false;

        public static OnlineCharacterScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineCharacters", true);
            var screen = root.gameObject.AddComponent<OnlineCharacterScreen>();
            screen.ui = ui;
            // Twelve empty body lines reserve the two card rows (to -422) and the status line under them.
            screen.BuildPanel(root, "", 640, new string('\n', 11), 20);
            screen.titleText = screen.panel.Find("Title").GetComponent<Text>();
            screen.panel.Find("Body").GetComponent<Text>().text = "";
            float w = Cols * CardW + (Cols - 1) * GapX;
            screen.grid = UIFactory.Place(UIFactory.Rect(screen.panel, "Cards"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, GridTop), new Vector2(w, 2 * CardH + GapY));
            screen.Status(GridTop - 2 * CardH - GapY - 4f);
            return screen;
        }

        public override void Show()
        {
            base.Show();
            deleting = false;
            if (linkFlow != null) { linkFlow.Cancel(); linkFlow = null; }
            Rebuild();
            var session = OnlineSession.Current;
            if (session == null) return;
            // [0045] Link buttons need the server's providers and the account's linked logins
            OnlineSession.FetchOauthProviders(() => session.LoadProviders(() => { if (gameObject.activeInHierarchy && !deleting && linkFlow == null && OnlineSession.Current == session) Rebuild(); }));
            busy = true;
            Say(L.T("캐릭터 목록을 불러오는 중..."));
            session.LoadCharacters(r =>
            {
                busy = false;
                Say(r.ok ? "" : Explain(r), !r.ok);
                Rebuild();
                // [0038] Just renamed: continue into the game with the new name.
                string id = pendingEnterId;
                pendingEnterId = null;
                var renamed = r.ok && id != null ? OnlineSession.Current?.Characters.Find(c => c.id == id) : null;
                if (renamed != null && !renamed.renameRequired) Enter(renamed);
            });
        }

        OnlineSession.CharacterSummary Picked
        {
            get
            {
                var s = OnlineSession.Current;
                return s != null && pick >= 0 && pick < s.Characters.Count ? s.Characters[pick] : null;
            }
        }

        void Rebuild()
        {
            var session = OnlineSession.Current;
            int count = session?.Characters.Count ?? 0;
            pick = Mathf.Clamp(pick, 0, Mathf.Max(0, count - 1));
            menu.Clear();
            titleText.text = deleting ? L.T("삭제할 캐릭터 고르기") : L.F("{0}  <size=18><color=#b8c4d8>{1}/{2}</color></size>", session?.LoginId ?? "", count, session?.CharacterLimit ?? 6);
            if (linkFlow != null)
            {
                actionItem = null;
                menu.AddButton(L.T("연동 취소"), CancelLink);
                menu.OnCancel = CancelLink;
            }
            else if (!deleting)
            {
                actionItem = menu.AddButton(L.T("접속하기"), () => { if (Picked != null) Enter(Picked); }, () => Picked != null);
                menu.AddButton(L.T("새 캐릭터 만들기"), () => ui.Push(ui.OnlineCreate), () => session != null && count < session.CharacterLimit);
                menu.AddButton(L.T("캐릭터 삭제"), () => { deleting = true; Rebuild(); }, () => count > 0);
                menu.AddButton(L.T("카카오 계정 연동"), () => Link("kakao"), () => OnlineSession.KakaoOn && session != null && !session.Providers.Contains("kakao"));
                menu.AddButton(L.T("Google 계정 연동"), () => Link("google"), () => OnlineSession.GoogleOn && session != null && !session.Providers.Contains("google"));
                menu.AddButton(L.T("회원 탈퇴"), () => ui.Push(ui.Withdrawal)); // [W2]
                menu.AddButton(L.T("로그아웃"), () => { OnlineSession.Logout(); ui.Pop(); });
                menu.OnCancel = () => { OnlineSession.Logout(); ui.Pop(); };
            }
            else
            {
                actionItem = menu.AddButton(L.T("삭제하기"), () => { if (Picked != null) Delete(Picked); }, () => Picked != null);
                menu.AddButton(L.T("취소"), () => { deleting = false; Rebuild(); });
                menu.OnCancel = () => { deleting = false; Rebuild(); };
            }
            FitPanel();
            menu.Refresh();
            BuildCards(session);
            UpdateActionLabel();
        }

        MenuList.Item actionItem;

        /// <summary>[0045] Attach Kakao / Google so this account can log in with it next time.</summary>
        void Link(string provider)
        {
            var session = OnlineSession.Current;
            if (busy || linkFlow != null || session == null) return;
            Say(L.F("{0} 로그인을 여는 중...", OnlineSession.ProviderName(provider)));
            OnlineSession.OauthFlow flow = null;
            flow = session.LinkOauth(provider, line => { if (linkFlow == flow) Say(line); }, r =>
            {
                if (linkFlow != flow) return;
                linkFlow = null;
                Rebuild();
                if (!r.ok) { Game.Audio.PlaySfx("cancel"); Say(Explain(r), true); return; }
                Game.Audio.PlaySfx("confirm");
                Say(L.F("{0} 계정을 연동했습니다. 다음부터 {0}로 로그인할 수 있습니다.", OnlineSession.ProviderName(provider)));
            });
            linkFlow = flow;
            Rebuild();
        }

        void CancelLink()
        {
            if (linkFlow == null) return;
            linkFlow.Cancel();
            linkFlow = null;
            Rebuild();
            Say(L.T("연동을 취소했습니다."));
        }

        /// <summary>The first row names the picked character: 접속하기 · 이름 (or 삭제하기 · 이름 in delete mode).</summary>
        void UpdateActionLabel()
        {
            if (actionItem == null || actionItem.labelText == null) return;
            string verb = deleting ? L.T("삭제하기") : L.T("접속하기");
            string text = Picked != null ? $"{verb} · {Picked.name}" : verb;
            actionItem.label = deleting ? $"<color=#ff8f80>{text}</color>" : text;
            menu.Refresh(); // re-draws the row labels (with the selection prefix) from item.label
        }

        void BuildCards(OnlineSession session)
        {
            foreach (var c in cards) Destroy(c.Rect.gameObject);
            foreach (var e in empties) Destroy(e);
            cards.Clear();
            empties.Clear();
            int count = session?.Characters.Count ?? 0, slots = Mathf.Max(count, session?.CharacterLimit ?? 6);
            float w = Cols * CardW + (Cols - 1) * GapX;
            for (int i = 0; i < slots && i < Cols * 2; i++)
            {
                var pos = new Vector2(-w / 2f + CardW / 2f + (i % Cols) * (CardW + GapX), -(i / Cols) * (CardH + GapY));
                int k = i;
                if (i < count)
                {
                    var ch = session.Characters[i];
                    var card = CharacterPreviewCard.Create(grid, "Card_" + i, pos, new Vector2(CardW, CardH), 104f, 20);
                    string sub = CharacterClassInfo.NameOf(ch.cls) + $" Lv.{ch.level}";
                    if (ch.renameRequired) sub = "<color=#ff9f7a>" + L.T("(이름 변경 필요)") + "</color>"; // [0038] one line: the card is narrow
                    card.Set(ch.cls, ch.name, sub);
                    card.OnClick(() => OnCard(k));
                    cards.Add(card);
                }
                else empties.Add(EmptySlot(pos, i == count && !deleting));
            }
            animTimer = 0f;
        }

        /// <summary>An unused slot: "새 캐릭터" (opens creation) for the first one, a dim frame for the rest.</summary>
        GameObject EmptySlot(Vector2 pos, bool create)
        {
            var rect = UIFactory.Place(UIFactory.Rect(grid, "Empty"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, new Vector2(CardW, CardH));
            var bg = UIFactory.Panel(rect, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);
            bg.color = new Color(1f, 1f, 1f, create ? .7f : .3f);
            var label = UIFactory.Text(rect, "Label", create ? L.T("+\n새 캐릭터") : L.T("빈 슬롯"), create ? 22 : 17, create ? UIColors.Cream : UIColors.Disabled, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(label.rectTransform);
            label.raycastTarget = false;
            if (create)
            {
                bg.raycastTarget = true;
                bg.gameObject.AddComponent<SlotClick>().action = () => { if (!busy) ui.Push(ui.OnlineCreate); };
            }
            return rect.gameObject;
        }

        sealed class SlotClick : MonoBehaviour, IPointerClickHandler
        {
            public System.Action action;
            public void OnPointerClick(PointerEventData e) => action?.Invoke();
        }

        /// <summary>First click picks the character; clicking the picked one again enters (or deletes in delete mode).</summary>
        void OnCard(int i)
        {
            if (busy) return;
            var s = OnlineSession.Current;
            if (s == null || i >= s.Characters.Count) return;
            if (i != pick) { pick = i; Game.Audio.PlaySfx("select"); UpdateActionLabel(); return; }
            if (deleting) Delete(s.Characters[i]); else Enter(s.Characters[i]);
        }

        protected override void Update()
        {
            base.Update();
            animTimer += Time.unscaledDeltaTime;
            int count = cards.Count;
            if (!busy && count > 0 && ui != null && !AnyFieldFocused)
            {
                int move = Input.GetKeyDown(KeyCode.RightArrow) ? 1 : Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : 0;
                if (move != 0) { pick = (pick + move + count) % count; UpdateActionLabel(); }
            }
            for (int i = 0; i < count; i++)
            {
                cards[i].SetSelected(i == pick);
                cards[i].Animate(animTimer);
            }
        }

        void Delete(OnlineSession.CharacterSummary c)
        {
            var session = OnlineSession.Current;
            if (busy || session == null) return;
            ui.Confirm(L.F("{0} 캐릭터를 삭제할까요?\n<size=18>아이템과 진행이 모두 사라집니다.</size>", c.name), () =>
            {
                busy = true;
                session.DeleteCharacter(c.id, r =>
                {
                    busy = false;
                    deleting = false;
                    Say(r.ok ? L.F("{0} 캐릭터를 삭제했습니다.", c.name) : Explain(r), !r.ok);
                    Rebuild();
                });
            }, overlay: true);
        }

        void Enter(OnlineSession.CharacterSummary c)
        {
            var session = OnlineSession.Current;
            if (busy || session == null) return;
            // [0038] An operator asked for a new name: pick it first, then enter (after the list reloads).
            if (c.renameRequired) { ui.OnlineRename.Open(c, () => pendingEnterId = c.id); return; }
            busy = true;
            Say(L.F("{0} 접속 중...", c.name));
            session.EnterCharacter(c.id, (r, data) =>
            {
                busy = false;
                if (!r.ok || data == null) { Say(Explain(r), true); Game.Audio.PlaySfx("cancel"); return; }
                Game.Audio.PlaySfx("confirm");
                Game.Flow.EnterOnline(data);
            });
        }
    }
}
