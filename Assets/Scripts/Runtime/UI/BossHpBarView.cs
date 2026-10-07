using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [MONSTER] Classic action-RPG boss bar, bottom-centre just above the skill bar: name and level,
    /// a multi-line HP bar ("×N" lines left, each line a different colour so the next one shows through),
    /// a pale lag chunk that drains after a hit, an overall strip with the phase markers, and the 무력화
    /// (groggy) gauge with "GROGGY!" while the boss is down. Super armor hits flash the frame. Binding a boss
    /// plays the intro banner and a screen shake. Binds itself to <see cref="EnemyController.BossSpawned"/>.
    /// </summary>
    public sealed class BossHpBarView : MonoBehaviour
    {
        // ---------- Layout (reference 1280x720) ----------
        // [UI] Bottom centre, 400 wide (bar 376), y 166..258 from the bottom: above the skill bar (top 156) and clear of the
        // top-left status / currency / party blocks at every UI size. At 1.3 (985 wide) it spans x 292..692, left of the
        // 자동 사냥 button (x >= 693); at 1.0 x 440..840. GROGGY / 무력화하라 sits right above it (262..298) and a boss
        // warning above that line (262, or 300 while the groggy line shows; at 1.3 that is 214 from the top, under the
        // party status line at 180..210).
        const float Bottom = 166f, Width = 376f, BarHeight = 22f;
        const float BarBlockHeight = 92f, WarningWidth = 480f, GroggyLineHeight = 36f;
        const float WarningBottom = Bottom + BarBlockHeight + 4f; // 262
        const float LagHold = 0.35f, LagSpeed = 0.9f;
        const float IntroTime = 2.6f, WarningTime = 2.2f;

        static readonly Color[] LineColors =
        {
            new Color32(214, 52, 52, 255),
            new Color32(234, 132, 38, 255),
            new Color32(212, 186, 48, 255),
            new Color32(86, 176, 70, 255),
            new Color32(62, 136, 214, 255),
            new Color32(150, 84, 212, 255),
        };
        static readonly Color LastBack = new Color32(46, 22, 24, 255);
        static readonly Color LagColor = new Color32(255, 236, 196, 255);
        static readonly Color GroggyColor = new Color32(170, 120, 255, 255);
        static readonly Color GroggyFull = new Color32(255, 214, 64, 255);

        public static BossHpBarView Instance { get; private set; }

        /// <summary>The boss this bar shows (null = hidden).</summary>
        public EnemyController Current { get; private set; }

        RectTransform root, banner, warning;
        Image frameGlow, back, lag, front, overall, groggyFill;
        Text nameText, linesText, groggyText, groggyLabel, bannerTitle, bannerSub, warningText;
        RectTransform markers;
        Image[] markerImages = new Image[0];
        float lagHp, lastHp, lagHoldUntil, glowUntil, introAt = -99f, warningAt = -99f, unbindAt = -1f;
        BossBrain brain;

        public static BossHpBarView Create(Transform hud)
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(hud, "BossHpBar"));
            var view = rt.gameObject.AddComponent<BossHpBarView>();
            view.Build(rt);
            Instance = view;
            return view;
        }

        void Awake() => EnemyController.BossSpawned += OnBossSpawned;

        void OnDestroy()
        {
            EnemyController.BossSpawned -= OnBossSpawned;
            BossBrain.Announce -= OnAnnounce;
            if (Instance == this) Instance = null;
        }

        void OnBossSpawned(EnemyController boss) => Bind(boss);

        Image Solid(Transform parent, string n, Color c)
        {
            var img = UIFactory.Image(parent, n, Game.Art.Get("ui_white"), c);
            img.preserveAspect = false;
            return img;
        }

        Image Bar(Transform parent, string n, Color c)
        {
            var img = Solid(parent, n, c);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            UIFactory.Stretch(img.rectTransform);
            return img;
        }

        void Build(RectTransform canvasRoot)
        {
            root = UIFactory.Place(UIFactory.Rect(canvasRoot, "Bar"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Bottom), new Vector2(Width + 24f, BarBlockHeight));
            frameGlow = Solid(root, "Glow", new Color(1f, 0.85f, 0.35f, 0f));
            UIFactory.Stretch(frameGlow.rectTransform, -4, -4, -4, -4);
            var bg = UIFactory.Panel(root, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);

            nameText = UIFactory.Text(root, "Name", "", 20, UIColors.Cream, TextAnchor.MiddleLeft, true);
            UIFactory.Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -6f), new Vector2(Width - 150f, 26f));
            linesText = UIFactory.Text(root, "Lines", "", 22, UIColors.Highlight, TextAnchor.MiddleRight, true);
            UIFactory.Place(linesText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -5f), new Vector2(140f, 28f));

            var barRt = UIFactory.Place(UIFactory.Rect(root, "HpBar"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(Width, BarHeight));
            var frame = Solid(barRt, "Frame", new Color32(20, 12, 10, 255));
            UIFactory.Stretch(frame.rectTransform, -2, -2, -2, -2);
            back = Bar(barRt, "Back", LastBack);
            back.fillAmount = 1f;
            lag = Bar(barRt, "Lag", LagColor);
            front = Bar(barRt, "Front", LineColors[0]);
            var shine = Solid(barRt, "Shine", new Color(1f, 1f, 1f, 0.16f));
            UIFactory.Place(shine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, 6f));
            shine.rectTransform.anchorMax = new Vector2(1f, 1f);
            shine.rectTransform.sizeDelta = new Vector2(0f, 6f);

            // Overall HP strip with the phase markers.
            var strip = UIFactory.Place(UIFactory.Rect(root, "Overall"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(Width, 5f));
            var stripBg = Solid(strip, "Bg", new Color32(30, 18, 16, 255));
            UIFactory.Stretch(stripBg.rectTransform, -1, -1, -1, -1);
            overall = Bar(strip, "Fill", new Color32(236, 96, 80, 255));
            markers = UIFactory.Stretch(UIFactory.Rect(strip, "Markers"));

            // Groggy gauge.
            groggyLabel = UIFactory.Text(root, "GroggyLabel", "무력화", UiTheme.FontMin, new Color32(214, 196, 255, 255), TextAnchor.MiddleLeft, true);
            UIFactory.Place(groggyLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -66f), new Vector2(64f, 22f));
            var gRt = UIFactory.Place(UIFactory.Rect(root, "Groggy"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(36f, -73f), new Vector2(Width - 72f, 9f));
            var gBg = Solid(gRt, "Bg", new Color32(28, 20, 40, 255));
            UIFactory.Stretch(gBg.rectTransform, -1, -1, -1, -1);
            groggyFill = Bar(gRt, "Fill", GroggyColor);
            groggyText = UIFactory.Text(root, "Groggy", "", 30, GroggyFull, TextAnchor.MiddleCenter, true);
            var go = groggyText.gameObject.AddComponent<Outline>();
            go.effectColor = new Color(0.15f, 0.05f, 0.1f, 1f);
            go.effectDistance = new Vector2(2f, -2f);
            UIFactory.Place(groggyText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(Width + 24f, GroggyLineHeight)); // above the bar

            // Intro banner (screen centre-top) and warnings (under the bar).
            banner = UIFactory.Place(UIFactory.Rect(canvasRoot, "BossBanner"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(UiTheme.HudBannerWidth, 120f)); // [UI] centre column only: clear of party frames / quest tracker
            var bb = Solid(banner, "Band", new Color(0.05f, 0.02f, 0.02f, 0.72f));
            UIFactory.Stretch(bb.rectTransform);
            var line1 = Solid(banner, "LineTop", new Color(0.85f, 0.2f, 0.18f, 0.9f));
            UIFactory.Place(line1.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(UiTheme.HudBannerWidth, 3f));
            var line2 = Solid(banner, "LineBottom", new Color(0.85f, 0.2f, 0.18f, 0.9f));
            UIFactory.Place(line2.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(UiTheme.HudBannerWidth, 3f));
            bannerSub = UIFactory.Text(banner, "Sub", "", 20, new Color32(255, 150, 130, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Place(bannerSub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(UiTheme.HudBannerWidth - 20f, 28f));
            bannerTitle = UIFactory.Text(banner, "Title", "", 52, Color.white, TextAnchor.MiddleCenter, true);
            var bo = bannerTitle.gameObject.AddComponent<Outline>();
            bo.effectColor = new Color(0.45f, 0.05f, 0.05f, 1f);
            bo.effectDistance = new Vector2(3f, -3f);
            UIFactory.Place(bannerTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(UiTheme.HudBannerWidth - 20f, 70f));
            banner.gameObject.SetActive(false);

            // [CONTENT] Right above the bar, inside the same centre column (clear of party frames and quest panel).
            warning = UIFactory.Place(UIFactory.Rect(canvasRoot, "BossWarning"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, WarningBottom), new Vector2(WarningWidth, 40f));
            warningText = UIFactory.Text(warning, "Text", "", 28, Color.white, TextAnchor.MiddleCenter, true);
            var wo = warningText.gameObject.AddComponent<Outline>();
            wo.effectColor = new Color(0.1f, 0.02f, 0.02f, 1f);
            wo.effectDistance = new Vector2(2f, -2f);
            UIFactory.Stretch(warningText.rectTransform);
            warning.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
            BossBrain.Announce += OnAnnounce;
        }

        // =============================== Binding ===============================

        /// <summary>Shows <paramref name="boss"/> on the bar (intro banner + shake).</summary>
        public void Bind(EnemyController boss)
        {
            if (boss == null) return;
            Unbind();
            Current = boss;
            brain = boss.Behaviour as BossBrain;
            lagHp = lastHp = boss.Health.Current;
            unbindAt = -1f;
            boss.SuperArmorHit += OnSuperArmor;
            BuildMarkers();
            root.gameObject.SetActive(true);
            string level = boss.Level > 1 ? $"  <size=16><color=#d8c8a8>Lv.{boss.Level}</color></size>" : "";
            nameText.text = boss.DisplayName + level;
            bannerTitle.text = boss.DisplayName;
            bannerSub.text = boss.Def != null && boss.Def.raid ? "· 레이드 보스 ·" : "· 던전 보스 ·";
            introAt = Time.unscaledTime;
            banner.gameObject.SetActive(true);
            Game.Camera?.Shake(0.25f, 0.4f);
            Refresh();
        }

        public void Unbind()
        {
            if (Current != null) Current.SuperArmorHit -= OnSuperArmor;
            Current = null;
            brain = null;
            root.gameObject.SetActive(false);
            banner.gameObject.SetActive(false);
        }

        void OnSuperArmor(EnemyController e) => glowUntil = Time.unscaledTime + 0.18f;

        void OnAnnounce(BossBrain b, string text, Color color)
        {
            if (Current == null || b == null || b.Controller != Current) return;
            warningText.text = text;
            warningText.color = color;
            warningAt = Time.unscaledTime;
            warning.gameObject.SetActive(true);
        }

        void BuildMarkers()
        {
            foreach (var m in markerImages) if (m != null) Destroy(m.gameObject);
            var phases = Current.Def != null ? Current.Def.phases : null;
            int n = phases != null ? phases.Length : 0;
            markerImages = new Image[n];
            for (int i = 0; i < n; i++)
            {
                var img = Solid(markers, "Phase" + i, new Color32(255, 230, 150, 255));
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(phases[i], 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(3f, 13f);
                rt.anchoredPosition = Vector2.zero;
                markerImages[i] = img;
            }
        }

        // =============================== Frame ===============================

        void Update()
        {
            float now = Time.unscaledTime;
            UpdateBanners(now);
            if (Current == null)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            if (Current.IsDead || Current.Health == null)
            {
                if (unbindAt < 0f) unbindAt = now + 1.2f;
                if (now >= unbindAt) { Unbind(); return; }
            }
            Refresh();
        }

        void UpdateBanners(float now)
        {
            if (banner.gameObject.activeSelf)
            {
                float t = (now - introAt) / IntroTime;
                // [UI] A boss killed during its intro must not leave the banner under the CLEAR banner.
                var run = Game.Dungeon != null ? Game.Dungeon.Run : null;
                if (run != null && run.State == DungeonRunState.Cleared) t = 1f;
                if (t >= 1f) banner.gameObject.SetActive(false);
                else
                {
                    float s = t < 0.12f ? Mathf.Lerp(1.6f, 1f, t / 0.12f) : 1f;
                    bannerTitle.rectTransform.localScale = new Vector3(s, s, 1f);
                    float a = t < 0.1f ? t / 0.1f : t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f;
                    SetAlpha(banner, a);
                }
            }
            if (warning.gameObject.activeSelf)
            {
                // Above the GROGGY / 무력화하라 line while it shows, else right on the bar.
                float wy = groggyText.text.Length > 0 && root.gameObject.activeSelf ? WarningBottom + GroggyLineHeight + 2f : WarningBottom;
                if (!Mathf.Approximately(warning.anchoredPosition.y, wy)) warning.anchoredPosition = new Vector2(0f, wy);
                float t = (now - warningAt) / WarningTime;
                if (t >= 1f) warning.gameObject.SetActive(false);
                else
                {
                    float s = t < 0.1f ? Mathf.Lerp(1.4f, 1f, t / 0.1f) : 1f;
                    warning.localScale = new Vector3(s, s, 1f);
                    var c = warningText.color;
                    c.a = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                    warningText.color = c;
                }
            }
        }

        static void SetAlpha(RectTransform rt, float a)
        {
            var group = rt.GetComponent<CanvasGroup>();
            if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = a;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        void Refresh()
        {
            var boss = Current;
            var hp = boss.Health;
            int lines = Mathf.Max(1, boss.Def != null ? boss.Def.hpLines : 1);
            float perLine = (float)hp.Max / lines;
            float cur = hp.Current;

            // Lag chunk: holds a moment after a hit, then drains.
            if (cur < lastHp) lagHoldUntil = Time.unscaledTime + LagHold;
            lastHp = cur;
            if (cur > lagHp) lagHp = cur;
            if (lagHp > cur && Time.unscaledTime >= lagHoldUntil)
                lagHp = Mathf.MoveTowards(lagHp, cur, Mathf.Max(perLine * 0.4f, hp.Max * 0.02f) * LagSpeed * Time.unscaledDeltaTime * 2f);

            int line = cur <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(cur / perLine - 0.0001f), 1, lines);
            float bottom = (line - 1) * perLine;
            front.fillAmount = line > 0 ? Mathf.Clamp01((cur - bottom) / perLine) : 0f;
            front.color = LineColors[Mathf.Max(0, line - 1) % LineColors.Length];
            back.color = line > 1 ? LineColors[(line - 2) % LineColors.Length] : LastBack;
            lag.fillAmount = lagHp >= bottom + perLine ? 1f : Mathf.Clamp01((lagHp - bottom) / perLine);
            linesText.text = line > 1 ? $"×{line}" : line == 1 ? "×1" : "";
            overall.fillAmount = hp.Max > 0 ? cur / hp.Max : 0f;

            // Groggy.
            bool hasGauge = boss.GroggyMax > 0f;
            groggyFill.transform.parent.gameObject.SetActive(hasGauge);
            groggyLabel.gameObject.SetActive(hasGauge);
            if (boss.IsGroggy)
            {
                float k = boss.GroggyDuration > 0f ? boss.GroggyRemaining / boss.GroggyDuration : 0f;
                groggyFill.fillAmount = k;
                groggyFill.color = Color.Lerp(GroggyFull, Color.white, Mathf.PingPong(Time.unscaledTime * 4f, 1f) * 0.5f);
                groggyText.text = $"GROGGY!  <size=20>{boss.GroggyRemaining:0.0}</size>";
                float s = 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.05f;
                groggyText.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                groggyFill.fillAmount = hasGauge ? boss.Groggy / boss.GroggyMax : 0f;
                groggyFill.color = brain != null && brain.CastingJudgment ? Color.Lerp(GroggyColor, new Color(1f, 0.3f, 0.3f), Mathf.PingPong(Time.unscaledTime * 3f, 1f)) : GroggyColor;
                groggyText.text = brain != null && brain.CastingJudgment ? $"<color=#ff7a6a>무력화하라!  <size=20>{(1f - brain.JudgmentProgress) * BossBrain.JudgmentCast:0.0}</size></color>" : "";
                groggyText.rectTransform.localScale = Vector3.one;
            }

            // Super armor flash.
            float g = Mathf.Clamp01((glowUntil - Time.unscaledTime) / 0.18f);
            frameGlow.color = new Color(1f, 0.85f, 0.35f, g * 0.85f);
        }

        // ---------- Test hooks ----------
        public string DevLinesText => linesText != null ? linesText.text : "";
        public bool DevVisible => root != null && root.gameObject.activeInHierarchy;
        public string DevGroggyText => groggyText != null ? groggyText.text : "";
        public int DevMarkers => markerImages.Length;
    }
}
