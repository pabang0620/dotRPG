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

        public static TipView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Tip"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(472f, 110f)); // [UI] 404..876: clear of the currency bar (x <= 400) and the field-boss plate left of the minimap (x >= 878)
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
            box.sizeDelta = new Vector2(box.sizeDelta.x, 56f + body.preferredHeight);
            box.gameObject.SetActive(true);
            hideAt = Time.unscaledTime + 9f;
            Game.Audio.PlaySfx("quest", 0.5f);
        }

        void Update()
        {
            if (box.gameObject.activeSelf && Time.unscaledTime >= hideAt) box.gameObject.SetActive(false);
        }
    }
}
