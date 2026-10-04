using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// One always-visible line under the gold row that says what the online party is doing, in plain words, and
    /// opens the party window when clicked: no party (invite a friend), members and ready count, applications
    /// waiting, the run gathering or under way, hunting together on this map.
    /// </summary>
    public class PartyStatusView : MonoBehaviour
    {
        public const float Left = 18f, Top = -152f, Width = 372f, Height = 30f;

        Image plate;
        Text text;
        float nextRefresh;

        public static PartyStatusView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "PartyStatus"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Left, Top), new Vector2(Width, Height));
            var v = root.gameObject.AddComponent<PartyStatusView>();
            v.plate = UIFactory.Image(root, "Plate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            v.plate.preserveAspect = false;
            v.plate.raycastTarget = true;
            UIFactory.Stretch(v.plate.rectTransform);
            var button = v.plate.gameObject.AddComponent<Button>();
            button.targetGraphic = v.plate;
            button.onClick.AddListener(Open);
            UiButton.Attach(button, false);
            v.text = UIFactory.Text(root, "Text", "", 16, UIColors.Cream, TextAnchor.MiddleLeft, true);
            v.text.raycastTarget = false;
            UIFactory.Stretch(v.text.rectTransform, 10f, 0f, 8f, 0f);
            root.gameObject.SetActive(false);
            return v;
        }

        static void Open()
        {
            if (!Game.IsPlaying || PartyLobbyScreen.Instance == null) return;
            Game.Audio.PlaySfx("select");
            Game.Flow.OpenWindow(PartyLobbyScreen.Instance);
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            bool show = OnlineSession.Playing && (Game.Dungeon == null || !Game.Dungeon.InRun || PartyRunSession.Active);
            if (plate.gameObject.activeSelf != show) { plate.gameObject.SetActive(show); text.gameObject.SetActive(show); }
            if (!show) return;
            text.text = Describe();
        }

        static string Describe()
        {
            var c = PartyClient.Instance;
            if (c == null || !c.InParty)
                return "<color=#8c96a8>파티 없음</color>  ·  눌러서 친구 초대";
            var d = DungeonDatabase.Get(c.DungeonId);
            string target = d == null ? "" : $"{d.name} {PartyFinderRules.ModeName(c.DungeonId, c.Difficulty)}";
            string role = c.IsLeader ? "<color=#ffd34a>방장</color>" : "파티원";
            var run = PartyRunSession.Instance;
            if (run != null)
            {
                string state = run.State == "gathering" ? "<color=#ffe066>출발 준비 중</color> · 방장에게 연결" : "<color=#8fe28f>던전 진행 중</color>";
                return $"{role} · {c.Members.Count}명 · {state}";
            }
            string line = $"{role} · 파티 {c.Members.Count}/{c.MaxMembers}명 · {target}";
            if (FieldSession.Active) line += " · <color=#8fe28f>함께 사냥 중</color>";
            else if (c.IsLeader) line += " · 요일던전 입장 시 함께 출발";
            else line += " · 방장이 입장하면 함께 출발";
            if (c.IsLeader && c.Applications.Count > 0) line += $" · <color=#ff9f7a>신청 {c.Applications.Count}건</color>";
            return line;
        }
    }
}
