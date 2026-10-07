using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UX] The career node card: a frame in the state's colour (gold = learned, teal = can be learned now, grey =
    /// not yet), the skill picture in a glowing ring, three rank pips, the key badge and a link to the next tier.
    /// </summary>
    public partial class SkillScreen
    {
        static readonly Color32 NodeMax = new Color32(255, 215, 106, 255), NodeLearned = new Color32(232, 184, 74, 255),
            NodeOpen = new Color32(95, 211, 200, 255), NodeLocked = new Color32(60, 70, 88, 255), NodeFace = new Color32(22, 30, 48, 255);

        Color32 NodeAccent(CareerSkill s, Progression p)
        {
            int rank = p.Career == browsing ? p.Rank(s.id) : 0;
            if (rank >= 3) return NodeMax;
            if (rank > 0) return NodeLearned;
            return p.Career == browsing && p.NodeLock(s) == "" ? NodeOpen : NodeLocked;
        }

        Image NodeCard(CareerSkill s, Progression p, float x, float y, bool selected)
        {
            const float W = 183f, H = 116f;
            var accent = NodeAccent(s, p);
            int rank = p.Career == browsing ? p.Rank(s.id) : 0;
            // Frame: a slightly larger plate behind the face, bright when selected.
            var frame = Panel(careerRoot, "NodeFrame_" + s.id, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x - 2, -(y - 2)), new Vector2(W + 4, H + 4),
                selected ? (Color)new Color32(255, 250, 230, 255) : (Color)accent);
            frame.raycastTarget = false;
            var bg = Panel(careerRoot, "Node_" + s.id, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(W, H), NodeFace);
            var strip = Panel(bg.transform, "Accent", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(W, 4), accent);
            strip.raycastTarget = false;
            // Picture in a ring, with a halo once learned.
            if (rank > 0)
            {
                var halo = UIFactory.Image(bg.transform, "Halo", ArcaneUiArt.Get("halo"), new Color(accent.r / 255f, accent.g / 255f, accent.b / 255f, .55f));
                halo.raycastTarget = false;
                UIFactory.Place(halo.rectTransform, new Vector2(0, 1), new Vector2(.5f, .5f), new Vector2(42, -46), Vector2.one * 92);
            }
            var ring = UIFactory.Image(bg.transform, "Ring", ArcaneUiArt.Get("ring"), accent);
            ring.raycastTarget = false;
            UIFactory.Place(ring.rectTransform, new Vector2(0, 1), new Vector2(.5f, .5f), new Vector2(42, -46), Vector2.one * 70);
            var icon = UIFactory.Image(bg.transform, "Icon", CareerMoves.Icon(s.Icon), rank > 0 || p.Career != browsing ? Color.white : new Color(.62f, .64f, .7f, 1f));
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            UIFactory.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(.5f, .5f), new Vector2(42, -46), Vector2.one * 60);
            // Name, kind and requirement.
            string kind = s.kind == CareerSkillKind.Passive ? "<color=#b8a0ff>패시브</color>" : "<color=#9fd0ff>액티브</color>";
            CareerText(bg.transform, "Name", $"<b>{s.name}</b>\n<size=13>{kind} · Lv.{s.level}</size>", 16, 82, 12, 98, 50);
            // Rank pips.
            for (int i = 0; i < 3; i++)
            {
                var pip = UIFactory.Image(bg.transform, "Pip" + i, Game.Art.Get("ui_circle"), i < rank ? (Color)accent : new Color(1, 1, 1, .18f));
                pip.raycastTarget = false;
                UIFactory.Place(pip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(84 + i * 17, -66), Vector2.one * 12);
            }
            CareerText(bg.transform, "State", NodeState(s, p), 14, 10, 88, 168, 24);
            // Hover / press feedback and gamepad reach.
            bg.raycastTarget = true;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor = new Color(.92f, .92f, .92f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(.75f, .75f, .75f, 1f);
            cb.colorMultiplier = 1.25f;
            btn.colors = cb;
            btn.onClick.AddListener(() => { selectedSkill = s.id; Refresh(); });
            return bg;
        }

        /// <summary>The short link from a card to the next tier of its branch (gold once the next one is learned).</summary>
        void NodeLink(Progression p, CareerSkill next, float x, float y)
        {
            bool on = p.Career == browsing && p.Rank(next.id) > 0;
            var line = Panel(careerRoot, "Link_" + next.id, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(x, -y), new Vector2(12, on ? 5 : 3),
                on ? (Color)NodeLearned : new Color(1, 1, 1, .2f));
            line.raycastTarget = false;
        }
    }
}
