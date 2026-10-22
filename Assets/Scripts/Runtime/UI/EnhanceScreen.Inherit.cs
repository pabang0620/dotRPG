using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [INHERIT] The blacksmith's 계승 tab (InheritRules, Docs/PLAN_GEAR_INHERIT_2026-10-22.md): the selected Lv.10 / 20 / 30
    /// piece becomes the same piece one band up, keeping its +level by chance (else -1, inherited pieces lose 2 more).
    /// Tab (or the town scroll key) switches between 강화 and 계승, Esc goes back to 강화 first. Online the server rolls;
    /// offline <see cref="Equipment.TryInherit"/> applies the same rules locally.
    /// </summary>
    public partial class EnhanceScreen
    {
        Button inheritTab;
        Text inheritTabLabel;
        bool inheritMode;

        protected override bool UsesTabKey => true;

        void BuildInherit(Transform right)
        {
            inheritTab = Button(right, "InheritTab", "", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(124f, 52f), ToggleInherit, 18);
            inheritTabLabel = inheritTab.GetComponentInChildren<Text>();
        }

        static int PlayerLevel => Game.Session.Progression != null ? Game.Session.Progression.Level : 1;

        string TabKeyLabel => Game.Input != null && Game.Input.UsingGamepad ? Game.Input.GetBindingLabel(GameAction.TownScroll) : "Tab";

        void ToggleInherit() => SetInheritMode(!inheritMode);

        void SetInheritMode(bool on)
        {
            if (busy || ticketPending || !IsTop) return;
            inheritMode = on;
            Picked();
            Game.Audio.PlaySfx("select", 0.6f);
            Refresh();
        }

        /// <summary>Tab / town scroll key switch the tab, Esc in the 계승 tab goes back to 강화 (the window stays open).</summary>
        bool InheritInput()
        {
            if (!TakesInput) return false;
            if (inheritMode && Game.Input.CancelPressed)
            {
                SetInheritMode(false);
                return true;
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab) || Game.Input.TownScrollPressed)
            {
                ToggleInherit();
                return true;
            }
            return false;
        }

        void RefreshInheritTab()
        {
            if (inheritTab == null) return;
            inheritTab.interactable = !busy;
            string name = inheritMode ? L.T("강화") : L.T("계승");
            inheritTabLabel.text = $"<b>{name}</b> <size=15>({TabKeyLabel})</size>";
            if (inheritMode && promoteButton != null) promoteButton.gameObject.SetActive(false);
        }

        void ShowInheritDetails(Entry e)
        {
            var bag = Game.Session.Inventory;
            var gear = EquipmentDatabase.Get(e.key);
            var p = InheritRules.Preview(e.key);
            RefreshTicket(null);
            bigIcon.enabled = true;
            bigIcon.sprite = Game.Art.Get(gear.iconKey);
            bigFrame.color = EquipmentDatabase.RarityTint(gear.rarity);
            foreach (var t in costCells) t.text = "";
            string where = e.slot.HasValue ? L.F("{0} 착용 중", L.T(EquipmentDatabase.SlotName(e.slot.Value))) : L.F("가방 {0}개", bag.Count(e.key));
            string head = $"<b>{EquipmentDatabase.RichName(e.key)}</b>\n<size=19><color=#b8c4d8>[{L.T(EquipmentDatabase.RarityName(gear.rarity))}] {L.T(gear.CategoryName)} · {where}</color></size>";
            if (p.kind != InheritOutcome.Ready)
            {
                title.text = head;
                statsText.text = CannotInherit(gear, p);
                costTitle.text = chanceText.text = failText.text = "";
                enhanceButton.interactable = false;
                enhanceLabel.text = L.T("계승 불가");
                return;
            }
            title.text = head + "\n" + L.F("<size=19>→ Lv.{0} {1}</size>", p.to.reqLevel, EquipmentDatabase.RichName(EquipmentDatabase.KeyFor(p.to.id, 0)));
            var now = gear.StatsAt(p.oldLevel);
            var kept = p.to.StatsAt(p.keptLevel);
            var failed = p.to.StatsAt(p.failLevel);
            statsText.text = L.F("<b>계승 후 능력치</b>   <color=#b8c4d8>전투력 {0:N0} → </color><color=#8fe28f>{1:N0}</color>", now.Score, kept.Score) +
                             (p.keptLevel != p.failLevel ? L.F("<color=#b8c4d8> (실패 시 {0:N0})</color>", failed.Score) : "") + "\n" +
                             L.F("현재  {0}\n계승 후  <color=#8fe28f>{1}</color>", gear.StatLine(p.oldLevel), p.to.StatLine(p.keptLevel));
            costTitle.text = L.T("<b>계승 비용</b>");
            costCells[0].text = Need(L.T("골드"), Game.Session.Gold, p.gold);
            costCells[1].text = L.F("계승 횟수  {0}회 → <color=#ffe066>{1}회</color>", p.oldCount, p.newCount);
            chanceText.text = p.keptLevel == p.failLevel
                ? L.F("결과  <color=#ffe066><b>+{0}</b></color>  (확정)", p.keptLevel)
                : L.F("유지 <color=#ffe066><b>{0}%</b></color> → +{1}   /   실패 → <color=#ff9f43>+{2}</color>", p.keepPercent, p.keptLevel, p.failLevel);
            bool tooLow = e.slot.HasValue && PlayerLevel < p.to.reqLevel;
            failText.text = tooLow
                ? L.F("<color=#ff7070>착용 중인 장비는 Lv.{0}부터 계승할 수 있습니다. 벗어서 가방에 두면 바로 됩니다.</color>", p.to.reqLevel)
                : p.oldCount > 0
                    ? L.F("<color=#8c96a8>이미 계승한 장비라 결과가 {0}단계 더 내려갑니다.</color>", InheritRules.StackPenalty(p.oldCount))
                    : L.T("<color=#8c96a8>강화 수치는 오르지 않고, 실패하면 1단계 내려갑니다.</color>");
            bool can = Game.Session.Gold >= p.gold && !tooLow;
            enhanceButton.interactable = can && !busy;
            enhanceLabel.text = busy ? L.T("계승 중…") : tooLow ? L.T("레벨 부족") : can ? L.T("계승") : L.T("골드 부족");
        }

        static string CannotInherit(EquipmentItem gear, InheritResult p)
        {
            if (gear.starter) return L.T("<color=#ff9f43>처음 받은 장비는 계승할 수 없습니다.</color>");
            if (p.oldCount >= InheritRules.MaxCount) return L.T("<color=#ff9f43>더 이상 계승할 수 없는 장비입니다.</color>");
            if (InheritRules.NextLevel(gear.reqLevel) == 0 || gear.bossOnly)
                return L.T("<color=#ff9f43>Lv.10 · 20 · 30 장비만 다음 레벨대(Lv.20 · 30 · 40)로 계승할 수 있습니다.</color>");
            return L.T("<color=#ff9f43>다음 레벨대에 같은 등급의 장비가 없어 계승할 수 없습니다.</color>");
        }

        void OnInheritPressed()
        {
            if (busy || ticketPending || entries.Count == 0 || !IsTop) return;
            var e = entries[selected];
            var p = InheritRules.Preview(e.key);
            if (p.kind != InheritOutcome.Ready) { Game.Audio.PlaySfx("cancel"); return; }
            if (e.slot.HasValue && PlayerLevel < p.to.reqLevel)
            {
                Game.Audio.PlaySfx("cancel");
                resultText.text = L.F("<color=#ff7070>착용 중인 장비는 Lv.{0}부터 계승할 수 있습니다.</color>", p.to.reqLevel);
                return;
            }
            if (Game.Session.Gold < p.gold)
            {
                Game.Audio.PlaySfx("cancel");
                resultText.text = L.T("<color=#ff7070>골드가 부족합니다.</color>");
                return;
            }
            string odds = p.keptLevel == p.failLevel ? L.F("결과 +{0} (확정)", p.keptLevel) : L.F("유지 {0}% → +{1} / 실패 → +{2}", p.keepPercent, p.keptLevel, p.failLevel);
            Game.Audio.PlaySfx("select");
            Game.UI.Confirm(L.F("<b>장비 계승</b>\n{0}  →  {1}\n<color=#b8c4d8>{2} · 골드 {3:N0} · 계승 {4}회째</color>\n원래 장비는 새 장비로 바뀝니다. 계승할까요?",
                EquipmentDatabase.RichName(e.key), EquipmentDatabase.RichName(EquipmentDatabase.KeyFor(p.to.id, 0)), odds, p.gold, p.newCount), () => StartInherit(e, p), true);
        }

        void StartInherit(Entry e, InheritResult preview)
        {
            if (busy || ticketPending) return;
            busy = true;
            resultText.text = L.T("<color=#b8c4d8>계승하는 중…</color>");
            Game.Audio.PlaySfx("hammer");
            EnhanceFx.Sparks(bigIcon.rectTransform, 10, new Color32(160, 200, 255, 255));
            if (OnlineEconomy.On)
            {
                OnlineEconomy.Inherit(e.slot.HasValue ? (int)e.slot.Value : (int?)null, e.slot.HasValue ? null : e.key, d =>
                {
                    busy = false;
                    if (d == null) { resultText.text = ""; Refresh(); return; }
                    var cost = MiniJson.Obj(d, "cost");
                    if (MiniJson.Has(d, "pity")) Game.Session.Equipment.SetPityFromServer(MiniJson.Str(d, "new_key"), MiniJson.Int(d, "pity"));
                    FinishInherit(e, MiniJson.Str(d, "new_key"), d.TryGetValue("kept", out var kv) && kv is bool kb && kb, preview.oldLevel, cost != null ? MiniJson.Int(cost, "gold") : preview.gold);
                });
                return;
            }
            InheritResult r;
            try { r = Game.Session.Equipment.TryInherit(e.Target, Authority.Current.EnhanceRoll()); }
            finally { busy = false; }
            if (r.kind != InheritOutcome.Done)
            {
                Game.Audio.PlaySfx("cancel");
                resultText.text = r.kind == InheritOutcome.NotEnough ? L.T("<color=#ff7070>골드가 부족합니다.</color>") : "";
                Refresh();
                return;
            }
            FinishInherit(e, r.newKey, r.kept, r.oldLevel, r.gold);
        }

        void FinishInherit(Entry e, string newKey, bool kept, int oldLevel, int gold)
        {
            int newLevel = EquipmentDatabase.LevelOfKey(newKey);
            if (kept && newLevel == oldLevel)
            {
                Game.Audio.PlaySfx("enhance_success");
                EnhanceFx.Burst(bigIcon.rectTransform, 24, new Color32(160, 210, 255, 255), false);
                resultText.text = L.F("<color=#8fe28f><b>계승 성공! 강화 수치 유지</b></color>\n{0}", EquipmentDatabase.RichName(newKey));
            }
            else
            {
                Game.Audio.PlaySfx(kept ? "enhance_success" : "enhance_fail");
                if (!kept) EnhanceFx.Fail(bigIcon.rectTransform, false);
                string why = kept ? L.T("계승 횟수만큼 내려갔습니다") : L.T("유지 실패");
                resultText.text = L.F("<color=#ffb070><b>계승 완료 · {0}</b>  (+{1} → +{2})</color>\n{3}", why, oldLevel, newLevel, EquipmentDatabase.RichName(newKey));
            }
            GameEvents.RaiseToast(L.F("계승: {0}", EquipmentDatabase.RichName(newKey)));
            // Keep the cursor on the inherited piece.
            BuildEntries();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].key == newKey && entries[i].slot == e.slot) { selected = i; break; }
            Game.Flow.Autosave();
            Refresh();
        }

        /// <summary>[DEV] Inherits the selected entry with a fixed roll (offline rules), for automation.</summary>
        public InheritResult DevInherit(int roll)
        {
            if (entries.Count == 0) return default;
            return Game.Session.Equipment.TryInherit(entries[selected].Target, roll);
        }

        /// <summary>[DEV] The 계승 tab's preview of the selected entry.</summary>
        public InheritResult DevInheritPreview => entries.Count == 0 ? default : InheritRules.Preview(entries[selected].key);
    }
}
