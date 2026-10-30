using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 캐시샵 > 합성·컬렉션. Two tabs:
    /// 합성: a cosmetic drawn again is kept as a spare. Four spares of a grade go into one synthesis for a chance at the next
    /// grade (a fail gives one back, and enough fails in a row make the next one certain); spares can also be dismantled
    /// into 별조각 at the old duplicate refund, so nothing drawn is ever wasted.
    /// 컬렉션: own every cosmetic of a set and register it for a permanent account-wide bonus (nothing is consumed).
    /// The server decides everything; this shows its state and sends the requests.
    /// </summary>
    public class CosmeticSynthScreen : OnlineWindow
    {
        const int SpareRows = 6, SetRows = 5;
        int sparePage, setPage; // [UI] lists longer than their rows turn pages
        protected override bool PadNavigation => !busy;
        // [UI] Sized for the 1220 x 594 window content (the old 1500 x 860 layout ran off the right and bottom).
        // [SKIN TIERS] Four rules now (unique -> legend added), so each card is shorter with its buttons stacked on the right.
        const float RuleH = 114f, RuleGap = 8f, RuleW = 600f, SpareX = 616f, SpareW = 604f, SpareH = 50f, SetH = 88f, RuleTextW = 436f;

        sealed class RuleView { public Text title, info, pity; public Button one, all; public Image bar; public UiHelpTip pityHelp; }
        sealed class SpareView { public RectTransform row; public Image icon; public Text name; public Button dismantle, dismantleOne; public string id; }
        sealed class SetView { public RectTransform row; public Text title, members; public Button register; public string id; }

        public static CosmeticSynthScreen Instance { get; private set; }

        RectTransform synthTab, collectionTab;
        Button synthTabBtn, collectionTabBtn, hiddenSetsBtn;
        /// <summary>The collection tab shows the Hidden sets on their own.</summary>
        bool showHiddenSets;
        Text resultText, spareHead, bonusText;
        readonly List<RuleView> rules = new List<RuleView>();
        readonly List<SpareView> spares = new List<SpareView>();
        readonly List<SetView> sets = new List<SetView>();
        bool showCollections, busy;

        public static CosmeticSynthScreen Create(Transform canvas)
        {
            var w = CreateWindow<CosmeticSynthScreen>(canvas, "CosmeticSynth", L.T("합성 · 컬렉션"), "menuicon_synth");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            w.synthTabBtn = Button(w.content, "TabSynth", L.T("합성"), "ui_btn", tl, tl, Vector2.zero, new Vector2(200f, 52f), () => w.ShowTab(false), 20);
            w.collectionTabBtn = Button(w.content, "TabCollection", L.T("컬렉션"), "ui_btngray", tl, tl, new Vector2(212f, 0f), new Vector2(200f, 52f), () => w.ShowTab(true), 20);
            // 0044: spares and gacha gear become Star Crystals, spent at the Starlight Exchange.
            Button(w.content, "Exchange", L.T("별빛 교환소"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(200f, 52f),
                () => { if (ExchangeShopScreen.Instance != null) Game.Flow.OpenWindow(ExchangeShopScreen.Instance); }, 20);

            // ---------- 합성 ----------
            // [UI 2026-10-30] P5: the help line (it ran past the window in other languages) is a "?" tip next to the tabs,
            // and the cards move up into its place.
            w.synthTab = UIFactory.Place(UIFactory.Rect(w.content, "Synth"), tl, tl, new Vector2(0f, -26f), new Vector2(1220f, 568f));
            UiHelpTip.Create(w.synthTab, "Help", tl, new Vector2(424f, 16f), L.T("같은 외형이 또 나오면 <color=#ffd34a>여분</color>으로 쌓입니다. 같은 등급 여분 4개로 한 등급 위 외형에 도전하거나, 여분을 별조각으로 분해할 수 있습니다."));
            string[] froms = { "common", "rare", "epic", "unique" };
            for (int i = 0; i < froms.Length; i++)
            {
                string from = froms[i];
                var card = Panel(w.synthTab, "Rule" + i, tl, tl, new Vector2(0f, -40f - i * (RuleH + RuleGap)), new Vector2(RuleW, RuleH), new Color32(24, 36, 54, 235));
                var v = new RuleView();
                v.title = Label(card.transform, "Title", "", 20, tl, tl, new Vector2(16f, -8f), new Vector2(RuleTextW, 28f));
                v.info = Label(card.transform, "Info", "", 16, tl, tl, new Vector2(16f, -36f), new Vector2(RuleTextW, 42f)); // [UI 2026-10-30] renders 14 px
                var barBg = UIFactory.Image(card.transform, "PityBg", Game.Art.Get("ui_white"), new Color32(10, 12, 20, 230));
                barBg.preserveAspect = false; barBg.raycastTarget = false;
                UIFactory.Place(barBg.rectTransform, tl, tl, new Vector2(16f, -78f), new Vector2(280f, 5f));
                v.bar = UIFactory.Image(barBg.transform, "Fill", Game.Art.Get("ui_white"), new Color32(197, 140, 255, 255));
                v.bar.preserveAspect = false; v.bar.raycastTarget = false;
                UIFactory.Place(v.bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 3f));
                v.pity = Label(card.transform, "Pity", "", 16, tl, tl, new Vector2(16f, -84f), new Vector2(RuleTextW - 36f, 24f));
                UIFactory.OneLine(v.pity, true); // [UI 2026-10-30] P2 "n / total" on one line, the rule in the "?" tip
                v.pityHelp = UiHelpTip.Create(card.transform, "PityHelp", tl, new Vector2(16f + RuleTextW - 32f, -76f), ""); // right end of the text column, left of the buttons
                v.one = Button(card.transform, "One", L.T("1회 합성"), "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(124f, 42f), () => w.DoSynth(from, false), 17);
                v.all = Button(card.transform, "All", L.T("모두 합성"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -60f), new Vector2(124f, 42f), () => w.DoSynth(from, true), 17);
                w.rules.Add(v);
            }
            w.resultText = Label(w.synthTab, "Result", "", 19, tl, tl, new Vector2(SpareX, -40f - (72f + SpareRows * SpareH + 16f) - 8f), new Vector2(SpareW, 48f));

            var sparePanel = Panel(w.synthTab, "Spares", tl, tl, new Vector2(SpareX, -40f), new Vector2(SpareW, 72f + SpareRows * SpareH + 16f), new Color32(18, 26, 40, 240));
            w.spareHead = Label(sparePanel.transform, "Head", "", 19, tl, tl, new Vector2(16f, -8f), new Vector2(420f, 60f));
            w.spareHead.resizeTextForBestFit = true; // [UI 2026-10-30] three lines at 14 px for the long translations
            w.spareHead.resizeTextMinSize = UIFactory.ReadableFloor;
            w.spareHead.resizeTextMaxSize = 19;
            w.spareHead.verticalOverflow = VerticalWrapMode.Truncate;
            w.sparePager = Pager(sparePanel.transform, new Vector2(-10f, -6f), d => { w.sparePage += d; w.Refresh(); });
            for (int i = 0; i < SpareRows; i++)
            {
                var v = new SpareView { row = Row(sparePanel.transform, i, -76f, SpareH, SpareW) };
                v.icon = UIFactory.SharpIcon(v.row, "Icon", Color.white);
                v.icon.raycastTarget = false;
                UIFactory.Place(v.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 44f));
                v.name = Cell(v.row, "Name", 60f, 232f);
                var view = v;
                v.dismantle = Button(v.row, "Dismantle", "", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(200f, 44f), () => w.AskDismantle(view.id, false), 16);
                v.dismantleOne = Button(v.row, "DismantleOne", L.T("1개 분해"), "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-218f, 0f), new Vector2(90f, 44f), () => w.AskDismantle(view.id, true), 16);
                w.spares.Add(v);
            }

            // ---------- 컬렉션 ----------
            w.collectionTab = UIFactory.Place(UIFactory.Rect(w.content, "Collection"), tl, tl, new Vector2(0f, -66f), new Vector2(1220f, 528f));
            w.bonusText = Label(w.collectionTab, "Bonus", "", 20, tl, tl, Vector2.zero, new Vector2(860f, 32f), TextAnchor.MiddleLeft);
            UIFactory.OneLine(w.bonusText, true); // [UI 2026-10-30] one line, shrinks to 14 px at most
            w.hiddenSetsBtn = Button(w.collectionTab, "HiddenSets", L.T("히든 컬렉션"), "ui_btngray", tl, tl, new Vector2(872f, 2f), new Vector2(170f, 34f), () => { w.showHiddenSets = !w.showHiddenSets; w.setPage = 0; w.Refresh(); }, 16);
            w.setPager = Pager(w.collectionTab, new Vector2(-20f, 0f), d => { w.setPage += d; w.Refresh(); });
            for (int i = 0; i < SetRows; i++)
            {
                var v = new SetView { row = Row(w.collectionTab, i, -44f, SetH, 1200f) };
                v.title = Label(v.row, "Title", "", 21, tl, tl, new Vector2(18f, -8f), new Vector2(900f, 32f));
                v.members = Label(v.row, "Members", "", 17, tl, tl, new Vector2(18f, -42f), new Vector2(900f, 42f));
                var view = v;
                v.register = Button(v.row, "Register", "", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(220f, 56f), () => w.Register(view.id), 18);
                w.sets.Add(v);
            }

            w.status = Label(w.content, "Status", "", 16, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(SpareX, 0f), new Vector2(SpareW, 30f), TextAnchor.MiddleLeft);
            StarShopClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            w.ShowTab(false);
            return w;
        }

        public override void Show()
        {
            base.Show();
            _ = StarShopClient.RefreshAsync();
        }

        void ShowTab(bool collections)
        {
            showCollections = collections;
            synthTab.gameObject.SetActive(!collections);
            collectionTab.gameObject.SetActive(collections);
            synthTabBtn.image.sprite = UiTheme.Tab(!collections);
            collectionTabBtn.image.sprite = UiTheme.Tab(collections);
            Refresh();
        }

        static string Hex(string rarity) =>
            rarity == "legend" ? "#ff6b5e" : rarity == "unique" ? "#ffb347" : rarity == "epic" ? "#c58cff" : rarity == "rare" ? "#9fc4ff" : "#cfd6e2";

        static string Grade(string rarity) =>
            rarity == "legend" ? L.T("전설") : rarity == "unique" ? L.T("유니크") : rarity == "epic" ? L.T("에픽") : rarity == "rare" ? L.T("희귀") : L.T("일반");

        protected override void Refresh()
        {
            if (!StarShopClient.Loaded) { SetStatus(StarShopClient.Available ? L.T("불러오는 중...") : L.T("온라인 캐릭터로 접속하면 이용할 수 있습니다."), false); }
            else SetStatus(L.F("보유 {0} · {1}", StarShopClient.Stars(StarShopClient.Balance), StarShopClient.CrystalText(StarShopClient.Crystals)));
            if (showCollections) RefreshCollections(); else RefreshSynth();
        }

        void RefreshSynth()
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var v = rules[i];
                var r = i < StarShopClient.Synth.Count ? StarShopClient.Synth[i] : null;
                if (r == null) { v.title.text = ""; v.info.text = ""; v.pity.text = ""; v.one.interactable = v.all.interactable = false; continue; }
                int have = StarShopClient.SparesOf(r.from);
                string target = r.to == "legend" ? L.T("내 직업 전설 스킨") : r.to == "unique" ? L.T("내 직업 유니크 스킨") : r.to == "epic" ? L.T("에픽 오라·내 직업 에픽 스킨") : L.T("희귀 오라·내 직업 희귀 스킨");
                v.title.text = L.F("<color={0}>{1}</color> 여분 {2}개  →  <color={3}><b>{4}</b></color> 1개", Hex(r.from), Grade(r.from), r.count, Hex(r.to), Grade(r.to));
                v.info.text = L.F("성공 <color=#ffd34a>{0:0.#}%</color> · 결과: {1}", r.rate, target) + "\n" + L.F("실패 시 1개 반환 · 히든 제외 · <color=#b8c4d8>보유 여분 {0}개</color>", have);
                bool hasPity = r.pity > 0;
                v.bar.transform.parent.gameObject.SetActive(hasPity);
                v.pityHelp.gameObject.SetActive(hasPity);
                if (hasPity)
                {
                    v.bar.rectTransform.sizeDelta = new Vector2(276f * Mathf.Clamp01(r.fails / (float)r.pity), 3f);
                    v.pity.text = r.fails >= r.pity ? L.T("<color=#ffd34a><b>다음 합성은 성공 확정!</b></color>") : L.F("연속 실패 {0} / {1}", r.fails, r.pity);
                    v.pityHelp.SetText(L.F("연속 실패 {0}/{1} · {1}번 실패하면 다음은 확정", r.fails, r.pity));
                }
                else v.pity.text = "";
                bool can = !busy && StarShopClient.Loaded && have >= r.count;
                v.one.interactable = can;
                v.all.interactable = can && have >= r.count * 2;
            }
            // Spares, most valuable grade first.
            var list = new List<(CosmeticProduct p, int n)>();
            foreach (var kv in StarShopClient.Copies)
            {
                var p = CosmeticCatalog.Find(kv.Key);
                if (p != null && kv.Value > 0) list.Add((p, kv.Value));
            }
            list.Sort((a, b) => b.p.Rarity != a.p.Rarity ? b.p.Rarity.CompareTo(a.p.Rarity) : b.n.CompareTo(a.n));
            int total = 0;
            foreach (var x in list) total += x.n;
            spareHead.text = list.Count == 0 ? L.T("<b>여분</b>   <color=#8c96a8>아직 여분이 없습니다. 이미 가진 외형이 다시 나오면 여기에 쌓입니다.</color>") : L.F("<b>여분</b> {0}개   <color=#b8c4d8>분해하면 별조각과 별의 결정을 받습니다.</color>", total);
            int sparePages = Mathf.Max(1, (list.Count + SpareRows - 1) / SpareRows);
            sparePage = Mathf.Clamp(sparePage, 0, sparePages - 1);
            SetPager(sparePager, sparePage, sparePages);
            for (int i = 0; i < spares.Count; i++)
            {
                var v = spares[i];
                int at = sparePage * SpareRows + i;
                bool on = at < list.Count;
                v.row.gameObject.SetActive(on);
                if (!on) { v.id = null; continue; }
                var (p, n) = list[at];
                var card = CosmeticAura.Card(p);
                v.icon.enabled = card != null;
                v.icon.sprite = card;
                string key = StarShopClient.RarityKey(p.Rarity);
                var (each, gems) = DismantleValue(p);
                v.id = p.Id;
                string grade = p.Hidden ? $"<color={CosmeticCatalog.HiddenHex}>[{p.GradeName}]</color>" : $"<color={Hex(key)}>[{Grade(key)}]</color>";
                v.name.text = $"{grade} {L.T(p.Name)}" + (p.IsSkin ? L.T(" (스킨)") : "") + L.F("   여분 <b>{0}</b>", n);
                TextOf(v.dismantle).text = gems > 0 ? L.F("모두 분해 · 별조각 {0:N0} · 결정 {1:N0}", each * n, gems * n) : L.F("모두 분해 · 별조각 {0:N0}", each * n);
                v.dismantle.interactable = !busy;
                v.dismantleOne.gameObject.SetActive(n > 1);
                v.dismantleOne.interactable = !busy;
            }
        }

        (Button prev, Text label, Button next) sparePager, setPager;

        /// <summary>One spare's dismantle value: the server's per-item figure (skins and Hidden differ from auras).</summary>
        static (int stars, int crystals) DismantleValue(CosmeticProduct p)
        {
            if (StarShopClient.DismantleOf.TryGetValue(p.Id, out var v)) return v;
            string key = StarShopClient.RarityKey(p.Rarity);
            StarShopClient.Dismantle.TryGetValue(key, out int each);
            StarShopClient.DismantleCrystals.TryGetValue(key, out int gems);
            return (each, gems);
        }

        /// <summary>◀ 1/3 ▶ at the top-right of a list (hidden while everything fits on one page).</summary>
        static (Button, Text, Button) Pager(Transform parent, Vector2 topRight, System.Action<int> turn)
        {
            var tr = new Vector2(1f, 1f);
            var next = Button(parent, "PageNext", "▶", "ui_btngray", tr, tr, topRight, new Vector2(40f, 32f), () => turn(1), 16);
            var label = Label(parent, "PageText", "", 16, tr, tr, topRight + new Vector2(-44f, 0f), new Vector2(60f, 32f), TextAnchor.MiddleCenter);
            var prev = Button(parent, "PagePrev", "◀", "ui_btngray", tr, tr, topRight + new Vector2(-108f, 0f), new Vector2(40f, 32f), () => turn(-1), 16);
            return (prev, label, next);
        }

        static void SetPager((Button prev, Text label, Button next) p, int page, int pages)
        {
            bool show = pages > 1;
            p.prev.gameObject.SetActive(show); p.next.gameObject.SetActive(show); p.label.gameObject.SetActive(show);
            if (!show) return;
            p.label.text = $"{page + 1}/{pages}";
            p.prev.interactable = page > 0;
            p.next.interactable = page < pages - 1;
        }

        void RefreshCollections()
        {
            bonusText.text = L.F("<b>컬렉션 효과</b>   공격력 <color=#ffb347>+{0}%</color>   최대 체력 <color=#8fe28f>+{1}%</color>   <color=#b8c4d8>(계정 전체 · 외형은 소모되지 않음)</color>", StarShopClient.CollectionAttack, StarShopClient.CollectionHealth);
            TextOf(hiddenSetsBtn).text = showHiddenSets ? L.T("전체 컬렉션") : L.T("히든 컬렉션");
            var everything = StarShopClient.Collections;
            var all = everything.FindAll(x => x.hidden == showHiddenSets);
            int setPages = Mathf.Max(1, (all.Count + SetRows - 1) / SetRows);
            setPage = Mathf.Clamp(setPage, 0, setPages - 1);
            SetPager(setPager, setPage, setPages);
            for (int i = 0; i < sets.Count; i++)
            {
                var v = sets[i];
                int at = setPage * SetRows + i;
                bool on = at < all.Count;
                v.row.gameObject.SetActive(on);
                if (!on) { v.id = null; continue; }
                var c = all[at];
                v.id = c.id;
                var reward = new List<string>();
                if (c.attack > 0) reward.Add(L.F("공격력 +{0}%", c.attack));
                if (c.health > 0) reward.Add(L.F("최대 체력 +{0}%", c.health));
                v.title.text = $"<b>{L.T(c.name)}</b>   <color=#ffd34a>{string.Join(" · ", reward)}</color>" + (c.registered ? L.T("   <color=#8fe28f>등록 완료</color>") : "");
                var parts = new List<string>();
                int have = 0, need = 0;
                foreach (var m in c.members)
                {
                    var p = CosmeticCatalog.Find(m);
                    bool owned = StarShopClient.Owned.Contains(m);
                    need++; if (owned) have++;
                    parts.Add(owned ? $"<color=#8fe28f>{L.T(p?.Name ?? m)}</color>" : $"<color=#6c7486>{L.T(p?.Name ?? m)}</color>");
                }
                foreach (var s in c.requires)
                {
                    var other = everything.Find(x => x.id == s);
                    bool done = other != null && other.registered;
                    need++; if (done) have++;
                    parts.Add(done ? $"<color=#8fe28f>{L.T(other?.name ?? s)}</color>" : $"<color=#6c7486>{L.T(other?.name ?? s)}</color>");
                }
                // Big sets (the common / rare wardrobes) list only what is still missing.
                if (parts.Count > 8)
                {
                    var missing = new List<string>();
                    foreach (var m in c.members) if (!StarShopClient.Owned.Contains(m) && missing.Count < 6) missing.Add($"<color=#6c7486>{L.T(CosmeticCatalog.Find(m)?.Name ?? m)}</color>");
                    v.members.text = $"{have}/{need}   " + (missing.Count > 0 ? L.F("남은 것: {0}", string.Join(" · ", missing)) + (need - have > missing.Count ? " ..." : "") : L.T("<color=#8fe28f>모두 모음</color>"));
                }
                else v.members.text = $"{have}/{need}   " + string.Join(" · ", parts);
                bool can = StarShopClient.CanRegister(c);
                TextOf(v.register).text = c.registered ? L.T("등록됨") : can ? L.T("등록하기") : L.F("모으는 중 {0}/{1}", have, need);
                v.register.interactable = can && !busy;
            }
        }

        void DoSynth(string from, bool all)
        {
            if (busy) return;
            var rule = StarShopClient.Synth.Find(r => r.from == from);
            if (rule == null) return;
            int have = StarShopClient.SparesOf(from);
            int times = all ? Mathf.Min(20, have / rule.count) : 1;
            if (times < 1) { Game.Audio.PlaySfx("cancel"); return; }
            if (all)
            {
                Game.UI.Confirm(L.F("여분 {0}개로 {1}번 합성합니다.\n<size=18>실패할 때마다 넣은 여분 중 1개만 돌아옵니다.</size>\n합성할까요?", times * rule.count, times), () => Synth(from, times), true);
                return;
            }
            Synth(from, times);
        }

        void Synth(string from, int times)
        {
            if (busy) return;
            busy = true;
            resultText.text = L.T("<color=#b8c4d8>합성하는 중...</color>");
            Refresh();
            StarShopClient.DoSynth(from, times, (ok, msg, list) =>
            {
                busy = false;
                if (!ok) { resultText.text = $"<color=#ff9f7a>{msg}</color>"; Game.Audio.PlaySfx("cancel"); Refresh(); return; }
                int wins = 0;
                var got = new List<string>();
                var gotCount = new List<int>(); // [UI 2026-10-30] P8: the same cosmetic twice reads "name x2"
                foreach (var r in list)
                {
                    if (!r.success) continue;
                    wins++;
                    var p = CosmeticCatalog.Find(r.itemId);
                    string label = $"<color={Hex(r.rarity)}>{L.T(p?.Name ?? r.itemId)}</color>" + (r.byPity ? L.T(" (확정)") : "");
                    int k = got.IndexOf(label);
                    if (k >= 0) gotCount[k]++; else { got.Add(label); gotCount.Add(1); }
                }
                for (int g = 0; g < got.Count; g++) if (gotCount[g] > 1) got[g] += $" x{gotCount[g]}";
                if (wins > 0)
                {
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast(L.F("<color=#ffd34a>합성 성공!</color> {0}", string.Join(", ", got)));
                }
                else Game.Audio.PlaySfx("select");
                resultText.text = L.F("합성 {0}회 · 성공 <color=#ffd34a>{1}</color>회", list.Count, wins) + "\n" + (got.Count > 0 ? L.F("획득: {0}", string.Join(", ", got)) : L.T("<color=#b8c4d8>실패한 만큼 여분 1개씩 돌려받았고, 연속 실패가 쌓였습니다.</color>"));
                Refresh();
            });
        }

        void AskDismantle(string id, bool one)
        {
            if (busy || string.IsNullOrEmpty(id)) return;
            var p = CosmeticCatalog.Find(id);
            int n = StarShopClient.CopiesOf(id);
            if (one) n = Mathf.Min(1, n);
            if (p == null || n <= 0) return;
            var (each, gems) = DismantleValue(p);
            // Registered-set cosmetics give 20% more crystals; the server's figure is what arrives.
            Game.UI.Confirm(L.F("{0} 여분 {1}개를 분해합니다.\n별조각 {2:N0}개와 별의 결정 {3:N0}개 이상을 받습니다. (착용 중인 외형은 그대로)", L.T(p.Name), n, each * n, gems * n), () =>
            {
                busy = true;
                StarShopClient.DoDismantle(id, n, (ok, msg, stars, crystals) =>
                {
                    busy = false;
                    if (!ok) { GameEvents.RaiseToast(msg); Game.Audio.PlaySfx("cancel"); }
                    else { GameEvents.RaiseToast(L.F("분해 완료 · 별조각 +{0:N0} · 별의 결정 +{1:N0}", stars, crystals)); Game.Audio.PlaySfx("confirm"); }
                    Refresh();
                });
            }, true);
        }

        void Register(string id)
        {
            if (busy || string.IsNullOrEmpty(id)) return;
            busy = true;
            StarShopClient.DoRegister(id, (ok, msg) =>
            {
                busy = false;
                var c = StarShopClient.Collections.Find(x => x.id == id);
                if (!ok) { GameEvents.RaiseToast(msg); Game.Audio.PlaySfx("cancel"); }
                else
                {
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast(L.F("<color=#ffd34a>컬렉션 완성!</color> {0}", L.T(c?.name)));
                }
                Refresh();
            });
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
