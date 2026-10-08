using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class HudView
    {
        void Update()
        {
            if (!built) return;
            // [CONTENT] The quest tracker is hidden inside dungeons (room map + boss bar own that space).
            bool inRun = Game.Dungeon != null && Game.Dungeon.InRun;
            var hudRect = ((RectTransform)transform).rect;
            var bossBar = BossHpBarView.Instance;
            bool bossShown = bossBar != null && bossBar.DevVisible;
            // [UI] On narrow canvases (UI size 1.15 / 1.3: W < 1160) the bottom-centre boss bar (W/2 +- 200) reaches the
            // quest column (W-380): a field boss fight owns that space, but only while the player is in it (a field boss
            // stays bound until killed). A first-time tip in the column hides it too.
            bool showQuest = !inRun && !TipView.InQuestColumn && !(bossShown && bossBar.Engaged && hudRect.width < 1160f);
            if (questPanel.gameObject.activeSelf != showQuest) questPanel.gameObject.SetActive(showQuest);
            // [UI] A bound but not engaged boss bar on a narrow canvas: the tracker narrows to the auto buttons' column
            // (W-292..W-20) so it stays clear of the bar's right end (its "xN" count), and widens back afterwards.
            float questWidth = TouchUi.Enabled || (bossShown && hudRect.width < 1160f) ? QuestNarrowWidth : QuestWidth;
            if (!Mathf.Approximately(questPanel.sizeDelta.x, questWidth)) questPanel.sizeDelta = new Vector2(questWidth, questPanel.sizeDelta.y);
            if (showQuest && (!Mathf.Approximately(questWidth, questFitWidth) || !Mathf.Approximately(hudRect.height, questFitHeight))) RefreshQuest(); // UI size / width changed
            // [AUTO] Hotkeys for 자동 진행 / 자동 사냥 (rebindable, default F6 / F7), only in normal play with no window open.
            var keys = Game.Input;
            if (keys != null && Game.IsPlaying && (!inRun || QuestAutoPilot.Active))
            {
                if (keys.HotkeyPressed(GameAction.AutoQuest)) QuestAutoPilot.Toggle();
                else if (keys.HotkeyPressed(GameAction.AutoHunt)) QuestAutoPilot.ToggleHunt();
            }
            if (autoLabel != null)
            {
                bool auto = QuestAutoPilot.Active;
                bool hunting = QuestAutoPilot.Hunting;
                autoLabel.text = auto && !hunting ? "자동 중지" : QuestAutoPilot.Targets.Count > 0 ? $"자동 진행 ({QuestAutoPilot.Targets.Count})" : "자동 진행";
                if (huntLabel != null) huntLabel.text = hunting ? "사냥 중지" : "자동 사냥";
                autoStatus.text = auto ? QuestAutoPilot.Status : "";
                if (huntBtn != null)
                {
                    bool canHunt = hunting || QuestAutoPilot.CanHuntHere;
                    if (huntBtn.interactable != canHunt) huntBtn.interactable = canHunt;
                }
                ShowKey(autoKey, GameAction.AutoQuest, ref autoKeyShown);
                ShowKey(huntKey, GameAction.AutoHunt, ref huntKeyShown);
            }
            // [UI] Toasts move up over the dialogue name plate and over the boss bar.
            var dialogue = Game.Dialogue;
            // A boss warning lifted over the GROGGY line (300..340) pushes them higher still.
            float toastY = bossShown ? Mathf.Max(ToastBaseBoss, bossBar.TopEdge + 4f) : dialogue != null && dialogue.IsOpen ? ToastBaseDialogue : ToastBase;
            if (!Mathf.Approximately(toastRoot.anchoredPosition.y, toastY)) toastRoot.anchoredPosition = new Vector2(0f, toastY);
            float now = Time.unscaledTime;
            // [UI] Over the boss bar (a warning lifts the base to 344) the stack must stay under the party status line and
            // the currency row (top 212 px): at UI size 1.3 only 1 toast fits, at 1.15 3. Older ones fade out early.
            int maxToasts = bossShown ? Mathf.Max(1, Mathf.FloorToInt((hudRect.height - 212f - toastY - ToastHeight) / ToastStep) + 1) : 4;
            // Toast layout & fade.
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                if (age > ToastLife)
                {
                    Destroy(t.root.gameObject);
                    toasts.RemoveAt(i);
                }
            }
            for (int i = 0; i < toasts.Count - maxToasts; i++)
            {
                // Pushed past the limit: jump to the last 0.5 s (the normal fade) instead of covering the HUD above.
                var t = toasts[i];
                if (now - t.bornAt < ToastLife - 0.5f) t.bornAt = now - (ToastLife - 0.5f);
            }
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                int fromBottom = toasts.Count - 1 - i;
                var rt = t.root;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0, fromBottom * ToastStep), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                float alpha = age < ToastLife - 0.5f ? 1f : Mathf.Clamp01((ToastLife - age) / 0.5f);
                var c = t.text.color;
                c.a = alpha;
                t.text.color = c;
                var pc = UiTheme.HudPlate;
                pc.a *= alpha;
                t.plate.color = pc;
            }

            // Item count pulse.
            foreach (var pair in itemCounts)
            {
                float s = 1f;
                if (itemPulse.TryGetValue(pair.Key, out float at)) s = 1f + 0.35f * Mathf.Max(0f, 1f - (now - at) / 0.25f);
                pair.Value.rectTransform.localScale = new Vector3(s, s, 1f);
            }

            var mobilityPlayer = Game.Player;
            if (mobilityPlayer != null)
            {
                float remaining = mobilityPlayer.MobilityCooldownRemaining;
                string key = Game.Input.GetBindingLabel(GameAction.Mobility);
                int stateKey = mobilityPlayer.IsDashing ? -2 : Mathf.CeilToInt(remaining * 10f);
                if (stateKey != mobilityStateShown || !ReferenceEquals(key, mobilityKeyShown) || !ReferenceEquals(mobilityPlayer.MobilityName, mobilityNameShown))
                {
                    mobilityStateShown = stateKey; mobilityKeyShown = key; mobilityNameShown = mobilityPlayer.MobilityName;
                    string state = mobilityPlayer.IsDashing ? "이동 중" : remaining > 0f ? $"{remaining:0.0}초" : "준비";
                    mobilityHint.text = $"[{key}] {mobilityPlayer.MobilityName}  {state}";
                }
                mobilityHint.color = remaining > 0f ? new Color(.7f, .74f, .8f) : new Color(.65f, .94f, 1f);
            }
            UpdatePrompt();
            RefreshControls(false);
        }

        // [P5] Last values shown in the mobility hint (format only on change).
        int mobilityStateShown = int.MinValue;
        string mobilityKeyShown, mobilityNameShown;

        void UpdatePrompt()
        {
            var player = Game.Player;
            var target = player != null && Game.IsPlaying ? player.Interactor.Current : null;
            if (target == null || Game.Camera == null)
            {
                if (prompt.gameObject.activeSelf) prompt.gameObject.SetActive(false);
                return;
            }
            if (!prompt.gameObject.activeSelf) prompt.gameObject.SetActive(true);
            var padIcon = Game.Input.UsingGamepad ? Game.Art.Optional("pad_" + Game.Input.GetBindingLabel(GameAction.Interact).ToLowerInvariant()) : null;
            promptPad.enabled = padIcon != null;
            promptPad.sprite = padIcon;
            promptText.text = padIcon != null || TouchUi.Enabled ? target.Prompt : $"<color=#ffd34a>[{Game.Input.GetBindingLabel(GameAction.Interact)}]</color> {target.Prompt}";
            promptText.rectTransform.offsetMin = new Vector2(padIcon != null ? 38f : 8f, 0f);
            prompt.sizeDelta = new Vector2(Mathf.Max(150f, promptText.preferredWidth + (padIcon != null ? 66f : 36f)), 40f);
            Vector3 screen = Game.Camera.Camera.WorldToScreenPoint(target.PromptWorldPosition);
            prompt.position = screen + new Vector3(0f, Mathf.Sin(Time.unscaledTime * 4f) * 3f, 0f);
        }
    }
}
