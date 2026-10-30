using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [GACHA VFX 2026-10-30] The bonghwa summon reveal (js/ui/hero.js reveal / beam / spotlight, css/fx-summon.css) with
    /// bonghwa's 영웅+ mapped to 유니크 and 레전더리 (skin / aura: 유니크 and 전설). Docs/PLAN_GACHA_VFX_2026-10-30.md.
    /// - The result page opens with the 유니크 clip over it when the batch holds one of them (even a 레전더리: the surprise
    ///   is kept); a tap skips it, 2 s ends it. Then the cards are dealt (Update in GachaScreen.Reveal.cs).
    /// - Lower grades turn up by themselves; 유니크 / 레전더리 stay face-down with one gold glow and wait for a click.
    /// - Click on a 유니크: 0.7 s charge-up, then it opens and a big copy shows in the middle of the screen with the clip
    ///   behind it (the spotlight: a tap after 0.25 s or 2.4 s closes it).
    /// - Click on a 레전더리 (user rule): charge-up, then only the 레전더리 clip over the page, the card stays face-down and
    ///   trembles; the next click charges up again and opens it with the spotlight (3.2 s), a bigger fanfare and a shake.
    /// - 확인 / 한 번 더 / 자동 뽑기 stay locked until every card is open and no spotlight shows (toast + nudge).
    /// - 모두 열기 opens everything at once without charge-up or spotlight. 자동 뽑기 opens the top cards itself, quickly,
    ///   still with a short spotlight.
    /// - Screen shake off (the game's reduce motion): no clips, sparks or shake; the clicks and the spotlight card stay.
    /// </summary>
    public partial class GachaScreen
    {
        const float IntroHold = 2f, IntroHoldAuto = 1.2f, IntroSkipAfter = 0.15f;
        const float DealFirst = 0.35f, DealStep = 0.09f;
        const float Charge = 0.7f, ChargeAuto = 0.4f, TeaseTapLock = 0.6f, QuickTease = 1f, QuickGap = 0.3f;
        const float SpotUnique = 2.4f, SpotLegend = 3.2f, SpotUniqueAuto = 1.2f, SpotLegendAuto = 1.6f, SpotTapAfter = 0.25f;
        const float SpotCardHeight = 300f;

        readonly bool[] held = new bool[11], teased = new bool[11];
        readonly float[] tapLockUntil = new float[11], chargeEnd = new float[11];
        readonly float[] nudgeAt = { -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f };
        readonly Vector2[] cellBase = new Vector2[11];
        readonly List<int> openQueue = new List<int>();
        readonly List<GachaClip> liveClips = new List<GachaClip>();
        readonly Dictionary<string, GachaClip> warmClips = new Dictionary<string, GachaClip>();
        GachaClip intro;
        float introUntil = -1f, introSkipFrom;
        bool batchAuto, queueRunning;
        int batchSerial;
        // the spotlight (bonghwa .sm-spot)
        RectTransform spot, spotCard;
        float spotAt, spotUntil, spotScale;

        /// <summary>2 = the 레전더리 clip (gear 레전더리, skin / aura 전설), 1 = the 유니크 clip, 0 = none. The sealed box maps its
        /// item grades the same way (Legendary -> "legendary", Unique -> "unique"; 에픽 has no clip).</summary>
        static int ClipTier(StarPullResult r) => r.rarity == "legendary" || r.rarity == "legend" ? 2 : r.rarity == "unique" ? 1 : 0;

        bool IsOpen(int i) => i < revealed && i < shown.Count && !held[i];

        bool AnyHeld()
        {
            for (int i = 0; i < shown.Count && i < held.Length; i++) if (held[i]) return true;
            return false;
        }

        /// <summary>Every card is face up and no spotlight shows (bonghwa allSeen).</summary>
        bool AllSeen() => !Revealing && spot == null;

        bool IntroOn => introUntil > 0f;

        /// <summary>Pressing 확인 / 한 번 더 / 자동 뽑기 too early: a toast and the closed cards shake (bonghwa notYet).</summary>
        void NotYet()
        {
            Game.Audio.PlaySfx("cancel");
            GameEvents.RaiseToast(L.T("카드를 모두 확인해 주세요"));
            for (int i = 0; i < shown.Count && i < nudgeAt.Length; i++) if (!IsOpen(i)) nudgeAt[i] = Time.unscaledTime;
        }

        /// <summary>Enter / A / Esc on the result page: skip the clip, close the spotlight, open the next face-down card
        /// (Enter only, as a click would), or close the page once everything is seen.</summary>
        void ResultKey(bool submit)
        {
            if (IntroOn) { EndIntro(); return; }
            if (spot != null) { if (Time.unscaledTime >= spotAt + SpotTapAfter) CloseSpotlight(); return; }
            if (AllSeen()) { CloseResults(); return; }
            if (submit)
                for (int i = 0; i < shown.Count; i++)
                    if (held[i]) { TapCard(i); return; }
            NotYet();
        }

        /// <summary>Drops every clip, the spotlight and the face-down state; <paramref name="warm"/> prepares both clips again.</summary>
        void ResetVfx(bool warm)
        {
            ClearBatch();
            foreach (var c in liveClips) if (c != null) c.Close(true);
            liveClips.Clear();
            foreach (var c in warmClips.Values) if (c != null) c.Close(true);
            warmClips.Clear();
            if (warm && GachaClip.Enabled) { WarmClip(GachaClip.Unique); WarmClip(GachaClip.Legendary); }
        }

        void ClearBatch()
        {
            EndIntro();
            DestroySpotlight();
            for (int i = 0; i < held.Length; i++) { held[i] = teased[i] = false; tapLockUntil[i] = chargeEnd[i] = 0f; nudgeAt[i] = -10f; }
            openQueue.Clear();
            queueRunning = false;
            batchSerial++;
        }

        void WarmClip(string key)
        {
            if (content == null || !content.gameObject.activeInHierarchy) return;
            warmClips[key] = GachaClip.Create(key, content);
        }

        /// <summary>The prepared clip of <paramref name="key"/> (a new one starts preparing for next time). Null when clips are off.</summary>
        GachaClip TakeClip(string key)
        {
            if (!GachaClip.Enabled) return null;
            warmClips.TryGetValue(key, out var c);
            if (c == null && content != null) c = GachaClip.Create(key, content);
            WarmClip(key);
            if (c == null) return null;
            liveClips.RemoveAll(x => x == null);
            liveClips.Add(c);
            return c;
        }

        /// <summary>A new result is on the page (OpenResults): clear the last batch and start the opening clip (bonghwa beam).</summary>
        void StartVfxBatch()
        {
            ClearBatch();
            batchAuto = autoOn;
            foreach (var c in liveClips) if (c != null) c.Close(); // the last draw's clips fade out
            liveClips.Clear();
            float now = Time.unscaledTime;
            revealAt = now; // dealing starts at once (the first card turns 0.35 s later) unless the clip holds it
            bool any = false;
            foreach (var r in shown) if (ClipTier(r) > 0) { any = true; break; }
            if (!any) return;
            Game.Audio.PlaySfx("rank_reveal");
            intro = TakeClip(GachaClip.Unique); // the 유니크 clip even with a 레전더리 inside: the surprise is kept
            if (intro == null) return;
            intro.PlayOver(resultPage);
            introUntil = now + (batchAuto ? IntroHoldAuto : IntroHold);
            introSkipFrom = now + IntroSkipAfter;
            revealAt = introUntil;
        }

        /// <summary>Ends the opening clip (tap, time or 모두 열기); dealing starts at once.</summary>
        void EndIntro()
        {
            if (intro != null) intro.Close();
            intro = null;
            if (introUntil > 0f && revealAt > Time.unscaledTime) revealAt = Time.unscaledTime;
            introUntil = -1f;
        }

        void UpdateIntro()
        {
            if (!IntroOn) return;
            float now = Time.unscaledTime;
            bool tap = now >= introSkipFrom && (UnityEngine.Input.GetMouseButtonDown(0)
                || UnityEngine.Input.touchCount > 0 && UnityEngine.Input.GetTouch(0).phase == TouchPhase.Began);
            if (intro == null || tap || now >= introUntil) EndIntro(); // null: the clip failed and closed itself
        }

        /// <summary>A face-down card was clicked: the charge-up starts (bonghwa flip()).</summary>
        void TapCard(int i)
        {
            if (i >= shown.Count || !held[i] || chargeEnd[i] > 0f || spot != null || openQueue.Contains(i) || Time.unscaledTime < tapLockUntil[i]) return;
            StartCharge(i, Charge);
        }

        void StartCharge(int i, float seconds)
        {
            chargeEnd[i] = Time.unscaledTime + seconds;
            Game.Audio.PlaySfx("enhance_charge");
        }

        /// <summary>Every frame: a finished charge-up opens (or teases) its card; the spotlight pops in and closes on time.</summary>
        void UpdateCards()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < shown.Count && i < chargeEnd.Length; i++)
            {
                if (chargeEnd[i] <= 0f || now < chargeEnd[i]) continue;
                chargeEnd[i] = 0f;
                if (!held[i]) continue;
                if (ClipTier(shown[i]) == 2 && !teased[i]) Tease(i);
                else OpenLoud(i);
            }
            if (spot == null) return;
            float k = Mathf.Clamp01((now - spotAt) / 0.35f);
            float pop = 1f + 2.70158f * Mathf.Pow(k - 1f, 3f) + 1.70158f * Mathf.Pow(k - 1f, 2f); // ease-out-back 0 -> 1
            if (spotCard != null) spotCard.localScale = Vector3.one * spotScale * (0.9f + 0.1f * pop);
            if (now >= spotUntil) CloseSpotlight();
        }

        /// <summary>The first click on a 레전더리: its clip big over the page and a shake; the card stays face-down.</summary>
        void Tease(int i)
        {
            teased[i] = true;
            tapLockUntil[i] = Time.unscaledTime + TeaseTapLock;
            var clip = TakeClip(GachaClip.Legendary);
            if (clip != null) clip.PlayOver(resultPage);
            Game.Audio.PlaySfx("rank_reveal");
            Game.Audio.PlaySfx("enhance_great");
            Game.Audio.Duck(3.5f);
            Game.Camera?.Shake(0.2f, 0.45f); // off by itself with screen shake off
        }

        /// <summary>Opens a face-down card with the fanfare, the sparks on the small card and the spotlight (bonghwa open(c, true)).</summary>
        void OpenLoud(int i)
        {
            if (i >= shown.Count || !held[i]) return;
            held[i] = false;
            var r = shown[i];
            bool legend = ClipTier(r) == 2;
            waiting = i; // the punch (Animate) lands on this card
            Announce(CellOf(i), r); // flash, sparks, toast
            if (legend)
            {
                Game.Audio.PlaySfx("dungeon_clear");
                Game.Audio.Duck(3.5f);
                Game.Camera?.Shake(0.22f, 0.5f);
            }
            else
            {
                Game.Audio.PlaySfx("enhance_great");
                Game.Audio.Duck(2.2f);
            }
            DrawCells();
            ShowSpotlight(i, legend, batchAuto ? (legend ? SpotLegendAuto : SpotUniqueAuto) : (legend ? SpotLegend : SpotUnique));
        }

        /// <summary>A big copy of the card in the middle of the screen over a dark veil, the clip behind it (bonghwa spotlight).</summary>
        void ShowSpotlight(int i, bool legend, float hold)
        {
            DestroySpotlight();
            var canvas = content.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : content;
            var veil = UIFactory.Image(root, "GachaSpotlight", Game.Art.Get("ui_white"), new Color(10f / 255f, 6f / 255f, 20f / 255f, .72f));
            veil.preserveAspect = false;
            veil.raycastTarget = true; // takes every click while it shows
            spot = UIFactory.Stretch(veil.rectTransform);
            spot.SetAsLastSibling();
            var close = veil.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.navigation = new Navigation { mode = Navigation.Mode.None };
            close.onClick.AddListener(() => { if (Time.unscaledTime >= spotAt + SpotTapAfter) CloseSpotlight(); });
            var clip = TakeClip(legend ? GachaClip.Legendary : GachaClip.Unique);
            if (clip != null) clip.PlayOver(spot); // behind the card: the card is added after it

            var src = cells[CellOf(i)].bg;
            var copy = Instantiate(src.gameObject, spot, false);
            copy.name = "SpotCard";
            foreach (var b in copy.GetComponentsInChildren<Button>(true)) Destroy(b);
            foreach (var g in copy.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false; // taps go to the veil
            spotCard = (RectTransform)copy.transform;
            spotCard.anchorMin = spotCard.anchorMax = spotCard.pivot = new Vector2(0.5f, 0.5f);
            spotCard.anchoredPosition = new Vector2(0f, 30f);
            spotCard.localRotation = Quaternion.identity;
            spotScale = SpotCardHeight / Mathf.Max(1f, src.rectTransform.rect.height);
            spotCard.localScale = Vector3.one * spotScale * 0.9f;
            spotCard.SetAsLastSibling();

            var hint = UIFactory.Text(spot, "Hint", L.T("탭하여 계속"), 22, UIColors.Cream, TextAnchor.MiddleCenter, true);
            hint.raycastTarget = false;
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f - SpotCardHeight / 2f - 50f), new Vector2(600f, 36f));
            spotAt = Time.unscaledTime;
            spotUntil = spotAt + hold;
            RefreshLocks();
        }

        void CloseSpotlight()
        {
            DestroySpotlight();
            RefreshLocks();
        }

        void DestroySpotlight()
        {
            if (spot != null) Destroy(spot.gameObject); // its clip goes with it
            spot = spotCard = null;
        }

        /// <summary>자동 뽑기 opens the face-down cards one after another without a click.</summary>
        void QueueOpen(int i)
        {
            if (!held[i] || openQueue.Contains(i)) return;
            openQueue.Add(i);
            if (!queueRunning && gameObject.activeInHierarchy) StartCoroutine(RunQueue(batchSerial));
        }

        IEnumerator RunQueue(int serial)
        {
            queueRunning = true;
            while (serial == batchSerial && openQueue.Count > 0)
            {
                if (IntroOn || spot != null) { yield return null; continue; }
                int i = openQueue[0];
                if (i >= shown.Count || !held[i]) { openQueue.RemoveAt(0); continue; }
                if (ClipTier(shown[i]) == 2 && !teased[i])
                {
                    StartCharge(i, ChargeAuto);
                    while (serial == batchSerial && held[i] && !teased[i]) yield return null;
                    yield return new WaitForSecondsRealtime(QuickTease);
                    if (serial != batchSerial) yield break;
                }
                if (held[i] && chargeEnd[i] <= 0f) StartCharge(i, ChargeAuto);
                while (serial == batchSerial && held[i]) yield return null;
                while (serial == batchSerial && spot != null) yield return null;
                if (serial != batchSerial) yield break;
                openQueue.Remove(i);
                yield return new WaitForSecondsRealtime(QuickGap);
            }
            if (serial == batchSerial) queueRunning = false;
        }

        /// <summary>확인 / 한 번 더 / 자동 뽑기 look off until every card is seen (they still answer with the toast);
        /// 모두 열기 only for a multi-draw with cards left; 자동 뽑기 only after a purchase.</summary>
        void RefreshLocks()
        {
            if (okBtn == null) return;
            bool ok = AllSeen();
            var off = new Color(1f, 1f, 1f, .45f);
            okBtn.image.color = againBtn.image.color = ok ? Color.white : off;
            skipBtn.gameObject.SetActive(shown.Count > 1 && Revealing);
            if (autoPageStop != null)
            {
                autoPageStop.gameObject.SetActive(autoOn || purchaseResult);
                TextOf(autoPageStop).text = autoOn ? L.T("자동 뽑기 멈춤") : L.T("자동 뽑기");
                autoPageStop.image.sprite = Game.Art.Get(autoOn ? "ui_btngray" : "ui_btn");
                autoPageStop.image.color = autoOn || ok ? Color.white : off;
            }
        }
    }
}
