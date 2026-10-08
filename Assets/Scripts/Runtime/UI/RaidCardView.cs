using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Drives a <see cref="RaidCardView"/>'s glows and effects every frame on unscaled time (the windows freeze the world).</summary>
    public sealed class RaidCardTicker : MonoBehaviour
    {
        public RaidCardView view;

        void Update() => view?.Tick(Time.unscaledDeltaTime);
    }

    /// <summary>
    /// [RAID] One reward card of the raid result window and of the raid shop's box popup
    /// (Docs/server/phase13_raid_rewards.md §7.4): a face-down card that breathes, and a flip whose staging depends on
    /// what is on the card (material / ticket plain, epic, unique, legendary). Everything runs on unscaled time.
    /// </summary>
    public sealed class RaidCardView
    {
        public enum Kind { Material, Ticket, Epic, Unique, Legendary }

        public const float BaseW = 150f, BaseH = 210f;
        const float PressTime = 0.08f, FoldTime = 0.14f, UnfoldTime = 0.14f, BounceTime = 0.1f;
        static readonly Color Ink = new Color32(42, 28, 16, 255);
        static readonly Color LegendInk = new Color32(255, 240, 208, 255);
        static readonly Color BoneGold = new Color32(216, 179, 106, 255);
        static readonly Color EpicColor = new Color32(183, 123, 255, 255);
        static readonly Color UniqueColor = new Color32(255, 225, 90, 255);
        static readonly Color LegendColor = new Color32(255, 138, 61, 255);
        const int SparkCount = 12;

        public readonly RectTransform FrameRt;
        public readonly Image Frame, Card;
        public readonly Text Who;
        readonly Image glow, rays, ring, icon;
        readonly Text band, face;
        readonly CanvasGroup group;
        readonly float scale;
        readonly Vector2 basePos;
        Image[] sparks;
        Text legendText;

        /// <summary>Set by the result window: darkens the other cards (0..1) while a legendary card is revealed.</summary>
        public Action<float> DimOthers;
        /// <summary>Set by the result window: a full-panel white image for the legendary flash.</summary>
        public Image Flash;

        public bool Flipped { get; private set; }
        public Kind CardKind { get; private set; }
        public RewardCard Content { get; private set; }

        float clock;
        bool breathBack = true;
        Color lingerColor;
        float lingerLow, lingerHigh;
        bool lingering;
        bool raysSpin;
        float ringT, sparkT, textT, popT, flashT, glowFlashT;
        float flashWhite;

        RaidCardView(Transform parent, string name, Vector2 anchor, Vector2 pos, float scale, Action onClick, Func<string> tooltipKey)
        {
            this.scale = scale;
            basePos = pos;
            float w = BaseW * scale, h = BaseH * scale;
            Frame = UIFactory.Image(parent, name, Game.Art.Get("ui_white"), new Color(1f, 1f, 1f, 0f));
            Frame.preserveAspect = false;
            FrameRt = Frame.rectTransform;
            UIFactory.Place(FrameRt, anchor, anchor, pos, new Vector2(w + 8f, h + 8f));
            group = Frame.gameObject.AddComponent<CanvasGroup>();

            glow = UIFactory.Image(FrameRt, "Glow", Game.Art.Get("raid_glow"), new Color(1f, 1f, 1f, 0f));
            glow.preserveAspect = false;
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w + 56f * scale, h + 56f * scale));
            rays = UIFactory.Image(FrameRt, "Rays", Game.Art.Get("raid_rays"), new Color(LegendColor.r, LegendColor.g, LegendColor.b, 0f));
            rays.preserveAspect = false;
            UIFactory.Place(rays.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(h * 2.2f, h * 2.2f));

            Card = UIFactory.Image(FrameRt, "Card", Game.Art.Get("raid_cardback"), Color.white);
            Card.preserveAspect = false;
            Card.raycastTarget = true;
            UIFactory.Place(Card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            if (onClick != null)
            {
                var b = Card.gameObject.AddComponent<Button>();
                b.targetGraphic = Card;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onClick());
            }
            band = UIFactory.Text(Card.transform, "Band", "", 14, Ink, TextAnchor.MiddleCenter);
            UIFactory.Place(band.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -15f * scale), new Vector2(w - 16f * scale, 19f * scale));
            icon = UIFactory.Image(Card.transform, "Icon", null, Color.white);
            UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f * scale), new Vector2(72f * scale, 72f * scale));
            icon.enabled = false;
            if (tooltipKey != null) GearTooltip.Hook(icon, () => icon.enabled ? tooltipKey() : null);
            face = UIFactory.Text(Card.transform, "Face", "", Mathf.RoundToInt(17 * scale), Ink, TextAnchor.UpperCenter);
            UIFactory.Place(face.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -114f * scale), new Vector2(w - 16f * scale, 80f * scale));
            ring = UIFactory.Image(FrameRt, "Ring", Game.Art.Get("raid_ring"), new Color(1f, 1f, 1f, 0f));
            ring.preserveAspect = false;
            UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, w));

            Who = UIFactory.Text(FrameRt, "Who", "", Mathf.RoundToInt(18 * scale), Color.white, TextAnchor.UpperCenter, true);
            UIFactory.Place(Who.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(w + 16f, 28f * scale));
            Frame.gameObject.AddComponent<RaidCardTicker>().view = this;
        }

        /// <summary>A face-down card at <paramref name="pos"/> (the anchor is also the pivot). <paramref name="onClick"/> may be null.</summary>
        public static RaidCardView Create(Transform parent, string name, Vector2 anchor, Vector2 pos, float scale, Action onClick, Func<string> tooltipKey = null) =>
            new RaidCardView(parent, name, anchor, pos, scale, onClick, tooltipKey);

        // ---------------- state ----------------

        public void SetActive(bool on) => Frame.gameObject.SetActive(on);

        /// <summary>Back to a face-down card (a new run's result, or a new box).</summary>
        public void ResetBack()
        {
            Flipped = false;
            breathBack = true;
            lingering = false;
            raysSpin = false;
            ringT = sparkT = textT = popT = flashT = glowFlashT = 0f;
            Card.sprite = Game.Art.Get("raid_cardback");
            Card.color = Color.white;
            icon.enabled = false;
            band.text = face.text = Who.text = "";
            FrameRt.anchoredPosition = basePos;
            Card.rectTransform.localScale = Vector3.one;
            Card.rectTransform.localRotation = Quaternion.identity;
            face.rectTransform.localScale = Vector3.one;
            Frame.color = new Color(1f, 1f, 1f, 0f);
            SetAlpha(glow, 0f);
            SetAlpha(rays, 0f);
            SetAlpha(ring, 0f);
            if (sparks != null) foreach (var s in sparks) s.enabled = false;
            if (legendText != null) legendText.enabled = false;
            group.alpha = 1f;
        }

        /// <summary>The yellow cursor frame (only a face-down card shows it).</summary>
        public void SetCursor(bool on) => Frame.color = on && !Flipped ? (Color)UIColors.Highlight : new Color(1f, 1f, 1f, 0f);

        /// <summary>Darkens the card (0 = normal, 1 = 35% darker) while another card is revealed.</summary>
        public void SetDim(float amount) => Card.color = Color.Lerp(Color.white, new Color(0.65f, 0.65f, 0.65f, 1f), amount);

        public static Kind KindOf(RewardCard card)
        {
            if (card.itemId == ConsumableDatabase.ProtectTicket) return Kind.Ticket;
            var gear = EquipmentDatabase.Get(card.itemId) ?? EquipmentDatabase.Get(EquipmentDatabase.BaseId(card.itemId));
            if (gear == null) return Kind.Material;
            return gear.rarity >= ItemRarity.Legendary ? Kind.Legendary : gear.rarity >= ItemRarity.Unique ? Kind.Unique : gear.rarity >= ItemRarity.Epic ? Kind.Epic : Kind.Material;
        }

        static string FrontKey(Kind k) => k == Kind.Ticket ? "raid_front_ticket" : k == Kind.Epic ? "raid_front_epic" : k == Kind.Unique ? "raid_front_unique" : k == Kind.Legendary ? "raid_front_legend" : "raid_front_mat";

        static string BandText(Kind k) => k == Kind.Ticket ? "보호권" : k == Kind.Epic ? "에픽" : k == Kind.Unique ? "유니크" : k == Kind.Legendary ? "레전더리" : "재료";

        static void SetAlpha(Graphic g, float a) { var c = g.color; c.a = a; g.color = c; }

        // ---------------- appear and per-frame effects ----------------

        /// <summary>The face-down card rises 40 px from below while it fades in (0.25 s), after <paramref name="delay"/>.</summary>
        public IEnumerator Appear(float delay)
        {
            group.alpha = 0f;
            FrameRt.anchoredPosition = basePos + new Vector2(0f, -40f);
            for (float t = 0f; t < delay; t += Time.unscaledDeltaTime) yield return null;
            const float time = 0.25f;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                float k = t / time;
                group.alpha = k;
                FrameRt.anchoredPosition = basePos + new Vector2(0f, -40f * (1f - k));
                yield return null;
            }
            group.alpha = 1f;
            FrameRt.anchoredPosition = basePos;
        }

        /// <summary>Glows, rays, ring, sparks and texts that outlive the flip routine.</summary>
        public void Tick(float dt)
        {
            clock += dt;
            float breath = 0.5f + 0.5f * Mathf.Sin(clock * Mathf.PI * 2f / 1.6f);
            if (breathBack && !Flipped) SetGlow(BoneGold, Mathf.Lerp(0.12f, 0.28f, breath));
            else if (lingering) SetGlow(lingerColor, Mathf.Lerp(lingerLow, lingerHigh, breath));
            else if (glowFlashT > 0f)
            {
                glowFlashT = Mathf.Max(0f, glowFlashT - dt);
                SetGlow(Color.white, 0.5f * glowFlashT / 0.3f);
            }
            else SetAlpha(glow, 0f);

            if (raysSpin)
            {
                rays.rectTransform.Rotate(0f, 0f, -30f * dt);
            }
            else if (rays.color.a > 0f) SetAlpha(rays, Mathf.Max(0f, rays.color.a - dt * 1.6f)); // fades once the card has landed

            if (ringT > 0f)
            {
                ringT = Mathf.Max(0f, ringT - dt);
                float k = 1f - ringT / 0.4f;
                float s = Mathf.Lerp(1f, 1.6f, k);
                ring.rectTransform.localScale = new Vector3(s, s, 1f);
                ring.color = new Color(UniqueColor.r, UniqueColor.g, UniqueColor.b, 1f - k);
            }
            else if (ring.color.a > 0f) SetAlpha(ring, 0f);

            if (sparkT > 0f && sparks != null)
            {
                sparkT = Mathf.Max(0f, sparkT - dt);
                float k = 1f - sparkT / 1.2f;
                for (int i = 0; i < sparks.Length; i++)
                {
                    float x = (i - (SparkCount - 1) * 0.5f) * 11f * scale + Mathf.Sin(i * 2.4f) * 8f * scale;
                    float startY = (-BaseH * 0.4f + (i % 3) * 20f) * scale;
                    sparks[i].rectTransform.anchoredPosition = new Vector2(x, startY + 60f * scale * k * (0.7f + 0.3f * ((i * 7) % 5) / 4f));
                    sparks[i].color = new Color(1f, 0.7f + 0.2f * (i % 2), 0.28f, 1f - k);
                    sparks[i].enabled = sparkT > 0f;
                }
            }
            if (textT > 0f && legendText != null)
            {
                textT = Mathf.Max(0f, textT - dt);
                legendText.enabled = textT > 0f;
                SetAlpha(legendText, Mathf.Clamp01(textT / 0.3f));
            }
            if (popT > 0f)
            {
                popT = Mathf.Max(0f, popT - dt);
                float k = 1f - popT / 0.15f;
                float s = Mathf.Lerp(0.2f, 1f, k * k * (3f - 2f * k));
                face.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            if (flashT > 0f && Flash != null)
            {
                flashT = Mathf.Max(0f, flashT - dt);
                SetAlpha(Flash, 0.8f * flashT / 0.25f);
            }
        }

        void SetGlow(Color c, float a) => glow.color = new Color(c.r, c.g, c.b, a);

        // ---------------- flip ----------------

        /// <summary>
        /// The flip: a 0.08 s press, the staging for the card's kind, the fold (0.14 s) with the face swapped in the middle, the
        /// unfold (0.14 s) and a 1.06 -> 1.0 bounce (0.1 s). Returns once the card has landed; its glow, ring and sparks go on.
        /// </summary>
        public IEnumerator Flip(RewardCard reward)
        {
            Content = reward;
            Flipped = true;
            CardKind = KindOf(reward);
            breathBack = false;
            var rt = Card.rectTransform;
            for (float t = 0f; t < PressTime; t += Time.unscaledDeltaTime)
            {
                float s = Mathf.Lerp(1f, 0.96f, t / PressTime);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = new Vector3(0.96f, 0.96f, 1f);
            yield return PreFlip(rt);

            float start = rt.localScale.y;
            rt.localRotation = Quaternion.identity;
            Game.Audio.PlaySfx("card_flip");
            for (float t = 0f; t < FoldTime; t += Time.unscaledDeltaTime)
            {
                rt.localScale = new Vector3(start * (1f - t / FoldTime), start, 1f);
                yield return null;
            }
            popT = CardKind == Kind.Material || CardKind == Kind.Ticket ? 0.15f : 0f;
            ApplyFace(reward);
            if (CardKind == Kind.Material && RaidRewards.IsRaidMaterial(reward.itemId)) glowFlashT = 0.3f;
            for (float t = 0f; t < UnfoldTime; t += Time.unscaledDeltaTime)
            {
                rt.localScale = new Vector3(1.06f * t / UnfoldTime, 1.06f, 1f);
                yield return null;
            }
            for (float t = 0f; t < BounceTime; t += Time.unscaledDeltaTime)
            {
                float s = Mathf.Lerp(1.06f, 1f, t / BounceTime);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            Frame.color = new Color(1f, 1f, 1f, 0f);
            PostFlip(reward);
        }

        IEnumerator PreFlip(RectTransform rt)
        {
            switch (CardKind)
            {
                case Kind.Epic:
                {
                    // 0.45 s: the border blinks purple twice and the card lifts 6 px.
                    Game.Audio.PlaySfx("select");
                    const float time = 0.45f;
                    for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
                    {
                        bool on = Mathf.Sin(t / time * Mathf.PI * 4f) > 0f;
                        Frame.color = on ? EpicColor : new Color(EpicColor.r, EpicColor.g, EpicColor.b, 0f);
                        FrameRt.anchoredPosition = basePos + new Vector2(0f, 6f * scale * Mathf.Min(1f, t / 0.2f));
                        yield return null;
                    }
                    Frame.color = EpicColor;
                    break;
                }
                case Kind.Unique:
                {
                    // 0.9 s: gold pulse, the card trembles and grows (the old jackpot staging).
                    Game.Audio.PlaySfx("rank_reveal");
                    const float time = 0.9f;
                    for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
                    {
                        float k = t / time;
                        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 22f);
                        Frame.color = new Color(UniqueColor.r, UniqueColor.g, UniqueColor.b, 0.35f + 0.65f * pulse);
                        Card.color = Color.Lerp(Color.white, UniqueColor, 0.25f + 0.35f * pulse * k);
                        float s = 1f + 0.08f * k + 0.03f * Mathf.Sin(t * 40f);
                        rt.localScale = new Vector3(s, s, 1f);
                        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 50f) * 3f * k);
                        yield return null;
                    }
                    Game.Camera?.Shake(0.1f, 0.2f);
                    break;
                }
                case Kind.Legendary:
                {
                    // 1.5 s: the others dim, eight rays turn up behind the card, the card shakes hard; a white flash at the end.
                    Game.Audio.PlaySfx("rank_reveal");
                    Frame.transform.SetAsLastSibling();
                    raysSpin = true;
                    const float time = 1.5f;
                    for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
                    {
                        float k = t / time;
                        DimOthers?.Invoke(0.35f * Mathf.Min(1f, k * 4f));
                        SetAlpha(rays, 0.8f * k);
                        Frame.color = new Color(LegendColor.r, LegendColor.g, LegendColor.b, 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(t * 24f)));
                        float s = 1f + 0.12f * k;
                        rt.localScale = new Vector3(s, s, 1f);
                        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 4f * k);
                        yield return null;
                    }
                    flashT = 0.25f;
                    if (Flash != null) Flash.transform.SetAsLastSibling();
                    Game.Camera?.Shake(0.15f, 0.3f);
                    break;
                }
            }
        }

        void ApplyFace(RewardCard reward)
        {
            Card.sprite = Game.Art.Get(FrontKey(CardKind));
            Card.color = Color.white;
            var ink = CardKind == Kind.Legendary ? LegendInk : Ink;
            band.text = BandText(CardKind);
            band.color = ink;
            face.color = ink;
            icon.enabled = true;
            icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(reward.itemId));
            int small = Mathf.RoundToInt(16 * scale);
            var gear = EquipmentDatabase.Get(reward.itemId);
            if (gear != null)
                face.text = $"<color={EquipmentDatabase.RarityColor(gear.rarity)}><b>{gear.name}</b></color>\n<size={small}>{EquipmentDatabase.RarityName(gear.rarity)} · {gear.CategoryName}</size>";
            else
                face.text = $"<b>{DungeonDatabase.ItemName(reward.itemId)}</b>\n×{reward.count:N0}";
            face.rectTransform.localScale = popT > 0f ? new Vector3(0.2f, 0.2f, 1f) : Vector3.one;
        }

        void PostFlip(RewardCard reward)
        {
            string gearName = EquipmentDatabase.Get(reward.itemId)?.name ?? DungeonDatabase.ItemName(reward.itemId);
            FrameRt.anchoredPosition = basePos;
            switch (CardKind)
            {
                case Kind.Epic:
                    StartLinger(EpicColor, 0.25f, 0.45f);
                    Game.Audio.PlaySfx("pickup");
                    break;
                case Kind.Unique:
                    StartLinger(UniqueColor, 0.25f, 0.5f);
                    ringT = 0.4f;
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast($"<color=#ffe15a>유니크 획득:</color> {gearName}");
                    break;
                case Kind.Legendary:
                    raysSpin = false;
                    DimOthers?.Invoke(0f);
                    StartLinger(LegendColor, 0.35f, 0.7f);
                    EnsureSparks();
                    sparkT = 1.2f;
                    textT = 1.5f;
                    legendText.enabled = true;
                    SetAlpha(legendText, 1f);
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast($"<color=#ff8a3d>레전더리 획득:</color> {gearName}");
                    break;
                default:
                    Game.Audio.PlaySfx("pickup");
                    break;
            }
        }

        void StartLinger(Color c, float low, float high)
        {
            lingerColor = c;
            lingerLow = low;
            lingerHigh = high;
            lingering = true;
        }

        void EnsureSparks()
        {
            if (sparks != null) return;
            sparks = new Image[SparkCount];
            for (int i = 0; i < SparkCount; i++)
            {
                var s = UIFactory.Image(FrameRt, "Spark" + i, Game.Art.Get("ui_white"), LegendColor);
                s.preserveAspect = false;
                UIFactory.Place(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f * scale, 4f * scale));
                s.enabled = false;
                sparks[i] = s;
            }
            legendText = UIFactory.Text(FrameRt, "LegendText", "레전더리!", Mathf.RoundToInt(28 * scale), LegendColor, TextAnchor.MiddleCenter);
            var ol = legendText.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color32(42, 22, 8, 255);
            ol.effectDistance = new Vector2(2f, -2f);
            UIFactory.Place(legendText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(BaseW * scale + 60f, 40f * scale));
            legendText.horizontalOverflow = HorizontalWrapMode.Overflow;
            legendText.enabled = false;
        }
    }
}
