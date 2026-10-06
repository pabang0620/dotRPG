using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [MAIL 10] The mailbox as its own window (side menu "우편"): the list on the left (title, sender tag, days left,
    /// first attachment), the chosen mail on the right (title, body, every attachment, expiry) with 받기 and 모두 받기.
    /// Operator mail (maintenance, apology, event) carries a title, a body and up to five attachments; auction mail
    /// keeps its one-line text. Same service as the auction house mail tab (OnlineServices.Auction).
    /// </summary>
    public class MailScreen : OnlineWindow
    {
        public static MailScreen Instance { get; private set; }
        const int Rows = 9;
        const float ListW = 500f, RowH = 58f, DetailW = 690f;

        sealed class RowView { public Image bg, icon; public Text title, sub; }
        readonly List<RowView> rows = new List<RowView>();
        readonly List<(Image icon, Text count)> atts = new List<(Image, Text)>();
        IAuctionService Service => OnlineServices.Auction;
        IReadOnlyList<AuctionMail> mails = new List<AuctionMail>();
        int page, selected;
        Text pageText, empty, title, meta, body, attNote;
        Button claimBtn, claimAllBtn;

        protected override bool PadNavigation => true;

        public static MailScreen Create(Transform canvas)
        {
            var w = CreateWindow<MailScreen>(canvas, "Mail", "우편함", "menuicon_mail");
            Instance = w;
            var tl = new Vector2(0f, 1f);
            var list = Panel(w.content, "List", tl, tl, new Vector2(0f, -6f), new Vector2(ListW, Rows * RowH + 70f), new Color32(18, 26, 40, 240));
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var v = new RowView();
                v.bg = Panel(list.transform, "Row" + i, tl, tl, new Vector2(8f, -8f - i * RowH), new Vector2(ListW - 16f, RowH - 4f), new Color(1f, 1f, 1f, 0.04f));
                v.bg.raycastTarget = true;
                var b = v.bg.gameObject.AddComponent<Button>();
                b.targetGraphic = v.bg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => w.Select(w.page * Rows + idx));
                v.icon = UIFactory.SharpIcon(v.bg.transform, "Icon", Color.white);
                v.icon.raycastTarget = false;
                UIFactory.Place(v.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(40f, 40f));
                v.title = Label(v.bg.transform, "Title", "", 18, tl, tl, new Vector2(58f, -4f), new Vector2(ListW - 90f, 26f), TextAnchor.MiddleLeft);
                v.sub = Label(v.bg.transform, "Sub", "", 16, tl, tl, new Vector2(58f, -28f), new Vector2(ListW - 90f, 22f), TextAnchor.MiddleLeft);
                w.rows.Add(v);
            }
            w.empty = Label(list.transform, "Empty", "받을 우편이 없습니다.", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(ListW - 40f, 40f), TextAnchor.MiddleCenter);
            var br = new Vector2(1f, 0f);
            Button(list.transform, "Prev", "◀", "ui_btngray", br, br, new Vector2(-130f, 10f), new Vector2(44f, 40f), () => w.Turn(-1), 18);
            w.pageText = Label(list.transform, "Page", "", 18, br, br, new Vector2(-54f, 10f), new Vector2(76f, 40f), TextAnchor.MiddleCenter);
            Button(list.transform, "Next", "▶", "ui_btngray", br, br, new Vector2(-8f, 10f), new Vector2(44f, 40f), () => w.Turn(1), 18);
            w.claimAllBtn = Button(list.transform, "ClaimAll", "모두 받기", "ui_btn", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(10f, 8f), new Vector2(200f, 48f), w.ClaimAll, 20);

            var detail = Panel(w.content, "Detail", tl, tl, new Vector2(ListW + 16f, -6f), new Vector2(DetailW, Rows * RowH + 70f), new Color32(24, 36, 54, 235));
            var d = detail.transform;
            w.title = Label(d, "Title", "", 26, tl, tl, new Vector2(24f, -16f), new Vector2(DetailW - 48f, 38f));
            w.meta = Label(d, "Meta", "", 16, tl, tl, new Vector2(24f, -56f), new Vector2(DetailW - 48f, 24f));
            w.body = Label(d, "Body", "", 19, tl, tl, new Vector2(24f, -90f), new Vector2(DetailW - 48f, 230f));
            Label(d, "AttHead", "<b>첨부</b>", 18, tl, tl, new Vector2(24f, -330f), new Vector2(200f, 26f));
            for (int i = 0; i < 5; i++)
            {
                var slot = Panel(d, "Att" + i, tl, tl, new Vector2(24f + i * 92f, -362f), new Vector2(80f, 80f), new Color32(14, 20, 32, 255));
                var icon = UIFactory.SharpIcon(slot.transform, "Icon", Color.white);
                icon.raycastTarget = true;
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(56f, 56f));
                var count = UIFactory.Text(slot.transform, "Count", "", 16, Color.white, TextAnchor.LowerRight, true);
                UIFactory.Stretch(count.rectTransform, 2f, 2f, 6f, 2f);
                int at = i;
                GearTooltip.Hook(icon, () => w.AttKey(at));
                w.atts.Add((icon, count));
            }
            w.attNote = Label(d, "AttNote", "", 16, tl, tl, new Vector2(24f, -450f), new Vector2(DetailW - 48f, 44f));
            w.claimBtn = Button(d, "Claim", "받기", "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 14f), new Vector2(220f, 56f), w.ClaimSelected, 22);
            w.Service.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Show()
        {
            base.Show();
            page = 0;
            selected = 0;
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeInHierarchy) return;
            if (Service is ServerAuctionService server) server.Tick(true); // faster summary polling while open
        }

        protected override void Refresh()
        {
            mails = Service.Mailbox();
            foreach (var m in mails)
                if (m.attachments.Count == 0) // offline preview / old data: one item and gold
                {
                    if (m.gold > 0) m.attachments.Add(new MailAttachment { kind = "gold", count = m.gold });
                    if (!string.IsNullOrEmpty(m.itemKey)) m.attachments.Add(new MailAttachment { kind = "item", itemKey = m.itemKey, count = m.count });
                }
            int pages = Mathf.Max(1, (mails.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, mails.Count - 1));
            pageText.text = $"{page + 1}/{pages}";
            empty.gameObject.SetActive(mails.Count == 0);
            for (int i = 0; i < rows.Count; i++)
            {
                var v = rows[i];
                int at = page * Rows + i;
                bool on = at < mails.Count;
                v.bg.gameObject.SetActive(on);
                if (!on) continue;
                var m = mails[at];
                v.bg.color = at == selected ? new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.28f) : new Color(1f, 1f, 1f, 0.04f);
                v.icon.sprite = Game.Art.Get(IconOf(m.attachments.Count > 0 ? m.attachments[0] : null));
                v.title.text = Plain(m.title ?? m.text);
                v.sub.text = $"<color=#b8c4d8>{Tag(m)}</color>" + (m.attachments.Count > 1 ? $"  <color=#8c96a8>첨부 {m.attachments.Count}개</color>" : "")
                             + (m.daysLeft > 0 ? $"  <color={(m.daysLeft <= 3 ? "#ff9f7a" : "#8c96a8")}>{m.daysLeft}일 남음</color>" : "");
            }
            claimAllBtn.interactable = mails.Count > 0;
            ShowDetail(mails.Count > 0 ? mails[selected] : null);
        }

        void ShowDetail(AuctionMail m)
        {
            claimBtn.gameObject.SetActive(m != null);
            if (m == null)
            {
                title.text = meta.text = body.text = attNote.text = "";
                foreach (var a in atts) a.icon.transform.parent.gameObject.SetActive(false);
                return;
            }
            title.text = $"<b>{Plain(m.title ?? m.text)}</b>";
            meta.text = $"<color=#b8c4d8>{Tag(m)}</color>" + (m.daysLeft > 0 ? $"   <color=#8c96a8>받을 수 있는 기간 {m.daysLeft}일 남음 · 지나면 사라집니다</color>" : "");
            body.text = !string.IsNullOrEmpty(m.body) ? m.body : DefaultBody(m);
            var note = new StringBuilder();
            for (int i = 0; i < atts.Count; i++)
            {
                var (icon, count) = atts[i];
                bool on = i < m.attachments.Count;
                icon.transform.parent.gameObject.SetActive(on);
                if (!on) continue;
                var a = m.attachments[i];
                icon.sprite = Game.Art.Get(IconOf(a));
                count.text = a.kind == "gold" ? $"{a.count:N0}" : a.count > 1 ? $"x{a.count:N0}" : "";
                if (a.kind == "sweep_ticket" && a.validDays > 0) note.Append($"이벤트 클리어권은 받은 날부터 {a.validDays}일 안에 써야 합니다. ");
                note.Append($"{NameOf(a)}{(a.kind == "gold" ? "" : a.count > 1 ? $" x{a.count:N0}" : "")}   ");
            }
            attNote.text = $"<color=#b8c4d8>{note}</color>";
        }

        string AttKey(int index)
        {
            if (mails.Count == 0 || selected >= mails.Count) return null;
            var m = mails[selected];
            return index < m.attachments.Count ? m.attachments[index].itemKey : null;
        }

        void Select(int index)
        {
            if (index < 0 || index >= mails.Count) return;
            selected = index;
            Game.Audio.PlaySfx("select", 0.5f);
            Refresh();
        }

        void Turn(int delta)
        {
            int pages = Mathf.Max(1, (mails.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page + delta, 0, pages - 1);
            selected = Mathf.Min(page * Rows, Mathf.Max(0, mails.Count - 1));
            Refresh();
        }

        void ClaimSelected()
        {
            if (mails.Count == 0 || selected >= mails.Count) return;
            var r = Service.Claim(mails[selected].id);
            if (!r.ok) GameEvents.RaiseToast(r.message);
            Game.Audio.PlaySfx(r.ok ? "confirm" : "cancel");
        }

        void ClaimAll()
        {
            if (mails.Count == 0) return;
            var r = Service.ClaimAll();
            if (!r.ok) GameEvents.RaiseToast(r.message);
            Game.Audio.PlaySfx(r.ok ? "confirm" : "cancel");
        }

        static string IconOf(MailAttachment a) =>
            a == null ? "menuicon_mail" : a.kind == "gold" ? "icon_gold" : DungeonDatabase.ItemIcon(string.IsNullOrEmpty(a.itemKey) ? ConsumableDatabase.Gold : a.itemKey);

        static string NameOf(MailAttachment a) =>
            a.kind == "gold" ? $"골드 {a.count:N0}" : EquipmentDatabase.IsEquipment(a.itemKey) ? EquipmentDatabase.RichName(a.itemKey) : DungeonDatabase.ItemName(a.itemKey);

        static string Tag(AuctionMail m)
        {
            if (m.kind == "system")
                return m.systemCode switch
                {
                    "maintenance" => "운영팀 · 점검 보상",
                    "apology" => "운영팀 · 사과 보상",
                    "event" => "운영팀 · 이벤트",
                    "attendance" => "운영팀 · 출석",
                    "compensation" => "운영팀 · 보상",
                    "refund" => "운영팀 · 환불",
                    _ => "운영팀",
                };
            return m.kind switch
            {
                "sold" => "경매장 · 판매 대금",
                "bought" => "경매장 · 구매",
                "outbid" => "경매장 · 입찰 반환",
                "expired" => "경매장 · 기간 만료",
                "cancelled" => "경매장 · 등록 취소",
                _ => "우편",
            };
        }

        static string DefaultBody(AuctionMail m) => m.kind switch
        {
            "sold" => "경매장에 올린 물건이 팔렸습니다. 판매 대금(수수료 제외)을 받으세요.",
            "bought" => "경매장에서 산 물건입니다.",
            "outbid" => "더 높은 입찰이 들어와 예치한 골드를 돌려드립니다.",
            "expired" => "등록 기간이 끝나 팔리지 않은 물건을 돌려드립니다.",
            "cancelled" => "등록을 취소한 물건을 돌려드립니다. 보증금은 돌려드리지 않습니다.",
            _ => "운영팀이 보낸 우편입니다.",
        };

        /// <summary>The list shows the title without the "N일 남음" tail the old one-line text carries.</summary>
        static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOf("  <color=#8c96a8>", System.StringComparison.Ordinal);
            return cut > 0 ? text.Substring(0, cut) : text;
        }
    }
}
