using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [GACHA VFX 2026-10-30] High-grade reveal clips (Docs/PLAN_GACHA_VFX_2026-10-30.md, bonghwa hero.js beam / bigReveal):
    /// - A result with a 유니크 or 레전더리 (skin / aura: 유니크 or 전설) opens with the 유니크 clip over the result page, even
    ///   when a 레전더리 is inside; a tap skips it, 2 s ends it. Then the cards are dealt.
    /// - Those cards are dealt face-down with one gold glow (the two grades look alike). A tap on a 유니크 opens it with the
    ///   유니크 clip on the card; the first tap on a 레전더리 only plays the 레전더리 clip on it (still face-down), the next
    ///   tap opens it.
    /// - 자동 뽑기 and 모두 열기 do the same by themselves with short gaps.
    /// - With screen shake off (the game's reduce-motion setting) no clip plays; the taps stay the same.
    /// </summary>
    public partial class GachaScreen
    {
        const float IntroHold = 2f, IntroHoldAuto = 1.2f, IntroSkipAfter = 0.15f;
        const float QuickTease = 1.1f, QuickGap = 0.5f, TeaseTapLock = 0.6f;

        readonly bool[] held = new bool[11], teased = new bool[11];
        readonly float[] tapLockUntil = new float[11];
        readonly List<int> openQueue = new List<int>();
        readonly List<GachaClip> liveClips = new List<GachaClip>();
        readonly Dictionary<string, GachaClip> warmClips = new Dictionary<string, GachaClip>();
        GachaClip intro;
        float introUntil = -1f, introSkipFrom;
        bool batchAuto, queueRunning;
        int batchSerial;

        /// <summary>2 = the 레전더리 clip (gear 레전더리, skin / aura 전설), 1 = the 유니크 clip, 0 = none. The sealed box maps its
        /// item grades the same way (Legendary -> "legendary", Unique -> "unique"; 에픽 stays without a clip).</summary>
        static int ClipTier(StarPullResult r) => r.rarity == "legendary" || r.rarity == "legend" ? 2 : r.rarity == "unique" ? 1 : 0;

        bool IsOpen(int i) => i < revealed && i < shown.Count && !held[i];

        bool AnyHeld()
        {
            for (int i = 0; i < shown.Count && i < held.Length; i++) if (held[i]) return true;
            return false;
        }

        bool IntroOn => introUntil > 0f;

        /// <summary>Drops every clip and the face-down state; <paramref name="warm"/> prepares both clips again.</summary>
        void ResetVfx(bool warm)
        {
            EndIntro();
            for (int i = 0; i < held.Length; i++) { held[i] = teased[i] = false; tapLockUntil[i] = 0f; }
            openQueue.Clear();
            queueRunning = false;
            batchSerial++;
            foreach (var c in liveClips) if (c != null) c.Close(true);
            liveClips.Clear();
            foreach (var c in warmClips.Values) if (c != null) c.Close(true);
            warmClips.Clear();
            if (warm && GachaClip.Enabled) { WarmClip(GachaClip.Unique); WarmClip(GachaClip.Legendary); }
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

        /// <summary>A new result is on the page (OpenResults): clear the last batch and start the opening clip.</summary>
        void StartVfxBatch()
        {
            EndIntro();
            for (int i = 0; i < held.Length; i++) { held[i] = teased[i] = false; tapLockUntil[i] = 0f; }
            openQueue.Clear();
            queueRunning = false;
            batchSerial++;
            batchAuto = autoOn;
            foreach (var c in liveClips) if (c != null) c.Close(); // the last draw's card clips fade out
            liveClips.Clear();
            bool any = false;
            foreach (var r in shown) if (ClipTier(r) > 0) { any = true; break; }
            if (!any) return;
            intro = TakeClip(GachaClip.Unique); // the 유니크 clip even with a 레전더리 inside: the surprise is kept
            if (intro == null) return;
            intro.PlayOver(resultPage);
            float now = Time.unscaledTime;
            introUntil = now + (batchAuto ? IntroHoldAuto : IntroHold);
            introSkipFrom = now + IntroSkipAfter;
            revealAt = Mathf.Max(revealAt, introUntil);
            Game.Audio.PlaySfx("rank_reveal");
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

        /// <summary>A face-down card was tapped.</summary>
        void TapCard(int i)
        {
            if (i >= shown.Count || !held[i] || openQueue.Contains(i) || Time.unscaledTime < tapLockUntil[i]) return;
            if (ClipTier(shown[i]) == 2 && !teased[i]) { Tease(i); return; }
            OpenHeld(i);
        }

        /// <summary>First tap on a 레전더리: its clip, a bigger fanfare and a shake, but the card stays face-down.</summary>
        void Tease(int i)
        {
            teased[i] = true;
            tapLockUntil[i] = Time.unscaledTime + TeaseTapLock;
            var clip = TakeClip(GachaClip.Legendary);
            if (clip != null) clip.PlaySpot(cells[CellOf(i)].rt);
            Game.Audio.PlaySfx("dungeon_clear");
            Game.Audio.PlaySfx("rank_reveal");
            Game.Audio.Duck(3.5f);
            Game.Camera?.Shake(0.2f, 0.45f); // off by itself with screen shake off
            Burst(CellOf(i), new Color(1f, .62f, .25f), 30);
        }

        /// <summary>Turns a face-down card up: the 유니크 clip and fanfare on a 유니크 (a 레전더리 had its clip on the first tap).</summary>
        void OpenHeld(int i)
        {
            if (i >= shown.Count || !held[i]) return;
            held[i] = false;
            var r = shown[i];
            int cell = CellOf(i);
            if (ClipTier(r) == 1)
            {
                var clip = TakeClip(GachaClip.Unique);
                if (clip != null) clip.PlaySpot(cells[cell].rt);
                Game.Audio.PlaySfx("enhance_great");
                Game.Audio.Duck(2.2f);
            }
            waiting = i; // the punch (Animate) lands on this card
            Announce(cell, r);
            DrawCells();
        }

        /// <summary>자동 뽑기 / 모두 열기: opens the face-down cards one after another without a tap.</summary>
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
                if (IntroOn) { yield return null; continue; }
                int i = openQueue[0];
                openQueue.RemoveAt(0);
                if (i >= shown.Count || !held[i]) continue;
                if (ClipTier(shown[i]) == 2 && !teased[i])
                {
                    Tease(i);
                    yield return new WaitForSecondsRealtime(QuickTease);
                    if (serial != batchSerial) yield break;
                }
                OpenHeld(i);
                yield return new WaitForSecondsRealtime(QuickGap);
            }
            if (serial == batchSerial) queueRunning = false;
        }
    }
}
