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
            menu?.ResetSelection();
        }

        public virtual void Hide() => gameObject.SetActive(false);

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
            var bg = UIFactory.Panel(panel, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);

            float y = -28f;
            var titleText = UIFactory.Text(panel, "Title", title, 34, UIColors.Ink, TextAnchor.UpperCenter);
            UIFactory.Place(titleText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width - 40, 46));
            y -= 58f;

            if (!string.IsNullOrEmpty(body))
            {
                var bodyText = UIFactory.Text(panel, "Body", body, bodySize, UIColors.InkSoft, TextAnchor.UpperCenter);
                bodyText.lineSpacing = 1.2f;
                // preferredHeight needs a layout pass; estimate from the line count instead.
                int lines = body.Split('\n').Length;
                float h = lines * bodySize * 1.45f + 8f;
                UIFactory.Place(bodyText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width - 70, h));
                y -= h + 10f;
            }

            menu = MenuList.Create(panel, "Menu", width - 80, 46, 24, false);
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

            var logo = UIFactory.Text(root, "Logo", "dotRPG", 96, UIColors.Cream, TextAnchor.MiddleCenter, true);
            var logoOutline = logo.gameObject.AddComponent<Outline>();
            logoOutline.effectColor = new Color32(62, 39, 26, 255);
            logoOutline.effectDistance = new Vector2(4, -4);
            UIFactory.Place(logo.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(900, 120));
            var sub = UIFactory.Text(root, "Subtitle", "해골 숲 옆 작은 마을", 30, UIColors.Highlight, TextAnchor.MiddleCenter, true);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -186), new Vector2(900, 44));

            screen.panel = UIFactory.Place(UIFactory.Rect(root, "Panel"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(380, 300));
            var bg = UIFactory.Panel(screen.panel, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);
            screen.menu = MenuList.Create(screen.panel, "Menu", 320, 46, 24, false);
            UIFactory.Place(screen.menu.RectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(320, 10));

            screen.menu.AddButton("새 게임", () =>
            {
                if (Game.Saves.HasSave())
                    ui.Confirm("새 게임을 시작할까요?\n다음 자동 저장 때 기존 저장 데이터를 덮어씁니다.", () => ui.Push(ui.CharacterSelect));
                else ui.Push(ui.CharacterSelect);
            });
            screen.menu.AddButton("이어하기", () => Game.Flow.ContinueGame(), () => Game.Saves.HasSave());
            screen.menu.AddButton("설정", () => ui.Push(ui.Settings));
            screen.menu.AddButton("조작 방법", () => ui.Push(ui.Controls));
            screen.menu.AddButton("게임 종료", () => Game.Flow.QuitGame());
            screen.panel.sizeDelta = new Vector2(380, screen.menu.Height + 48);

            var footer = UIFactory.Text(root, "Footer", $"v{Application.version}  ·  프로토타입 (임시 그래픽/사운드는 모두 코드로 자체 생성)", 16,
                new Color(1, 1, 1, 0.7f), TextAnchor.LowerRight, true);
            UIFactory.Place(footer.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 10), new Vector2(900, 26));
            return screen;
        }
    }

    public class PauseScreen : MenuScreen
    {
        UIRoot ui;

        public static PauseScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Pause", true);
            var screen = root.gameObject.AddComponent<PauseScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "일시정지", 420);
            screen.menu.AddButton("계속하기", () => Game.Flow.Resume());
            screen.menu.AddButton("가방 · 장비", () => Game.Flow.OpenWindow(null));
            screen.menu.AddButton("저장하기", () => Game.Flow.SaveGame());
            screen.menu.AddButton("설정", () => ui.Push(ui.Settings));
            screen.menu.AddButton("조작 방법", () => ui.Push(ui.Controls));
            screen.menu.AddButton("타이틀로", () => ui.Confirm("타이틀로 돌아갈까요?\n저장하지 않은 진행은 사라집니다.", () => Game.Flow.ReturnToTitle()));
            screen.menu.AddButton("게임 종료", () => ui.Confirm("게임을 종료할까요?\n저장하지 않은 진행은 사라집니다.", () => Game.Flow.QuitGame()));
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

        public static SettingsScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Settings", true);
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            screen.ui = ui;
            string note = Application.isEditor ? "에디터에서는 해상도/화면 모드가 빌드에서만 적용됩니다." : "변경 사항은 즉시 적용되고 자동 저장됩니다.";
            screen.BuildPanel(root, "설정", 640, note, 16);
            var s = Game.Settings;
            var menu = screen.menu;

            menu.AddOption("전체 음량", () => Percent(s.Data.masterVolume), d => { s.Data.masterVolume = Step(s.Data.masterVolume, d); s.Apply(); });
            menu.AddOption("배경음", () => Percent(s.Data.musicVolume), d => { s.Data.musicVolume = Step(s.Data.musicVolume, d); s.Apply(); });
            menu.AddOption("효과음", () => Percent(s.Data.sfxVolume), d => { s.Data.sfxVolume = Step(s.Data.sfxVolume, d); s.Apply(); });
            menu.AddOption("해상도", () => SettingsManager.ResolutionLabel(new Vector2Int(s.Data.resolutionWidth, s.Data.resolutionHeight)), d =>
            {
                screen.resolutions ??= SettingsManager.GetResolutionOptions();
                int current = screen.resolutions.IndexOf(new Vector2Int(s.Data.resolutionWidth, s.Data.resolutionHeight));
                int next = ((Mathf.Max(0, current) + d) % screen.resolutions.Count + screen.resolutions.Count) % screen.resolutions.Count;
                s.Data.resolutionWidth = screen.resolutions[next].x;
                s.Data.resolutionHeight = screen.resolutions[next].y;
                s.Apply();
            });
            menu.AddOption("화면 모드", () => SettingsManager.WindowModeLabel(s.Data.windowMode), d =>
            {
                int count = Enum.GetValues(typeof(WindowMode)).Length;
                s.Data.windowMode = (WindowMode)((((int)s.Data.windowMode + d) % count + count) % count);
                s.Apply();
            });
            menu.AddOption("수직 동기화", () => s.Data.vSync ? "켜기" : "끄기", d => { s.Data.vSync = !s.Data.vSync; s.Apply(); });
            menu.AddOption("화면 흔들림", () => s.Data.screenShake ? "켜기" : "끄기", d => { s.Data.screenShake = !s.Data.screenShake; s.Apply(); });
            menu.AddButton("돌아가기", () => screen.Close());
            menu.OnCancel = screen.Close;
            screen.FitPanel();
            return screen;
        }

        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

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
            screen.BuildPanel(root, "조작 방법", 700, BuildBody(), 20);
            screen.menu.AddButton("돌아가기", () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        static string BuildBody()
        {
            var input = Game.Input;
            string Row(string name, GameAction action) =>
                $"{name}   키보드 <b>{input.GetBindingLabel(action, false)}</b>   ·   게임패드 <b>{input.GetBindingLabel(action, true)}</b>";
            string Pair(string name, GameAction a, GameAction b) =>
                $"{name}   키보드 <b>{input.GetBindingLabel(a, false)} / {input.GetBindingLabel(b, false)}</b>   ·   게임패드 <b>{input.GetBindingLabel(a, true)} / {input.GetBindingLabel(b, true)}</b>";
            return string.Join("\n", new[]
            {
                Row("이동", GameAction.Move),
                Row("공격 / 채집", GameAction.Attack),
                Row("대화 / 상호작용", GameAction.Interact),
                Row("체력 물약 (없으면 당근)", GameAction.UseItem),
                Row("마나 물약", GameAction.UseMana),
                Row("마을 귀환 주문서", GameAction.TownScroll),
                Row("가방 · 장비", GameAction.Inventory),
                Pair("스킬 1 / 2", GameAction.Skill1, GameAction.Skill2),
                Pair("스킬 3 / 4", GameAction.Skill3, GameAction.Skill4),
                Row("각성 기술", GameAction.Skill5),
                Row("메뉴 / 일시정지", GameAction.Pause),
                "키 변경 기능은 다음 단계에서 추가 예정입니다 (저장 구조는 준비됨).",
            });
        }
    }

    public class GameOverScreen : MenuScreen
    {
        public static GameOverScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "GameOver", true);
            var screen = root.gameObject.AddComponent<GameOverScreen>();
            screen.BuildPanel(root, "쓰러졌다...", 460, "마을 사람들이 당신을 집까지 데려다 주었다.\n가진 물건과 의뢰 진행은 그대로다.", 18);
            screen.menu.AddButton("마을에서 다시 일어나기", () => Game.Flow.RespawnInVillage());
            screen.menu.AddButton("마지막 저장 불러오기", () => Game.Flow.ContinueGame(), () => Game.Saves.HasSave());
            screen.menu.AddButton("타이틀로", () => Game.Flow.ReturnToTitle());
            screen.FitPanel();
            return screen;
        }
    }

    public class EndingScreen : MenuScreen
    {
        Text statsText;

        public static EndingScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Ending", true);
            var screen = root.gameObject.AddComponent<EndingScreen>();
            screen.BuildPanel(root, "의뢰 완료!", 620, "\n\n\n", 20);
            screen.statsText = screen.panel.Find("Body").GetComponent<Text>();
            screen.menu.AddButton("계속 탐험하기", () => Game.Flow.Resume());
            screen.menu.AddButton("타이틀로", () => Game.Flow.ReturnToTitle());
            screen.FitPanel();
            return screen;
        }

        public override void Show()
        {
            base.Show();
            var p = Game.Session.Quest;
            int seconds = Mathf.FloorToInt(Game.Session.PlayTimeSeconds);
            statsText.text =
                $"새 공방이 세워지고 마을에 활기가 돌아왔다.\n" +
                $"플레이 시간 {seconds / 60:00}:{seconds % 60:00}   ·   해골 퇴치 {p.skeletonsDefeated}\n" +
                $"보상: 최대 HP +{Game.Quest.Config.rewardMaxHealth}\n" +
                "세로 슬라이스를 플레이해 주셔서 감사합니다!";
        }
    }

    public class ConfirmScreen : MenuScreen
    {
        Text message;
        Action onYes;
        UIRoot ui;

        public static ConfirmScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Confirm", true);
            var screen = root.gameObject.AddComponent<ConfirmScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "확인", 560, "\n", 20);
            screen.message = screen.panel.Find("Body").GetComponent<Text>();
            screen.menu.AddButton("예", () =>
            {
                var action = screen.onYes;
                screen.ui.Pop();
                action?.Invoke();
            });
            screen.menu.AddButton("아니오", () => screen.ui.Pop());
            screen.menu.OnCancel = () => screen.ui.Pop();
            screen.FitPanel();
            return screen;
        }

        public void Setup(string text, Action yes)
        {
            message.text = text;
            onYes = yes;
        }

        public override void Show()
        {
            base.Show();
            // Default to "아니오" for destructive questions.
            menu.Select(1);
        }
    }
}
