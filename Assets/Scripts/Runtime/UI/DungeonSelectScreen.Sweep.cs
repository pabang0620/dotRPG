using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [SWEEP] Dungeon clear tickets on the dungeon window (Docs/PLAN_SWEEP_AND_MAIL.md, phase10 12): a "소탕" button
    /// next to 입장 opens a panel over the detail with the wallet, today's entries, sweep once / sweep all, ticket
    /// purchase and the weekly activity reward. Every number comes from the server (SweepClient).
    /// </summary>
    public partial class DungeonSelectScreen
    {
        Button sweepButton;
        RectTransform sweepPanel;
        Text sweepHead, sweepInfo, sweepRule, sweepShop, sweepWeekly, sweepResult;
        Button sweepOne, sweepAll, buyOne, buyMax, weeklyClaim;
        bool sweepHooked;

        void BuildSweep(Transform d)
        {
            sweepButton = Button(d, "Sweep", "소탕", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-276f, 20f), new Vector2(140f, 62f), OpenSweep, 24);

            var panel = Panel(d, "SweepPanel", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero, new Color32(18, 26, 40, 250));
            sweepPanel = panel.rectTransform;
            sweepPanel.anchorMin = Vector2.zero; sweepPanel.anchorMax = Vector2.one; sweepPanel.offsetMin = sweepPanel.offsetMax = Vector2.zero;
            panel.raycastTarget = true; // blocks the detail underneath
            var t = sweepPanel.transform;
            var tl = new Vector2(0f, 1f);
            sweepHead = Label(t, "Head", "", 26, tl, tl, new Vector2(24f, -14f), new Vector2(620f, 38f));
            Button(t, "Close", "닫기", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(110f, 40f), CloseSweep, 18);
            var icon = UIFactory.SharpIcon(t, "Ticket", Color.white);
            icon.sprite = Game.Art.Get("icon_sweep");
            UIFactory.Place(icon.rectTransform, tl, tl, new Vector2(24f, -64f), new Vector2(56f, 56f));
            sweepInfo = Label(t, "Info", "", 18, tl, tl, new Vector2(92f, -60f), new Vector2(712f, 66f));
            sweepRule = Label(t, "Rule", "", 17, tl, tl, new Vector2(24f, -134f), new Vector2(780f, 96f));
            sweepOne = Button(t, "SweepOne", "소탕 1회", "ui_btn", tl, tl, new Vector2(24f, -236f), new Vector2(220f, 56f), () => DoSweep(false), 22);
            sweepAll = Button(t, "SweepAll", "모두 소탕", "ui_btn", tl, tl, new Vector2(256f, -236f), new Vector2(240f, 56f), () => DoSweep(true), 22);
            sweepResult = Label(t, "Result", "", 17, tl, tl, new Vector2(512f, -232f), new Vector2(296f, 120f));

            sweepShop = Label(t, "Shop", "", 17, tl, tl, new Vector2(24f, -314f), new Vector2(480f, 50f));
            buyOne = Button(t, "BuyOne", "1장 구매", "ui_btngray", tl, tl, new Vector2(24f, -368f), new Vector2(150f, 44f), () => Buy(false), 18);
            buyMax = Button(t, "BuyMax", "남은 만큼 구매", "ui_btngray", tl, tl, new Vector2(186f, -368f), new Vector2(190f, 44f), () => Buy(true), 18);

            sweepWeekly = Label(t, "Weekly", "", 17, tl, tl, new Vector2(24f, -428f), new Vector2(560f, 50f));
            weeklyClaim = Button(t, "WeeklyClaim", "주간 보상 받기", "ui_btn", tl, tl, new Vector2(596f, -430f), new Vector2(200f, 46f), ClaimWeekly, 18);
            sweepPanel.gameObject.SetActive(false);
        }

        void OnDisable() { if (sweepPanel != null) sweepPanel.gameObject.SetActive(false); }

        bool SweepOpen => sweepPanel != null && sweepPanel.gameObject.activeSelf;

        void OpenSweep()
        {
            var def = Selected;
            if (def == null || def.isRaid) return;
            if (!OnlineSession.Playing) { GameEvents.RaiseToast("던전 소탕은 온라인 캐릭터로 접속했을 때 쓸 수 있습니다."); return; }
            if (!sweepHooked) { SweepClient.Changed += () => { if (this != null && SweepOpen) RefreshSweep(); }; sweepHooked = true; }
            sweepResult.text = "";
            sweepPanel.gameObject.SetActive(true);
            sweepPanel.SetAsLastSibling();
            Game.Audio.PlaySfx("select");
            SweepClient.Refresh();
            RefreshSweep();
        }

        void CloseSweep()
        {
            if (!SweepOpen) return;
            sweepPanel.gameObject.SetActive(false);
            Game.Audio.PlaySfx("cancel");
        }

        void RefreshSweep()
        {
            if (sweepButton == null) return;
            var def = Selected;
            bool daily = def != null && !def.isRaid;
            sweepButton.gameObject.SetActive(daily && OnlineSession.Playing);
            if (!SweepOpen) return;
            if (!daily) { sweepPanel.gameObject.SetActive(false); return; }

            string diffName = DungeonDatabase.Difficulty(difficulty).name;
            sweepHead.text = $"<b>{def.name}</b>  <color=#ffd34a>{diffName}</color>  소탕";
            if (SweepClient.Unavailable || !SweepClient.Loaded)
            {
                sweepInfo.text = SweepClient.Unavailable ? "<color=#ff9f7a>던전 소탕은 아직 준비 중입니다.</color>" : "<color=#b8c4d8>불러오는 중...</color>";
                sweepRule.text = sweepShop.text = sweepWeekly.text = "";
                foreach (var b in new[] { sweepOne, sweepAll, buyOne, buyMax, weeklyClaim }) b.gameObject.SetActive(false);
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"던전 클리어권 <color=#ffd34a><b>{SweepClient.TicketsTotal}</b>장</color>");
            int eventCount = 0; foreach (var e in SweepClient.EventTickets) eventCount += e.count;
            if (eventCount > 0) sb.Append($" <color=#b8c4d8>(이벤트 {eventCount}장 · 기한 가까운 것부터 사용)</color>");
            sb.Append($"\n오늘 남은 입장 <color={(SweepClient.EntriesLeft > 0 ? "#8fe28f" : "#ff9f7a")}>{SweepClient.EntriesLeft}/{SweepClient.EntriesLimit}</color>");
            sweepInfo.text = sb.ToString();

            var slot = SweepClient.Slot(def.id, difficulty);
            int level = Game.Session.Progression.Level;
            string cond = slot == null ? "<color=#ff9f7a>이 던전은 소탕할 수 없습니다.</color>"
                : slot.canSweep ? $"<color=#8fe28f>소탕 가능</color> · 최고 등급 {RankName(slot.bestRank)} · 예상 경험치 {Progression.XpPercent(slot.xp, level)}"
                : $"<color=#ff9f7a>{SweepClient.BlockText(slot.block, slot)}</color>" + (slot.bestRank >= 0 ? $" <color=#b8c4d8>(최고 {RankName(slot.bestRank)})</color>" : "");
            sweepRule.text = cond + "\n<color=#b8c4d8>소탕 1회 = 클리어권 1장 + 입장 1회. 보상은 기본 클리어 경험치(등급 보너스 없음)와 무작위 카드 1장입니다.\n처치 경험치·드롭은 없고 장비 카드는 덜 나옵니다. 직접 B등급 이상으로 깬 난이도만 됩니다.</color>";

            bool can = slot != null && slot.canSweep && !SweepClient.Hold && SweepClient.EntriesLeft > 0 && SweepClient.TicketsTotal > 0 && !SweepClient.Busy;
            int times = Mathf.Min(SweepClient.EntriesLeft, SweepClient.TicketsTotal);
            sweepOne.gameObject.SetActive(true); sweepAll.gameObject.SetActive(true);
            sweepOne.interactable = can;
            sweepAll.interactable = can && times > 1;
            TextOf(sweepAll).text = times > 1 ? $"모두 소탕 ({times}회)" : "모두 소탕";

            sweepShop.text = $"<b>클리어권 구매</b>  장당 <color=#ffd34a>{SweepClient.UnitPrice:N0} G</color>\n<color=#b8c4d8>이번 주 남은 구매 {SweepClient.ShopWeeklyLeft}/{SweepClient.ShopWeeklyLimit}장 (계정 공용, 목요일 06:00 초기화)</color>";
            bool canBuy = SweepClient.ShopWeeklyLeft > 0 && !SweepClient.Hold && !SweepClient.Busy;
            buyOne.gameObject.SetActive(true); buyMax.gameObject.SetActive(true);
            buyOne.interactable = canBuy;
            buyMax.interactable = canBuy && SweepClient.ShopWeeklyLeft > 1;

            sweepWeekly.text = $"<b>주간 활동</b>  요일 던전 직접 클리어 <color=#ffd34a>{SweepClient.WeeklyProgress}/{SweepClient.WeeklyGoal}</color> -> 클리어권 {SweepClient.WeeklyReward}장" +
                               (SweepClient.WeeklyClaimed ? "  <color=#8fe28f>받음</color>" : "") + "\n<color=#b8c4d8>계정의 모든 캐릭터 기록을 합칩니다. 소탕은 세지 않습니다.</color>";
            weeklyClaim.gameObject.SetActive(!SweepClient.WeeklyClaimed);
            weeklyClaim.interactable = SweepClient.WeeklyClaimable && !SweepClient.Busy;
            if (SweepClient.Hold) sweepShop.text += "\n<color=#ff9f7a>계정 점검 중이라 소탕·구매가 잠시 막혀 있습니다.</color>";
        }

        static string RankName(int rank) => rank >= 0 && rank <= (int)DungeonRank.F ? ((DungeonRank)rank).ToString() : "-";

        void DoSweep(bool all)
        {
            var def = Selected;
            if (def == null) return;
            int times = Mathf.Min(SweepClient.EntriesLeft, SweepClient.TicketsTotal);
            string ask = all ? $"클리어권 {times}장과 입장 {times}회를 써서 {times}번 소탕할까요?" : "클리어권 1장과 입장 1회를 써서 소탕할까요?";
            Game.UI.Confirm($"{def.name} {DungeonDatabase.Difficulty(difficulty).name}\n{ask}", () =>
            {
                sweepResult.text = "<color=#b8c4d8>소탕하는 중...</color>";
                SweepClient.Run(def.id, difficulty, all, (ok, msg, list, limitedBy) =>
                {
                    if (!ok) { sweepResult.text = $"<color=#ff9f7a>{msg}</color>"; Game.Audio.PlaySfx("cancel"); return; }
                    Game.Audio.PlaySfx("dungeon_clear");
                    var sb = new StringBuilder($"<b>소탕 완료</b> {list.Count}회");
                    long xp = 0; bool up = false;
                    foreach (var s in list) { xp += s.xp; up |= s.leveledUp; }
                    sb.Append($"\n경험치 <color=#8fe28f>+{xp:N0}</color>{(up ? "  <color=#ffd34a>레벨 업!</color>" : "")}");
                    foreach (var s in list)
                        if (!string.IsNullOrEmpty(s.cardKey))
                            sb.Append($"\n카드: {(EquipmentDatabase.IsEquipment(s.cardKey) ? EquipmentDatabase.RichName(s.cardKey) : DungeonDatabase.ItemName(s.cardKey))}{(s.cardCount > 1 ? $" x{s.cardCount:N0}" : "")}");
                    if (limitedBy == "tickets") sb.Append("\n<color=#b8c4d8>클리어권이 모자라 일부만 소탕했습니다.</color>");
                    sweepResult.text = sb.ToString();
                    GameEvents.RaiseToast($"소탕 {list.Count}회 완료 · 경험치 +{xp:N0}");
                    Refresh();
                });
            }, true);
        }

        void Buy(bool max)
        {
            int n = max ? SweepClient.ShopWeeklyLeft : 1;
            if (n <= 0) return;
            Game.UI.Confirm($"던전 클리어권 {n}장을 {SweepClient.UnitPrice * n:N0} G에 살까요?\n<size=18>클리어권은 계정 공용이며 거래할 수 없습니다.</size>", () =>
                SweepClient.Buy(n, (ok, msg) => { GameEvents.RaiseToast(msg); Game.Audio.PlaySfx(ok ? "confirm" : "cancel"); }), true);
        }

        void ClaimWeekly() => SweepClient.ClaimWeekly((ok, msg) => { GameEvents.RaiseToast(msg); Game.Audio.PlaySfx(ok ? "quest" : "cancel"); });
    }
}
