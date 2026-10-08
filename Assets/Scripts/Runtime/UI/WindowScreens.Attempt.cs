using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class EnhanceScreen
    {
        void StartAttempt(Entry e)
        {
            if (busy || ticketPending || !gameObject.activeInHierarchy) return;
            StartCoroutine(AttemptRoutine(e));
        }

        IEnumerator AttemptRoutine(Entry e)
        {
            busy = true;
            busyTime = 0f;
            resultText.text = "<color=#b8c4d8>망치질 중…</color>";
            Refresh();
            // [SERVER] Online the server rolls; ask now so the answer arrives during the hammer strikes.
            bool online = OnlineEconomy.On, answered = false;
            Dictionary<string, object> server = null;
            if (online)
                OnlineEconomy.Enhance(e.slot.HasValue ? (int)e.slot.Value : (int?)null, e.slot.HasValue ? null : e.key,
                    d => { server = d; answered = true; });
            // [A] Suspense: three hammer strikes over a rising charge, the glow grows around the item.
            Game.Audio.PlaySfx("enhance_charge");
            EnhanceFx.Charge(bigIcon.rectTransform, SuspenseSeconds * 1.5f);
            for (int i = 0; i < 3; i++)
            {
                Game.Audio.PlaySfx("hammer");
                EnhanceFx.Sparks(bigIcon.rectTransform, 6, new Color32(255, 220, 120, 255));
                yield return new WaitForSecondsRealtime(SuspenseSeconds * 0.5f);
            }
            float waited = 0f;
            while (online && !answered && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
            EnhanceResult result;
            try
            {
                if (online) result = FromServer(e, server);
                else
                {
                    int roll = devRoll ?? Authority.Current.EnhanceRoll();
                    devRoll = null;
                    result = Game.Session.Equipment.TryEnhance(e.Target, roll, Class);
                }
            }
            finally
            {
                // Even if the attempt (or a Changed handler) throws, the window must not stay locked.
                busy = false;
                bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            }
            ShowResult(result);
            Follow(e, result);
            if (result.kind == EnhanceOutcome.Destroyed) pickRequired = true;
            if (result.Attempted) Game.Flow.Autosave();
            Refresh();
        }

        /// <summary>[SERVER] The server's answer (its delta is already applied) as the local result shape.</summary>
        static EnhanceResult FromServer(Entry e, Dictionary<string, object> d)
        {
            if (d == null) return new EnhanceResult { kind = EnhanceOutcome.Invalid, oldKey = e.key, slot = e.slot };
            EnhanceOutcome kind;
            switch (MiniJson.Str(d, "outcome"))
            {
                case "success": kind = EnhanceOutcome.Success; break;
                case "keep": kind = EnhanceOutcome.Keep; break;
                case "drop3": kind = EnhanceOutcome.Drop3; break;
                case "destroyed": kind = EnhanceOutcome.Destroyed; break;
                case "protected": kind = EnhanceOutcome.Protected; break;
                default: kind = EnhanceOutcome.Invalid; break;
            }
            string oldKey = MiniJson.Str(d, "old_key", e.key);
            Game.Session.Equipment.SetPityFromServer(oldKey, MiniJson.Int(d, "pity"));
            return new EnhanceResult
            {
                kind = kind,
                oldKey = oldKey,
                newKey = MiniJson.Str(d, "new_key"),
                oldLevel = MiniJson.Int(d, "old_level"),
                newLevel = MiniJson.Int(d, "new_level"),
                slot = e.slot,
                roll = MiniJson.Int(d, "roll"),
            };
        }

        void ShowResult(EnhanceResult r)
        {
            switch (r.kind)
            {
                case EnhanceOutcome.Success:
                    bool great = EquipmentDatabase.LevelOfKey(r.newKey) >= 10;
                    Game.Audio.PlaySfx(great ? "enhance_great" : "enhance_success");
                    EnhanceFx.Burst(bigIcon.rectTransform, great ? 40 : 22, new Color32(255, 230, 120, 255), great);
                    if (great) GameEvents.RaiseToast($"<color=#ffd84a>[알림]</color> {Game.Session.Journal.PlayerName}님이 {EquipmentDatabase.NameOfKey(r.newKey)} 강화에 성공했습니다!");
                    resultText.text = $"<color=#8fe28f><b>강화 성공!</b></color>  {EquipmentDatabase.RichName(r.newKey)}";
                    GameEvents.RaiseToast($"강화 성공! {EquipmentDatabase.RichName(r.newKey)}");
                    break;
                case EnhanceOutcome.Keep:
                    Game.Audio.PlaySfx("enhance_fail");
                    EnhanceFx.Fail(bigIcon.rectTransform, false);
                    resultText.text = "<color=#ffb070>강화 실패… 강화 수치는 그대로입니다.</color>";
                    break;
                case EnhanceOutcome.Drop3:
                    Game.Audio.PlaySfx("enhance_fail");
                    EnhanceFx.Fail(bigIcon.rectTransform, false);
                    resultText.text = $"<color=#ff9f43>강화 실패… 강화 수치가 3 떨어졌습니다.  (+{r.oldLevel} → +{r.newLevel})</color>";
                    break;
                case EnhanceOutcome.Destroyed:
                    Game.Audio.PlaySfx("enhance_break");
                    EnhanceFx.Fail(bigIcon.rectTransform, true);
                    resultText.text = $"<color=#ff5050><b>강화 실패… 장비가 파괴되었습니다!</b></color>\n<color=#ff8080>{EquipmentDatabase.NameOfKey(r.oldKey)}</color>";
                    GameEvents.RaiseToast($"<color=#ff5050>장비 파괴: {EquipmentDatabase.NameOfKey(r.oldKey)}</color>");
                    break;
                case EnhanceOutcome.Protected:
                    Game.Audio.PlaySfx("confirm");
                    resultText.text = $"<color=#ffd84a>강화 실패… 장비 보호권이 장비를 지켰습니다.</color>\n<color=#b8c4d8>{EquipmentDatabase.NameOfKey(r.newKey)} (+0으로 초기화)</color>";
                    break;
                case EnhanceOutcome.NotEnough:
                    Game.Audio.PlaySfx("cancel");
                    resultText.text = "<color=#ff7070>골드나 재료가 부족합니다.</color>";
                    break;
                default:
                    Game.Audio.PlaySfx("cancel");
                    resultText.text = "";
                    break;
            }
        }

        /// <summary>Keeps the cursor on the same piece after an attempt (same slot, or the bag key it became).</summary>
        void Follow(Entry e, EnhanceResult r)
        {
            BuildEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                var x = entries[i];
                bool same = e.slot.HasValue ? x.slot == e.slot : !x.slot.HasValue && x.key == r.newKey;
                if (!same) continue;
                selected = i;
                return;
            }
        }

        protected override bool HasKeyTags => true;

        protected override void Update()
        {
            if (busy)
            {
                // [UX] A 강화권 waits on the server (up to the API timeout): Esc / I still close the forge meanwhile.
                if (ticketPending && TakesInput && (Game.Input.CancelPressed || Game.Input.InventoryPressed))
                {
                    Game.Audio.PlaySfx("cancel");
                    Close();
                    return;
                }
                // No input while the hammer falls; the piece shakes a little.
                busyTime += Time.unscaledDeltaTime;
                bigIcon.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(busyTime * 70f) * 3f, 0f);
                if (dirty) Refresh();
                return;
            }
            base.Update();
            if (!gameObject.activeSelf) return;
            if (TakesInput)
            {
                var nav = Game.Input.NavigateStep;
                if (nav != Vector2Int.zero && entries.Count > 0)
                {
                    // One continuous list: moving past the edge of a page turns the page.
                    selected = Mathf.Clamp(selected + nav.x - nav.y * Cols, 0, entries.Count - 1);
                    Game.Audio.PlaySfx("select", 0.5f);
                    Picked();
                }
                if (Game.Input.SubmitPressed) OnEnhancePressed();
                // [UX] Keys for the side buttons: UseMana = 강화권, UseItem = 승급, [ / ] or LB / RB = switch 강화권.
                // (Interact shares F / A with Submit, which already presses 강화.)
                else if (Game.Input.UseManaPressed) UseTicket();
                else if (Game.Input.UseItemPressed) OnPromotePressed();
                else if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5)) SwitchTicket(1);
                else if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton4)) SwitchTicket(-1);
            }
            if (dirty) Refresh();
        }

        // ---------- Developer automation (DevCapture) ----------

        /// <summary>Selects the first entry holding this key (worn or bag). False when it is not listed.</summary>
        public bool DevSelect(string key)
        {
            BuildEntries();
            int i = entries.FindIndex(x => x.key == key);
            if (i < 0) return false;
            selected = i;
            Picked();
            Refresh();
            return true;
        }

        /// <summary>Selects a worn slot's entry. False when that slot is empty.</summary>
        public bool DevSelectSlot(EquipSlot slot)
        {
            BuildEntries();
            int i = entries.FindIndex(x => x.slot == slot);
            if (i < 0) return false;
            selected = i;
            Picked();
            Refresh();
            return true;
        }

        /// <summary>Presses the enhance button (risky attempts open the confirm dialog first).</summary>
        public void DevPress() => OnEnhancePressed();
        /// <summary>The next attempt uses this 0..99 roll instead of a random one (cleared when used).</summary>
        public void DevForceRoll(int roll) => devRoll = roll;
        int? devRoll;
        public bool DevBusy => busy;
        public bool DevPickRequired => pickRequired;
        public string DevSelectedKey => entries.Count > 0 && selected < entries.Count ? entries[selected].key : null;
        public int DevSelectedIndex => selected;
        public string DevStats => statsText.text;
        public string DevResult => resultText.text;
        public string DevFailLine => failText.text;
        public string DevPage => $"{selected / PerPage + 1} / {PageCount}";
        public int DevEntries => entries.Count;
    }
}
