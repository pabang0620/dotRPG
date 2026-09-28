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
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Common PC courtesy: alt-tabbing out pauses the game (not in the editor, where it gets in the way).
            if (!hasFocus && !Application.isEditor && Game.State != null &&
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

        public void NewGame()
        {
            StartCoroutine(Transition(() =>
            {
                Game.Session.ResetForNewGame(Game.Config);
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
            }));
        }

        void EnterWorld()
        {
            Game.Dialogue.Abort();
            Game.World.Rebuild();
            var session = Game.Session;
            var player = Game.Player;
            player.gameObject.SetActive(true);
            Vector2 start = session.StartPosition ?? Game.World.PlayerSpawn;
            player.Spawn(start, session.StartFacing, session.PlayerHealth, session.PlayerMaxHealth);
            Game.Camera.SetTarget(player.transform, true);
            Game.Quest.NotifyChanged();
            Game.UI.Hud.ClearToasts();
            Game.UI.Hud.RefreshAll();
            Game.Audio.PlayMusic("music_village");
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
                Game.World.Rebuild();
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
                player.Spawn(Game.World.PlayerSpawn, Facing.Down, Game.Session.PlayerMaxHealth, Game.Session.PlayerMaxHealth);
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
