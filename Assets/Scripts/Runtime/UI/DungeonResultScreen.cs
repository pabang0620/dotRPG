using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 던전 결과 (plan §6.0): the rank stamp, the score breakdown (time 40 + hits 30 + kills 10 + combo 20,
    /// −10 per revive), time and XP, per-member damage bars and four face-down reward cards. The player flips
    /// one (its reward goes into the bag), each companion then flips one (labelled with its name), the rest
    /// are revealed. Then 다시 도전 (entries left) / 던전 선택 / 마을로. Animations run on unscaled time (the
    /// window freezes the world). Keys: ←/→ card, Enter / attack flips; afterwards Enter or Esc = 마을로.
    /// </summary>
    public class DungeonResultScreen : WindowScreen
    {
        const float LeftW = 380f, RightX = 392f, RightW = 828f;
        const float CardW = 150f, CardH = 210f, CardGap = 20f;
        const float StampDelay = 0.25f, StampTime = 0.3f, LineStep = 0.12f, FlipHalf = 0.12f, CompanionGap = 0.4f;

        sealed class Card
        {
            public Image bg, icon, frame;
            public Text face, who;
            public RectTransform rt;
            public bool flipped;
        }

        readonly List<Card> cards = new List<Card>();
        readonly List<(Image bar, Text text)> bars = new List<(Image, Text)>();
        Text stamp, stampSub, breakdown, cardTitle, noCards, hint;
        RectTransform stampRt;
        Button retryButton, selectButton, villageButton;
        DungeonRun run;
        float shownAt;
        int cursor;
        bool picking, picked, done;

        public static DungeonResultScreen Create(Transform canvas)
        {
            var w = CreateWindow<DungeonResultScreen>(canvas, "DungeonResult", "던전 결과", "menuicon_dungeon");
            var left = Panel(w.content, "Stamp", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(LeftW, 230f), new Color32(24, 36, 54, 235));
            w.stamp = UIFactory.Text(left.transform, "Rank", "", 120, Color.white, TextAnchor.MiddleCenter, true);
            w.stampRt = w.stamp.rectTransform;
            UIFactory.Place(w.stampRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(LeftW, 150f));
            var ol = w.stamp.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.08f, 0.05f, 0.03f, 0.9f);
            ol.effectDistance = new Vector2(4f, -4f);
            w.stampSub = Label(left.transform, "Sub", "", 18, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(LeftW - 20f, 52f), TextAnchor.LowerCenter);

            var score = Panel(w.content, "Score", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -240f), new Vector2(LeftW, 354f), new Color32(24, 36, 54, 235));
            w.breakdown = Label(score.transform, "Lines", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -16f), new Vector2(LeftW - 40f, 330f));

            var cardsPanel = Panel(w.content, "Cards", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RightX, 0f), new Vector2(RightW, 340f), new Color32(24, 36, 54, 235));
            w.cardTitle = Label(cardsPanel.transform, "Title", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(RightW - 44f, 30f));
            float total = DungeonRewards.CardCount * CardW + (DungeonRewards.CardCount - 1) * CardGap;
            for (int i = 0; i < DungeonRewards.CardCount; i++)
            {
                int index = i;
                var c = new Card();
                c.frame = Panel(cardsPanel.transform, "Frame" + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-total * 0.5f + CardW * 0.5f + i * (CardW + CardGap), -46f), new Vector2(CardW + 8f, CardH + 8f), new Color(1f, 1f, 1f, 0f));
                c.bg = UIFactory.Image(c.frame.transform, "Card", Game.Art.Get("dgn_cardback"), Color.white);
                c.bg.preserveAspect = false;
                c.bg.raycastTarget = true;
                c.rt = c.bg.rectTransform;
                UIFactory.Place(c.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH));
                var b = c.bg.gameObject.AddComponent<Button>();
                b.targetGraphic = c.bg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { w.cursor = index; w.PlayerPick(index); });
                c.icon = UIFactory.Image(c.bg.transform, "Icon", null, Color.white);
                UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(72f, 72f));
                c.face = UIFactory.Text(c.bg.transform, "Face", "", 17, UIColors.Cream, TextAnchor.UpperCenter, true);
                UIFactory.Place(c.face.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -114f), new Vector2(CardW - 16f, 80f));
                c.who = Label(cardsPanel.transform, "Who" + i, "", 18, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-total * 0.5f + CardW * 0.5f + i * (CardW + CardGap), -46f - CardH - 14f), new Vector2(CardW + 16f, 28f), TextAnchor.UpperCenter);
                w.cards.Add(c);
            }
            w.noCards = Label(cardsPanel.transform, "NoCards", "", 22, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(RightW - 60f, 120f), TextAnchor.MiddleCenter);

            var dmg = Panel(w.content, "Damage", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RightX, -350f), new Vector2(RightW, 164f), new Color32(24, 36, 54, 235));
            Label(dmg.transform, "Title", "<b>딜 기여도</b>", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(300f, 26f));
            for (int i = 0; i < PartyManager.MaxMembers; i++)
            {
                var track = Panel(dmg.transform, "Track" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -42f - i * 30f), new Vector2(520f, 20f), new Color32(12, 18, 28, 255));
                var bar = Panel(track.transform, "Bar", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 20f), Color.white);
                var text = Label(dmg.transform, "Name" + i, "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -40f - i * 30f), new Vector2(124f, 26f));
                var value = Label(track.transform, "Value", "", UiTheme.FontCaption, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(130f, 24f), TextAnchor.MiddleLeft);
                w.bars.Add((bar, text));
                track.gameObject.SetActive(false);
                text.gameObject.SetActive(false);
                value.name = "Value";
            }

            w.retryButton = Button(w.content, "Retry", "다시 도전", "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-2f * 262f, -524f), new Vector2(250f, 62f), w.OnRetry, 24);
            w.selectButton = Button(w.content, "Select", "던전 선택", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-262f, -524f), new Vector2(250f, 62f), () => w.Leave(true), 24);
            w.villageButton = Button(w.content, "Village", "마을로", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -524f), new Vector2(250f, 62f), () => w.Leave(false), 24);
            w.hint = Label(cardsPanel.transform, "Hint", "", 16, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 8f), new Vector2(400f, 24f), TextAnchor.LowerRight);
            return w;
        }

        /// <summary>Called by the director right before the window opens.</summary>
        public void Setup(DungeonRun result)
        {
            run = result;
            picking = picked = done = false;
            cursor = 0;
            bool hasCards = run != null && run.Cards != null && run.Cards.Count > 0;
            foreach (var c in cards)
            {
                c.flipped = false;
                c.bg.sprite = Game.Art.Get("dgn_cardback");
                c.bg.color = Color.white;
                c.icon.enabled = false;
                c.face.text = "";
                c.who.text = "";
                c.rt.localScale = Vector3.one;
                c.frame.gameObject.SetActive(hasCards);
                c.who.gameObject.SetActive(hasCards);
            }
            if (!hasCards) done = true;
        }

        public override void Show()
        {
            base.Show();
            shownAt = Time.unscaledTime;
            Game.Audio.PlaySfx(run != null && run.State == DungeonRunState.Cleared ? "quest" : "cancel");
        }

        protected override void Refresh()
        {
            if (run == null) return;
            bool cleared = run.State == DungeonRunState.Cleared;
            stamp.text = cleared ? run.Rank.ToString() : "실패";
            stamp.color = cleared ? DungeonRanking.Tint(run.Rank) : new Color32(230, 90, 80, 255);
            stamp.fontSize = cleared ? 120 : 96;
            stampSub.text = $"<b>{run.Dungeon.name}</b> · {run.Numbers.name}\n<color=#b8c4d8>{(cleared ? "던전 클리어" : run.FailReason)}</color>";

            bool hasCards = run.Cards != null && run.Cards.Count > 0;
            noCards.text = hasCards ? "" : !cleared ? "<color=#ff9f7a>던전 실패 — 보상 없음</color>"
                : "<color=#ff9f7a>이번 주 레이드 보상을 이미 받았다.</color>\n<color=#b8c4d8>목요일 06:00에 초기화된다.</color>";
            RefreshCardTitle();
            RefreshDamage();
            RefreshButtons();
        }

        string BreakdownText(int lines)
        {
            var s = run.Score;
            bool cleared = run.State == DungeonRunState.Cleared;
            var all = new List<string>
            {
                $"시간 <color=#ffe066>{DungeonRun.Clock(run.Elapsed)}</color>" + (cleared ? $"   <color=#b8c4d8>{s.time}/{DungeonRanking.TimeMax}</color>" : ""),
                $"피격 {run.HitsTaken}회" + (cleared ? $"   <color=#b8c4d8>{s.hits}/{DungeonRanking.HitsMax}</color>" : ""),
                $"처치 {run.Kills}/{run.Monsters}" + (cleared ? $"   <color=#b8c4d8>{s.kills}/{DungeonRanking.KillsMax}</color>" : ""),
                $"최대 콤보 {run.MaxCombo}" + (cleared ? $"   <color=#b8c4d8>{s.combo}/{DungeonRanking.ComboMax}</color>" : ""),
                $"부활 {run.RevivesUsed}회" + (cleared && s.revivePenalty > 0 ? $"   <color=#ff9f7a>−{s.revivePenalty}</color>" : ""),
            };
            if (cleared)
            {
                all.Add($"\n<size=24><b>합계 {s.Total}점</b></size>   랭크 <color=#{ColorUtility.ToHtmlStringRGB(DungeonRanking.Tint(run.Rank))}>{run.Rank}</color>");
                all.Add(run.RewardsLocked ? "<color=#b8c4d8>경험치 없음 (연습 입장)</color>"
                    : $"경험치 <color=#8fe28f>+{run.XpGained:N0}</color>  <color=#b8c4d8>(랭크 보너스 +{DungeonRanking.XpBonusPercent(run.Rank)}%)</color>");
            }
            else all.Add("\n<color=#b8c4d8>실패한 던전은 랭크와 경험치가 없다.</color>");
            return string.Join("\n", all.GetRange(0, Mathf.Clamp(lines, 0, all.Count)));
        }

        void RefreshCardTitle()
        {
            bool hasCards = run.Cards != null && run.Cards.Count > 0;
            if (!hasCards) { cardTitle.text = "<b>보상 카드</b>"; return; }
            cardTitle.text = !picked ? "<b>보상 카드</b>  <color=#ffe066>카드 한 장을 고르세요</color>" : done ? "<b>보상 카드</b>  <color=#8fe28f>획득 완료</color>" : "<b>보상 카드</b>  파티원이 카드를 고르는 중…";
            for (int i = 0; i < cards.Count; i++)
                if (!cards[i].flipped) cards[i].frame.color = !picked && i == cursor ? (Color)UIColors.Highlight : new Color(1f, 1f, 1f, 0f);
        }

        void RefreshDamage()
        {
            int total = 0, max = 1;
            foreach (var m in run.MemberDamage) { total += m.damage; max = Mathf.Max(max, m.damage); }
            for (int i = 0; i < bars.Count; i++)
            {
                var (bar, text) = bars[i];
                bool on = i < run.MemberDamage.Count;
                bar.transform.parent.gameObject.SetActive(on);
                text.gameObject.SetActive(on);
                if (!on) continue;
                var m = run.MemberDamage[i];
                text.text = m.local ? $"<color=#ffe066>{m.name}</color>" : m.name;
                bar.color = m.local ? new Color32(255, 196, 70, 255) : new Color32(110, 170, 240, 255);
                bar.rectTransform.sizeDelta = new Vector2(520f * m.damage / max, 20f);
                var value = bar.transform.parent.Find("Value").GetComponent<Text>();
                int pct = total > 0 ? Mathf.RoundToInt(100f * m.damage / total) : 0;
                value.text = $"{m.damage * DamageNumber.DisplayScale:N0} ({pct}%)";
            }
        }

        void RefreshButtons()
        {
            bool canRetry = Game.Dungeon != null && Game.Dungeon.CanRetry;
            foreach (var b in new[] { retryButton, selectButton, villageButton }) b.gameObject.SetActive(done);
            retryButton.image.color = canRetry ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            hint.text = done ? "<color=#b8c4d8>Enter / Esc 마을로</color>" : "<color=#b8c4d8>←/→ 카드   Enter 뒤집기</color>";
        }

        protected override void Update()
        {
            if (run == null) return;
            float age = Time.unscaledTime - shownAt;
            // Stamp: slams down from 3x after a short beat.
            float t = Mathf.Clamp01((age - StampDelay) / StampTime);
            float scale = age < StampDelay ? 0f : Mathf.Lerp(3f, 1f, t * t);
            stampRt.localScale = new Vector3(scale, scale, 1f);
            var c = stamp.color;
            c.a = Mathf.Clamp01(t * 1.5f);
            stamp.color = c;
            if (t >= 1f && !stampLanded) { stampLanded = true; Game.Audio.PlaySfx("rank_reveal"); Game.Camera?.Shake(0.08f, 0.15f); }
            if (age < StampDelay) stampLanded = false;
            int lines = Mathf.FloorToInt((age - StampDelay - StampTime) / LineStep) + 1;
            string text = BreakdownText(Mathf.Max(0, lines));
            if (breakdown.text != text) breakdown.text = text;

            if (!TakesInput) return;
            var input = Game.Input;
            if (!done)
            {
                if (picked) return;
                float x = input.Move.x;
                int dir = x > 0.5f && lastMoveX <= 0.5f ? 1 : x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
                lastMoveX = x;
                if (dir != 0)
                {
                    cursor = (cursor + dir + cards.Count) % cards.Count;
                    Game.Audio.PlaySfx("select");
                    RefreshCardTitle();
                }
                if (input.SubmitPressed || input.AttackPressed) PlayerPick(cursor);
                else if (input.InventoryPressed || input.CancelPressed) Game.Audio.PlaySfx("cancel");
                return;
            }
            if (input.SubmitPressed || input.InventoryPressed || input.CancelPressed) Leave(false);
        }

        bool stampLanded;
        float lastMoveX;

        /// <summary>The local player's pick; companions and the reveal follow.</summary>
        public bool PlayerPick(int index)
        {
            if (run == null || run.Cards == null || picking || picked || index < 0 || index >= cards.Count || cards[index].flipped) return false;
            if (Time.unscaledTime - shownAt < StampDelay) return false;
            picking = true;
            StartCoroutine(PickRoutine(index));
            return true;
        }

        IEnumerator PickRoutine(int index)
        {
            var reward = Game.Dungeon.TakeCard(index);
            yield return Flip(index, "나", true, true);
            picked = true;
            if (reward.HasValue) GameEvents.RaiseToast($"보상 획득: {reward.Value.Label}");
            RefreshCardTitle();
            // Companions each flip one of the cards still face down.
            var party = Game.Party;
            if (party != null)
                for (int m = 1; m < party.Members.Count; m++)
                {
                    var faceDown = new List<int>();
                    for (int i = 0; i < cards.Count; i++) if (!cards[i].flipped) faceDown.Add(i);
                    if (faceDown.Count == 0) break;
                    yield return new WaitForSecondsRealtime(CompanionGap);
                    int k = DungeonAuthority.Current.CompanionPick(run, m, faceDown);
                    int card = faceDown[Mathf.Clamp(k, 0, faceDown.Count - 1)];
                    yield return Flip(card, party.Members[m] != null ? party.Members[m].DisplayName : "동료", false, true);
                }
            yield return new WaitForSecondsRealtime(CompanionGap);
            for (int i = 0; i < cards.Count; i++) if (!cards[i].flipped) StartCoroutine(Flip(i, "", false, false));
            yield return new WaitForSecondsRealtime(FlipHalf * 2f + 0.05f);
            picking = false;
            done = true;
            RefreshCardTitle();
            RefreshButtons();
        }

        IEnumerator Flip(int index, string who, bool local, bool taken)
        {
            var c = cards[index];
            c.flipped = true;
            c.frame.color = local ? (Color)UIColors.Highlight : new Color(1f, 1f, 1f, 0f);
            Game.Audio.PlaySfx("card_flip"); // [H3]
            for (float t = 0f; t < FlipHalf; t += Time.unscaledDeltaTime)
            {
                c.rt.localScale = new Vector3(1f - t / FlipHalf, 1f, 1f);
                yield return null;
            }
            var reward = run.Cards[index];
            c.bg.sprite = Game.Art.Get("dgn_cardfront");
            c.bg.color = taken ? Color.white : new Color(0.62f, 0.62f, 0.66f, 1f);
            c.icon.enabled = true;
            c.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(reward.itemId));
            c.face.text = CardText(reward);
            c.who.text = taken ? (local ? $"<color=#ffe066><b>{who}</b></color>" : $"<color=#9fd0ff>{who}</color>") : "<color=#8c96a8>미획득</color>";
            for (float t = 0f; t < FlipHalf; t += Time.unscaledDeltaTime)
            {
                c.rt.localScale = new Vector3(t / FlipHalf, 1f, 1f);
                yield return null;
            }
            c.rt.localScale = Vector3.one;
            if (local) Game.Audio.PlaySfx("pickup");
        }

        static string CardText(RewardCard r)
        {
            var gear = EquipmentDatabase.Get(r.itemId);
            if (gear != null) return $"<color={EquipmentDatabase.RarityColor(gear.rarity)}><b>{gear.name}</b></color>\n<size=16>{EquipmentDatabase.RarityName(gear.rarity)}</size>";
            string name = DungeonDatabase.ItemName(r.itemId);
            return $"<b>{name}</b>\n×{r.count:N0}";
        }

        void OnRetry()
        {
            if (!done || Game.Dungeon == null) return;
            if (!Game.Dungeon.CanRetry)
            {
                GameEvents.RaiseToast("오늘 입장 횟수를 모두 사용했다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            Game.Dungeon.Retry();
        }

        void Leave(bool openSelect)
        {
            if (!done || Game.Dungeon == null) return;
            Game.Audio.PlaySfx("confirm");
            Game.Dungeon.ExitToVillage(openSelect);
        }

        /// <summary>Esc / the bag key: nothing until the cards are done, then back to the village.</summary>
        public override void Close()
        {
            if (done) Leave(false);
        }

        // ---------- Automated checks ----------
        public bool DevDone => done;
        public bool DevPicked => picked;
        public string DevStamp => stamp != null ? stamp.text : "";
        public string DevWho(int i) => i >= 0 && i < cards.Count ? cards[i].who.text : "";
        public bool DevFlipped(int i) => i >= 0 && i < cards.Count && cards[i].flipped;
        public void DevRetry() => OnRetry();
        public void DevLeave(bool openSelect) => Leave(openSelect);
    }
}
