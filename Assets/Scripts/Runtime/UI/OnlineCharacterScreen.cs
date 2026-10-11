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
        int pick;
        RectTransform grid;
        readonly System.Collections.Generic.List<CharacterPreviewCard> cards = new System.Collections.Generic.List<CharacterPreviewCard>();
        readonly System.Collections.Generic.List<GameObject> empties = new System.Collections.Generic.List<GameObject>();
        float animTimer;

        protected override bool AnyFieldFocused => false;

        public static OnlineCharacterScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineCharacters", true);
            var screen = root.gameObject.AddComponent<OnlineCharacterScreen>();
            screen.ui = ui;
            // Twelve empty body lines reserve the two card rows (to -422) and the status line under them.
            screen.BuildPanel(root, "온라인 캐릭터", 640, new string('\n', 11), 20);
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
            titleText.text = deleting ? "삭제할 캐릭터 고르기" : $"온라인 캐릭터  <size=18><color=#b8c4d8>{count}/{session?.CharacterLimit ?? 6}</color></size>";
            if (!deleting)
            {
                actionItem = menu.AddButton("접속하기", () => { if (Picked != null) Enter(Picked); }, () => Picked != null);
                menu.AddButton("새 캐릭터 만들기", () => ui.Push(ui.OnlineCreate), () => session != null && count < session.CharacterLimit);
                menu.AddButton("캐릭터 삭제", () => { deleting = true; Rebuild(); }, () => count > 0);
                menu.AddButton("회원 탈퇴", () => ui.Push(ui.Withdrawal)); // [W2]
                menu.AddButton("로그아웃", () => { OnlineSession.Logout(); ui.Pop(); });
                menu.OnCancel = () => { OnlineSession.Logout(); ui.Pop(); };
            }
            else
            {
                actionItem = menu.AddButton("삭제하기", () => { if (Picked != null) Delete(Picked); }, () => Picked != null);
                menu.AddButton("취소", () => { deleting = false; Rebuild(); });
                menu.OnCancel = () => { deleting = false; Rebuild(); };
            }
            FitPanel();
            menu.Refresh();
            BuildCards(session);
            UpdateActionLabel();
        }

        MenuList.Item actionItem;

        /// <summary>The first row names the picked character: 접속하기 · 이름 (or 삭제하기 · 이름 in delete mode).</summary>
        void UpdateActionLabel()
        {
            if (actionItem == null || actionItem.labelText == null) return;
            string verb = deleting ? "삭제하기" : "접속하기";
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
                    card.Set(ch.cls, ch.name, $"{(ch.cls == CharacterClass.Mage ? "마법사" : "전사")} Lv.{ch.level}");
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
            var label = UIFactory.Text(rect, "Label", create ? "+\n새 캐릭터" : "빈 슬롯", create ? 22 : 17, create ? UIColors.Cream : UIColors.Disabled, TextAnchor.MiddleCenter, true);
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
        }

        void Enter(OnlineSession.CharacterSummary c)
        {
            var session = OnlineSession.Current;
            if (busy || session == null) return;
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
}
