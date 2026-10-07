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
        const float RuleH = 144f, RuleGap = 8f, RuleW = 600f, SpareX = 616f, SpareW = 604f, SpareH = 56f, SetH = 88f;

        sealed class RuleView { public Text title, info, pity; public Button one, all; public Image bar; }
        sealed class SpareView { public RectTransform row; public Image icon; public Text name; public Button dismantle, dismantleOne; public string id; }
        sealed class SetView { public RectTransform row; public Text title, members; public Button register; public string id; }

        public static CosmeticSynthScreen Instance { get; private set; }

        RectTransform synthTab, collectionTab;
        Button synthTabBtn, collectionTabBtn;
        Text resultText, spareHead, bonusText;
        readonly List<RuleView> rules = new List<RuleView>();
        readonly List<SpareView> spares = new List<SpareView>();
        readonly List<SetView> sets = new List<SetView>();
        bool showCollections, busy;

        public static CosmeticSynthScreen Create(Transform canvas)
        {
            var w = CreateWindow<CosmeticSynthScreen>(canvas, "CosmeticSynth", "합성 · 컬렉션", "menuicon_synth");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            w.synthTabBtn = Button(w.content, "TabSynth", "합성", "ui_btn", tl, tl, Vector2.zero, new Vector2(200f, 52f), () => w.ShowTab(false), 20);
            w.collectionTabBtn = Button(w.content, "TabCollection", "컬렉션", "ui_btngray", tl, tl, new Vector2(212f, 0f), new Vector2(200f, 52f), () => w.ShowTab(true), 20);

            // ---------- 합성 ----------
            w.synthTab = UIFactory.Place(UIFactory.Rect(w.content, "Synth"), tl, tl, new Vector2(0f, -66f), new Vector2(1220f, 528f));
            Label(w.synthTab, "Help", "같은 외형이 또 나오면 <color=#ffd34a>여분</color>으로 쌓입니다. 같은 등급 여분 4개로 한 등급 위 외형에 도전하거나, 여분을 별조각으로 분해할 수 있습니다.", 18,
                tl, tl, Vector2.zero, new Vector2(1220f, 30f), TextAnchor.MiddleLeft);
            string[] froms = { "common", "rare", "epic" };
            for (int i = 0; i < froms.Length; i++)
            {
                string from = froms[i];
                var card = Panel(w.synthTab, "Rule" + i, tl, tl, new Vector2(0f, -40f - i * (RuleH + RuleGap)), new Vector2(RuleW, RuleH), new Color32(24, 36, 54, 235));
                var v = new RuleView();
                v.title = Label(card.transform, "Title", "", 22, tl, tl, new Vector2(20f, -12f), new Vector2(560f, 30f));
                v.info = Label(card.transform, "Info", "", 17, tl, tl, new Vector2(20f, -44f), new Vector2(560f, 48f));
                var barBg = UIFactory.Image(card.transform, "PityBg", Game.Art.Get("ui_white"), new Color32(10, 12, 20, 230));
                barBg.preserveAspect = false; barBg.raycastTarget = false;
                UIFactory.Place(barBg.rectTransform, tl, tl, new Vector2(20f, -100f), new Vector2(280f, 16f));
                v.bar = UIFactory.Image(barBg.transform, "Fill", Game.Art.Get("ui_white"), new Color32(197, 140, 255, 255));
                v.bar.preserveAspect = false; v.bar.raycastTarget = false;
                UIFactory.Place(v.bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 12f));
                v.pity = Label(card.transform, "Pity", "", 16, tl, tl, new Vector2(20f, -118f), new Vector2(280f, 24f));
                v.one = Button(card.transform, "One", "1회 합성", "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-154f, 10f), new Vector2(130f, 46f), () => w.DoSynth(from, false), 18);
                v.all = Button(card.transform, "All", "모두 합성", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 10f), new Vector2(130f, 46f), () => w.DoSynth(from, true), 18);
                w.rules.Add(v);
            }
            w.resultText = Label(w.synthTab, "Result", "", 19, tl, tl, new Vector2(SpareX, -40f - (40f + SpareRows * SpareH + 16f) - 8f), new Vector2(SpareW, 48f));

            var sparePanel = Panel(w.synthTab, "Spares", tl, tl, new Vector2(SpareX, -40f), new Vector2(SpareW, 40f + SpareRows * SpareH + 16f), new Color32(18, 26, 40, 240));
            w.spareHead = Label(sparePanel.transform, "Head", "", 19, tl, tl, new Vector2(16f, -8f), new Vector2(420f, 30f));
            w.sparePager = Pager(sparePanel.transform, new Vector2(-10f, -6f), d => { w.sparePage += d; w.Refresh(); });
            for (int i = 0; i < SpareRows; i++)
            {
                var v = new SpareView { row = Row(sparePanel.transform, i, -44f, SpareH, SpareW) };
                v.icon = UIFactory.SharpIcon(v.row, "Icon", Color.white);
                v.icon.raycastTarget = false;
                UIFactory.Place(v.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 44f));
                v.name = Cell(v.row, "Name", 60f, 232f);
                var view = v;
                v.dismantle = Button(v.row, "Dismantle", "", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(200f, 44f), () => w.AskDismantle(view.id, false), 16);
                v.dismantleOne = Button(v.row, "DismantleOne", "1개 분해", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-218f, 0f), new Vector2(90f, 44f), () => w.AskDismantle(view.id, true), 16);
                w.spares.Add(v);
            }

            // ---------- 컬렉션 ----------
            w.collectionTab = UIFactory.Place(UIFactory.Rect(w.content, "Collection"), tl, tl, new Vector2(0f, -66f), new Vector2(1220f, 528f));
            w.bonusText = Label(w.collectionTab, "Bonus", "", 20, tl, tl, Vector2.zero, new Vector2(1060f, 32f), TextAnchor.MiddleLeft);
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

            w.status = Label(w.content, "Status", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(1200f, 30f), TextAnchor.MiddleLeft);
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
            rarity == "unique" ? "#ffb347" : rarity == "epic" ? "#c58cff" : rarity == "rare" ? "#9fc4ff" : "#cfd6e2";

        static string Grade(string rarity) =>
            rarity == "unique" ? "유니크" : rarity == "epic" ? "에픽" : rarity == "rare" ? "희귀" : "일반";

        protected override void Refresh()
        {
            if (!StarShopClient.Loaded) { SetStatus(StarShopClient.Available ? "불러오는 중..." : "온라인 캐릭터로 접속하면 이용할 수 있습니다.", false); }
            else SetStatus($"보유 {StarShopClient.Stars(StarShopClient.Balance)}");
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
                string target = r.to == "unique" ? "내 직업 유니크 스킨" : r.to == "epic" ? "에픽 오라·내 직업 에픽 스킨" : "희귀 오라";
                v.title.text = $"<color={Hex(r.from)}>{Grade(r.from)}</color> 여분 {r.count}개  →  <color={Hex(r.to)}><b>{Grade(r.to)}</b></color> 1개";
                v.info.text = $"성공 <color=#ffd34a>{r.rate:0.#}%</color> · 결과: {target} (아직 없는 것부터)\n실패하면 넣은 여분 중 1개를 돌려받습니다.   <color=#b8c4d8>보유 여분 {have}개</color>";
                bool hasPity = r.pity > 0;
                v.bar.transform.parent.gameObject.SetActive(hasPity);
                if (hasPity)
                {
                    v.bar.rectTransform.sizeDelta = new Vector2(276f * Mathf.Clamp01(r.fails / (float)r.pity), 12f);
                    v.pity.text = r.fails >= r.pity ? "<color=#ffd34a><b>다음 합성은 성공 확정!</b></color>" : $"연속 실패 {r.fails}/{r.pity} · {r.pity}번 실패하면 다음은 확정";
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
            spareHead.text = list.Count == 0 ? "<b>여분</b>   <color=#8c96a8>아직 여분이 없습니다. 이미 가진 외형이 다시 나오면 여기에 쌓입니다.</color>" : $"<b>여분</b> {total}개   <color=#b8c4d8>분해하면 별조각으로 돌려받습니다.</color>";
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
                StarShopClient.Dismantle.TryGetValue(key, out int each);
                v.id = p.Id;
                v.name.text = $"<color={Hex(key)}>[{Grade(key)}]</color> {p.Name}{(p.IsSkin ? " (스킨)" : "")}   여분 <b>{n}</b>";
                TextOf(v.dismantle).text = $"모두 분해 · 별조각 {each * n:N0}";
                v.dismantle.interactable = !busy;
                v.dismantleOne.gameObject.SetActive(n > 1);
                v.dismantleOne.interactable = !busy;
            }
        }

        (Button prev, Text label, Button next) sparePager, setPager;

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
            bonusText.text = $"<b>컬렉션 효과</b>   공격력 <color=#ffb347>+{StarShopClient.CollectionAttack}%</color>   최대 체력 <color=#8fe28f>+{StarShopClient.CollectionHealth}%</color>   <color=#b8c4d8>(계정 전체 · 외형은 소모되지 않음)</color>";
            var all = StarShopClient.Collections;
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
                if (c.attack > 0) reward.Add($"공격력 +{c.attack}%");
                if (c.health > 0) reward.Add($"최대 체력 +{c.health}%");
                v.title.text = $"<b>{c.name}</b>   <color=#ffd34a>{string.Join(" · ", reward)}</color>{(c.registered ? "   <color=#8fe28f>등록 완료</color>" : "")}";
                var parts = new List<string>();
                int have = 0, need = 0;
                foreach (var m in c.members)
                {
                    var p = CosmeticCatalog.Find(m);
                    bool owned = StarShopClient.Owned.Contains(m);
                    need++; if (owned) have++;
                    parts.Add(owned ? $"<color=#8fe28f>{p?.Name ?? m}</color>" : $"<color=#6c7486>{p?.Name ?? m}</color>");
                }
                foreach (var s in c.requires)
                {
                    var other = all.Find(x => x.id == s);
                    bool done = other != null && other.registered;
                    need++; if (done) have++;
                    parts.Add(done ? $"<color=#8fe28f>{other?.name ?? s}</color>" : $"<color=#6c7486>{other?.name ?? s}</color>");
                }
                v.members.text = $"{have}/{need}   " + string.Join(" · ", parts);
                bool can = StarShopClient.CanRegister(c);
                TextOf(v.register).text = c.registered ? "등록됨" : can ? "등록하기" : $"모으는 중 {have}/{need}";
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
                Game.UI.Confirm($"여분 {times * rule.count}개로 {times}번 합성합니다.\n<size=18>실패할 때마다 넣은 여분 중 1개만 돌아옵니다.</size>\n합성할까요?", () => Synth(from, times), true);
                return;
            }
            Synth(from, times);
        }

        void Synth(string from, int times)
        {
            if (busy) return;
            busy = true;
            resultText.text = "<color=#b8c4d8>합성하는 중...</color>";
            Refresh();
            StarShopClient.DoSynth(from, times, (ok, msg, list) =>
            {
                busy = false;
                if (!ok) { resultText.text = $"<color=#ff9f7a>{msg}</color>"; Game.Audio.PlaySfx("cancel"); Refresh(); return; }
                int wins = 0;
                var got = new List<string>();
                foreach (var r in list)
                {
                    if (!r.success) continue;
                    wins++;
                    var p = CosmeticCatalog.Find(r.itemId);
                    got.Add($"<color={Hex(r.rarity)}>{p?.Name ?? r.itemId}</color>{(r.byPity ? " (확정)" : "")}");
                }
                if (wins > 0)
                {
                    Game.Audio.PlaySfx("quest");
                    GameEvents.RaiseToast($"<color=#ffd34a>합성 성공!</color> {string.Join(", ", got)}");
                }
                else Game.Audio.PlaySfx("select");
                resultText.text = $"합성 {list.Count}회 · 성공 <color=#ffd34a>{wins}</color>회" + (got.Count > 0 ? $"\n획득: {string.Join(", ", got)}" : "\n<color=#b8c4d8>실패한 만큼 여분 1개씩 돌려받았고, 연속 실패가 쌓였습니다.</color>");
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
            StarShopClient.Dismantle.TryGetValue(StarShopClient.RarityKey(p.Rarity), out int each);
            Game.UI.Confirm($"{p.Name} 여분 {n}개를 분해합니다.\n별조각 {each * n:N0}개를 받습니다. (착용 중인 외형은 그대로)", () =>
            {
                busy = true;
                StarShopClient.DoDismantle(id, n, (ok, msg, stars) =>
                {
                    busy = false;
                    if (!ok) { GameEvents.RaiseToast(msg); Game.Audio.PlaySfx("cancel"); }
                    else { GameEvents.RaiseToast($"분해 완료 · 별조각 +{stars:N0}"); Game.Audio.PlaySfx("confirm"); }
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
                    GameEvents.RaiseToast($"<color=#ffd34a>컬렉션 완성!</color> {c?.name}");
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
