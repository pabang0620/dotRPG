using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [STORY] Quest log: left, the quests in play (main first, then side quests, then what is done);
    /// right, the chosen quest's chapter, story, objectives and reward. Up/down picks, Enter pins a side
    /// quest to the HUD tracker (the main quest is always shown there). The box at the left of each open quest
    /// checks it for auto-progress (several at once, done in the order they were checked).
    /// </summary>
    public class QuestScreen : WindowScreen
    {
        const float RowHeight = 54f, ListWidth = 420f;

        sealed class Row
        {
            public QuestDef quest;
            public Image bg, box;
            public Text text, check;
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
            // Check this quest for auto-progress (nothing checked: the main quest first, then the pinned side quest).
            w.autoBtn = Button(side.transform, "AutoTarget", "자동 진행에 체크", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 52f), new Vector2(240f, 44f), w.ToggleAutoTarget, 18);
            Button(list.transform,"CareerQuest","전직 · 각성 이야기 보기", "ui_btngray",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,14),new Vector2(390,48),()=>Game.UI.Skills.ShowAwakening(),20);
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
            int maxRows = Mathf.FloorToInt((520f - 16f) / RowHeight);
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
            string pin = Game.Session.Journal.Tracked == q.id ? " <color=#78d6ff>[추적]</color>" : "";
            row.text = UIFactory.Text(row.bg.rectTransform, "Text", $"{tag}  {q.DisplayTitle}{pin}  {state}", 20,
                st == QuestStatus.Completed ? new Color32(150, 160, 176, 255) : new Color32(246, 231, 200, 255), TextAnchor.MiddleLeft, true);
            UIFactory.Stretch(row.text.rectTransform, 50f, 8f, 0f, 0f);
            // Auto-progress check box (open quests only).
            if (st != QuestStatus.Completed && st != QuestStatus.Locked)
            {
                bool on = QuestAutoPilot.IsTarget(q.id);
                row.box = Img(row.bg.rectTransform, "AutoBox", "ui_white", on ? new Color32(70, 160, 90, 255) : new Color32(10, 14, 22, 230));
                row.box.raycastTarget = true;
                UIFactory.Place(row.box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, -13f), new Vector2(26f, 26f));
                var outline = row.box.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color32(184, 196, 216, 255);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                row.check = UIFactory.Text(row.box.rectTransform, "Check", on ? "V" : "", 18, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(row.check.rectTransform, 0f, 0f, 0f, 0f);
                var boxButton = row.box.gameObject.AddComponent<Button>();
                boxButton.targetGraphic = row.box;
                string qid = q.id;
                boxButton.onClick.AddListener(() => ToggleAuto(qid));
            }
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
            if (canAuto) TextOf(autoBtn).text = QuestAutoPilot.IsTarget(q.id) ? "자동 진행 체크 해제" : "자동 진행에 체크";
            string autoHint = QuestAutoPilot.Targets.Count > 0 ? $"자동 진행: 체크한 {QuestAutoPilot.Targets.Count}개를 체크한 순서대로" : "왼쪽 칸을 체크하면 그 퀘스트만 자동 진행";
            hint.text = q.Kind == QuestKind.Sub && st != QuestStatus.Completed
                ? $"[{Game.Input.GetBindingLabel(GameAction.Submit)}] 알리미 추적 / 해제   ·   {autoHint}"
                : $"↑↓ 선택   ·   {autoHint}";
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
            ToggleAuto(rows[selected].quest.id);
        }

        void ToggleAuto(string questId)
        {
            var q = Game.Quest.Database.Get(questId);
            if (q == null) return;
            QuestAutoPilot.ToggleTarget(questId);
            selectedId = questId;
            Game.Audio.PlaySfx("confirm");
            GameEvents.RaiseToast(QuestAutoPilot.IsTarget(questId) ? $"자동 진행에 '{q.title}'을(를) 체크했습니다."
                : QuestAutoPilot.Targets.Count > 0 ? $"'{q.title}' 체크를 풀었습니다." : "체크한 퀘스트가 없어 메인 퀘스트부터 자동 진행합니다.");
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
