using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Base for full-screen menus managed by <see cref="UIRoot"/>'s menu stack.</summary>
    public abstract class MenuScreen : MonoBehaviour
    {
        protected MenuList menu;
        protected RectTransform panel;
        protected int shownFrame = -1;

        public virtual void Show()
        {
            shownFrame = Time.frameCount;
            gameObject.SetActive(true);
            if (fitToScreen) FitToScreen();
            menu?.ResetSelection();
        }

        // [UI] BuildPanel panels shrink to fit the canvas (the settings list is taller than 720, and the
        // bigger UI sizes shrink the canvas to 1280/k x 720/k).
        bool fitToScreen;

        protected void FitToScreen()
        {
            if (panel == null) return;
            Vector2 canvas = UIFactory.ReferenceResolution;
            var scaler = GetComponentInParent<CanvasScaler>();
            if (scaler != null && Screen.width > 0 && Screen.height > 0)
            {
                var r = scaler.referenceResolution;
                float sf = Mathf.Min(Screen.width / r.x, Screen.height / r.y); // ScreenMatchMode.Expand
                canvas = new Vector2(Screen.width / sf, Screen.height / sf);
            }
            var size = panel.sizeDelta;
            float k = Mathf.Min(1f, Mathf.Min((canvas.x - 24f) / Mathf.Max(1f, size.x), (canvas.y - 24f) / Mathf.Max(1f, size.y)));
            panel.localScale = new Vector3(k, k, 1f);
        }

        public virtual void Hide() => gameObject.SetActive(false);

        // [ENH] Back on top after an overlay closed: ignore this frame's input (the key that answered the overlay).
        public void MarkShown() => shownFrame = Time.frameCount;

        protected static RectTransform CreateRoot(Transform canvas, string name, bool dim)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(canvas, name));
            if (dim)
            {
                var overlay = UIFactory.Overlay(root, "Dim", new Color(0.05f, 0.04f, 0.08f, 0.6f));
                overlay.raycastTarget = true;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>Cream panel with a title and a menu under it; sizes itself to the content.</summary>
        protected void BuildPanel(RectTransform root, string title, float width, string body = null, int bodySize = 20)
        {
            panel = UIFactory.Place(UIFactory.Rect(root, "Panel"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 200));
            fitToScreen = true;
            // [UI] Dark panel like every other window (the cream panel mixed two themes); see UiTheme.
            var fill = UIFactory.Image(panel, "Fill", Game.Art.Get("ui_white"), UiTheme.PanelDeep); // opaque: nothing shows through
            fill.preserveAspect = false;
            UIFactory.Stretch(fill.rectTransform, 6f, 6f, 6f, 6f);
            var bg = UIFactory.Panel(panel, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);

            float y = -28f;
            var titleText = UIFactory.Text(panel, "Title", title, 34, UiTheme.Accent, TextAnchor.UpperCenter, true);
            UIFactory.Place(titleText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width - 40, 46));
            y -= 58f;

            if (!string.IsNullOrEmpty(body))
            {
                var bodyText = UIFactory.Text(panel, "Body", body, UiTheme.Size(bodySize), UiTheme.TextSecondary, TextAnchor.UpperCenter, true);
                bodyText.lineSpacing = 1.2f;
                // preferredHeight needs a layout pass; estimate from the line count instead.
                int lines = body.Split('\n').Length;
                float h = lines * bodySize * 1.45f + 8f;
                UIFactory.Place(bodyText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width - 70, h));
                y -= h + 10f;
            }

            menu = MenuList.Create(panel, "Menu", width - 80, 46, 24, true);
            UIFactory.Place(menu.RectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width - 80, 10));
            menuTop = -y;
        }

        float menuTop;

        /// <summary>Call after adding menu items so the panel wraps them.</summary>
        protected void FitPanel()
        {
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, menuTop + menu.Height + 34f);
        }
    }

    public class TitleScreen : MenuScreen
    {
        public static TitleScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Title", false);
            var screen = root.gameObject.AddComponent<TitleScreen>();

            // Soft vignette so the drifting village stays visible behind the logo.
            var shade = UIFactory.Overlay(root, "Shade", new Color(0.05f, 0.08f, 0.05f, 0.35f));
            shade.raycastTarget = true;

            // [E4] Generated pixel logo (Art/ui_logo); the text logo stays as a fallback.
            var logoSprite = Game.Art.Get("ui_logo");
            if (logoSprite != null && logoSprite.texture != null && logoSprite.texture.width > 100)
            {
                var logoImg = UIFactory.Image(root, "Logo", logoSprite, Color.white);
                UIFactory.Place(logoImg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(560, 141));
            }
            else
            {
                var logo = UIFactory.Text(root, "Logo", "dotRPG", 96, UIColors.Cream, TextAnchor.MiddleCenter, true);
                var logoOutline = logo.gameObject.AddComponent<Outline>();
                logoOutline.effectColor = new Color32(62, 39, 26, 255);
                logoOutline.effectDistance = new Vector2(4, -4);
                UIFactory.Place(logo.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(900, 120));
            }
            var sub = UIFactory.Text(root, "Subtitle", L.T("해골 숲 옆 작은 마을"), 30, UIColors.Highlight, TextAnchor.MiddleCenter, true);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -186), new Vector2(900, 44));

            screen.panel = UIFactory.Place(UIFactory.Rect(root, "Panel"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(380, 300));
            var fill = UIFactory.Image(screen.panel, "Fill", Game.Art.Get("ui_white"), UiTheme.PanelDeep);
            fill.preserveAspect = false;
            UIFactory.Stretch(fill.rectTransform, 6f, 6f, 6f, 6f);
            var bg = UIFactory.Panel(screen.panel, "Bg", true); // [UI] dark theme
            UIFactory.Stretch(bg.rectTransform);
            screen.menu = MenuList.Create(screen.panel, "Menu", 320, 46, 24, true);
            UIFactory.Place(screen.menu.RectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(320, 10));

            // [I] Both go through the save-slot picker (3 slots).
            // Online RPG: the only way in is the online login (offline new game / continue are not offered).
            screen.menu.AddButton(L.T("게임 시작"), () =>
            {
                if (GameFlow.DemoSlots) { ui.Slots.Open(true); return; } // [DEMO] trial characters are local slots
                ui.Push(OnlineSession.Current != null ? (MenuScreen)ui.OnlineCharacters : ui.OnlineLogin);
            });
            screen.menu.AddButton(L.T("설정"), () => ui.Push(ui.Settings));
            if (!Application.isMobilePlatform) screen.menu.AddButton(L.T("키보드 설정"), () => ui.Push(ui.KeyBind));
            screen.menu.AddButton(L.T("게임 종료"), () => Game.Flow.QuitGame());
            screen.panel.sizeDelta = new Vector2(380, screen.menu.Height + 48);

            var footer = UIFactory.Text(root, "Footer", L.F("버전 {0}", Application.version), UiTheme.FontMin, // [UI] no "prototype" disclaimer on the title screen
                new Color(1, 1, 1, 0.7f), TextAnchor.LowerRight, true);
            UIFactory.Place(footer.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 10), new Vector2(900, 26));
            return screen;
        }
    }

    public class PauseScreen : MenuScreen
    {
        UIRoot ui;

        public override void Show()
        {
            base.Show();
            panel.Find("Title").GetComponent<Text>().text = Game.IsOnlineWorld ? L.T("메뉴 · 온라인 진행 중") : L.T("일시정지");
        }

        public static PauseScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Pause", true);
            var screen = root.gameObject.AddComponent<PauseScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("일시정지"), 420);
            screen.menu.AddButton(L.T("계속하기"), () => Game.Flow.Resume());
            screen.menu.AddButton(L.T("가방 · 장비"), () => Game.Flow.OpenWindow(null));
            screen.menu.AddButton(L.T("설정"), () => ui.Push(ui.Settings));
            if (!Application.isMobilePlatform) screen.menu.AddButton(L.T("키보드 설정"), () => ui.Push(ui.KeyBind));
            screen.menu.AddButton(L.T("도움말"), () => ui.Push(ui.Help)); // [E5]
            if (GameFlow.DemoSlots) // [DEMO] the flag is set from the command line before the UI is built
                screen.menu.AddButton(L.T("캐릭터 변경"), () => ui.Confirm(L.T("다른 체험 캐릭터로 바꿀까요?\n진행 상황은 자동으로 저장됩니다."), () => Game.Flow.SwitchCharacter()));
            screen.menu.AddButton(L.T("타이틀로"), () => ui.Confirm(L.T("타이틀로 돌아갈까요?\n진행 상황은 자동으로 저장됩니다."), () => Game.Flow.ReturnToTitle()));
            screen.menu.AddButton(L.T("게임 종료"), () => ui.Confirm(L.T("게임을 종료할까요?\n진행 상황은 자동으로 저장됩니다."), () => Game.Flow.QuitGame(), keepOpenOnYes: true));
            screen.menu.OnCancel = () => Game.Flow.Resume();
            screen.FitPanel();
            return screen;
        }

        void Update()
        {
            // Start/Esc toggles the pause menu closed (only when this is the top screen).
            if (Game.Input.PausePressed && !Game.State.ChangedThisFrame && Time.frameCount != shownFrame && ui.Top == this)
                Game.Flow.Resume();
        }
    }

    public class SettingsScreen : MenuScreen
    {
        List<Vector2Int> resolutions;
        UIRoot ui;
        int keepRow = -1; // row to return to after the keep-display question

        public static SettingsScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Settings", true);
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            screen.ui = ui;
            string note = Application.isEditor ? L.T("에디터에서는 해상도/화면 모드가 빌드에서만 적용됩니다.") : L.T("변경 사항은 즉시 적용되고 자동 저장됩니다.");
            screen.BuildPanel(root, L.T("설정"), 640, note, 16);
            var s = Game.Settings;
            var menu = screen.menu;

            menu.AddOption(L.T("전체 음량"), () => Percent(s.Data.masterVolume), d => { s.Data.masterVolume = Step(s.Data.masterVolume, d); s.Apply(); });
            menu.AddOption(L.T("배경음"), () => Percent(s.Data.musicVolume), d => { s.Data.musicVolume = Step(s.Data.musicVolume, d); s.Apply(); });
            menu.AddOption(L.T("효과음"), () => Percent(s.Data.sfxVolume), d => { s.Data.sfxVolume = Step(s.Data.sfxVolume, d); s.Apply(); });
            if (!SettingsManager.HideDisplayOptions)
            {
                menu.AddOption(L.T("해상도"), () => SettingsManager.ResolutionLabel(new Vector2Int(s.Data.resolutionWidth, s.Data.resolutionHeight)), d =>
                {
                    screen.resolutions ??= SettingsManager.GetResolutionOptions();
                    int current = screen.resolutions.IndexOf(new Vector2Int(s.Data.resolutionWidth, s.Data.resolutionHeight));
                    int next = ((Mathf.Max(0, current) + d) % screen.resolutions.Count + screen.resolutions.Count) % screen.resolutions.Count;
                    screen.ChangeDisplay(() => { s.Data.resolutionWidth = screen.resolutions[next].x; s.Data.resolutionHeight = screen.resolutions[next].y; });
                });
                menu.AddOption(L.T("화면 모드"), () => SettingsManager.WindowModeLabel(s.Data.windowMode), d =>
                {
                    int count = Enum.GetValues(typeof(WindowMode)).Length;
                    screen.ChangeDisplay(() => s.Data.windowMode = (WindowMode)((((int)s.Data.windowMode + d) % count + count) % count));
                });
                menu.AddOption(L.T("수직 동기화"), () => s.Data.vSync ? L.T("켜기") : L.T("끄기"), d => { s.Data.vSync = !s.Data.vSync; s.Apply(); });
            }
            // [I18N] Bilingual label so a player in the wrong language can still find it. Applies on restart.
            menu.AddOption("언어 / Language", () => L.NativeName(s.Data.language), d => // i18n-ignore bilingual on purpose
            {
                s.Data.language = L.Next(s.Data.language, d < 0 ? -1 : 1);
                s.Save();
                GameEvents.RaiseToast(s.Data.language == L.Korean ? "다시 시작하면 적용됩니다. / Restart the game to apply." : "Restart the game to apply. / 다시 시작하면 적용됩니다."); // i18n-ignore bilingual on purpose
            });
            menu.AddOption(L.T("화면 흔들림"), () => s.Data.screenShake ? L.T("켜기") : L.T("끄기"), d => { s.Data.screenShake = !s.Data.screenShake; s.Apply(); });
            // [I] Accessibility and keys.
            menu.AddOption(L.T("글자·UI 크기"), () => UiTheme.UiScaleNames[Mathf.Clamp(s.Data.uiScale, 0, 3)], d => { s.Data.uiScale = Mathf.Clamp(s.Data.uiScale + d, 0, 3); s.Apply(); screen.FitToScreen(); });
            menu.AddOption(L.T("색약 보정"), () => s.Data.colorBlind ? L.T("켜기") : L.T("끄기"), d => { s.Data.colorBlind = !s.Data.colorBlind; s.Apply(); });
            // Loot filter: what is left on the ground (gold and higher gear are always picked up).
            menu.AddButton(L.T("줍기 설정"), () => ui.Push(ui.LootFilter)); // its own screen: the list stays within the screen height
            menu.AddButton(L.T("약관 및 정책"), () => ui.Push(ui.Policies)); // [POLICY]
            if (!Application.isMobilePlatform) menu.AddButton(L.T("키보드 설정"), () => ui.Push(ui.KeyBind));
            menu.AddButton(L.T("돌아가기"), () => screen.Close());
            menu.OnCancel = screen.Close;
            screen.FitPanel();
            return screen;
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        // [UX] Resolution / window mode: apply, then keep only when confirmed within 10 s (else back to the old values).
        const float KeepDisplaySeconds = 10f;

        void ChangeDisplay(Action change)
        {
            var d = Game.Settings.Data;
            int oldW = d.resolutionWidth, oldH = d.resolutionHeight;
            var oldMode = d.windowMode;
            change();
            if (d.resolutionWidth == oldW && d.resolutionHeight == oldH && d.windowMode == oldMode) return;
            Game.Settings.Apply();
            // Not an overlay: this screen's menu list would still read the keys under the dialog.
            keepRow = menu.Selected;
            ui.Confirm(L.T("이 설정을 유지할까요? ({초}초 후 되돌림)"), () => Game.Settings.Save(), defaultYes: false, onNo: () =>
            {
                d.resolutionWidth = oldW; d.resolutionHeight = oldH; d.windowMode = oldMode;
                Game.Settings.Apply();
                menu.Refresh();
            }, timeoutSeconds: KeepDisplaySeconds);
        }

        public override void Show()
        {
            base.Show();
            if (keepRow >= 0) { menu.SetSelectedSilently(keepRow); keepRow = -1; }
        }

        static float Step(float value, int direction) => Mathf.Clamp01(Mathf.Round((value + direction * 0.1f) * 10f) / 10f);

        void Close()
        {
            Game.Settings.Save();
            ui.Pop();
        }
    }

    public class ControlsScreen : MenuScreen
    {
        public static ControlsScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Controls", true);
            var screen = root.gameObject.AddComponent<ControlsScreen>();
            screen.BuildPanel(root, L.T("조작 안내"), 700, BuildBody(), 20);
            screen.menu.AddButton(L.T("돌아가기"), () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        static string BuildBody()
        {
            var input = Game.Input;
            string Row(string name, GameAction action) =>
                L.F("{0}   키보드 <b>{1}</b>   ·   게임패드 <b>{2}</b>", name, input.GetBindingLabel(action, false), input.GetBindingLabel(action, true));
            string Pair(string name, GameAction a, GameAction b) =>
                L.F("{0}   키보드 <b>{1} / {2}</b>   ·   게임패드 <b>{3} / {4}</b>", name, input.GetBindingLabel(a, false), input.GetBindingLabel(b, false), input.GetBindingLabel(a, true), input.GetBindingLabel(b, true));
            return string.Join("\n", new[]
            {
                Row(L.T("이동"), GameAction.Move),
                Row(L.T("공격"), GameAction.Attack),
                Row(L.T("이동기 (대시 / 텔레포트)"), GameAction.Mobility),
                Row(L.T("대화 / 상호작용"), GameAction.Interact),
                Row(L.T("체력 물약 (없으면 당근)"), GameAction.UseItem),
                Row(L.T("마나 물약"), GameAction.UseMana),
                Row(L.T("마을 귀환 주문서"), GameAction.TownScroll),
                Row(L.T("가방 · 장비"), GameAction.Inventory),
                Pair(L.T("스킬 1 / 2"), GameAction.Skill1, GameAction.Skill2),
                Pair(L.T("스킬 3 / 4"), GameAction.Skill3, GameAction.Skill4),
                Pair(L.T("스킬 5 / 6"), GameAction.Skill6, GameAction.Skill7),
                Row(L.T("스킬 7"), GameAction.Skill8),
                Row(L.T("각성 기술"), GameAction.Skill5),
                Row(L.T("메뉴 / 일시정지"), GameAction.Pause),
                L.T("단축키는 메뉴의 키보드 설정에서 변경할 수 있습니다."),
            });
        }
    }

    public class GameOverScreen : MenuScreen
    {
        MenuList.Item reviveHere;

        public static GameOverScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "GameOver", true);
            var screen = root.gameObject.AddComponent<GameOverScreen>();
            screen.BuildPanel(root, L.T("쓰러졌습니다..."), 520, L.T("그 자리에서 일어나거나, 마을에서 다시 시작할 수 있습니다.\n가진 물건과 의뢰 진행은 그대로입니다."), 18);
            // [REVIVE] Where you fell: free up to Lv.10, then one revive coin (one a day).
            screen.reviveHere = screen.menu.AddButton(ReviveCoins.FieldLabel, () => Game.Flow.ReviveHere(), () => !ReviveCoins.Busy); // [REVIVE] fields: always free
            ReviveCoins.Changed += screen.RefreshRevive;
            screen.menu.AddButton(L.T("마을에서 다시 일어나기"), () => Game.Flow.RespawnInVillage());
            // [UX] Returning to the title drops progress since the last save: ask first.
            screen.menu.AddButton(L.T("타이틀로"), () => ui.Confirm(L.T("타이틀로 돌아갈까요?\n<size=18>마지막 저장 이후 진행은 사라집니다.</size>"), () => Game.Flow.ReturnToTitle()));
            screen.FitPanel();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            RefreshRevive();
            ReviveCoins.Refresh();
        }

        void RefreshRevive()
        {
            if (reviveHere != null && reviveHere.labelText != null) reviveHere.labelText.text = ReviveCoins.FieldLabel;
        }

        void OnDestroy() => ReviveCoins.Changed -= RefreshRevive;
    }

    public class EndingScreen : MenuScreen
    {
        Text statsText;

        public static EndingScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Ending", true);
            var screen = root.gameObject.AddComponent<EndingScreen>();
            screen.BuildPanel(root, L.T("의뢰 완료!"), 620, "\n\n\n", 20);
            screen.statsText = screen.panel.Find("Body").GetComponent<Text>();
            screen.menu.AddButton(L.T("계속 탐험하기"), () => Game.Flow.Resume());
            screen.menu.AddButton(L.T("타이틀로"), () => Game.Flow.ReturnToTitle());
            screen.FitPanel();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            var p = Game.Session.Quest;
            int seconds = Mathf.FloorToInt(Game.Session.PlayTimeSeconds);
            statsText.text =
                L.T("새 공방이 세워지고 마을에 활기가 돌아왔습니다.") + "\n" +
                L.F("플레이 시간 {0:00}:{1:00}   ·   해골 퇴치 {2}", seconds / 60, seconds % 60, p.skeletonsDefeated) + "\n" +
                L.F("보상: 최대 HP +{0}", Game.Quest.Config.rewardMaxHealth) + "\n" +
                L.T("세로 슬라이스를 플레이해 주셔서 감사합니다!");
        }
    }


}
