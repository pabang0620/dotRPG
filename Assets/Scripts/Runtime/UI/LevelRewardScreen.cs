using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [LEVEL 13] 레벨 보상 window (side menu): each milestone with its star shards and a 받기 button once an account
    /// character has reached it. Rewards are per account, so a second character does not take them again.
    /// </summary>
    public class LevelRewardScreen : OnlineWindow
    {
        public static LevelRewardScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        const float RowH = 76f;

        Text header;
        readonly List<(Image bg, Text name, Text info, Button claim, Image icon)> rows = new List<(Image, Text, Text, Button, Image)>();

        public static LevelRewardScreen Create(Transform canvas)
        {
            var w = CreateWindow<LevelRewardScreen>(canvas, "LevelRewards", "레벨 보상", "menuicon_achievement");
            Instance = w;
            w.header = Label(w.content, "Header", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1100f, 40f), TextAnchor.MiddleLeft);
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var bg = Panel(w.content, "Tier" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -50f - i * RowH), new Vector2(1000f, RowH - 6f), i % 2 == 0 ? RowA : RowB);
                var icon = UIFactory.Image(bg.transform, "Shard", Game.Art.Get("icon_star_shard"), Color.white);
                icon.preserveAspect = true; icon.raycastTarget = false;
                UIFactory.Place(icon.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(12f, 0f), new Vector2(52f, 52f));
                var name = Label(bg.transform, "Name", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -6f), new Vector2(600f, 32f), TextAnchor.MiddleLeft);
                var info = Label(bg.transform, "Info", "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -38f), new Vector2(600f, 26f), TextAnchor.MiddleLeft);
                var claim = Button(bg.transform, "Claim", "받기", "ui_btn", new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-12f, 0f), new Vector2(150f, 46f), () => w.ClaimAt(idx), 19);
                w.rows.Add((bg, name, info, claim, icon));
            }
            LevelRewardClient.Changed += () => { if (w != null && w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        public override void Show()
        {
            base.Show();
            LevelRewardClient.Refresh();
            Refresh();
        }

        void ClaimAt(int i)
        {
            var tiers = LevelRewardClient.Tiers;
            if (i >= tiers.Count || !tiers[i].claimable) return;
            LevelRewardClient.Claim(tiers[i].level, (ok, msg) =>
            {
                Game.Audio.PlaySfx(ok ? "quest" : "cancel");
                GameEvents.RaiseToast(msg);
            });
        }

        protected override void Refresh()
        {
            var tiers = LevelRewardClient.Tiers;
            header.text = !OnlineSession.Playing ? "<color=#8c96a8>온라인으로 접속하면 받을 수 있습니다.</color>"
                : !LevelRewardClient.Loaded ? "<color=#8c96a8>불러오는 중입니다...</color>"
                : $"계정 최고 레벨 <color=#ffd34a>Lv.{LevelRewardClient.AccountLevel}</color>  ·  <color=#b8c4d8>계정당 한 번씩 받을 수 있습니다</color>";
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var t = i < tiers.Count ? tiers[i] : null;
                r.bg.gameObject.SetActive(t != null);
                if (t == null) continue;
                bool max = i == tiers.Count - 1;
                r.name.text = $"<b>Lv.{t.level} 달성{(max ? " · 만렙" : "")}</b>   별조각 <color=#ffd34a>{t.stars:N0}</color>개";
                r.info.text = t.claimed ? "<color=#8fe28f>받음</color>" : t.claimable ? "<color=#ffe066>받을 수 있습니다</color>" : $"<color=#8c96a8>계정의 캐릭터 하나가 Lv.{t.level}에 도달하면 받을 수 있습니다</color>";
                r.icon.color = t.claimed ? new Color(1f, 1f, 1f, .4f) : Color.white;
                r.claim.gameObject.SetActive(!t.claimed);
                r.claim.interactable = t.claimable && !LevelRewardClient.Busy;
                TextOf(r.claim).text = t.claimable ? "받기" : "미달성";
            }
        }
    }
}
