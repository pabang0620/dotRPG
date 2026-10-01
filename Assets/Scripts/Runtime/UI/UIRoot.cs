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
        public CharacterSelectScreen CharacterSelect { get; private set; }
        public EquipmentScreen Equipment { get; private set; }
        public EnhanceScreen Enhance { get; private set; }
        public SkillScreen Skills { get; private set; }
        public WorldMapScreen WorldMap { get; private set; }
        public QuestScreen QuestLog { get; private set; }
        // [DUNGEON] 던전 선택 (weekday + raid tabs; Raid is the same window) and the result screen.
        public DungeonSelectScreen Dungeon { get; private set; }
        public DungeonSelectScreen Raid => Dungeon;
        public DungeonResultScreen DungeonResult { get; private set; }
        public ShopScreen Shop { get; private set; }
        public StorageScreen Storage { get; private set; }
        // [PARTY] 파티 window (mercenary roster).
        public PartyScreen Party { get; private set; }
        /// <summary>Window to show when the Inventory state starts (null = bag).</summary>
        public MenuScreen PendingWindow { get; set; }

        /// <summary>Opens the window of a town service NPC (general store, blacksmith, storage).</summary>
        public void OpenService(NpcDefinition npc)
        {
            switch (npc.service)
            {
                case NpcService.Shop:
                    Shop.SetKeeper(npc.displayName, npc.greeting);
                    Game.Flow.OpenWindow(Shop);
                    break;
                case NpcService.Blacksmith:
                    Enhance.SetKeeper(npc.displayName, npc.greeting);
                    Game.Audio.PlaySfx("hammer");
                    Game.Flow.OpenWindow(Enhance);
                    break;
                case NpcService.Storage:
                    Storage.SetKeeper(npc.displayName, npc.greeting);
                    Game.Flow.OpenWindow(Storage);
                    break;
                // [DUNGEON] 던전 안내원: the dungeon select window.
                case NpcService.Dungeon:
                    Dungeon.Open(false, npc.displayName, npc.greeting);
                    break;
            }
        }

        /// <summary>Swaps the open window without leaving the window state.</summary>
        public void ShowWindow(MenuScreen window)
        {
            ClearStack();
            Push(window != null ? window : Equipment);
        }
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
            // [UI] Expand: the 1280x720 reference always fits inside the canvas, so 16:10 (Steam Deck 1280x800)
            // and 21:9 only add space instead of shrinking the width (MatchWidthOrHeight 0.5 made the centred
            // boss bar overlap the status bars at 16:10). 16:9 resolutions scale exactly as before.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
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
            ui.CharacterSelect = CharacterSelectScreen.Create(t, ui);
            ui.Equipment = EquipmentScreen.Create(t, ui);
            ui.Enhance = EnhanceScreen.Create(t);
            ui.Skills = SkillScreen.Create(t);
            ui.WorldMap = WorldMapScreen.Create(t);
            ui.QuestLog = QuestScreen.Create(t);
            ui.Dungeon = DungeonSelectScreen.Create(t); // [DUNGEON] replaces the 미니던전 / 레이드 info windows
            ui.DungeonResult = DungeonResultScreen.Create(t); // [DUNGEON]
            ui.Shop = ShopScreen.Create(t);
            ui.Storage = StorageScreen.Create(t);
            ui.Party = PartyScreen.Create(t); // [PARTY]
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
                          || next == GameState.GameOver || next == GameState.Ending || next == GameState.Inventory;
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
                case GameState.Inventory: Push(PendingWindow != null ? PendingWindow : Equipment); break;
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
            // [ENH] An overlay never hid the screen under it: no Show() again, just no input this frame.
            if (top == overlay)
            {
                overlay = null;
                Top?.MarkShown();
                return;
            }
            Top?.Show();
        }

        // [ENH] Overlay push: the screen underneath stays visible (dimmed by the overlay) and ignores input while not on top.
        MenuScreen overlay;

        void PushOverlay(MenuScreen screen)
        {
            if (screen == null) return;
            if (overlay != null && Top == overlay) { Push(screen); return; }
            // A button the mouse selected under the dialog must not receive the dialog's Enter.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            stack.Add(screen);
            overlay = screen;
            screen.transform.SetAsLastSibling();
            if (Fader != null) Fader.transform.SetAsLastSibling();
            screen.Show();
        }

        void ClearStack()
        {
            overlay = null; // [ENH]
            foreach (var s in stack) s.Hide();
            stack.Clear();
            // Make sure no stray screen stays open.
            foreach (var s in new MenuScreen[] { Title, Pause, Settings, Controls, GameOver, Ending, CharacterSelect, Equipment, Enhance, Skills, WorldMap, QuestLog, Dungeon, Raid, Shop, Storage, confirm })
                if (s != null) s.Hide();
            if (Party != null) Party.Hide(); // [PARTY]
            if (DungeonResult != null) DungeonResult.Hide(); // [DUNGEON]
        }

        /// <summary>
        /// Yes/no question. <paramref name="overlay"/> = drawn over the current screen, which stays visible
        /// (dimmed) and gets no input until the answer; otherwise the current screen is hidden meanwhile.
        /// </summary>
        public void Confirm(string message, Action onYes, bool overlay = false)
        {
            // [ENH] overlay option (wider dialog for the longer item questions)
            confirm.Setup(message, onYes, overlay ? ConfirmScreen.WideWidth : ConfirmScreen.DefaultWidth);
            if (overlay) PushOverlay(confirm);
            else Push(confirm);
        }

        /// <summary>The yes/no dialog (for automated checks).</summary>
        public ConfirmScreen ConfirmDialog => confirm;
    }
}
