using System;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [I] Keyboard key rebinding: each action shows its key; choosing it waits for the next key press
    /// (Esc cancels). A key already used by another action is swapped. Saved with the settings.
    /// </summary>
    public class KeyBindScreen : MenuScreen
    {
        UIRoot ui;
        GameAction? listening;
        int listenFrame;

        static readonly string[] Names = { "공격", "이동기", "상호작용", "스킬 1", "스킬 2", "스킬 3", "스킬 4", "각성 스킬", "체력 물약", "마나 물약", "귀환 주문서", "가방" };

        public static KeyBindScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "KeyBind", true);
            var screen = root.gameObject.AddComponent<KeyBindScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "조작 키 변경", 620, "항목을 고르고 새 키를 누르세요. 이미 쓰는 키면 서로 바뀝니다.\n이동(방향키)·메뉴(Esc)·게임패드 버튼은 바꿀 수 없습니다.", 16);
            for (int i = 0; i < InputReader.Rebindable.Length; i++)
            {
                var action = InputReader.Rebindable[i];
                string name = Names[i];
                screen.menu.AddOption(name, () => screen.listening == action ? "<color=#ffd84a>키를 누르세요…</color>" : InputReader.KeyLabel(InputReader.KeyboardKey(action)),
                    d => { screen.listening = action; screen.listenFrame = Time.frameCount; });
            }
            screen.menu.AddButton("기본값으로", () => { InputReader.ResetKeyboardKeys(); Save(); });
            screen.menu.AddButton("돌아가기", () => { Save(); ui.Pop(); });
            screen.menu.OnCancel = () => { Save(); ui.Pop(); };
            screen.FitPanel();
            return screen;
        }

        static void Save()
        {
            Game.Settings.Data.keyOverrides = InputReader.SaveKeyOverrides();
            Game.Settings.Save();
        }

        void Update()
        {
            if (listening == null) { if (menu != null) menu.enabled = true; return; }
            if (menu != null) menu.enabled = false;
            if (Time.frameCount <= listenFrame + 1) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { listening = null; Game.Audio.PlaySfx("cancel"); menu.Refresh(); return; }
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.None || k == KeyCode.Escape || k >= KeyCode.Mouse0 || k == KeyCode.UpArrow || k == KeyCode.DownArrow
                    || k == KeyCode.LeftArrow || k == KeyCode.RightArrow || k == KeyCode.Return || k == KeyCode.KeypadEnter) continue;
                if (!UnityEngine.Input.GetKeyDown(k)) continue;
                InputReader.SetKeyboardKey(listening.Value, k);
                listening = null;
                Game.Audio.PlaySfx("confirm");
                Save();
                menu.Refresh();
                return;
            }
        }
    }

    /// <summary>[I] Three save slots: summary of each (name, class, level, story point, play time, date), new game or continue.</summary>
    public class SaveSlotScreen : MenuScreen
    {
        UIRoot ui;
        bool loading;
        UnityEngine.UI.Text titleText;

        public static SaveSlotScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "SaveSlots", true);
            var screen = root.gameObject.AddComponent<SaveSlotScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "저장 슬롯", 820);
            screen.titleText = screen.panel.Find("Title").GetComponent<UnityEngine.UI.Text>();
            return screen;
        }

        public void Open(bool load)
        {
            loading = load;
            titleText.text = load ? "이어하기 · 슬롯 선택" : "새 게임 · 슬롯 선택";
            menu.Clear();
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                int slot = i;
                menu.AddButton(Summary(slot), () => Pick(slot), () => !loading || Game.Saves.HasSave(slot));
            }
            menu.AddButton("돌아가기", () => ui.Pop());
            menu.OnCancel = () => ui.Pop();
            FitPanel();
            ui.Push(this);
        }

        void Pick(int slot)
        {
            if (loading)
            {
                SaveSystem.ActiveSlot = slot;
                Game.Flow.ContinueGame();
                return;
            }
            void Start()
            {
                SaveSystem.ActiveSlot = slot;
                ui.Push(ui.CharacterSelect);
            }
            if (Game.Saves.HasSave(slot)) ui.Confirm($"슬롯 {slot + 1}의 저장을 지우고 새로 시작할까요?\n다음 저장 때 덮어씁니다.", Start);
            else Start();
        }

        static string Summary(int slot)
        {
            if (!Game.Saves.HasSave(slot)) return $"슬롯 {slot + 1}   <color=#8a94a8>비어 있음</color>";
            var d = Game.Saves.Read(slot);
            if (d == null) return $"슬롯 {slot + 1}   <color=#ff8080>손상된 저장</color>";
            string cls = d.playerClass == "mage" ? "마법사" : "전사";
            string name = string.IsNullOrEmpty(d.playerName) ? QuestJournal.DefaultName : d.playerName;
            string story = "";
            if (Game.Quest != null && d.quests != null)
                foreach (var q in Game.Quest.Database.All)
                {
                    if (q.Kind != QuestKind.Main) continue;
                    var st = d.quests.Find(x => x.id == q.id);
                    if (st != null && (st.status == (int)QuestStatus.Active || st.status == (int)QuestStatus.ReadyToTurnIn)) { story = q.DisplayTitle; break; }
                }
            var t = TimeSpan.FromSeconds(d.playTimeSeconds);
            string date = DateTime.TryParse(d.savedAtUtc, out var at) ? at.ToLocalTime().ToString("MM-dd HH:mm") : "";
            return $"슬롯 {slot + 1}  {name} · {cls} Lv.{d.level}  <color=#b8c4d8>{story}  {(int)t.TotalHours}시간 {t.Minutes}분  {date}</color>";
        }
    }

    /// <summary>[E5] Help: how the main systems work and which key does what.</summary>
    public class HelpScreen : MenuScreen
    {
        public static HelpScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Help", true);
            var screen = root.gameObject.AddComponent<HelpScreen>();
            screen.BuildPanel(root, "도움말", 900, Body(), 18);
            screen.menu.AddButton("돌아가기", () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            var body = panel.Find("Body")?.GetComponent<UnityEngine.UI.Text>();
            if (body != null) body.text = Body();
        }

        static string Key(GameAction a) => Game.Input != null ? Game.Input.GetBindingLabel(a) : "";

        static string Body()
        {
            var sb = new StringBuilder();
            sb.Append($"<color=#ffd84a>퀘스트</color>  머리 위 <color=#ffd640>!</color>는 새 퀘스트, <color=#ffd640>?</color>는 진행·보고. 오른쪽 위 알리미를 따라가세요.\n");
            sb.Append($"<color=#ffd84a>전투</color>  공격 [{Key(GameAction.Attack)}]  이동기 [{Key(GameAction.Mobility)}]  스킬 [{Key(GameAction.Skill1)}][{Key(GameAction.Skill2)}][{Key(GameAction.Skill3)}][{Key(GameAction.Skill4)}]  각성 [{Key(GameAction.Skill5)}]\n");
            sb.Append($"<color=#ffd84a>회복</color>  체력 물약 [{Key(GameAction.UseItem)}]  마나 물약 [{Key(GameAction.UseMana)}]  귀환 주문서 [{Key(GameAction.TownScroll)}]\n");
            sb.Append("<color=#ffd84a>요일 던전</color>  광장의 던전 안내원. 요일마다 열리는 던전이 다르고 주말엔 전부 열립니다. 하루 3회.\n");
            sb.Append("<color=#ffd84a>레이드</color>  중간 레이드는 수·토·일, 하루 1회 보상과 봉인 열쇠 조각. 조각 100개로 일요일 최종 레이드.\n");
            sb.Append("<color=#ffd84a>강화</color>  대장간. +10부터 실패하면 장비가 파괴될 수 있으니 장비 보호권을 챙기세요.\n");
            sb.Append("<color=#ffd84a>성장</color>  레벨마다 패시브 포인트 1. 메뉴 → 스킬에서 트리와 보조 젬을 고르세요.\n");
            sb.Append("<color=#ffd84a>파티</color>  메뉴 → 파티에서 용병을 고용합니다. 스토리 동료는 마을 밖에서 자동으로 합류합니다.");
            return sb.ToString();
        }
    }
}
