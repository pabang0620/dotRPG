using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[UI 2026-10-30] Split from DungeonSelectScreen.cs (500-line rule): entering a dungeon, key input, reset ticket and automated checks.</summary>
    public partial class DungeonSelectScreen
    {
        /// <summary>The mid raids that drop seal key fragments, by name ("해골왕").</summary>
        static string KeySources()
        {
            var names = new List<string>();
            foreach (var r in DungeonDatabase.Raids) if (r.keyMax > 0) names.Add(L.T(r.name));
            return string.Join("·", names);
        }

        void PickDifficulty(DungeonDifficulty d)
        {
            var def = Selected;
            if (def == null || (def.isRaid && d > DungeonDatabase.RaidHeroic)) return;
            if (!Game.Session.Dungeons.IsUnlocked(def, d))
            {
                GameEvents.RaiseToast(def.isRaid ? L.F("{0} 레이드를 먼저 한 번 클리어하면 심화 레이드가 열립니다.", L.T(def.name)) : L.F("{0} 난이도를 먼저 클리어해야 합니다.", L.T(DungeonDatabase.Difficulty(d - 1).name)));
                Game.Audio.PlaySfx("cancel");
                return;
            }
            difficulty = d;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>Why a raid entry would give no reward (null = it gives the reward). Daily dungeons always reward.</summary>
        string PracticeReason(DungeonDef def, DateTime now) => PracticeReason(def, difficulty, now);

        static string PracticeReason(DungeonDef def, DungeonDifficulty difficulty, DateTime now)
        {
            if (def == null || !def.isRaid) return null;
            if (!Game.Session.Dungeons.RaidRewardAvailable(def, now, difficulty))
                return def.raidTier == RaidTier.Mid && difficulty != DungeonDatabase.RaidHeroic ? L.T("오늘 보상을 이미 받았습니다.") : L.T("이번 주 보상을 이미 받았습니다.");
            if (def.keyCost > 0 && Game.Session.Inventory.Count(DungeonDatabase.SealKey) < def.keyCost) return L.T("봉인 열쇠 조각이 모자랍니다.");
            return null;
        }

        bool TryEnter() => Enter(Selected, difficulty, Refresh);

        /// <summary>
        /// [UX] The one way into a dungeon, from this window or the party window's 출발: the practice-entry question, then DungeonDirector.Enter (with a party it takes everyone, the free seats get the
        /// AI companions). <paramref name="refresh"/> runs when the entry was refused.
        /// </summary>
        public static bool Enter(DungeonDef def, DungeonDifficulty difficulty, System.Action refresh = null)
        {
            if (def == null || Game.Dungeon == null) return false;
            string practice = Game.Dungeon.CannotEnterReason(def, difficulty, ResetClock.Now) == null ? PracticeReason(def, difficulty, ResetClock.Now) : null;
            // Automated runs (-dotrpgMeasure, captures) have nobody to answer the practice confirm: they enter straight away.
            if (practice != null && FindAnyObjectByType<DevCapture>() == null)
            {
                Game.UI.Confirm(practice + "\n" + L.T("<size=18>클리어해도 보상은 없고, 이야기 진행만 이어집니다.</size>\n연습으로 입장할까요?"), () => { if (!Game.Dungeon.Enter(def, difficulty)) refresh?.Invoke(); }, defaultYes: true, overlay: true); // [UX] nothing is lost: cursor on 예
                return false;
            }
            bool ok = Game.Dungeon.Enter(def, difficulty);
            if (!ok) refresh?.Invoke();
            return ok;
        }

        protected override bool UsesTabKey => true;
        protected override bool HasKeyTags => true;

        protected override void Update()
        {
            if (TouchUi.Enabled) FitTouch(); // [TOUCH] DungeonSelectScreen.Mobile.cs
            if (BossTrailer.Blocking) return; // [TRAILER] keys skip the trailer, they do not reach the window
            // [SWEEP] The sweep panel takes Esc (closes itself, not the window) and blocks the list keys under it.
            if (SweepOpen)
            {
                if (TakesInput && (Game.Input.CancelPressed || Game.Input.InventoryPressed)) CloseSweep();
                return;
            }
            if (RaidShopOpen) { RaidShopInput(); return; } // [RAID] the shop panel (and its box popup) takes the keys
            base.Update();
            if (!TakesInput || !gameObject.activeInHierarchy) return;
            var input = Game.Input;
            Vector2 move = input.Move;
            int dx = move.x > 0.5f && lastMoveX <= 0.5f ? 1 : move.x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
            int dy = move.y > 0.5f && lastMoveY <= 0.5f ? -1 : move.y < -0.5f && lastMoveY >= -0.5f ? 1 : 0;
            lastMoveX = move.x;
            lastMoveY = move.y;
            if (dy != 0 && rows.Count > 0)
            {
                selected = (selected + dy + rows.Count) % rows.Count;
                Game.Audio.PlaySfx("select");
                Refresh();
            }
            if (dx != 0 && Selected != null)
            {
                int top = Selected.isRaid ? (int)DungeonDatabase.RaidHeroic : DungeonDatabase.ShownDifficultyCount(Selected) - 1;
                var next = (DungeonDifficulty)Mathf.Clamp((int)difficulty + dx, 0, top);
                if (next != difficulty) PickDifficulty(next);
            }
            // [UX] Tab / LB · RB switch the tab; A (Submit) only enters (A was bound to both, so the pad could never enter).
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton4) || UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5)) SetTab((tab + 1) % 3);
            else if (input.SubmitPressed) TryEnter();
            // [UX] The mouse-only buttons get keys too: 1 / Y 소탕 (weekday dungeons online), 2 / L3 파티 편성.
            else if (input.UseItemPressed && sweepButton != null && sweepButton.gameObject.activeInHierarchy) OpenSweep();
            else if (input.UseItemPressed && raidShopButton != null && raidShopButton.gameObject.activeInHierarchy) OpenRaidShop();
            else if (input.UseManaPressed) { Game.Audio.PlaySfx("select"); PartyHub.Open(null, this); }
        }

        void ShowRareChances()
        {
            var def = Selected;
            if (def == null || Game.Player == null) return;
            Game.Audio.PlaySfx("select");
            GameEvents.RaiseToast(DungeonRewards.RareChanceText(def, DungeonDatabase.DifficultyFor(def, difficulty), Game.Player.Class));
        }

        void UseResetTicket()
        {
            if (Game.Player != null && Game.Player.UseConsumable(ConsumableDatabase.DungeonResetTicket)) Refresh();
        }

        // ---------- Automated checks ----------

        public bool DevSelect(string dungeonId, DungeonDifficulty d)
        {
            var def = DungeonDatabase.Get(dungeonId);
            if (def == null) return false;
            tab = def.isRaid ? TabRaid : def.always ? TabNormal : TabWeekday;
            var list = Listed;
            for (int i = 0; i < list.Count; i++) if (list[i].id == dungeonId) selected = i;
            difficulty = d;
            Refresh();
            return Selected == def;
        }

        public bool DevEnter() => TryEnter();
        public string DevStatus => status != null ? status.text : "";
        public string DevDayText => dayText != null ? dayText.text : "";
        public bool DevRaidTab => raidTab;

        /// <summary>"개방" / "닫힘" tag of a listed dungeon row (null if not listed on this tab).</summary>
        public string DevRowTag(string dungeonId)
        {
            foreach (var r in rows) if (r.def.id == dungeonId) return r.tag.text;
            return null;
        }
    }
}
