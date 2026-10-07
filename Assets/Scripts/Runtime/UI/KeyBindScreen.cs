using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Clickable keyboard, staged rebinding, conflict swaps and explicit apply/cancel.</summary>
    public sealed class KeyBindScreen : MenuScreen
    {
        sealed class KeyView { public KeyCode key; public Image cap, stripe; public Text label, action; }
        sealed class ActionView { public GameAction action; public Image bg; public Text text; }
        readonly List<KeyView> keys = new List<KeyView>();
        readonly List<ActionView> actions = new List<ActionView>();
        static readonly KeyCode[] keyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        UIRoot ui;
        GameAction? listening;
        string saved;
        int listenFrame;
        Text status, dirtyLabel;
        bool dirty, windowsTab;
        Image combatTab, windowTab;
        public bool IsListening => listening.HasValue;
        public static string ActionName(GameAction a)
        {
            switch (a)
            {
                case GameAction.Attack: return "기본 공격"; case GameAction.Mobility: return "이동기";
                case GameAction.Interact: return "상호작용"; case GameAction.Inventory: return "가방";
                case GameAction.Skill1: return "스킬 1"; case GameAction.Skill2: return "스킬 2";
                case GameAction.Skill3: return "스킬 3"; case GameAction.Skill4: return "스킬 4";
                case GameAction.Skill5: return "각성 스킬"; case GameAction.UseItem: return "체력 물약";
                case GameAction.UseMana: return "마나 물약"; case GameAction.TownScroll: return "귀환";
                case GameAction.MoveUp: return "위로 이동"; case GameAction.MoveDown: return "아래 이동";
                case GameAction.MoveLeft: return "왼쪽 이동"; case GameAction.MoveRight: return "오른쪽 이동";
                case GameAction.Map: return "지도 (맵)";
                case GameAction.SkillWindow: return "스킬창"; case GameAction.QuestWindow: return "퀘스트창";
                case GameAction.WeekdayDungeon: return "요일던전"; case GameAction.RaidWindow: return "레이드";
                case GameAction.PartyWindow: return "파티창"; case GameAction.PartyFinder: return "파티 찾기";
                case GameAction.Auction: return "경매장"; case GameAction.Friends: return "친구 목록";
                case GameAction.Cosmetics: return "외형 상점";
                case GameAction.AutoQuest: return "자동 진행"; case GameAction.AutoHunt: return "자동 사냥";
                default: return a.ToString();
            }
        }
        static string ShortName(GameAction a)
        {
            switch (a)
            {
                case GameAction.Map: return "지도"; case GameAction.QuestWindow: return "퀘스트";
                case GameAction.PartyWindow: return "파티"; case GameAction.PartyFinder: return "파티찾기";
                case GameAction.Friends: return "친구"; case GameAction.Cosmetics: return "외형";
                case GameAction.Attack: return "공격"; case GameAction.Interact: return "대화";
                case GameAction.Skill5: return "각성"; case GameAction.UseItem: return "HP"; case GameAction.UseMana: return "MP";
                case GameAction.MoveUp: return "이동"; case GameAction.MoveDown: return "이동";
                case GameAction.MoveLeft: return "이동"; case GameAction.MoveRight: return "이동";
                case GameAction.AutoQuest: return "자동"; case GameAction.AutoHunt: return "사냥";
                default: return ActionName(a);
            }
        }
        static Color Category(GameAction a)
        {
            if (a >= GameAction.MoveUp && a <= GameAction.MoveRight || a == GameAction.Mobility) return new Color32(94, 180, 237, 255);
            if (a == GameAction.Attack || a >= GameAction.Skill1 && a <= GameAction.Skill5) return new Color32(186, 126, 241, 255);
            if (a == GameAction.UseItem || a == GameAction.UseMana || a == GameAction.TownScroll) return new Color32(113, 210, 158, 255);
            return UiTheme.AccentWarm;
        }
        public static KeyBindScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "KeyBind", true);
            var view = root.gameObject.AddComponent<KeyBindScreen>(); view.ui = ui;
            view.BuildPanel(root, "키보드 설정", 1200);
            Destroy(view.menu.gameObject); view.menu = null;
            view.panel.sizeDelta = new Vector2(1200, 670);
            view.Build(); return view;
        }
        Image Plate(Transform parent, string name, Vector2 at, Vector2 size, Color color)
        {
            var image = UIFactory.Image(parent, name, Game.Art.Get("ui_white"), color); image.preserveAspect = false;
            UIFactory.Place(image.rectTransform, new Vector2(0, 1), new Vector2(0, 1), at, size); return image;
        }
        Text TextAt(Transform parent, string name, string text, Vector2 at, Vector2 size, int font, Color color)
        {
            var label = UIFactory.Text(parent, name, text, font, color, TextAnchor.MiddleLeft, true);
            UIFactory.Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), at, size); return label;
        }
        void ButtonAt(string name, string label, float x, float width, Action click, Color color)
        {
            var bg = Plate(panel, name, new Vector2(x, -607), new Vector2(width, 38), color);
            bg.raycastTarget = true; bg.gameObject.AddComponent<PointerRelay>().onClick = _ => click();
            var text = TextAt(bg.transform, "Label", label, new Vector2(10, 0), new Vector2(width - 20, 38), 17, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
        }
        void Build()
        {
            TextAt(panel, "Instructions", "기능이나 배정된 키 선택 → 새 키 입력 / 클릭 → 적용    ·    겹치는 단축키는 서로 교환됩니다.", new Vector2(30, -76), new Vector2(1140, 28), 17, UiTheme.TextSecondary);
            TextAt(panel, "Categories", "<color=#5eb4ed>■ 이동</color>     <color=#ba7ef1>■ 공격 · 스킬</color>     <color=#71d29e>■ 아이템</color>     <color=#ebbd5f>■ 편의 기능</color>     <color=#8391a7>■ 고정 키</color>", new Vector2(30, -105), new Vector2(1140, 25), 15, Color.white);
            var keyboard = Plate(panel, "Keyboard", new Vector2(30, -139), new Vector2(1140, 252), new Color32(10, 17, 28, 255)).rectTransform;
            BuildKeyboard(keyboard);
            combatTab = MakeTab("CombatTab", "이동 · 전투", 30, false);
            windowTab = MakeTab("WindowTab", "창 열기 · 닫기", 214, true);
            for (int i = 0; i < InputReader.Rebindable.Length; i++)
            {
                var a = InputReader.Rebindable[i]; int col = i / 6, row = i % 6;
                var bg = Plate(panel, "Binding_" + a, new Vector2(30 + col * 382, -433 - row * 24), new Vector2(370, 22), UiTheme.RowDark);
                bg.raycastTarget = true; bg.gameObject.AddComponent<PointerRelay>().onClick = _ => BeginBinding(a);
                var label = TextAt(bg.transform, "BindingText", "", new Vector2(10, 0), new Vector2(348, 22), 15, Color.white);
                actions.Add(new ActionView { action = a, bg = bg, text = label });
            }
            status = TextAt(panel, "Status", "", new Vector2(30, -578), new Vector2(1130, 24), 16, new Color32(170, 211, 240, 255));
            ButtonAt("ResetKeys", "기본값", 30, 115, ResetDraft, UiTheme.Slate);
            dirtyLabel = TextAt(panel, "PendingChanges", "", new Vector2(165, -607), new Vector2(615, 38), 15, UiTheme.AccentWarm);
            ButtonAt("ApplyKeys", "적용", 887, 125, ApplyChanges, UiTheme.SelectBlue);
            ButtonAt("CloseKeys", "닫기 / 취소", 1022, 148, () => ui.Pop(), UiTheme.Slate);
        }
        Image MakeTab(string name, string label, float x, bool windows)
        {
            var tab = Plate(panel, name, new Vector2(x, -398), new Vector2(174, 28), Color.white);
            tab.raycastTarget = true; tab.gameObject.AddComponent<PointerRelay>().onClick = _ => SelectTab(windows);
            TextAt(tab.transform, "Label", label, new Vector2(12, 0), new Vector2(150, 28), 16, Color.white);
            return tab;
        }
        public void SelectTab(bool windows)
        {
            windowsTab = windows;
            int index = 0;
            foreach (var v in actions)
            {
                bool visible = InputReader.IsWindowAction(v.action) == windows;
                v.bg.gameObject.SetActive(visible);
                if (!visible) continue;
                v.bg.rectTransform.anchoredPosition = new Vector2(30 + (index / 6) * 382, -433 - (index % 6) * 24);
                index++;
            }
            combatTab.color = !windows ? UiTheme.SelectBlue : UiTheme.RowDark;
            windowTab.color = windows ? UiTheme.SelectBlue : UiTheme.RowDark;
        }
        void Key(RectTransform parent, KeyCode code, string label, float x, int row, float width = 1)
        {
            const float unit = 45, height = 38;
            var pos = new Vector2(12 + x * unit, -8 - row * 40);
            var bg = Plate(parent, "Key_" + code, pos, new Vector2(width * unit - 3, height), UiTheme.RowDark);
            var stripe = Plate(bg.transform, "Category", new Vector2(2, -height + 4), new Vector2(width * unit - 7, 2), Color.clear);
            bg.raycastTarget = true;
            bg.gameObject.AddComponent<PointerRelay>().onClick = _ => ClickKey(code);
            var title = TextAt(bg.transform, "KeyLabel", label, new Vector2(5, -1), new Vector2(width * unit - 10, 17), 13, Color.white);
            title.resizeTextForBestFit = true; title.resizeTextMinSize = 8; title.resizeTextMaxSize = label.Length > 4 && width < 1.5f ? 10 : 13;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            var action = TextAt(bg.transform, "Function", "", new Vector2(4, -18), new Vector2(width * unit - 8, 16), 10, new Color32(197, 214, 235, 255));
            action.resizeTextForBestFit = true; action.resizeTextMinSize = 8; action.resizeTextMaxSize = 10;
            keys.Add(new KeyView { key = code, cap = bg, stripe = stripe, label = title, action = action });
        }
        void BuildKeyboard(RectTransform board)
        {
            Key(board, KeyCode.Escape, "ESC", 0, 0);
            for (int i = 0; i < 12; i++) Key(board, KeyCode.F1 + i, "F" + (i + 1), 1.5f + i + (i / 4) * .35f, 0);
            Key(board, KeyCode.BackQuote, "`", 0, 1);
            for (int i = 1; i <= 10; i++) Key(board, KeyCode.Alpha0 + i % 10, (i % 10).ToString(), i, 1);
            Key(board, KeyCode.Minus, "−", 11, 1); Key(board, KeyCode.Equals, "=", 12, 1); Key(board, KeyCode.Backspace, "Backspace", 13, 1, 2);
            Key(board, KeyCode.Tab, "Tab", 0, 2, 1.5f);
            string row = "QWERTYUIOP";
            for (int i = 0; i < row.Length; i++) Key(board, (KeyCode)char.ToLowerInvariant(row[i]), row[i].ToString(), i + 1.5f, 2);
            Key(board, KeyCode.LeftBracket, "[", 11.5f, 2); Key(board, KeyCode.RightBracket, "]", 12.5f, 2); Key(board, KeyCode.Backslash, "\\", 13.5f, 2, 1.5f);
            Key(board, KeyCode.CapsLock, "Caps", 0, 3, 1.75f); row = "ASDFGHJKL";
            for (int i = 0; i < row.Length; i++) Key(board, (KeyCode)char.ToLowerInvariant(row[i]), row[i].ToString(), i + 1.75f, 3);
            Key(board, KeyCode.Semicolon, ";", 10.75f, 3); Key(board, KeyCode.Quote, "'", 11.75f, 3); Key(board, KeyCode.Return, "Enter", 12.75f, 3, 2.25f);
            Key(board, KeyCode.LeftShift, "Shift", 0, 4, 2.25f); row = "ZXCVBNM";
            for (int i = 0; i < row.Length; i++) Key(board, (KeyCode)char.ToLowerInvariant(row[i]), row[i].ToString(), i + 2.25f, 4);
            Key(board, KeyCode.Comma, ",", 9.25f, 4); Key(board, KeyCode.Period, ".", 10.25f, 4); Key(board, KeyCode.Slash, "/", 11.25f, 4); Key(board, KeyCode.RightShift, "Shift", 12.25f, 4, 2.75f);
            Key(board, KeyCode.LeftControl, "Ctrl", 0, 5, 1.5f); Key(board, KeyCode.LeftWindows, "Win", 1.5f, 5, 1.25f); Key(board, KeyCode.LeftAlt, "Alt", 2.75f, 5, 1.25f);
            Key(board, KeyCode.Space, "Space", 4, 5, 6); Key(board, KeyCode.RightAlt, "Alt", 10, 5, 1.5f); Key(board, KeyCode.RightControl, "Ctrl", 11.5f, 5, 3.5f);
            Key(board, KeyCode.Print, "Prt", 15.5f, 0); Key(board, KeyCode.ScrollLock, "Scr", 16.5f, 0); Key(board, KeyCode.Pause, "Pau", 17.5f, 0);
            Key(board, KeyCode.Insert, "Ins", 15.5f, 1); Key(board, KeyCode.Home, "Home", 16.5f, 1); Key(board, KeyCode.PageUp, "PgUp", 17.5f, 1);
            Key(board, KeyCode.Delete, "Del", 15.5f, 2); Key(board, KeyCode.End, "End", 16.5f, 2); Key(board, KeyCode.PageDown, "PgDn", 17.5f, 2);
            Key(board, KeyCode.UpArrow, "↑", 16.5f, 4); Key(board, KeyCode.LeftArrow, "←", 15.5f, 5); Key(board, KeyCode.DownArrow, "↓", 16.5f, 5); Key(board, KeyCode.RightArrow, "→", 17.5f, 5);
            Key(board, KeyCode.Numlock, "Num", 19.5f, 1); Key(board, KeyCode.KeypadDivide, "/", 20.5f, 1); Key(board, KeyCode.KeypadMultiply, "*", 21.5f, 1); Key(board, KeyCode.KeypadMinus, "−", 22.5f, 1);
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) { int n = 7 - r * 3 + c; Key(board, KeyCode.Keypad0 + n, n.ToString(), 19.5f + c, r + 2); }
            Key(board, KeyCode.KeypadPlus, "+", 22.5f, 2); Key(board, KeyCode.KeypadEnter, "Enter", 22.5f, 4);
            Key(board, KeyCode.Keypad0, "0", 19.5f, 5, 2); Key(board, KeyCode.KeypadPeriod, ".", 21.5f, 5);
        }
        public override void Show()
        {
            saved = InputReader.SaveKeyOverrides(); listening = null; dirty = false;
            base.Show();
            SelectTab(windowsTab);
            var available = ((RectTransform)transform).rect.size;
            float fit = Mathf.Min(1f, (available.x - 24) / 1200f, (available.y - 20) / 670f);
            panel.localScale = Vector3.one * Mathf.Max(.5f, fit);
            RefreshKeyboard("변경할 기능이나 색이 있는 키를 선택하세요. ESC·Enter·Backspace는 메뉴 조작용 고정 키입니다.");
        }
        public override void Hide()
        {
            if (saved != null) InputReader.LoadKeyOverrides(saved);
            listening = null; dirty = false; base.Hide();
        }
        public void BeginBinding(GameAction action)
        {
            SelectTab(InputReader.IsWindowAction(action));
            listening = action; listenFrame = Time.frameCount;
            RefreshKeyboard(ActionName(action) + "에 사용할 키를 누르거나 키보드 그림에서 클릭하세요. ESC: 선택 취소");
        }
        public void ClickKey(KeyCode key)
        {
            if (listening.HasValue) { AssignKey(key); return; }
            foreach (var a in InputReader.Rebindable) if (InputReader.KeyboardKey(a) == key) { BeginBinding(a); return; }
            if (key == KeyCode.RightShift && InputReader.KeyboardKey(GameAction.Mobility) == KeyCode.LeftShift) { BeginBinding(GameAction.Mobility); return; }
            if (key == KeyCode.Tab && InputReader.KeyboardKey(GameAction.Inventory) == KeyCode.I) { BeginBinding(GameAction.Inventory); return; }
            RefreshKeyboard(InputReader.CanBindKeyboardKey(key) ? "미지정 키입니다. 아래에서 변경할 기능을 먼저 선택하세요." : "메뉴·운영체제에서 사용하는 고정 키입니다.");
        }
        public void AssignKey(KeyCode key)
        {
            if (!listening.HasValue) return;
            if (!InputReader.CanBindKeyboardKey(key)) { RefreshKeyboard("이 키는 메뉴·운영체제 조작용으로 예약되어 있습니다. 다른 키를 선택하세요."); return; }
            var action = listening.Value; string swap = "";
            foreach (var other in InputReader.Rebindable)
                if (other != action && InputReader.KeyboardKey(other) == key) swap = " · " + ActionName(other) + "와 키 교환";
            InputReader.SetKeyboardKey(action, key); listening = null;
            dirty = InputReader.SaveKeyOverrides() != saved;
            RefreshKeyboard(ActionName(action) + " → " + InputReader.KeyLabel(key) + swap + " (적용 버튼으로 저장)");
            Game.Audio.PlaySfx("select");
        }
        public void ResetDraft() { InputReader.ResetKeyboardKeys(); listening = null; dirty = InputReader.SaveKeyOverrides() != saved; RefreshKeyboard("기본 배치로 돌아왔습니다. 적용하면 저장됩니다."); }
        public void ApplyChanges()
        {
            listening = null; saved = InputReader.SaveKeyOverrides(); Game.Settings.Data.keyOverrides = saved;
            Game.Settings.Save(); dirty = false; RefreshKeyboard("저장했습니다. 게임을 다시 실행해도 이 키 배치가 유지됩니다."); Game.Audio.PlaySfx("confirm");
        }
        void RefreshKeyboard(string message)
        {
            status.text = message;
            dirtyLabel.text = dirty ? "● 저장하지 않은 변경 사항 · 닫으면 취소됩니다" : "저장된 키 배치";
            foreach (var key in keys)
            {
                GameAction? assigned = null;
                foreach (var a in InputReader.Rebindable) if (InputReader.KeyboardKey(a) == key.key) { assigned = a; break; }
                // Secondary convenience keys are only active while not assigned to another action.
                bool alias = false;
                if (!assigned.HasValue && key.key == KeyCode.RightShift && InputReader.KeyboardKey(GameAction.Mobility) == KeyCode.LeftShift) { assigned = GameAction.Mobility; alias = true; }
                if (!assigned.HasValue && key.key == KeyCode.Tab && InputReader.KeyboardKey(GameAction.Inventory) == KeyCode.I) { assigned = GameAction.Inventory; alias = true; }
                Color c = assigned.HasValue ? Category(assigned.Value) : new Color32(93, 109, 132, 255);
                bool selected = assigned.HasValue && listening == assigned;
                key.cap.color = Color.Lerp(UiTheme.PanelDeep, c, selected ? .85f : assigned.HasValue ? .4f : .14f);
                key.stripe.color = assigned.HasValue ? c : Color.clear;
                key.action.text = assigned.HasValue ? ShortName(assigned.Value) + (alias ? "*" : "") : key.key == KeyCode.Escape ? "메뉴" : key.key == KeyCode.Return || key.key == KeyCode.KeypadEnter ? "확인" : key.key == KeyCode.Backspace ? "취소" : "";
            }
            foreach (var v in actions)
            {
                v.bg.color = listening == v.action ? new Color32(47, 82, 112, 255) : UiTheme.RowDark;
                var c = (Color32)Category(v.action);
                v.text.text = $"<color=#{c.r:x2}{c.g:x2}{c.b:x2}>●</color> {ActionName(v.action)}    <color=#d6eaff>{(listening == v.action ? "새 키 입력…" : InputReader.KeyLabel(InputReader.KeyboardKey(v.action)))}</color>";
            }
        }
        void Update()
        {
            if (Time.frameCount <= shownFrame + 1 || Time.frameCount <= listenFrame + 1) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (listening.HasValue) { listening = null; RefreshKeyboard("키 선택을 취소했습니다."); }
                else ui.Pop();
                return;
            }
            if (!listening.HasValue) return;
            foreach (var key in keyCodes) if (key > KeyCode.None && key < KeyCode.Mouse0 && Input.GetKeyDown(key)) { AssignKey(key); return; }
        }
    }
}
