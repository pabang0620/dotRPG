using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace DotRPG
{
    /// <summary>
    /// [TRAILER] 10-second raid boss trailer (Resources/Video/trailer_&lt;dungeon id&gt;), full screen over every UI.
    /// Plays once on the first 입장 of each raid on this device, and again from the banner in the 던전 window.
    /// Any key, click or touch skips it. Real-time clock, so it runs while an offline menu has frozen the world.
    /// </summary>
    public sealed class BossTrailer : MonoBehaviour
    {
        const string SeenKey = "boss_trailer_seen_";
        const float SkipDelay = 0.5f, CaptionAt = 1.2f, CaptionFade = 0.6f;

        static readonly Dictionary<string, VideoClip> clips = new Dictionary<string, VideoClip>();
        static BossTrailer current;
        static int blockUntilFrame = -1;

        VideoPlayer player;
        RenderTexture texture;
        CanvasGroup caption;
        Action done;
        float startTime;
        bool finished;

        /// <summary>True while a trailer is on screen and on the frame it closes (so the skipping key does nothing else).</summary>
        public static bool Blocking => current != null || Time.frameCount <= blockUntilFrame;

        public static bool Has(DungeonDef def) => Clip(def) != null;

        /// <summary>Plays the trailer if this raid's was never shown on this device. False: nothing to show, call on.</summary>
        public static bool PlayOnce(DungeonDef def, Action onDone)
        {
            if (def == null || !def.isRaid || !Has(def) || PlayerPrefs.GetInt(SeenKey + def.id, 0) == 1) return false;
            if (FindAnyObjectByType<DevCapture>() != null) return false; // automated runs enter straight away
            PlayerPrefs.SetInt(SeenKey + def.id, 1);
            PlayerPrefs.Save();
            return Play(def, onDone);
        }

        public static bool Play(DungeonDef def, Action onDone = null)
        {
            var clip = Clip(def);
            if (clip == null || current != null) return false;
            var go = new GameObject("BossTrailer");
            DontDestroyOnLoad(go);
            current = go.AddComponent<BossTrailer>();
            current.Build(def, clip, onDone);
            return true;
        }

        static VideoClip Clip(DungeonDef def)
        {
            if (def == null) return null;
            if (!clips.TryGetValue(def.id, out var clip))
            {
                clip = Resources.Load<VideoClip>("Video/trailer_" + def.id);
                clips[def.id] = clip;
            }
            return clip;
        }

        void Build(DungeonDef def, VideoClip clip, Action onDone)
        {
            done = onDone;
            startTime = Time.unscaledTime;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UIFactory.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Black backdrop takes every click so nothing under the trailer reacts.
            var back = UIFactory.Image(transform, "Back", null, Color.black);
            back.raycastTarget = true;
            UIFactory.Stretch(back.rectTransform);

            texture = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "BossTrailer" };
            var screen = UIFactory.Rect(transform, "Screen").gameObject.AddComponent<RawImage>();
            screen.texture = texture;
            screen.raycastTarget = false;
            UIFactory.Stretch(screen.rectTransform);
            var fit = screen.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = (float)clip.width / clip.height;

            // Caption: raid name over the boss name, bottom left, fades in after the first beat.
            var box = UIFactory.Rect(transform, "Caption");
            UIFactory.Place(box, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 40f), new Vector2(760f, 110f));
            caption = box.gameObject.AddComponent<CanvasGroup>();
            caption.alpha = 0f;
            var raid = UIFactory.Text(box, "Raid", L.F("레이드 · {0}", L.T(def.themeName)), 20, new Color32(200, 210, 230, 255), TextAnchor.LowerLeft, true);
            UIFactory.Place(raid.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(760f, 30f));
            var boss = UIFactory.Text(box, "Boss", $"<b>{L.T(def.bossName)}</b>", 44, UIColors.Highlight, TextAnchor.UpperLeft, true);
            UIFactory.Place(boss.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -32f), new Vector2(760f, 70f));
            var outline = boss.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);

            // Skip plate, top right like the cutscene one: click / tap it (or press any key) to skip at once.
            var plate = UIFactory.Place(UIFactory.Rect(transform, "SkipPlate"), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f),
                TouchUi.Enabled ? new Vector2(230f, 60f) : new Vector2(250f, 46f));
            var plateBg = UIFactory.Panel(plate, "Bg", true);
            UIFactory.Stretch(plateBg.rectTransform);
            plateBg.raycastTarget = true;
            var skipButton = plate.gameObject.AddComponent<Button>();
            skipButton.targetGraphic = plateBg;
            skipButton.onClick.AddListener(Finish);
            var skip = UIFactory.Text(plate, "Text", TouchUi.Enabled ? L.T("장면 건너뛰기") : "<color=#ffd34a><b>ESC</b></color>  " + L.T("장면 건너뛰기"),
                20, UIColors.Cream, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(skip.rectTransform, 8, 0, 8, 0);

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.SetDirectAudioVolume(0, Game.Audio != null ? Game.Audio.TrailerVolume : 0.7f);
            player.loopPointReached += _ => Finish();
            player.errorReceived += (_, message) => { Debug.LogWarning("[dotRPG] Boss trailer: " + message); Finish(); };
            player.prepareCompleted += p => p.Play();
            player.Prepare();
            if (Game.Audio != null) Game.Audio.MusicMuted = true;
        }

        void Update()
        {
            if (finished) return;
            float t = Time.unscaledTime - startTime;
            if (caption != null) caption.alpha = Mathf.Clamp01((t - CaptionAt) / CaptionFade);
            if (t < SkipDelay) return; // a stray key from 입장 does not skip; the plate works from the first frame
            if (UnityEngine.Input.anyKeyDown || UnityEngine.Input.touchCount > 0 && UnityEngine.Input.GetTouch(0).phase == TouchPhase.Began) Finish();
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            blockUntilFrame = Time.frameCount + 1;
            if (Game.Audio != null) Game.Audio.MusicMuted = false;
            if (player != null) player.Stop();
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (current == this) current = null;
            Destroy(gameObject);
            var next = done;
            done = null;
            next?.Invoke();
        }

        void OnDestroy()
        {
            if (current == this) current = null;
            if (!finished && Game.Audio != null) Game.Audio.MusicMuted = false;
        }
    }
}
