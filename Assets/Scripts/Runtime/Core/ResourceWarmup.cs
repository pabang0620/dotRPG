using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [PERF] Builds every generated effect before play, behind a loading screen over the title. Skill effect clips
    /// and HD effect sprites are drawn in code the first time they are used, which cost up to 600 ms in the middle
    /// of a fight (measured: Logs/perf_probe.txt, all clips 1.5 s). Here they are made once at start, a few per frame
    /// inside a time budget so the progress bar keeps moving; the title takes input only when it is done.
    /// </summary>
    public sealed class ResourceWarmup : MonoBehaviour
    {
        const float FrameBudgetMs = 14f;

        /// <summary>True while the loading screen is up (the title waits).</summary>
        public static bool Busy { get; private set; }

        Text label;
        RectTransform fill;

        /// <summary>Procedural HD effect sprites the skills spawn by key.</summary>
        static readonly string[] FxKeys =
        {
            "fx_glow", "fx_shock", "fx_ring", "fx_rune", "fx_swoosh", "fx_blade", "fx_zap", "fx_frost", "fx_crack", "fx_spike",
            "fx_ice", "fx_shard", "fx_snow", "fx_spark", "fx_streak", "fx_cut", "fx_slash", "fx_crescent", "fx_arc", "fx_bigsword",
            "fx_frostorb", "fx_flame", "fx_meteor", "fx_scorch", "fx_star", "fx_aegis", "fx_feather", "fx_holy", "fx_sparkle", "fx_bolt",
            "fx_magic", "fx_dust", "fx_chip", "fx_cosmetic_stars",
        };

        /// <summary>Generated effect pictures (Resources/Art/FxImg) loaded ahead so the first cast does not read the disk.</summary>
        static readonly string[] FxImages =
        {
            "fxi_bigsword", "fxi_bigsword_dark", "fxi_aegis", "fxi_wings", "fxi_bell", "fxi_meteor",
            "fxi_shield_small", "fxi_blackhole", "fxi_crack", "fxi_holy_sigil", "fxi_life_lotus",
        };

        public static void Begin(Transform canvas)
        {
            var go = new GameObject("ResourceWarmup", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var w = go.AddComponent<ResourceWarmup>();
            w.Build((RectTransform)go.transform);
            Busy = true;
            w.StartCoroutine(w.Run());
        }

        void Build(RectTransform root)
        {
            UIFactory.Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            bg.raycastTarget = true; // the title underneath takes no clicks until ready
            label = UIFactory.Text(root, "Label", "게임 리소스를 준비하는 중...", 22, UiTheme.TextPrimary, TextAnchor.MiddleCenter, true);
            UIFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(700f, 40f));
            var track = UIFactory.Image(root, "Track", Game.Art.Get("ui_white"), new Color(1f, 1f, 1f, 0.12f));
            UIFactory.Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(520f, 12f));
            var bar = UIFactory.Image(track.transform, "Fill", Game.Art.Get("ui_white"), UiTheme.Accent);
            fill = bar.rectTransform;
            fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero; fill.sizeDelta = Vector2.zero;
            transform.SetAsLastSibling();
        }

        IEnumerator Run()
        {
            yield return null; // let the title draw first
            var jobs = new List<Action>();
            foreach (var clip in VfxArt.AllClips) { string c = clip; jobs.Add(() => VfxLibrary.Get(c)); }
            foreach (var key in FxKeys) { string k = key; jobs.Add(() => Game.Art.Get(k)); }
            foreach (var img in FxImages) { string i = img; jobs.Add(() => Game.Art.Optional("FxImg/" + i)); }
            var sw = new Stopwatch();
            int done = 0;
            while (done < jobs.Count)
            {
                sw.Restart();
                while (done < jobs.Count && sw.Elapsed.TotalMilliseconds < FrameBudgetMs)
                {
                    try { jobs[done](); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[dotRPG] warmup: " + e.Message); }
                    done++;
                }
                float t = done / (float)jobs.Count;
                fill.sizeDelta = new Vector2(520f * t, 0f);
                label.text = $"게임 리소스를 준비하는 중... {Mathf.RoundToInt(t * 100f)}%";
                yield return null;
            }
            UnityEngine.Debug.Log($"[dotRPG] warmup: {jobs.Count} effects ready in {Time.realtimeSinceStartup:0.0} s since start");
            Busy = false;
            Destroy(gameObject);
        }
    }
}
