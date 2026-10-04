using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class SkillScreen
    {
        static Image Ornament(Transform parent, string name, string shape, Vector2 pos, Vector2 size, Color color)
        {
            var image = UIFactory.Image(parent, name, ArcaneUiArt.Get(shape), color);
            UIFactory.Place(image.rectTransform, Vector2.one * .5f, Vector2.one * .5f, pos, size);
            return image;
        }
        void BuildConstellation(RectTransform center)
        {
            Ornament(center, "AstralSeal", "sigil", Vector2.zero, Vector2.one * 420, new Color(.3f, .56f, .82f, .12f));
            Ornament(center, "InnerSeal", "ring", Vector2.zero, Vector2.one * 190, new Color(.3f, .56f, .82f, .12f));
            Ornament(center, "CoreLight", "halo", Vector2.zero, Vector2.one * 300, new Color(.1f, .45f, .8f, .25f));
            for (int i = 0; i < 48; i++)
            {
                float x = (i * 137 % 960) - 480, y = (i * 83 % 480) - 240;
                Ornament(center, "ConstellationStar", "star", new Vector2(x, y), Vector2.one * (i % 3 == 0 ? 7 : 3), new Color(.6f, .75f, 1f, .18f));
            }
            var label = UIFactory.Text(center, "TreeCaption", "성장의 별자리", 15, new Color(.57f, .7f, .88f, .8f), TextAnchor.MiddleLeft, true);
            UIFactory.Place(label.rectTransform, Vector2.one * .5f, new Vector2(0, .5f), new Vector2(-475, 244), new Vector2(180, 24));
        }
        void AnimateTree()
        {
            if (tab != TabId.Tree) return;
            float t = Time.unscaledTime;
            foreach (var v in nodeViews)
            {
                bool selected = mouse ? hovered == v : keyboardUsed && selectedNode == v;
                float pulse = .8f + .2f * Mathf.Sin(t * 2f + v.node.pos.x * .02f);
                float opacity = selected ? .8f : v.owned ? .46f : v.available ? .28f * pulse : 0;
                Color c = v.owned ? new Color(1f, .7f, .28f) : v.accent;
                v.halo.color = new Color(c.r, c.g, c.b, opacity);
                v.rect.localScale = Vector3.Lerp(v.rect.localScale, Vector3.one * (selected ? 1.15f : 1f), Mathf.Min(1, Time.unscaledDeltaTime * 16));
                if (selected) v.ornament.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 18);
                else v.ornament.rectTransform.localRotation = Quaternion.identity;
            }
        }
    }
}
