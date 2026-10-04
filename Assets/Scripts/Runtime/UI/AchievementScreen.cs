using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 업적 window (side menu): every achievement with its progress, the ones done in gold with a 장착 button that
    /// puts its name as a title in front of my chat name ([칭호][이름]). The server judges and keeps everything.
    /// </summary>
    public class AchievementScreen : OnlineWindow
    {
        public static AchievementScreen Instance { get; private set; }
        const int PerColumn = 9;
        const float CellW = 590f, CellH = 58f, ColGap = 20f, Top = -56f;

        Text header;
        Button clearBtn;
        readonly List<(Image bg, Text name, Text info, Button equip)> cells = new List<(Image, Text, Text, Button)>();

        public static AchievementScreen Create(Transform canvas)
        {
            var w = CreateWindow<AchievementScreen>(canvas, "Achievements", "업적", "menuicon_quest");
            Instance = w;
            w.header = Label(w.content, "Header", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(900f, 40f), TextAnchor.MiddleLeft);
            w.clearBtn = Button(w.content, "Clear", "칭호 해제", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(150f, 40f), () => w.Equip(""), 17);
            for (int i = 0; i < PerColumn * 2; i++)
            {
                int col = i / PerColumn, row = i % PerColumn, idx = i;
                var bg = Panel(w.content, "A" + i, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(col * (CellW + ColGap), Top - row * CellH), new Vector2(CellW, CellH - 4f), i % 2 == 0 ? RowA : RowB);
                var name = Label(bg.transform, "Name", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -4f), new Vector2(360f, 26f), TextAnchor.MiddleLeft);
                var info = Label(bg.transform, "Info", "", 15, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -28f), new Vector2(440f, 24f), TextAnchor.MiddleLeft);
                var equip = Button(bg.transform, "Equip", "장착", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(110f, 38f), () => w.EquipAt(idx), 16);
                w.cells.Add((bg, name, info, equip));
            }
            AchievementClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Show()
        {
            base.Show();
            AchievementClient.Check(); // fresh progress when the window opens
            Refresh();
        }

        void EquipAt(int i)
        {
            var list = AchievementClient.All;
            if (i < list.Count && list[i].achieved) Equip(list[i].id);
        }

        void Equip(string id) => AchievementClient.Equip(id, (ok, msg) =>
        {
            Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
            if (!ok) GameEvents.RaiseToast(msg);
            else GameEvents.RaiseToast(string.IsNullOrEmpty(id) ? "칭호를 내렸습니다." : $"칭호 [{AchievementClient.MyTitle}]을(를) 달았습니다.");
        });

        protected override void Refresh()
        {
            var list = AchievementClient.All;
            int done = 0;
            foreach (var a in list) if (a.achieved) done++;
            header.text = list.Count == 0
                ? "<color=#8c96a8>업적을 불러오는 중입니다...</color>"
                : $"달성 {done}/{list.Count}   ·   칭호: " + (string.IsNullOrEmpty(AchievementClient.MyTitle) ? "<color=#8c96a8>없음</color>" : $"<color=#ffd34a>[{AchievementClient.MyTitle}]</color>  <color=#b8c4d8>채팅 이름 앞에 보입니다</color>");
            clearBtn.gameObject.SetActive(!string.IsNullOrEmpty(AchievementClient.MyTitleId));
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                var a = i < list.Count ? list[i] : null;
                c.bg.gameObject.SetActive(a != null);
                if (a == null) continue;
                c.name.text = a.achieved ? $"<color=#ffd34a><b>{a.title}</b></color>  <color=#8fe28f>달성</color>" : $"<color=#b8c4d8>{a.title}</color>";
                c.info.text = a.achieved ? $"<color=#b8c4d8>{a.description}</color>" : $"<color=#8c96a8>{a.description}  ({a.progress:N0}/{a.goal:N0})</color>";
                bool equipped = AchievementClient.MyTitleId == a.id;
                c.equip.gameObject.SetActive(a.achieved);
                TextOf(c.equip).text = equipped ? "장착 중" : "장착";
                c.equip.interactable = !equipped;
            }
        }
    }
}
