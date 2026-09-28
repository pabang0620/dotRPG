using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace DotRPG
{
    /// <summary>
    /// Owns the UI canvas and decides which screen is visible for each <see cref="GameState"/>.
    /// Menus are kept on a stack so sub-screens (settings, controls, confirm) return to whoever
    /// opened them — title or pause menu.
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        public HudView Hud { get; private set; }
        public DialogueBoxView DialogueBox { get; private set; }
        public ScreenFader Fader { get; private set; }
        public TitleScreen Title { get; private set; }
        public PauseScreen Pause { get; private set; }
        public SettingsScreen Settings { get; private set; }
        public ControlsScreen Controls { get; private set; }
        public GameOverScreen GameOver { get; private set; }
        public EndingScreen Ending { get; private set; }
        ConfirmScreen confirm;

        readonly List<MenuScreen> stack = new List<MenuScreen>();

        public MenuScreen Top => stack.Count > 0 ? stack[stack.Count - 1] : null;

        public static UIRoot Create(Transform parent)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UIFactory.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem(parent);

            var ui = go.AddComponent<UIRoot>();
            var t = go.transform;
            ui.Hud = HudView.Create(t);
            ui.DialogueBox = DialogueBoxView.Create(t);
            ui.Title = TitleScreen.Create(t, ui);
            ui.Pause = PauseScreen.Create(t, ui);
            ui.GameOver = GameOverScreen.Create(t, ui);
            ui.Ending = EndingScreen.Create(t, ui);
            ui.Settings = SettingsScreen.Create(t, ui);
            ui.Controls = ControlsScreen.Create(t, ui);
            ui.confirm = ConfirmScreen.Create(t, ui);
            ui.Fader = ScreenFader.Create(t);

            Game.State.Changed += ui.OnStateChanged;
            ui.OnStateChanged(Game.State.Current, Game.State.Current);
            return ui;
        }

        static void EnsureEventSystem(Transform parent)
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.transform.SetParent(parent, false);
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        void OnDestroy()
        {
            if (Game.State != null) Game.State.Changed -= OnStateChanged;
        }

        void OnStateChanged(GameState previous, GameState next)
        {
            bool inGame = next == GameState.Playing || next == GameState.Dialogue || next == GameState.Paused
                          || next == GameState.GameOver || next == GameState.Ending;
            Hud.gameObject.SetActive(inGame);
            DialogueBox.gameObject.SetActive(next == GameState.Dialogue || next == GameState.Paused);

            // Returning from pause to dialogue/playing just closes menus.
            ClearStack();
            switch (next)
            {
                case GameState.Title: Push(Title); break;
                case GameState.Paused: Push(Pause); break;
                case GameState.GameOver: Push(GameOver); break;
                case GameState.Ending: Push(Ending); break;
            }
        }

        public void Push(MenuScreen screen)
        {
            if (screen == null) return;
            Top?.Hide();
            stack.Add(screen);
            screen.Show();
        }

        public void Pop()
        {
            if (stack.Count == 0) return;
            var top = Top;
            stack.RemoveAt(stack.Count - 1);
            top.Hide();
            Top?.Show();
        }

        void ClearStack()
        {
            foreach (var s in stack) s.Hide();
            stack.Clear();
            // Make sure no stray screen stays open.
            foreach (var s in new MenuScreen[] { Title, Pause, Settings, Controls, GameOver, Ending, confirm })
                if (s != null) s.Hide();
        }

        public void Confirm(string message, Action onYes)
        {
            confirm.Setup(message, onYes);
            Push(confirm);
        }
    }
}
