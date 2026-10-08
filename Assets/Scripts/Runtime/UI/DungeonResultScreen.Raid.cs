using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [RAID] The result of a raid whose four cards are all taken (Docs/server/phase13_raid_rewards.md §7.4): the sure
    /// gold / core / seal key chips in the card panel's title row, four <see cref="RaidCardView"/>s flipped one by one
    /// (any order, or "남은 카드 모두 뒤집기"), and the one-line material summary. The weekday dungeons keep the old
    /// pick-one cards in the main file.
    /// </summary>
    public partial class DungeonResultScreen
    {
        const float ChipStart = StampDelay + StampTime + 0.2f, ChipStep = 0.1f, ChipCountTime = 0.6f, ChipGap = 14f, AutoFlipGap = 0.45f;

        sealed class Chip
        {
            public RectTransform root;
            public CanvasGroup group;
            public Text text;
            public Image icon;
            public float width;
            public int target, shown = -1, sound;
            public string prefix, suffix;
            public bool active;
        }

        readonly List<RaidCardView> raidViews = new List<RaidCardView>();
        readonly Chip[] chips = new Chip[3];
        Image flash;
        Text raidSummary;
        Button flipAllButton;
        bool autoFlipping;

        /// <summary>True while the window shows a take-all raid result.</summary>
        bool RaidMode => run != null && run.CardsTakeAll;

        void BuildRaid(Transform cardsPanel, float total)
        {
            for (int i = 0; i < DungeonRewards.CardCount; i++)
            {
                int index = i;
                var view = RaidCardView.Create(cardsPanel, "RaidCard" + i, new Vector2(0.5f, 1f), new Vector2(-total * 0.5f + CardW * 0.5f + i * (CardW + CardGap), -46f), 1f,
                    () => { cursor = index; RaidPick(index); },
                    () => run?.Cards != null && index < run.Cards.Count ? run.Cards[index].itemId : null);
                view.DimOthers = k => { foreach (var other in raidViews) if (other != view) other.SetDim(k); };
                view.SetActive(false);
                raidViews.Add(view);
            }
            flash = UIFactory.Overlay(cardsPanel, "RaidFlash", new Color(1f, 1f, 1f, 0f));
            foreach (var v in raidViews) v.Flash = flash;

            var widths = new[] { 170f, 76f, 96f };
            var icons = new[] { ConsumableDatabase.Gold, DungeonDatabase.RaidCore, DungeonDatabase.SealKey };
            var colors = new[] { new Color32(255, 224, 102, 255), new Color32(232, 216, 255, 255), new Color32(216, 232, 255, 255) };
            for (int i = 0; i < chips.Length; i++)
            {
                var c = new Chip { width = widths[i] };
                c.root = UIFactory.Rect(cardsPanel, "Chip" + i);
                c.group = c.root.gameObject.AddComponent<CanvasGroup>();
                c.icon = UIFactory.SharpIcon(c.root, "Icon", Color.white);
                c.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(icons[i]));
                UIFactory.Place(c.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
                c.text = UIFactory.Text(c.root, "Text", "", 18, colors[i], TextAnchor.MiddleLeft, true);
                UIFactory.Place(c.text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(widths[i] - 30f, 30f));
                c.text.horizontalOverflow = HorizontalWrapMode.Overflow;
                c.prefix = "+";
                c.suffix = i == 0 ? " G" : "";
                chips[i] = c;
            }
            raidSummary = Label(cardsPanel, "RaidSummary", "", 17, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 8f), new Vector2(520f, 24f), TextAnchor.LowerLeft);
            flipAllButton = Button(cardsPanel, "FlipAll", "남은 카드 모두 뒤집기", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 6f), new Vector2(250f, 36f), OnFlipAll, 16);
            flipAllButton.gameObject.SetActive(false);
        }

        /// <summary>Setup() hook: face-down raid cards and the chips of this run, or all of it hidden.</summary>
        void SetupRaid(bool hasCards)
        {
            bool raid = RaidMode && hasCards;
            autoFlipping = false;
            foreach (var v in raidViews)
            {
                v.SetActive(raid);
                if (raid) v.ResetBack();
            }
            if (flash != null) flash.color = new Color(1f, 1f, 1f, 0f);
            cardTitle.rectTransform.sizeDelta = new Vector2(raid ? 400f : RightW - 44f, 30f);
            raidSummary.text = "";
            flipAllButton.gameObject.SetActive(false);
            var values = raid ? new[] { run.RaidGold, run.RaidCoreGain, run.RaidKeyGain } : new int[3];
            for (int i = 0; i < chips.Length; i++)
            {
                var c = chips[i];
                c.target = values[i];
                c.active = raid && values[i] > 0;
                c.shown = -1;
                c.sound = 0;
                c.group.alpha = 0f;
                c.root.gameObject.SetActive(c.active);
            }
            LayoutChips();
            if (raid) { cursor = 0; RefreshRaidCursor(); }
        }

        /// <summary>The visible chips sit right-aligned in the title row, 14 px apart.</summary>
        void LayoutChips()
        {
            float x = -18f;
            for (int i = chips.Length - 1; i >= 0; i--)
            {
                var c = chips[i];
                if (!c.active) continue;
                UIFactory.Place(c.root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(x, -10f), new Vector2(c.width, 30f));
                x -= c.width + ChipGap;
            }
        }

        /// <summary>Show() hook: the four face-down cards rise in one after another.</summary>
        void StartRaidAppear()
        {
            if (!RaidMode || run.Cards == null) return;
            for (int i = 0; i < raidViews.Count; i++) StartCoroutine(raidViews[i].Appear(0.08f * i));
        }

        /// <summary>After the rank stamp lands, the chips appear left to right and their numbers count up (0.6 s).</summary>
        void UpdateRaidChips(float age)
        {
            if (!RaidMode) return;
            int order = 0;
            foreach (var c in chips)
            {
                if (!c.active) continue;
                float start = ChipStart + ChipStep * order++;
                c.group.alpha = Mathf.Clamp01((age - start) / 0.15f);
                if (age < start) continue;
                float p = Mathf.Clamp01((age - start) / ChipCountTime);
                int value = Mathf.RoundToInt(c.target * p);
                if (value != c.shown)
                {
                    c.shown = value;
                    c.text.text = $"{c.prefix}{value:N0}{c.suffix}";
                }
                // A quiet tick while the number climbs: at most two per chip (six in all).
                int ticks = Mathf.Min(2, Mathf.FloorToInt(p * 2.999f) + 1);
                if (ticks > c.sound) { c.sound = ticks; Game.Audio.PlaySfx("pickup"); }
            }
        }

        // ---------------- flipping ----------------

        bool RaidPick(int index)
        {
            if (run == null || run.Cards == null || picking || autoFlipping || index < 0 || index >= raidViews.Count || index >= run.Cards.Count || raidViews[index].Flipped) return false;
            if (Time.unscaledTime - shownAt < StampDelay) return false;
            picking = true;
            RefreshRaidCursor();
            StartCoroutine(RaidPickRoutine(index));
            return true;
        }

        /// <summary>Takes card <paramref name="index"/> (the server, or the bag offline) and flips it once the content is known.</summary>
        IEnumerator RaidPickRoutine(int index)
        {
            RewardCard? reward = null;
            if (OnlineEconomy.On)
            {
                // [SERVER] One request per card; the flip waits for the answer because the content is not known before.
                bool answered = false;
                Game.Dungeon.TakeCardOnline(index, r => { reward = r; answered = true; });
                float waited = 0f;
                while (!answered && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
            }
            else reward = Game.Dungeon.TakeCard(index);
            if (!reward.HasValue)
            {
                picking = false;
                RefreshRaidCursor();
                yield break;
            }
            run.Cards[index] = reward.Value;
            cursor = index;
            var view = raidViews[index];
            yield return view.Flip(reward.Value);
            view.Who.text = "<color=#8fe28f>획득</color>";
            picking = false;
            AfterRaidFlip(index);
        }

        void AfterRaidFlip(int index)
        {
            int next = NextFaceDown(index, 1);
            if (next < 0)
            {
                done = true;
                string material = run.Dungeon.raidReward != null ? run.Dungeon.raidReward.materialItem : null;
                int sum = 0;
                foreach (var card in run.Cards) if (material != null && card.itemId == material) sum += card.count;
                raidSummary.text = sum > 0 ? $"<b>{DungeonDatabase.ItemName(material)} +{sum:N0}</b>  <color=#b8c4d8>(보유 {Game.Session.Inventory.Count(material):N0})</color>" : "";
            }
            else cursor = next;
            RefreshRaidTitle();
            RefreshRaidCursor();
            RefreshButtons();
        }

        /// <summary>The nearest face-down card from <paramref name="from"/> going in <paramref name="dir"/> (wraps), or -1 if none is left.</summary>
        int NextFaceDown(int from, int dir)
        {
            int n = raidViews.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = ((from + dir * k) % n + n) % n;
                if (!raidViews[i].Flipped) return i;
            }
            return -1;
        }

        void RaidInput()
        {
            var input = Game.Input;
            float x = input.Move.x;
            int dir = x > 0.5f && lastMoveX <= 0.5f ? 1 : x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
            lastMoveX = x;
            if (autoFlipping || picking) return;
            if (dir != 0)
            {
                int next = NextFaceDown(cursor, dir);
                if (next >= 0 && next != cursor)
                {
                    cursor = next;
                    Game.Audio.PlaySfx("select");
                    RefreshRaidCursor();
                }
            }
            if (input.SubmitPressed || input.AttackPressed) RaidPick(cursor);
            else if (input.InventoryPressed || input.CancelPressed) Game.Audio.PlaySfx("cancel");
        }

        void OnFlipAll()
        {
            if (!RaidMode || picking || autoFlipping || done) return;
            StartCoroutine(FlipAllRoutine());
        }

        /// <summary>The cards still face down flip in number order, 0.45 s apart (a unique or legendary card finishes its staging first).</summary>
        IEnumerator FlipAllRoutine()
        {
            autoFlipping = true;
            RefreshRaidButtons();
            RefreshRaidCursor();
            for (int i = 0; i < raidViews.Count; i++)
            {
                if (raidViews[i].Flipped) continue;
                picking = true;
                yield return RaidPickRoutine(i);
                if (!raidViews[i].Flipped) break; // refused or no answer: stop here, the player can try again
                if (!done) yield return new WaitForSecondsRealtime(AutoFlipGap);
            }
            autoFlipping = false;
            RefreshRaidButtons();
            RefreshRaidCursor();
        }

        // ---------------- refresh ----------------

        void RefreshRaidTitle()
        {
            int taken = 0;
            foreach (var v in raidViews) if (v.Flipped) taken++;
            cardTitle.text = done ? "<b>보상 카드</b>  <color=#8fe28f>획득 완료</color>"
                : $"<b>보상 카드</b>  <color=#ffe066>카드를 눌러 뒤집으세요 ({taken}/{raidViews.Count})</color>";
        }

        void RefreshRaidCursor()
        {
            for (int i = 0; i < raidViews.Count; i++) raidViews[i].SetCursor(i == cursor && !picking && !autoFlipping && !done);
        }

        /// <summary>RefreshButtons() hook: the flip-all button replaces the key hint once a first card is open.</summary>
        void RefreshRaidButtons()
        {
            if (flipAllButton == null) return;
            bool any = false;
            foreach (var v in raidViews) any |= v.Flipped;
            bool show = RaidMode && !done && any && !autoFlipping;
            flipAllButton.gameObject.SetActive(show);
            if (show) hint.text = "";
        }

        // ---------- Automated checks ----------
        string DevRaidWho(int i) => i >= 0 && i < raidViews.Count ? raidViews[i].Who.text : "";
        bool DevRaidFlipped(int i) => i >= 0 && i < raidViews.Count && raidViews[i].Flipped;
        public bool DevRaidMode => RaidMode;
        public void DevFlipAll() => OnFlipAll();
    }
}
