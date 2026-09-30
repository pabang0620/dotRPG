using System;
using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-level flow: title → new game / continue → play ↔ pause → game over / ending → title / quit.
    /// UI buttons call these methods; nothing else changes the top-level state except dialogue.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        GameState stateBeforePause = GameState.Playing;
        bool transitioning;

        public bool IsTransitioning => transitioning;

        void Update()
        {
            if (transitioning || Game.State.ChangedThisFrame) return;
            var state = Game.State.Current;
            if ((state == GameState.Playing || state == GameState.Dialogue) && Game.Input.PausePressed) Pause();
            else if (state == GameState.Playing && Game.Input.InventoryPressed) OpenInventory();
        }

        public void OpenInventory() => OpenWindow(null);

        /// <summary>Opens a full-screen window (bag, skills, map, quest, dungeon, raid, enhance); the world freezes.</summary>
        public void OpenWindow(MenuScreen window)
        {
            var state = Game.State.Current;
            if (transitioning || (state != GameState.Playing && state != GameState.Paused && state != GameState.Inventory)) return;
            Game.UI.PendingWindow = window;
            if (state == GameState.Inventory) Game.UI.ShowWindow(window);
            else Game.State.Set(GameState.Inventory);
            Game.Audio.PlaySfx("select");
        }

        public void CloseInventory()
        {
            if (Game.State.Current == GameState.Inventory) Game.State.Set(GameState.Playing);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Common PC courtesy: alt-tabbing out pauses the game (not in the editor, where it gets in the way).
            // Automated capture runs (they redirect saves) keep playing so several can run side by side.
            if (!hasFocus && !Application.isEditor && SaveSystem.DirectoryOverride == null && Game.State != null &&
                (Game.State.Current == GameState.Playing || Game.State.Current == GameState.Dialogue))
                Pause();
        }

        public void Pause()
        {
            var state = Game.State.Current;
            if (state != GameState.Playing && state != GameState.Dialogue) return;
            stateBeforePause = state;
            Game.State.Set(GameState.Paused);
            Game.Audio.PlaySfx("select");
        }

        public void Resume()
        {
            var state = Game.State.Current;
            if (state == GameState.Paused) Game.State.Set(Game.Dialogue.IsOpen ? stateBeforePause : GameState.Playing);
            else if (state == GameState.Ending) Game.State.Set(GameState.Playing);
        }

        public void NewGame(CharacterClass playerClass = CharacterClass.Warrior)
        {
            StartCoroutine(Transition(() =>
            {
                Game.Session.ResetForNewGame(Game.Config, playerClass);
                EnterWorld();
                Game.State.Set(GameState.Playing);
                GameEvents.RaiseToast($"촌장 모리에게 말을 걸어 보자  [{Game.Input.GetBindingLabel(GameAction.Interact)}]");
            }));
        }

        public void ContinueGame()
        {
            var data = Game.Saves.Read();
            if (data == null)
            {
                GameEvents.RaiseToast("저장 데이터를 불러올 수 없습니다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            StartCoroutine(Transition(() =>
            {
                Game.Session.Restore(data, Game.Config);
                EnterWorld();
                Game.State.Set(GameState.Playing);
                GameEvents.RaiseToast("저장된 지점에서 이어합니다.");
                // [ENH] One-time note for saves converted to per-piece enhancement (v3 → v4).
                if (data.enhanceCompensation > 0)
                {
                    // Two toasts: one line is wider than the toast column.
                    GameEvents.RaiseToast("강화 규칙이 던전앤파이터 기준으로 바뀌었습니다.");
                    GameEvents.RaiseToast($"보상으로 장비 보호권 {data.enhanceCompensation}장을 받았다.");
                }
            }));
        }

        void EnterWorld()
        {
            Game.Dialogue.Abort();
            var session = Game.Session;
            Game.World.Load(session.MapId);
            var player = Game.Player;
            player.gameObject.SetActive(true);
            player.SetClass(session.PlayerClass);
            Vector2 start = session.StartPosition ?? Game.World.PlayerSpawn;
            // A saved spot that is now blocked (the map changed in an update) falls back to the start point.
            if (session.StartPosition.HasValue && !Game.World.IsFree(start)) start = Game.World.PlayerSpawn;
            player.Spawn(start, session.StartFacing, session.PlayerHealth, session.PlayerMaxHealth);
            Game.Camera.SetTarget(player.transform, true);
            Game.Quest.NotifyChanged();
            Game.UI.Hud.ClearToasts();
            Game.UI.Hud.RefreshAll();
            Game.Audio.PlayMusic(Game.World.Map.music);
        }

        /// <summary>Walks the player through a map portal to another map (fade out → rebuild → fade in).</summary>
        public void TravelTo(string mapId) => TravelTo(mapId, false);

        /// <summary>
        /// Moves the player to another map. <paramref name="arriveAtSpawn"/> = appear at the map's start
        /// point (the village square for the return scroll) instead of at the portal leading back.
        /// </summary>
        public void TravelTo(string mapId, bool arriveAtSpawn)
        {
            if (transitioning || !Game.IsPlaying || !MapRegistry.Exists(mapId) || mapId == Game.World.MapId) return;
            string from = Game.World.MapId;
            StartCoroutine(Transition(() =>
            {
                Game.Dialogue.Abort();
                var session = Game.Session;
                session.MapId = mapId;
                Game.World.Load(mapId);
                var player = Game.Player;
                var facing = Facing.Down;
                Vector2 arrival = arriveAtSpawn ? Game.World.PlayerSpawn : Game.World.ArrivalFrom(from, out facing);
                player.Spawn(arrival, facing, session.PlayerHealth, session.PlayerMaxHealth);
                Game.Camera.SetTarget(player.transform, true);
                Game.Quest.NotifyChanged();
                Game.UI.Hud.RefreshAll();
                Game.Audio.PlayMusic(Game.World.Map.music);
                Game.Audio.PlaySfx("confirm");
                GameEvents.RaiseToast($"— {Game.World.Map.displayName} —");
                if (arriveAtSpawn) Fx.Sparkle(player.Center + Vector2.up * 0.3f, 8, 0.8f);
            }));
        }

        bool readingScroll;

        /// <summary>
        /// 마을 귀환 주문서: a short light column around the player, then back to the village square.
        /// Returns true when a scroll was used up.
        /// </summary>
        public bool UseTownScroll()
        {
            if (transitioning || readingScroll || Game.Player == null || Game.Player.IsDead) return false;
            if (Game.World.MapId == MapRegistry.Village)
            {
                GameEvents.RaiseToast("이미 마을에 있다.");
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            if (!Game.Session.Inventory.Remove(ConsumableDatabase.TownScroll, 1)) return false;
            if (Game.State.Current == GameState.Inventory) CloseInventory();
            StartCoroutine(TownScrollRoutine());
            return true;
        }

        IEnumerator TownScrollRoutine()
        {
            readingScroll = true;
            var player = Game.Player;
            player.LockMovement(1.15f);
            SkillVisuals.TownPortal(player.Position);
            Game.Audio.PlaySfx("magic");
            GameEvents.RaiseToast("마을 귀환 주문서를 펼쳤다…");
            yield return new WaitForSeconds(1f);
            // Wait out a pause menu or a conversation that started meanwhile.
            while (!Game.IsPlaying || transitioning) yield return null;
            readingScroll = false;
            if (player.IsDead) yield break;
            TravelTo(MapRegistry.Village, true);
        }

        public void SaveGame()
        {
            bool ok = WriteSave();
            GameEvents.RaiseToast(ok ? "저장했습니다." : "저장에 실패했습니다.");
            Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
        }

        /// <summary>Silent save at milestones (quest start, workshop built, quest complete).</summary>
        public void Autosave()
        {
            if (Game.Config.autosave && WriteSave()) GameEvents.RaiseToast("자동 저장됨");
        }

        bool WriteSave()
        {
            var player = Game.Player;
            if (player == null || player.IsDead || !player.gameObject.activeInHierarchy) return false;
            var data = Game.Session.Capture(player.Position, player.Facing);
            return Game.Saves.Write(data);
        }

        public void ReturnToTitle()
        {
            StartCoroutine(Transition(() =>
            {
                Game.Dialogue.Abort();
                Game.World.Load(MapRegistry.Village);
                Game.Player.gameObject.SetActive(false);
                Game.Camera.SetTarget(null, false);
                Game.State.Set(GameState.Title);
                Game.Audio.PlayMusic("music_title");
            }));
        }

        public void QuitGame()
        {
            Game.Settings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void OnPlayerDied() => StartCoroutine(GameOverRoutine());

        IEnumerator GameOverRoutine()
        {
            yield return new WaitForSecondsRealtime(1.2f);
            if (Game.Player != null && Game.Player.IsDead) Game.State.Set(GameState.GameOver);
        }

        public void RespawnInVillage()
        {
            StartCoroutine(Transition(() =>
            {
                var player = Game.Player;
                if (Game.World.MapId != MapRegistry.Village)
                {
                    Game.Session.MapId = MapRegistry.Village;
                    Game.World.Load(MapRegistry.Village);
                    Game.Audio.PlayMusic(Game.World.Map.music);
                }
                player.Spawn(Game.World.PlayerSpawn, Facing.Down, int.MaxValue, Game.Session.PlayerMaxHealth);
                Game.Session.PlayerMana = CharacterStats.MaxMp;
                Game.Camera.SetTarget(player.transform, true);
                Game.State.Set(GameState.Playing);
            }));
        }

        public void ShowEnding() => StartCoroutine(EndingRoutine());

        IEnumerator EndingRoutine()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            Game.Audio.PlaySfx("ending");
            Game.State.Set(GameState.Ending);
        }

        IEnumerator Transition(Action action)
        {
            if (transitioning) yield break;
            transitioning = true;
            var fader = Game.UI.Fader;
            yield return fader.Fade(1f, 0.35f);
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            yield return null;
            yield return fader.Fade(0f, 0.35f);
            transitioning = false;
        }
    }
}
