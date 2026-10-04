using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [STORY] Quest log: left, the quests in play (main first, then side quests, then what is done);
    /// right, the chosen quest's chapter, story, objectives and reward. Up/down picks, Enter pins a side
    /// quest to the HUD tracker (the main quest is always shown there).
    /// </summary>
    public class QuestScreen : WindowScreen
    {
        const float RowHeight = 54f, ListWidth = 420f;

        sealed class Row
        {
            public QuestDef quest;
            public Image bg;
            public Text text;
        }

        RectTransform listRoot;
        Text detail, hint, empty;
        readonly List<Row> rows = new List<Row>();
        int selected;
        string selectedId = "";

        public static QuestScreen Create(Transform canvas)
        {
            var w = CreateWindow<QuestScreen>(canvas, "QuestLog", "퀘스트", "menuicon_quest");
            var list = Panel(w.content, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(ListWidth, 590f), new Color32(18, 26, 40, 240));
            w.listRoot = list.rectTransform;
            w.empty = Label(list.transform, "Empty", "진행 중인 퀘스트가 없다.", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(ListWidth - 40f, 40f), TextAnchor.MiddleCenter);
            var side = Panel(w.content, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ListWidth + 20f, 0f), new Vector2(760f, 590f), new Color32(24, 36, 54, 235));
            w.detail = Label(side.transform, "Body", "", 21, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(704f, 500f));
            w.hint = Label(side.transform, "Hint", "", UiTheme.FontMin, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 16f), new Vector2(704f, 30f));
            w.hint.color = new Color32(184, 196, 216, 255);
            // Pick this quest for auto-progress (unpicked: the main quest first, then the pinned side quest).
            w.autoBtn = Button(side.transform, "AutoTarget", "자동 진행 대상으로", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 52f), new Vector2(240f, 44f), w.ToggleAutoTarget, 18);
            if (Game.Quest != null) Game.Quest.Changed += w.OnQuestChanged;
            return w;
        }

        void OnDestroy()
        {
            if (Game.Quest != null) Game.Quest.Changed -= OnQuestChanged;
        }

        void OnQuestChanged()
        {
            if (gameObject.activeInHierarchy) Refresh();
        }

        List<QuestDef> Ordered()
        {
            var q = Game.Quest;
            var list = new List<QuestDef>();
            void AddAll(QuestKind kind, params QuestStatus[] st)
            {
                foreach (var d in q.WithStatus(st)) if (d.Kind == kind && !list.Contains(d)) list.Add(d);
            }
            AddAll(QuestKind.Main, QuestStatus.Active, QuestStatus.ReadyToTurnIn, QuestStatus.Available);
            AddAll(QuestKind.Sub, QuestStatus.Active, QuestStatus.ReadyToTurnIn, QuestStatus.Available);
            // Finished quests last, newest chapter first.
            var done = q.WithStatus(QuestStatus.Completed);
            done.Reverse();
            foreach (var d in done) list.Add(d);
            return list;
        }

        protected override void Refresh()
        {
            foreach (var r in rows) Destroy(r.bg.gameObject);
            rows.Clear();
            var quests = Ordered();
            empty.gameObject.SetActive(quests.Count == 0);
            int maxRows = Mathf.FloorToInt((590f - 16f) / RowHeight);
            for (int i = 0; i < quests.Count && i < maxRows; i++) rows.Add(BuildRow(quests[i], i));
            selected = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i].quest.id == selectedId) selected = i;
            Select(selected);
        }

        Row BuildRow(QuestDef q, int index)
        {
            var row = new Row { quest = q };
            row.bg = Img(listRoot, "Row_" + q.id, "ui_white", Color.clear);
            row.bg.raycastTarget = true;
            UIFactory.Place(row.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -8f - index * RowHeight), new Vector2(ListWidth - 16f, RowHeight - 4f));
            var st = Game.Quest.StatusOf(q.id);
            string tag = q.Kind == QuestKind.Main ? "<color=#ffd640>메인</color>" : "<color=#78d6ff>서브</color>";
            string state = st == QuestStatus.Completed ? "<color=#8fe28f>완료</color>"
                : st == QuestStatus.ReadyToTurnIn ? "<color=#ffd640>보고</color>"
                : st == QuestStatus.Available ? "<color=#ff9f43>수락 전</color>" : "";
            string pin = (Game.Session.Journal.Tracked == q.id ? " <color=#78d6ff>[추적]</color>" : "")
                + (QuestAutoPilot.TargetQuestId == q.id ? " <color=#8fe28f>[자동]</color>" : "");
            row.text = UIFactory.Text(row.bg.rectTransform, "Text", $"{tag}  {q.DisplayTitle}{pin}  {state}", 20,
                st == QuestStatus.Completed ? new Color32(150, 160, 176, 255) : new Color32(246, 231, 200, 255), TextAnchor.MiddleLeft, true);
            UIFactory.Stretch(row.text.rectTransform, 14f, 8f, 0f, 0f);
            var button = row.bg.gameObject.AddComponent<Button>();
            button.targetGraphic = row.bg;
            int captured = index;
            button.onClick.AddListener(() =>
            {
                if (captured == selected) TogglePin();
                else
                {
                    Game.Audio.PlaySfx("select", 0.5f);
                    Select(captured);
                }
            });
            return row;
        }

        void Select(int index)
        {
            if (rows.Count == 0)
            {
                detail.text = "";
                hint.text = "";
                return;
            }
            selected = Mathf.Clamp(index, 0, rows.Count - 1);
            selectedId = rows[selected].quest.id;
            for (int i = 0; i < rows.Count; i++)
                rows[i].bg.color = i == selected ? new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.28f) : new Color(1f, 1f, 1f, 0.04f);
            ShowDetail(rows[selected].quest);
        }

        void ShowDetail(QuestDef q)
        {
            var mgr = Game.Quest;
            var st = mgr.StatusOf(q.id);
            var sb = new StringBuilder();
            var chapter = mgr.Database.Chapter(q.chapter);
            if (chapter != null) sb.Append($"<color=#b8c4d8>챕터 {chapter.number} · {chapter.title}</color>\n");
            sb.Append($"<size=30><b>{q.DisplayTitle}</b></size>\n\n");
            if (!string.IsNullOrEmpty(q.summary)) sb.Append(mgr.FormatTokens(q.summary)).Append("\n\n");
            if (st != QuestStatus.Completed)
            {
                var step = mgr.CurrentStep(q);
                if (st == QuestStatus.Active && step != null && !string.IsNullOrEmpty(step.text))
                    sb.Append($"<color=#ffe066>{mgr.FormatTokens(step.text)}</color>\n");
                sb.Append("<b>목표</b>\n");
                foreach (var o in mgr.ObjectivesOf(q)) sb.Append(o.done ? $"<color=#8fe28f>● {o.text}</color>\n" : $"○ {o.text}\n");
                if (!string.IsNullOrEmpty(q.giver) && st == QuestStatus.Available) sb.Append($"\n<color=#b8c4d8>의뢰인: {mgr.NpcName(q.giver)}</color>\n");
            }
            else sb.Append("<color=#8fe28f>완료한 퀘스트</color>\n");
            string reward = RewardText(q.reward);
            if (reward.Length > 0) sb.Append($"\n<b>보상</b>\n{reward}");
            detail.text = sb.ToString();
            bool canAuto = st != QuestStatus.Completed && st != QuestStatus.Locked;
            autoBtn.gameObject.SetActive(canAuto);
            if (canAuto) TextOf(autoBtn).text = QuestAutoPilot.TargetQuestId == q.id ? "자동 진행 대상 해제" : "자동 진행 대상으로";
            hint.text = q.Kind == QuestKind.Sub && st != QuestStatus.Completed
                ? $"[{Game.Input.GetBindingLabel(GameAction.Submit)}] 화면 오른쪽 알리미에 추적 / 해제   ·   ↑↓ 선택"
                : "↑↓ 선택   ·   메인 퀘스트는 항상 알리미에 표시된다";
        }

        static string RewardText(QuestRewardDef r)
        {
            if (r == null) return "";
            var parts = new List<string>();
            if (r.xp > 0) parts.Add($"경험치 {Progression.QuestXp(r.xp):N0}");
            if (r.gold > 0) parts.Add($"골드 {r.gold:N0}");
            if (r.maxHealth > 0) parts.Add($"최대 체력 +{EquipmentDatabase.Hearts(r.maxHealth)}");
            if (r.items != null)
                foreach (var it in r.items)
                    if (!string.IsNullOrEmpty(it.id)) parts.Add(it.count > 1 ? $"{QuestManager.ItemName(it.id)} x{it.count}" : QuestManager.ItemName(it.id));
            return string.Join("   ·   ", parts);
        }

        Button autoBtn;

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        void ToggleAutoTarget()
        {
            if (rows.Count == 0) return;
            var q = rows[selected].quest;
            QuestAutoPilot.TargetQuestId = QuestAutoPilot.TargetQuestId == q.id ? "" : q.id;
            Game.Audio.PlaySfx("confirm");
            GameEvents.RaiseToast(QuestAutoPilot.TargetQuestId == q.id ? $"자동 진행: '{q.title}'를 진행합니다." : "자동 진행: 메인 퀘스트부터 진행합니다.");
            Refresh();
        }

        void TogglePin()
        {
            if (rows.Count == 0) return;
            var q = rows[selected].quest;
            if (q.Kind != QuestKind.Sub || Game.Quest.StatusOf(q.id) == QuestStatus.Completed) return;
            var j = Game.Session.Journal;
            j.Tracked = j.Tracked == q.id ? "" : q.id;
            Game.Audio.PlaySfx("confirm");
            Game.Quest.NotifyChanged();
        }

        protected override void Update()
        {
            base.Update();
            if (!TakesInput || !gameObject.activeInHierarchy) return;
            var input = Game.Input;
            int dy = input.NavigateStep.y;
            if (dy != 0 && rows.Count > 0)
            {
                Game.Audio.PlaySfx("select", 0.5f);
                Select((selected - dy + rows.Count) % rows.Count);
            }
            if (input.SubmitPressed) TogglePin();
        }
    }
}
