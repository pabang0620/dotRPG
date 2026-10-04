using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Runs a conversation: switches the game into the Dialogue state, reveals text with a
    /// typewriter effect and advances on Interact/Submit. The UI (DialogueBox) only renders the
    /// properties exposed here, so the presentation can change freely.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        DialogueData current;
        int lineIndex;
        float visibleChars;
        int openedFrame;
        Action onComplete;
        string defaultSpeaker;
        string currentText = "";
        int lastBlipChar;

        public bool IsOpen { get; private set; }
        public string Speaker { get; private set; } = "";
        public string FullText => currentText;
        public int VisibleCharacters => Mathf.Min(currentText.Length, Mathf.FloorToInt(visibleChars));
        public bool LineComplete => VisibleCharacters >= currentText.Length;
        public bool HasMoreLines => current != null && lineIndex < current.lines.Count - 1;

        public event Action Opened;
        public event Action Closed;

        public void Play(string dialogueId, Action completed = null, string speakerName = null)
        {
            Play(Game.Dialogues.Get(dialogueId), completed, speakerName);
        }

        public void Play(DialogueData data, Action completed = null, string speakerName = null)
        {
            if (data == null || data.lines.Count == 0)
            {
                completed?.Invoke();
                return;
            }
            current = data;
            onComplete = completed;
            defaultSpeaker = speakerName ?? "";
            lineIndex = 0;
            openedFrame = Time.frameCount;
            IsOpen = true;
            ShowLine();
            Game.State.Set(GameState.Dialogue);
            Opened?.Invoke();
        }

        void ShowLine()
        {
            var line = current.lines[lineIndex];
            Speaker = string.IsNullOrEmpty(line.speaker) ? defaultSpeaker : line.speaker;
            if (Game.Quest != null) Speaker = Game.Quest.FormatTokens(Speaker); // "{name}" is the hero's name
            currentText = Game.Quest != null ? Game.Quest.FormatTokens(line.text ?? "") : line.text ?? "";
            visibleChars = 0f;
            lastBlipChar = 0;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Game.State.Current != GameState.Dialogue) return; // paused on top of the dialogue

            float cps = Game.Config != null ? Game.Config.textCharsPerSecond : 45f;
            visibleChars += cps * Time.unscaledDeltaTime;
            int shown = VisibleCharacters;
            if (shown - lastBlipChar >= 3 && !LineComplete)
            {
                lastBlipChar = shown;
                Game.Audio.PlaySfx("blip", 0.35f);
            }

            if (Time.frameCount == openedFrame) return;
            var input = Game.Input;
            bool advance = input.InteractPressed || input.SubmitPressed || input.AttackPressed;
            if (!advance) return;

            if (!LineComplete)
            {
                visibleChars = currentText.Length;
                return;
            }
            if (HasMoreLines)
            {
                lineIndex++;
                ShowLine();
                Game.Audio.PlaySfx("select", 0.5f);
            }
            else
            {
                Close();
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            current = null;
            if (Game.State.Current == GameState.Dialogue) Game.State.Set(Game.Cutscenes != null && Game.Cutscenes.IsPlaying ? GameState.Cutscene : GameState.Playing);
            Closed?.Invoke();
            var callback = onComplete;
            onComplete = null;
            callback?.Invoke();
        }

        /// <summary>Force-closes without running callbacks (used when returning to title).</summary>
        public void Abort()
        {
            IsOpen = false;
            current = null;
            onComplete = null;
            Closed?.Invoke();
        }
    }
}
