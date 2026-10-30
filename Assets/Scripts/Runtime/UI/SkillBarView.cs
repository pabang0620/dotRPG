using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Bottom-centre skill bar: eight slots, Q W E R A S D and the larger awakening slot T last
    /// (<see cref="SkillGems.BarOrder"/>), with key, icon, MP cost, a radial cooldown with the seconds left,
    /// and a lock with the level a slot opens at. An open A/S/D key with nothing on it shows empty.
    /// </summary>
    public class SkillBarView : MonoBehaviour
    {
        // [P5] Last shown values per slot, so texts are formatted only when they change.
        readonly int[] timerShown = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        readonly int[] costShown = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        readonly int[] lockShown = { -1, -1, -1, -1, -1, -1, -1, -1 };

        // [SKILL 8] Smaller buttons so eight fit (was 70 / 84 / 10 for five).
        const float Size = 58f, UltSize = 70f, Gap = 6f;
        /// <summary>Width of the bar (bottom-centre); the potion bar sits right of it.</summary>
        public static float Width => (SkillGems.Slots - 1) * (Size + Gap) + UltSize;
        static readonly Color UltGold = new Color(1f, 0.84f, 0.42f, 1f);

        readonly Image[] frames = new Image[SkillGems.Slots];
        readonly Image[] icons = new Image[SkillGems.Slots];
        // [CH3 SPEC] Crimson (A) / silver (B) frame over the icon of a specialized skill.
        readonly Image[] specFrames = new Image[SkillGems.Slots];
        readonly char[] specShown = new char[SkillGems.Slots];
        readonly Image[] cooldowns = new Image[SkillGems.Slots];
        readonly Image[] locks = new Image[SkillGems.Slots];
        readonly Text[] keys = new Text[SkillGems.Slots];
        readonly Text[] costs = new Text[SkillGems.Slots];
        readonly Text[] timers = new Text[SkillGems.Slots];
        readonly Text[] lockTexts = new Text[SkillGems.Slots];
        // [UI] A toggle skill that is ON (검기 태세): a faint rim plus two bright dashes running clockwise around the slot.
        readonly Image[][] rims = new Image[SkillGems.Slots][];
        readonly Image[][] runners = new Image[SkillGems.Slots][];
        readonly float[] slotSize = new float[SkillGems.Slots];
        // [UI] Range preview: hovering a slot draws its area on the ground (SkillRangePreview); its text sits above the bar.
        readonly float[] slotX = new float[SkillGems.Slots];
        Text rangeLabel;
        string rangeShown;

        public static SkillBarView Create(Transform parent)
        {
            float width = (SkillGems.Slots - 1) * (Size + Gap) + UltSize;
            var root = UIFactory.Place(UIFactory.Rect(parent, "SkillBar"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(width, UltSize + 22f));
            var v = root.gameObject.AddComponent<SkillBarView>();
            float x = 0f;
            foreach (int i in SkillGems.BarOrder)
            {
                bool ult = i == SkillGems.UltimateSlot;
                float s = ult ? UltSize : Size;
                var bg = UIFactory.Image(root, "Slot" + i, Game.Art.Get("ui_slot"), ult ? UltGold : Color.white);
                bg.preserveAspect = false;
                UIFactory.Place(bg.rectTransform, Vector2.zero, Vector2.zero, new Vector2(x, 0f), new Vector2(s, s));
                v.frames[i] = bg;
                v.slotX[i] = x + s / 2f;
                bg.raycastTarget = true; // [UI] hover = range preview (the slot has no click action)
                bg.gameObject.AddComponent<SkillRangeHover>().slot = i;

                v.icons[i] = UIFactory.Image(bg.transform, "Icon", null, Color.white);
                UIFactory.Place(v.icons[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s * 0.7f, s * 0.7f));
                v.specFrames[i] = UIFactory.Image(bg.transform, "SpecFrame", null, Color.white);
                UIFactory.Place(v.specFrames[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s * 0.8f, s * 0.8f));
                v.specFrames[i].enabled = false;

                var cd = UIFactory.Image(bg.transform, "Cooldown", Game.Art.Get("ui_white"), new Color(0f, 0f, 0f, 0.6f));
                cd.preserveAspect = false;
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                UIFactory.Stretch(cd.rectTransform, 3f, 3f, 3f, 3f);
                v.cooldowns[i] = cd;

                v.locks[i] = UIFactory.Image(bg.transform, "Lock", Game.Art.Get("ui_white"), new Color(0.02f, 0.03f, 0.06f, 0.62f));
                v.locks[i].preserveAspect = false;
                UIFactory.Stretch(v.locks[i].rectTransform, 3f, 3f, 3f, 3f);
                // [ART] Padlock above the opening level (it lives on the dark cover, so it hides with it).
                var padlock = UIFactory.Image(v.locks[i].transform, "Padlock", Game.Art.Get("ui_lock"), Color.white);
                padlock.preserveAspect = true;
                UIFactory.Place(padlock.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(20f, 20f));
                v.lockTexts[i] = UIFactory.Text(bg.transform, "LockText", "", 14, new Color32(220, 226, 240, 255), TextAnchor.LowerCenter, true);
                UIFactory.Stretch(v.lockTexts[i].rectTransform, 0f, 3f, 0f, 0f);

                v.timers[i] = UIFactory.Text(bg.transform, "Timer", "", ult ? 21 : 18, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(v.timers[i].rectTransform);
                // [UI 2026-10-29] The key sits on its own dark tag in the top-left corner: drawn straight over the skill
                // picture it vanished on bright icons (only the empty S / D slots showed theirs).
                var keyTag = UIFactory.Image(bg.transform, "KeyTag", Game.Art.Get("ui_white"), new Color(0.03f, 0.04f, 0.08f, 0.82f));
                keyTag.preserveAspect = false;
                keyTag.raycastTarget = false;
                UIFactory.Place(keyTag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -3f), new Vector2(Mathf.Round(s * 0.55f), 19f));
                v.keys[i] = UIFactory.Text(keyTag.transform, "Key", "", 14, new Color32(255, 224, 102, 255), TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(v.keys[i].rectTransform, 2f, 0f, 2f, 0f);
                v.costs[i] = UIFactory.Text(bg.transform, "Cost", "", UiTheme.FontMin, new Color32(140, 190, 255, 255), TextAnchor.LowerRight, true);
                UIFactory.Stretch(v.costs[i].rectTransform, 2f, 5f, 7f, 2f);
                // [UI] A long rebound key ("Space") or "각성 시련" shrinks inside the smaller slot instead of wrapping out of it.
                foreach (var t in new[] { v.keys[i], v.lockTexts[i], v.costs[i] })
                {
                    t.resizeTextForBestFit = true;
                    t.resizeTextMinSize = UIFactory.ReadableFloor; // [UI 2026-10-30] was 9; the key tag grows with the key name instead
                    t.resizeTextMaxSize = t.fontSize;
                    t.verticalOverflow = VerticalWrapMode.Truncate;
                }
                v.slotSize[i] = s;
                v.rims[i] = new Image[4];
                for (int e = 0; e < 4; e++)
                {
                    var r = UIFactory.Image(bg.transform, "Rim" + e, Game.Art.Get("ui_white"), new Color(.55f, .85f, 1f, .55f));
                    r.preserveAspect = false;
                    r.raycastTarget = false;
                    bool horizontal = e % 2 == 0;
                    r.rectTransform.anchorMin = r.rectTransform.anchorMax = Vector2.zero;
                    r.rectTransform.pivot = new Vector2(.5f, .5f);
                    r.rectTransform.sizeDelta = horizontal ? new Vector2(s - 4f, 2f) : new Vector2(2f, s - 4f);
                    r.rectTransform.anchoredPosition = e == 0 ? new Vector2(s / 2f, s - 2f) : e == 1 ? new Vector2(s - 2f, s / 2f) : e == 2 ? new Vector2(s / 2f, 2f) : new Vector2(2f, s / 2f);
                    r.enabled = false;
                    v.rims[i][e] = r;
                }
                v.runners[i] = new Image[2];
                for (int k = 0; k < 2; k++)
                {
                    var r = UIFactory.Image(bg.transform, "Runner" + k, Game.Art.Get("ui_white"), new Color(.85f, .97f, 1f, 1f));
                    r.preserveAspect = false;
                    r.raycastTarget = false;
                    r.rectTransform.anchorMin = r.rectTransform.anchorMax = Vector2.zero;
                    r.rectTransform.pivot = new Vector2(.5f, .5f);
                    r.enabled = false;
                    v.runners[i][k] = r;
                }
                x += s + Gap;
            }
            // Caption above the awakening slot.
            var caption = UIFactory.Text(root, "UltCaption", L.T("각성"), UiTheme.FontMin, new Color32(255, 214, 110, 255), TextAnchor.LowerCenter, true);
            UIFactory.Place(caption.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(width - UltSize * 0.5f, UltSize + 1f), new Vector2(UltSize + 20f, 20f));
            // Above the buff row (which sits right over the bar).
            v.rangeLabel = UIFactory.Text(root, "RangeLabel", "", 16, new Color32(255, 240, 200, 255), TextAnchor.LowerCenter, true);
            v.rangeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.rangeLabel.raycastTarget = false;
            UIFactory.Place(v.rangeLabel.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(width / 2f, UltSize + 22f + 36f), new Vector2(width, 22f));
            v.rangeLabel.enabled = false;
            return v;
        }

        /// <summary>[CH3 SPEC] The generated frame of a specialization side (Art/spec_frame_a / _b), null for none or while missing.</summary>
        public static Sprite SpecFrame(char side) =>
            side == 'a' ? Game.Art.Optional("spec_frame_a") : side == 'b' ? Game.Art.Optional("spec_frame_b") : null;

        void ShowSpec(int i, char side)
        {
            if (specShown[i] == side) return;
            specShown[i] = side;
            var frame = SpecFrame(side);
            specFrames[i].sprite = frame;
            specFrames[i].enabled = frame != null;
        }

        /// <summary>Rim + two dashes travelling clockwise (one lap per 1.2 s) while a toggle skill is on.</summary>
        void ShowToggle(int i, bool on)
        {
            if (rims[i] == null) return;
            foreach (var r in rims[i]) if (r.enabled != on) r.enabled = on;
            foreach (var r in runners[i]) if (r.enabled != on) r.enabled = on;
            if (!on) return;
            float s = slotSize[i], inset = 2f, len = s - inset * 2f;
            for (int k = 0; k < 2; k++)
            {
                float t = Mathf.Repeat(Time.unscaledTime / 1.2f + k * .5f, 1f) * 4f; // 0..4 around the square
                int side = Mathf.Min(3, (int)t);
                float f = t - side;
                Vector2 pos;
                switch (side)
                {
                    case 0: pos = new Vector2(inset + f * len, s - inset); break;      // top, left -> right
                    case 1: pos = new Vector2(s - inset, s - inset - f * len); break;  // right, top -> bottom
                    case 2: pos = new Vector2(s - inset - f * len, inset); break;      // bottom, right -> left
                    default: pos = new Vector2(inset, inset + f * len); break;         // left, bottom -> top
                }
                var rt = runners[i][k].rectTransform;
                rt.anchoredPosition = pos;
                rt.sizeDelta = side % 2 == 0 ? new Vector2(s * .28f, 4f) : new Vector2(4f, s * .28f);
            }
        }

        void Update()
        {
            var player = Game.Player;
            if (player == null || player.Skills == null) return;
            var prog = Game.Session.Progression;
            // Range preview text over the hovered slot.
            int hovered = SkillRangePreview.Slot;
            string text = hovered >= 0 ? SkillRangePreview.Label : "";
            if (text != rangeShown)
            {
                rangeShown = text;
                rangeLabel.text = text;
                rangeLabel.enabled = text.Length > 0;
                if (hovered >= 0) rangeLabel.rectTransform.anchoredPosition = new Vector2(slotX[hovered], rangeLabel.rectTransform.anchoredPosition.y);
            }
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                var gem = prog.Active(i);
                ShowToggle(i, gem != null && CareerCatalog.Get(gem.id)?.effect == "stance" && CareerCombat.For(player).StanceOn);
                var shown = gem ?? (i==4 ? prog.IsPromoted ? CareerCatalog.For(prog.Career)[8].Gem : null : SkillGems.ForSlot(player.Class, i));
                string keyName = Game.Input.GetBindingLabel(SkillGems.ActionFor(i));
                if (keys[i].text != keyName)
                {
                    keys[i].text = keyName;
                    // [UI 2026-10-30] A long key ("Space") widens its tag (up to the slot) so it stays at the readable floor.
                    var tag = (RectTransform)keys[i].transform.parent;
                    tag.sizeDelta = new Vector2(Mathf.Clamp(8f + keyName.Length * 9f, Mathf.Round(slotSize[i] * 0.55f), slotSize[i] - 6f), tag.sizeDelta.y);
                }
                icons[i].enabled = shown != null;
                if (shown != null) icons[i].sprite = Game.Art.Get(CareerMoves.IconKey(shown.icon));
                ShowSpec(i, gem != null ? prog.SpecOf(gem.id) : '\0');
                bool open = gem != null;
                // [SKILL 8] An A/S/D key that is open but has nothing on it: an empty slot, not a lock.
                bool empty = !open && i != SkillGems.UltimateSlot && prog.IsSlotOpen(i);
                locks[i].gameObject.SetActive(!open && !empty); // the padlock child goes with it
                lockTexts[i].enabled = !open && !empty;
                if (empty)
                {
                    icons[i].enabled = false;
                    cooldowns[i].fillAmount = 0f;
                    costs[i].text = "";
                    timers[i].text = "";
                    timerShown[i] = costShown[i] = int.MinValue;
                    continue;
                }
                if (!open)
                {
                    if (lockShown[i] != Progression.SlotLevel(i)) { lockShown[i] = Progression.SlotLevel(i); lockTexts[i].text = i==4 ? "" : $"Lv.{lockShown[i]}"; } // [UI 2026-10-30] the awakening slot: padlock over the icon, "각성" caption under it (the trial name shrank to 9 px)
                    icons[i].color = new Color(0.45f, 0.45f, 0.52f, 1f);
                    cooldowns[i].fillAmount = 0f;
                    costs[i].text = "";
                    timers[i].text = "";
                    timerShown[i] = costShown[i] = int.MinValue;
                    if (i == SkillGems.UltimateSlot) frames[i].color = Color.Lerp(UltGold, Color.gray, 0.5f);
                    continue;
                }
                var n = player.Skills.Numbers(i);
                float p = player.Skills.CooldownProgress(i, out float left);
                cooldowns[i].fillAmount = 1f - p;
                // [P5] Format the countdown only when the shown digits change.
                int tKey = left > 0.05f ? (left >= 10f ? Mathf.RoundToInt(left) * 10 : Mathf.RoundToInt(left * 10f)) : -1;
                if (left >= 10f) tKey += 100000;
                if (tKey != timerShown[i]) { timerShown[i] = tKey; timers[i].text = left > 0.05f ? (left >= 10f ? $"{left:0}" : $"{left:0.0}") : ""; }
                bool affordable = n.usesLife ? player.Health.Current > n.manaCost : player.Mana >= n.manaCost;
                icons[i].color = affordable ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f);
                int cKey = n.manaCost * 2 + (n.usesLife ? 1 : 0);
                if (cKey != costShown[i]) { costShown[i] = cKey; costs[i].text = n.usesLife ? $"<color=#ff8080>{n.manaCost}</color>" : n.manaCost.ToString(); }
                if (i == SkillGems.UltimateSlot)
                {
                    // The awakening slot glows when it is ready.
                    float glow = left <= 0f && affordable ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
                    frames[i].color = Color.Lerp(UltGold, Color.white, glow * 0.6f);
                }
            }
        }
    }

    /// <summary>Big banner across the screen when an awakening skill is cast ("각성 · 천검 강림").</summary>
    public class AwakeningBanner : MonoBehaviour
    {
        const float Life = 1.7f;
        CanvasGroup group;
        Image lineTop, lineBottom;
        Text caption, title;
        float shownAt = -10f;
        Color color = Color.white;

        public static AwakeningBanner Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Awakening"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(UiTheme.HudBannerWidth, 112f)); // [UI] centre column
            var b = root.gameObject.AddComponent<AwakeningBanner>();
            b.group = root.gameObject.AddComponent<CanvasGroup>();
            b.group.blocksRaycasts = false;
            b.group.interactable = false;
            b.group.alpha = 0f;
            UIFactory.Overlay(root, "Band", new Color(0.02f, 0.02f, 0.06f, 0.5f));
            b.lineTop = UIFactory.Image(root, "LineTop", Game.Art.Get("ui_white"), Color.white);
            b.lineTop.preserveAspect = false;
            UIFactory.Place(b.lineTop.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(UiTheme.HudBannerWidth, 3f));
            b.lineBottom = UIFactory.Image(root, "LineBottom", Game.Art.Get("ui_white"), Color.white);
            b.lineBottom.preserveAspect = false;
            UIFactory.Place(b.lineBottom.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(UiTheme.HudBannerWidth, 3f));
            b.caption = UIFactory.Text(root, "Caption", L.T("· 각 성 ·"), 20, new Color32(255, 236, 180, 255), TextAnchor.UpperCenter, true);
            UIFactory.Place(b.caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(UiTheme.HudBannerWidth - 20f, 28f));
            b.title = UIFactory.Text(root, "Title", "", 46, Color.white, TextAnchor.MiddleCenter, true);
            var outline = b.title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.05f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);
            UIFactory.Place(b.title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(UiTheme.HudBannerWidth - 20f, 62f));
            return b;
        }

        void OnEnable() => GameEvents.Awakening += Show;

        void OnDisable() => GameEvents.Awakening -= Show;

        void Show(string skillName, Color themeColor)
        {
            title.text = L.T(skillName);
            color = themeColor;
            shownAt = Time.unscaledTime;
        }

        void Update()
        {
            float t = Time.unscaledTime - shownAt;
            if (t < 0f || t > Life)
            {
                if (group.alpha != 0f) group.alpha = 0f;
                return;
            }
            group.alpha = t < 0.12f ? t / 0.12f : t > Life - 0.4f ? (Life - t) / 0.4f : 1f;
            // The name slides in from the right, then drifts slowly.
            float k = Mathf.Clamp01(t / 0.2f);
            float slide = t < 0.2f ? Mathf.Lerp(260f, 0f, 1f - (1f - k) * (1f - k)) : -(t - 0.2f) * 18f;
            title.rectTransform.anchoredPosition = new Vector2(slide, 12f);
            title.color = Color.Lerp(Color.white, color, 0.5f);
            lineTop.color = lineBottom.color = color;
        }
    }
}
