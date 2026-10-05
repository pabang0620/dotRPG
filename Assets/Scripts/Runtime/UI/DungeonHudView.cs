using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// HUD parts shown only inside a dungeon run: the top-centre clock with "방 2/4" and the revives left,
    /// the room-grid map in place of the round minimap (current room highlighted, cleared rooms dimmed, the
    /// boss room marked with a skull), the CLEAR banner and the coin countdown ("부활하시겠습니까?").
    /// The band just below the clock (y −70 … −150) is left free for the boss HP bar.
    /// </summary>
    public class DungeonHudView : MonoBehaviour
    {
        int shownSecond = -1, shownRoom = -1;

        // ---------- Layout ----------
        public const float ClockWidth = 420f, ClockHeight = 50f, ClockTop = 12f;
        /// <summary>Lowest point of the clock panel (the boss HP bar may start below this).</summary>
        public const float ReservedBottom = ClockTop + ClockHeight + 8f;
        const float Cell = 30f, CellGap = 10f, MapWidth = 176f;

        RectTransform clock, roomMap, banner, revive;
        Text clockText, roomText, bannerText, reviveTitle, reviveBody, mapTitle;
        Image bannerBg;
        readonly List<Image> cells = new List<Image>();
        readonly List<Image> links = new List<Image>();
        Image skull;
        GameObject minimap;
        bool wasInRun;
        float bannerAt = -100f;
        int builtFor = -1;
        DungeonRun builtRun;

        public static DungeonHudView Create(Transform hudRoot)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(hudRoot, "DungeonHud"));
            var view = root.gameObject.AddComponent<DungeonHudView>();
            view.Build(root);
            return view;
        }

        void Build(RectTransform root)
        {
            // Clock (top centre).
            clock = UIFactory.Place(UIFactory.Rect(root, "Clock"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -ClockTop), new Vector2(ClockWidth, ClockHeight));
            var cbg = UIFactory.Panel(clock, "Bg", true);
            UIFactory.Stretch(cbg.rectTransform);
            clockText = UIFactory.Text(clock, "Time", "", 28, UIColors.Highlight, TextAnchor.MiddleLeft, true);
            UIFactory.Stretch(clockText.rectTransform, 22f, 0f, 0f, 0f);
            roomText = UIFactory.Text(clock, "Room", "", 20, UIColors.Cream, TextAnchor.MiddleRight, true);
            UIFactory.Stretch(roomText.rectTransform, 140f, 0f, 22f, 0f);

            // Room map (where the round minimap sits).
            roomMap = UIFactory.Place(UIFactory.Rect(root, "RoomMap"), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -16f), new Vector2(MapWidth, 96f));
            var mbg = UIFactory.Panel(roomMap, "Bg", true);
            UIFactory.Stretch(mbg.rectTransform);
            mapTitle = UIFactory.Text(roomMap, "Title", "", UiTheme.FontCaption, UIColors.Cream, TextAnchor.UpperCenter, true);
            UIFactory.Place(mapTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(MapWidth - 12f, 22f));

            // CLEAR banner (screen centre, above the characters).
            banner = UIFactory.Place(UIFactory.Rect(root, "Banner"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(UiTheme.HudBannerWidth, 130f)); // [UI] centre column
            bannerBg = UIFactory.Image(banner, "Bg", Game.Art.Get("ui_white"), new Color(0f, 0f, 0f, 0.45f));
            bannerBg.preserveAspect = false;
            UIFactory.Stretch(bannerBg.rectTransform);
            bannerText = UIFactory.Text(banner, "Text", "클리어!", 84, new Color32(255, 222, 90, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(bannerText.rectTransform);
            var outline = bannerText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.15f, 0.02f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);
            banner.gameObject.SetActive(false);

            // Coin countdown.
            revive = UIFactory.Place(UIFactory.Rect(root, "Revive"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(520f, 210f));
            var rbg = UIFactory.Panel(revive, "Bg", true);
            UIFactory.Stretch(rbg.rectTransform);
            reviveTitle = UIFactory.Text(revive, "Title", "부활하시겠습니까?", 30, Color.white, TextAnchor.UpperCenter, true);
            UIFactory.Place(reviveTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(480f, 40f));
            reviveBody = UIFactory.Text(revive, "Body", "", 20, UIColors.Cream, TextAnchor.UpperCenter, true);
            UIFactory.Place(reviveBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(480f, 56f));
            MakeButton(revive, "부활", "ui_btn", new Vector2(-110f, 22f), () => Game.Dungeon?.AcceptRevive());
            MakeButton(revive, "포기", "ui_btngray", new Vector2(110f, 22f), () => Game.Dungeon?.GiveUp());
            revive.gameObject.SetActive(false);

            // [DUNGEON] 마을로: leave the dungeon at any time (under the room map, shown with it).
            var leave = UIFactory.Image(roomMap, "Btn_Village", Game.Art.Get("ui_btngray"), Color.white);
            leave.preserveAspect = false;
            leave.raycastTarget = true;
            UIFactory.Place(leave.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -8f), new Vector2(140f, 44f));
            var lb = leave.gameObject.AddComponent<Button>();
            lb.targetGraphic = leave;
            lb.onClick.AddListener(AskLeave);
            UiButton.Attach(lb);
            var lt = UIFactory.Text(leave.transform, "Text", "마을로", 20, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(lt.rectTransform);

            clock.gameObject.SetActive(false);
            roomMap.gameObject.SetActive(false);
        }

        static void AskLeave()
        {
            var d = Game.Dungeon;
            if (d == null || !d.InRun || d.IsBusy) return;
            if (d.RunOver) { d.LeaveToVillage(); return; }
            Game.UI.Confirm("마을로 돌아갈까요?\n진행 중인 이번 판은 실패로 끝납니다.", () => d.LeaveToVillage(), true);
        }

        static void MakeButton(RectTransform parent, string label, string sprite, Vector2 pos, System.Action onClick)
        {
            var img = UIFactory.Image(parent, "Btn_" + label, Game.Art.Get(sprite), Color.white);
            img.preserveAspect = false;
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos, new Vector2(180f, 54f));
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            UiButton.Attach(b); // [UI]
            var t = UIFactory.Text(img.transform, "Text", label, UiTheme.ButtonFont, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(t.rectTransform);
        }

        void OnEnable()
        {
            if (Game.Dungeon != null) Game.Dungeon.ClearBanner += ShowClear;
        }

        void OnDisable()
        {
            if (Game.Dungeon != null) Game.Dungeon.ClearBanner -= ShowClear;
        }

        void ShowClear() => bannerAt = Time.unscaledTime;

        void Update()
        {
            var director = Game.Dungeon;
            var run = director != null ? director.Run : null;
            bool inRun = run != null;
            if (inRun != wasInRun)
            {
                wasInRun = inRun;
                clock.gameObject.SetActive(inRun);
                roomMap.gameObject.SetActive(inRun);
                if (minimap == null)
                {
                    var mm = transform.parent != null ? transform.parent.GetComponentInChildren<MinimapView>(true) : null;
                    if (mm != null) minimap = mm.gameObject;
                }
                if (minimap != null) minimap.SetActive(!inRun);
                // The village quest tracker would cover the CLEAR banner; dungeons have their own goal.
                var quest = transform.parent != null ? transform.parent.Find("Quest") : null;
                if (quest != null) quest.gameObject.SetActive(!inRun);
                if (!inRun) { revive.gameObject.SetActive(false); banner.gameObject.SetActive(false); }
            }
            if (!inRun) return;

            // [P5] Re-format only when the second, room or revive count changes.
            int sec = Mathf.FloorToInt(run.Elapsed);
            if (sec != shownSecond) { shownSecond = sec; clockText.text = $"<size=18><color=#b8c4d8>시간</color></size>  {DungeonRun.Clock(run.Elapsed)}"; }
            int roomKey = (run.RoomIndex * 100 + run.RoomCount) * 100 + run.RevivesLeft;
            if (roomKey != shownRoom)
            {
                shownRoom = roomKey;
                string revivesTag = run.RevivesLeft > 0 ? $"부활 {run.RevivesLeft}" : "<color=#ff8a7a>부활 0</color>";
                roomText.text = $"방 <color=#ffe066>{run.RoomIndex + 1}</color>/{run.RoomCount}    {revivesTag}";
            }
            RefreshRoomMap(run);

            // CLEAR: pops in big, settles, stays until the result opens.
            float age = Time.unscaledTime - bannerAt;
            bool showBanner = age >= 0f && age < DungeonDirector.SlowMotionSeconds + DungeonDirector.ClearHoldSeconds + 0.5f && run.State != DungeonRunState.Failed;
            if (banner.gameObject.activeSelf != showBanner) banner.gameObject.SetActive(showBanner);
            if (showBanner)
            {
                float s = Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(age / 0.25f));
                bannerText.rectTransform.localScale = new Vector3(s, s, 1f);
                bannerBg.color = new Color(0f, 0f, 0f, 0.45f * Mathf.Clamp01(age / 0.2f));
            }

            bool reviveShown = director.ReviveOpen;
            if (revive.gameObject.activeSelf != reviveShown) revive.gameObject.SetActive(reviveShown);
            if (reviveShown)
            {
                int left = Mathf.CeilToInt(director.ReviveRemaining);
                reviveTitle.text = $"부활하시겠습니까?  <color=#ffe066>{left}</color>";
                var input = Game.Input;
                reviveBody.text = $"남은 부활 <color=#ffe066>{run.RevivesLeft}</color>회 · 제자리에서 완전 회복 + 3초 무적\n" +
                                  $"<color=#b8c4d8>[Enter / {input.GetBindingLabel(GameAction.Attack)}] 부활    [ESC] 포기</color>";
            }
        }

        void RefreshRoomMap(DungeonRun run)
        {
            int n = run.RoomCount;
            if (builtRun != run || builtFor != n)
            {
                builtRun = run;
                builtFor = n;
                foreach (var c in cells) Destroy(c.gameObject);
                foreach (var l in links) Destroy(l.gameObject);
                cells.Clear();
                links.Clear();
                if (skull != null) Destroy(skull.gameObject);
                float total = n * Cell + (n - 1) * CellGap;
                float x0 = -total * 0.5f + Cell * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                    {
                        var link = UIFactory.Image(roomMap, "Link" + i, Game.Art.Get("ui_white"), new Color(1f, 1f, 1f, 0.35f));
                        link.preserveAspect = false;
                        UIFactory.Place(link.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x0 + i * (Cell + CellGap) - (Cell + CellGap) * 0.5f, 34f), new Vector2(CellGap, 4f));
                        links.Add(link);
                    }
                    var cell = UIFactory.Image(roomMap, "Room" + i, Game.Art.Get("ui_white"), Color.white);
                    cell.preserveAspect = false;
                    UIFactory.Place(cell.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x0 + i * (Cell + CellGap), 34f), new Vector2(Cell, Cell));
                    cells.Add(cell);
                }
                int boss = Mathf.Clamp(run.Dungeon.bossRoom, 0, n - 1);
                skull = UIFactory.Image(cells[boss].transform, "Skull", Game.Art.Get("dgn_skull"), Color.white);
                UIFactory.Place(skull.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
                mapTitle.text = $"{run.Dungeon.name} · {run.Numbers.name}";
            }
            for (int i = 0; i < cells.Count; i++)
            {
                bool current = i == run.RoomIndex;
                bool cleared = run.ClearedRooms.Contains(i);
                cells[i].color = current ? (Color)new Color32(255, 211, 74, 255)
                    : cleared ? new Color32(90, 100, 120, 200)
                    : new Color32(52, 64, 86, 255);
                cells[i].rectTransform.localScale = current ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
            }
        }

        // ---------- Automated checks ----------
        public bool DevClockVisible => clock != null && clock.gameObject.activeInHierarchy;
        public bool DevRoomMapVisible => roomMap != null && roomMap.gameObject.activeInHierarchy;
        public bool DevReviveVisible => revive != null && revive.gameObject.activeInHierarchy;
        public bool DevMinimapHidden => minimap != null && !minimap.activeSelf;
        public string DevRoomText => roomText != null ? roomText.text : "";
    }
}
