using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [GACHA 2026-10-29] 자동 뽑기: draws the chosen banner and count again and again until the player stops it, the star
    /// shards run out, a stop grade comes up, a draw fails, or the window closes. Every draw is the ordinary purchase call
    /// (its own request_id) and waits for the result and its reveal before the next one.
    /// </summary>
    public partial class GachaScreen
    {
        enum AutoStop { Unique, Legend, Never }

        bool autoOn, autoPending;
        int autoCount = 1, autoSerial, pullSerial;
        AutoStop autoStop = AutoStop.Legend;
        float autoNextAt = -1f;
        Button autoBtn, autoStopBtn, autoCountBtn, autoPageStop;
        /// <summary>The page shows a 1회 / 10+1회 purchase (not a gauge or ceiling pick): it offers 자동 뽑기.</summary>
        bool purchaseResult;
        const float AutoGap = 0.8f; // after a reveal ends, so the cards can be seen

        void BuildAuto(Transform bigBox, Transform page)
        {
            var br = new Vector2(1f, 0f);
            autoCountBtn = Button(bigBox, "AutoCount", "", "ui_btngray", br, br, new Vector2(-228f, 64f), new Vector2(190f, 40f), CycleAutoCount, 16);
            autoStopBtn = Button(bigBox, "AutoStop", "", "ui_btngray", br, br, new Vector2(-18f, 64f), new Vector2(200f, 40f), CycleAutoStop, 16);
            autoBtn = Button(bigBox, "Auto", "", "ui_btn", br, br, new Vector2(-18f, 14f), new Vector2(400f, 44f), ToggleAuto, 18);
            // Top right of the result page: 자동 뽑기 멈춤 while it runs, else [GACHA VFX] 자동 뽑기 after a purchase (so
            // players find it). One button for both, so the two never stack.
            autoPageStop = Button(page, "AutoStopNow", L.T("자동 뽑기 멈춤"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(200f, 48f), () => { if (autoOn) StopAuto(null); else AutoFromResult(); }, 18);
            autoPageStop.gameObject.SetActive(false);
        }

        /// <summary>자동 뽑기 from the result page: once every card is seen, closes it and starts 자동 뽑기 on this banner with
        /// this draw's count and the saved stop grade (the usual confirm first).</summary>
        void AutoFromResult()
        {
            if (!AllSeen()) { NotYet(); return; }
            autoCount = lastCount == 10 ? 10 : 1;
            CloseResults();
            ToggleAuto();
        }

        void CycleAutoCount() { if (autoOn) return; autoCount = autoCount == 1 ? 10 : 1; Game.Audio.PlaySfx("select"); Refresh(); }

        void CycleAutoStop() { autoStop = (AutoStop)(((int)autoStop + 1) % 3); Game.Audio.PlaySfx("select"); Refresh(); }

        string AutoStopName => autoStop == AutoStop.Unique ? L.T("유니크 이상에서 멈춤") : autoStop == AutoStop.Legend ? L.T("전설에서 멈춤") : L.T("등급으로 멈추지 않음");

        int AutoPrice => Sealed ? (autoCount == 10 ? CashClient.PriceEleven : CashClient.PriceOne) : (autoCount == 10 ? StarShopClient.PriceTen : StarShopClient.PriceOne);

        void ToggleAuto()
        {
            if (autoOn) { StopAuto(null); return; }
            if (busy || Revealing || !StarShopClient.Available || !StarShopClient.Loaded) return;
            if (StarShopClient.Balance < AutoPrice)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(L.F("별조각이 모자랍니다. (필요 {0:N0}, 보유 {1:N0})", AutoPrice, StarShopClient.Balance));
                return;
            }
            string name = Sealed ? L.T("봉인된 상자") : Current().name;
            Game.UI.Confirm(name + "\n" + L.F("{0}씩 자동으로 계속 뽑습니다. ({1})\n시작할까요?", StarShopClient.Stars(AutoPrice), AutoStopName) + "\n" + PolicyClient.RefundNotice, () =>
            {
                autoOn = true;
                autoNextAt = -1f;
                AutoStep();
            }, true);
        }

        void StopAuto(string reason)
        {
            if (!autoOn) return;
            autoOn = false;
            autoPending = false;
            autoNextAt = -1f;
            GameEvents.RaiseToast(string.IsNullOrEmpty(reason) ? L.T("자동 뽑기를 멈췄습니다.") : reason);
            if (gameObject.activeInHierarchy) Refresh();
        }

        void AutoStep()
        {
            if (!autoOn) return;
            if (!StarShopClient.Available || busy) { StopAuto(null); return; }
            if (StarShopClient.Balance < AutoPrice) { StopAuto(L.T("별조각이 모자라 자동 뽑기를 멈췄습니다.")); return; }
            autoPending = true;
            autoSerial = pullSerial;
            if (Sealed) PullSealed(autoCount); else Pull(autoCount);
        }

        bool AutoStopHit()
        {
            if (autoStop == AutoStop.Never) return false;
            foreach (var r in shown)
                if (autoStop == AutoStop.Unique ? IsTop(r) : r.rarity == "legend" || r.rarity == "legendary") return true;
            return false;
        }

        /// <summary>Called every frame (Update): checks the last draw, then starts the next once its reveal is over.</summary>
        void UpdateAuto()
        {
            if (!autoOn) return;
            if (busy) return;
            if (autoPending)
            {
                autoPending = false;
                if (pullSerial == autoSerial) { StopAuto(null); return; } // the draw failed (its error was shown)
                if (AutoStopHit()) { StopAuto(autoStop == AutoStop.Unique ? L.T("유니크 이상이 나와 자동 뽑기를 멈췄습니다.") : L.T("전설이 나와 자동 뽑기를 멈췄습니다.")); return; }
            }
            if (!AllSeen()) { autoNextAt = -1f; return; } // [GACHA VFX] the spotlight too
            if (autoNextAt < 0f) { autoNextAt = Time.unscaledTime + AutoGap; return; }
            if (Time.unscaledTime < autoNextAt) return;
            autoNextAt = -1f;
            AutoStep();
        }

        void RefreshAuto(bool online)
        {
            if (autoBtn == null) return;
            TextOf(autoBtn).text = autoOn ? L.T("<b>자동 뽑기 중</b> · 눌러서 멈춤") : L.F("자동 뽑기 · 별조각 {0:N0}씩", AutoPrice);
            TextOf(autoCountBtn).text = autoCount == 10 ? L.T("자동: 10+1회씩") : L.T("자동: 1회씩");
            TextOf(autoStopBtn).text = AutoStopName;
            autoBtn.interactable = autoOn || (online && StarShopClient.Loaded && !busy);
            autoCountBtn.interactable = !autoOn;
            RefreshLocks(); // [GACHA VFX] the result page top-right button (자동 뽑기 멈춤 / 자동 뽑기)
        }

        // Closing the window or leaving the scene stops it (the object is disabled or destroyed).
        void OnDisable() { StopAuto(null); ResetVfx(false); } // [GACHA VFX] a clip would freeze on a hidden window
    }
}
