using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [E5] First-time tips: a framed box under the top bar the first time the player reaches a hunting ground,
    /// a dungeon, a raid, a new town or the blacksmith. Each tip shows once per save (journal flag "tip_*").
    /// </summary>
    public class TipView : MonoBehaviour
    {
        RectTransform box;
        Text title, body;
        float hideAt;
        bool shown, inColumn, laidOut, laidOutNarrow;
        float laidOutWidth = -1f;

        // [UI] Wide canvases (UI size 1.0 and smaller, W >= 1240): centred 404..876 at 1.0, clear of the currency bar (x <= 382)
        // and the field-boss plate left of the minimap (x >= 878). Narrower (1.15 / 1.3) the centre is taken by the status
        // and party blocks, so the tip uses the quest column under the minimap (W-380..W-20) and the tracker hides meanwhile.
        // While a boss bar is bound on a canvas under 1160 (its right end reaches W-380) the column narrows to the auto
        // buttons' width (W-292..W-20), like the quest tracker.
        const float WideWidth = 472f, WideTop = 110f, ColumnWidth = 360f, ColumnNarrowWidth = 272f, ColumnRight = 20f, ColumnBelow = 1240f, BossClearBelow = 1160f;

        /// <summary>[UI] A tip is showing in the quest tracker's column (HudView hides the tracker meanwhile).</summary>
        public static bool InQuestColumn { get; private set; }

        public static TipView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Tip"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -WideTop), new Vector2(WideWidth, 110f));
            var v = root.gameObject.AddComponent<TipView>();
            v.box = root;
            var bg = UIFactory.Image(root, "Bg", Game.Art.Get("ui_tooltip"), Color.white);
            bg.preserveAspect = false;
            UIFactory.Stretch(bg.rectTransform);
            v.title = UIFactory.Text(root, "Title", "", 20, UIColors.Highlight, TextAnchor.UpperLeft, true);
            UIFactory.Stretch(v.title.rectTransform, 18f, 0f, 18f, 12f);
            v.body = UIFactory.Text(root, "Body", "", 16, UIColors.Cream, TextAnchor.UpperLeft, true);
            v.body.lineSpacing = 1.15f;
            UIFactory.Stretch(v.body.rectTransform, 18f, 10f, 18f, 42f);
            root.gameObject.SetActive(false);
            GameEvents.MapEntered += v.OnMap;
            GameEvents.QuestCompleted += v.OnQuest;
            return v;
        }

        void OnDestroy()
        {
            InQuestColumn = false;
            GameEvents.MapEntered -= OnMap;
            GameEvents.QuestCompleted -= OnQuest;
        }

        static string K(GameAction a) => Game.Input.GetBindingLabel(a);

        void OnMap(string mapId)
        {
            var map = MapRegistry.Get(mapId);
            if (map == null) return;
            if (mapId == MapRegistry.Forest)
                Show("forest", "사냥터", $"해골이 몸을 떨며 <color=#ffd84a>!</color>가 뜨면 곧 공격합니다. [{K(GameAction.Mobility)}] 이동기로 피한 뒤 반격하세요.\n위험하면 [{K(GameAction.UseItem)}] 물약, [{K(GameAction.TownScroll)}] 귀환 주문서.");
            else if (map.instanced && Game.Dungeon != null && Game.Dungeon.InRun)
            {
                if (Game.Dungeon.Run.Dungeon.isRaid)
                    Show("raid", "레이드", "부활은 파티 공용 3회. 바닥의 붉은 장판(예고)을 피하고, 보스의 기믹(토템 등)은 동료와 나눠 처리하세요.");
                else
                    Show("dungeon", "던전", "방의 적을 모두 쓰러뜨리면 문이 열립니다. 빨리, 덜 맞고, 많이 쓰러뜨릴수록 랭크가 오르고 보상 카드가 좋아집니다.");
            }
            else if (mapId == MapRegistry.Canyon)
                Show("canyon", "바위 협곡 마을", "협곡의 대장간·상점도 마을과 같습니다. 북쪽 문 너머는 눈꽃 숲 마을입니다.");
        }

        void OnQuest(string questId)
        {
            if (questId == "c1_stronger")
                Show("raid_schedule", "레이드 일정", "중간 레이드는 수·토·일에 열리고 하루 1회 보상과 <color=#ffd84a>봉인 열쇠 조각</color>을 줍니다.\n최종 레이드는 일요일에 열립니다. 조각 60개가 있으면 주 1회 보상을 받고, 모자라도 연습으로 들어가 이야기를 이어갈 수 있습니다.");
            else if (questId == "c1_rise")
                Show("menu", "메뉴", $"왼쪽 위 메뉴 버튼(또는 [{K(GameAction.Pause)}])에서 가방·스킬·지도·퀘스트를 엽니다.\n레벨이 오르면 스킬 창에서 패시브 포인트를 쓰세요.");
        }

        public void Show(string id, string heading, string text)
        {
            var j = Game.Session?.Journal;
            if (j == null || !QuestManager.StoryEnabled || !j.Flags.Add("tip_" + id)) return;
            title.text = heading;
            body.text = text;
            shown = true;
            box.gameObject.SetActive(true);
            laidOut = false;
            Layout();
            hideAt = Time.unscaledTime + 9f;
            Game.Audio.PlaySfx("quest", 0.5f);
        }

        /// <summary>Centre or quest column by the canvas width; the height follows the wrapped body.</summary>
        void Layout()
        {
            float w = ((RectTransform)box.parent).rect.width;
            var bossBar = BossHpBarView.Instance;
            bool narrow = bossBar != null && bossBar.DevVisible && w < BossClearBelow;
            if (laidOut && Mathf.Approximately(w, laidOutWidth) && narrow == laidOutNarrow) return;
            laidOut = true;
            laidOutWidth = w;
            laidOutNarrow = narrow;
            inColumn = w > 0f && w < ColumnBelow;
            if (inColumn)
            {
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 1f);
                box.anchoredPosition = new Vector2(-ColumnRight, -HudView.QuestTop);
                box.sizeDelta = new Vector2(narrow ? ColumnNarrowWidth : ColumnWidth, box.sizeDelta.y);
            }
            else
            {
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
                box.anchoredPosition = new Vector2(0f, -WideTop);
                box.sizeDelta = new Vector2(WideWidth, box.sizeDelta.y);
            }
            float height = 56f + body.preferredHeight;
            if (inColumn)
            {
                // [UI] In the quest column the box stops above the quick item bar; a longer body is cut (rare, UI size 1.3).
                float room = ((RectTransform)box.parent).rect.height - HudView.QuestTop - 136f;
                body.verticalOverflow = height > room ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
                height = Mathf.Min(height, Mathf.Max(80f, room));
            }
            box.sizeDelta = new Vector2(box.sizeDelta.x, height);
        }

        void Update()
        {
            if (shown) Layout(); // UI size or boss bar changed while showing
            // [UI] In the quest column (UI size 1.15 / 1.3) the tip would sit on the bottom-centre boss bar and its warning
            // line: it waits out the boss fight (its 9 s start again where they stopped).
            var bossBar = BossHpBarView.Instance;
            bool hold = shown && inColumn && bossBar != null && bossBar.Engaged;
            if (hold) hideAt += Time.unscaledDeltaTime;
            if (shown && Time.unscaledTime >= hideAt) shown = false;
            bool visible = shown && !hold;
            if (box.gameObject.activeSelf != visible) box.gameObject.SetActive(visible);
            InQuestColumn = visible && inColumn;
        }
    }
}
