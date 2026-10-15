using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TRADE] 1:1 교환 window: my offer (left), my bag to pick from (middle), the other side's offer (right).
    /// Without a trade it asks for a name to send a request; an incoming request shows 수락 / 거절. In a trade:
    /// click a bag row to offer it (the 개수 box, empty = the whole row), click an offered row to take it back,
    /// 골드 넣기 sets the gold. Any change unlocks both sides; 잠금 then 확정 on both sides makes the swap.
    /// </summary>
    public partial class TradeScreen : OnlineWindow
    {
        public static TradeScreen Instance { get; private set; }
        const int Rows = 8;
        const float ColW = 395f, Gap = 17.5f, RowH = 46f, PanelH = 470f;

        sealed class RowView { public Image bg, icon; public Text text; }
        readonly List<RowView> mineRows = new List<RowView>(), bagRows = new List<RowView>(), otherRows = new List<RowView>();
        Text head, mineHead, otherHead, mineGold, otherGold, bagPage;
        InputField nameField, goldField, countField;
        Button requestBtn, acceptBtn, declineBtn, lockBtn, confirmBtn, cancelBtn, goldBtn;
        List<TradeClient.Item> bag = new List<TradeClient.Item>();
        int page;
        string bagFor;

        protected override bool PadNavigation => focusField == null || !focusField.isFocused;

        public static TradeScreen Create(Transform canvas)
        {
            var w = CreateWindow<TradeScreen>(canvas, "Trade", L.T("교환"), "menuicon_mail");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            w.head = Label(w.content, "Head", "", 20, tl, tl, new Vector2(0f, 0f), new Vector2(1220f, 30f), TextAnchor.MiddleLeft);

            var mine = Panel(w.content, "Mine", tl, tl, new Vector2(0f, -36f), new Vector2(ColW, PanelH), new Color32(18, 26, 40, 240));
            w.mineHead = Label(mine.transform, "Head", "", 19, tl, tl, new Vector2(12f, -8f), new Vector2(ColW - 24f, 26f), TextAnchor.MiddleLeft);
            BuildRows(w, mine.transform, w.mineRows, i => w.TakeBack(i));
            w.mineGold = Label(mine.transform, "Gold", "", 18, tl, tl, new Vector2(12f, -46f - Rows * RowH), new Vector2(ColW - 24f, 26f), TextAnchor.MiddleLeft);
            w.goldField = w.Field(mine.transform, "GoldField", L.T("골드"), new Vector2(12f, -PanelH + 50f), new Vector2(200f, 38f));
            w.goldField.contentType = InputField.ContentType.IntegerNumber;
            w.goldField.characterLimit = 12;
            w.goldBtn = Button(mine.transform, "GoldBtn", L.T("골드 넣기"), "ui_btngray", tl, tl, new Vector2(222f, -PanelH + 50f), new Vector2(160f, 38f), w.SetGold, 17);

            var bagPanel = Panel(w.content, "Bag", tl, tl, new Vector2(ColW + Gap, -36f), new Vector2(ColW, PanelH), new Color32(24, 36, 54, 235));
            Label(bagPanel.transform, "Head", L.T("<b>내 가방</b>  <size=15><color=#8c96a8>눌러서 올리기</color></size>"), 19, tl, tl, new Vector2(12f, -8f), new Vector2(ColW - 24f, 26f), TextAnchor.MiddleLeft);
            BuildRows(w, bagPanel.transform, w.bagRows, i => w.Put(i));
            w.countField = w.Field(bagPanel.transform, "Count", L.T("개수 (비우면 전부)"), new Vector2(12f, -PanelH + 50f), new Vector2(190f, 38f));
            w.countField.contentType = InputField.ContentType.IntegerNumber;
            w.countField.characterLimit = 6;
            var br = new Vector2(1f, 0f);
            Button(bagPanel.transform, "Prev", "◀", "ui_btngray", br, br, new Vector2(-118f, 12f), UiSizes.PageButton, () => w.Turn(-1), UiSizes.PageFont);
            w.bagPage = Label(bagPanel.transform, "Page", "", 17, br, br, new Vector2(-54f, 12f), new Vector2(64f, 34f), TextAnchor.MiddleCenter);
            Button(bagPanel.transform, "Next", "▶", "ui_btngray", br, br, new Vector2(-8f, 12f), UiSizes.PageButton, () => w.Turn(1), UiSizes.PageFont);

            var other = Panel(w.content, "Other", tl, tl, new Vector2(2f * (ColW + Gap), -36f), new Vector2(ColW, PanelH), new Color32(18, 26, 40, 240));
            w.otherHead = Label(other.transform, "Head", "", 19, tl, tl, new Vector2(12f, -8f), new Vector2(ColW - 24f, 26f), TextAnchor.MiddleLeft);
            BuildRows(w, other.transform, w.otherRows, null);
            w.otherGold = Label(other.transform, "Gold", "", 18, tl, tl, new Vector2(12f, -46f - Rows * RowH), new Vector2(ColW - 24f, 26f), TextAnchor.MiddleLeft);

            var bl = new Vector2(0f, 0f);
            w.nameField = w.Field(w.content, "Name", L.T("상대 캐릭터 이름"), new Vector2(0f, -PanelH - 52f), new Vector2(300f, 42f));
            w.requestBtn = Button(w.content, "Request", L.T("교환 신청"), "ui_btn", bl, bl, new Vector2(312f, 10f), new Vector2(180f, 44f), () => TradeClient.Request(w.nameField.text), 18);
            w.acceptBtn = Button(w.content, "Accept", L.T("수락"), "ui_btn", bl, bl, new Vector2(0f, 10f), new Vector2(160f, 44f), TradeClient.Accept, 18);
            w.declineBtn = Button(w.content, "Decline", L.T("거절"), "ui_btngray", bl, bl, new Vector2(172f, 10f), new Vector2(160f, 44f), TradeClient.Decline, 18);
            w.lockBtn = Button(w.content, "Lock", L.T("잠금"), "ui_btngray", bl, bl, new Vector2(0f, 10f), new Vector2(160f, 44f), TradeClient.Lock, 18);
            w.confirmBtn = Button(w.content, "Confirm", L.T("확정"), "ui_btn", bl, bl, new Vector2(172f, 10f), new Vector2(160f, 44f), TradeClient.Confirm, 18);
            w.cancelBtn = Button(w.content, "Cancel", L.T("교환 취소"), "ui_btngray", br, br, new Vector2(0f, 10f), new Vector2(180f, 44f), TradeClient.Cancel, 18);
            w.status = Label(w.content, "Status", "", 17, bl, bl, new Vector2(520f, 14f), new Vector2(500f, 36f), TextAnchor.MiddleLeft);
            if (TouchUi.Enabled) FitTouch(w); // [TOUCH] TradeScreen.Mobile.cs
            return w;
        }

        static void BuildRows(TradeScreen w, Transform parent, List<RowView> into, System.Action<int> click)
        {
            var tl = new Vector2(0f, 1f);
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var v = new RowView();
                v.bg = Panel(parent, "Row" + i, tl, tl, new Vector2(8f, -40f - i * RowH), new Vector2(ColW - 16f, RowH - 4f), new Color(1f, 1f, 1f, 0.04f));
                if (click != null)
                {
                    v.bg.raycastTarget = true;
                    var b = v.bg.gameObject.AddComponent<Button>();
                    b.targetGraphic = v.bg;
                    b.onClick.AddListener(() => click(idx));
                }
                v.icon = UIFactory.SharpIcon(v.bg.transform, "Icon", Color.white);
                v.icon.raycastTarget = false;
                UIFactory.Place(v.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(34f, 34f));
                v.text = Label(v.bg.transform, "Text", "", 17, tl, tl, new Vector2(48f, -4f), new Vector2(ColW - 72f, RowH - 10f), TextAnchor.MiddleLeft);
                v.text.raycastTarget = false;
                into.Add(v);
            }
        }

        void OnEnable() { TradeClient.Changed += OnChanged; TradeClient.WindowOpen = true; }
        void OnDisable() { TradeClient.Changed -= OnChanged; TradeClient.WindowOpen = false; }
        void OnChanged() { if (gameObject.activeInHierarchy) Refresh(); }

        public override void Show()
        {
            TradeClient.Ensure();
            base.Show();
            page = 0;
            bagFor = null;
        }

        protected override void Update()
        {
            focusField = nameField != null && nameField.isFocused ? nameField : goldField != null && goldField.isFocused ? goldField : countField != null && countField.isFocused ? countField : null;
            base.Update();
        }

        protected override void Refresh()
        {
            var t = TradeClient.Current;
            bool active = t != null && t.Active, incoming = t != null && t.IncomingRequest, outgoing = t != null && t.Pending && !incoming;
            // The bag is read again when a trade starts (the ids of my rows) and after it ends.
            string want = active ? t.id : "none";
            if (bagFor != want) { bagFor = want; TradeClient.LoadBag(list => { bag = list; if (gameObject.activeInHierarchy) DrawLists(); }); }

            if (t == null) head.text = L.T("교환할 상대의 이름을 넣고 교환 신청을 누르세요. 상대가 접속 중이어야 합니다.");
            else if (incoming) head.text = L.F("<color=#ffe066>{0}</color>님이 교환을 신청했습니다.", t.other.name);
            else if (outgoing) head.text = L.F("<color=#ffe066>{0}</color>님의 수락을 기다리는 중...", t.other.name);
            else head.text = L.F("<color=#ffe066>{0}</color>님과 교환 중 · 물건을 바꾸면 양쪽 잠금이 풀립니다. 둘 다 잠근 뒤 둘 다 확정하면 바뀝니다.", t.other.name);

            nameField.gameObject.SetActive(t == null);
            requestBtn.gameObject.SetActive(t == null);
            acceptBtn.gameObject.SetActive(incoming);
            declineBtn.gameObject.SetActive(incoming);
            lockBtn.gameObject.SetActive(active);
            confirmBtn.gameObject.SetActive(active);
            cancelBtn.gameObject.SetActive(active || outgoing);
            goldBtn.interactable = active;
            if (active)
            {
                lockBtn.interactable = !t.me.locked;
                TextOf(lockBtn).text = t.me.locked ? L.T("잠금됨") : L.T("잠금");
                confirmBtn.interactable = t.me.locked && t.other.locked && !t.me.confirmed;
                TextOf(confirmBtn).text = t.me.confirmed ? L.T("확정됨") : L.T("확정");
            }
            requestBtn.interactable = acceptBtn.interactable = declineBtn.interactable = !TradeClient.Busy;
            DrawLists();
        }

        static string State(TradeClient.Side s) =>
            s.confirmed ? L.T("<color=#8fe28f>확정</color>") : s.locked ? L.T("<color=#ffe066>잠금</color>") : L.T("<color=#8c96a8>준비 중</color>");

        void DrawLists()
        {
            var t = TradeClient.Current;
            bool active = t != null && t.Active;
            mineHead.text = L.T("<b>내 제안</b>") + (active ? "  " + State(t.me) : "");
            otherHead.text = (t != null ? L.F("<b>{0}님의 제안</b>", t.other.name) : L.T("<b>상대 제안</b>")) + (active ? "  " + State(t.other) : "");
            Fill(mineRows, active ? t.me.items : null, 0);
            Fill(otherRows, active ? t.other.items : null, 0);
            mineGold.text = active ? L.F("골드 {0:N0}G", t.me.gold) : "";
            otherGold.text = active ? L.F("골드 {0:N0}G", t.other.gold) : "";
            var left = Available();
            int pages = Mathf.Max(1, (left.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            bagPage.text = $"{page + 1}/{pages}";
            Fill(bagRows, active ? left : null, page * Rows);
        }

        /// <summary>Bag rows minus what is already offered.</summary>
        List<TradeClient.Item> Available()
        {
            var t = TradeClient.Current;
            var list = new List<TradeClient.Item>();
            foreach (var b in bag)
            {
                int offered = 0;
                if (t != null) foreach (var o in t.me.items) if (o.uuid == b.uuid) offered += o.count;
                if (b.count - offered > 0) list.Add(new TradeClient.Item { uuid = b.uuid, key = b.key, count = b.count - offered });
            }
            return list;
        }

        static void Fill(List<RowView> rows, List<TradeClient.Item> items, int from)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var v = rows[i];
                int at = from + i;
                bool on = items != null && at < items.Count;
                v.bg.gameObject.SetActive(on);
                if (!on) continue;
                var it = items[at];
                v.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(it.key));
                v.text.text = NameOf(it.key) + (it.count > 1 ? $"  <color=#b8c4d8>x{it.count:N0}</color>" : "");
            }
        }

        static string NameOf(string key) =>
            EquipmentDatabase.IsEquipment(key) ? EquipmentDatabase.RichName(key) : L.T(DungeonDatabase.ItemName(key));

        void Turn(int d) { page += d; DrawLists(); }

        void Put(int row)
        {
            var t = TradeClient.Current;
            if (t == null || !t.Active || TradeClient.Busy) return;
            var left = Available();
            int at = page * Rows + row;
            if (at >= left.Count) return;
            if (t.me.items.Count >= Rows) { SetStatus(L.T("한 번에 8칸까지 올릴 수 있습니다."), false); return; }
            var pick = left[at];
            int n = int.TryParse(countField.text, out int want) && want > 0 ? Mathf.Min(want, pick.count) : pick.count;
            var items = Copy(t.me.items);
            var same = items.Find(i => i.uuid == pick.uuid);
            if (same != null) same.count += n;
            else items.Add(new TradeClient.Item { uuid = pick.uuid, key = pick.key, count = n });
            TradeClient.Offer(t.me.gold, items);
        }

        void TakeBack(int row)
        {
            var t = TradeClient.Current;
            if (t == null || !t.Active || TradeClient.Busy || row >= t.me.items.Count) return;
            var items = Copy(t.me.items);
            items.RemoveAt(row);
            TradeClient.Offer(t.me.gold, items);
        }

        void SetGold()
        {
            var t = TradeClient.Current;
            if (t == null || !t.Active || TradeClient.Busy) return;
            long.TryParse(goldField.text, out long gold);
            TradeClient.Offer(System.Math.Max(0L, gold), Copy(t.me.items));
        }

        static List<TradeClient.Item> Copy(List<TradeClient.Item> src)
        {
            var list = new List<TradeClient.Item>();
            foreach (var i in src) list.Add(new TradeClient.Item { uuid = i.uuid, key = i.key, count = i.count });
            return list;
        }

        /// <summary>From the chat (/교환 이름) or the friends window: send a request, then show the window.</summary>
        public static void RequestAndOpen(string name)
        {
            TradeClient.Ensure();
            if (!string.IsNullOrWhiteSpace(name) && TradeClient.Current == null) TradeClient.Request(name);
            if (Instance != null) Game.Flow.OpenWindow(Instance);
        }
    }
}
