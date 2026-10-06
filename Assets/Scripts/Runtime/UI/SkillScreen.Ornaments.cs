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
            var label = UIFactory.Text(center, "TreeCaption", "성장의 별자리", 16, new Color(.57f, .7f, .88f, .8f), TextAnchor.MiddleLeft, true);
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

    /// <summary>Hand-authored 16px node emblems: opaque stepped pixels, one-pixel outline and a limited palette.</summary>
    public static class SkillNodeArt
    {
        static readonly System.Collections.Generic.Dictionary<string, Sprite> cache = new System.Collections.Generic.Dictionary<string, Sprite>();
        public static Sprite For(PassiveNode node) => Get(KeyFor(node));
        static string KeyFor(PassiveNode n)
        {
            if (n.kind == PassiveKind.Start) return "compass";
            if (n.kind == PassiveKind.Mastery) return "crown";
            if (n.kind == PassiveKind.Keystone)
                return n.keystone == Keystone.Unwavering ? "shield" : n.keystone == Keystone.GlassCannon ? "flame" : n.keystone == Keystone.BloodMagic ? "drop" : "sun";
            var stat = n.stats.Count > 0 ? n.stats[0].stat : PassiveStat.IncDamage;
            switch (stat)
            {
                case PassiveStat.FlatHp: case PassiveStat.IncHp: case PassiveStat.LifeOnKill: return "heart";
                case PassiveStat.FlatMp: return "crystal";
                case PassiveStat.ManaRegen: return "moon";
                case PassiveStat.ManaCost: case PassiveStat.ManaOnKill: return "leech";
                case PassiveStat.Block: return "shield";
                case PassiveStat.Speed: return "boot";
                case PassiveStat.AttackSpeed: return "bolt";
                case PassiveStat.Aoe: case PassiveStat.SkillArea: return "area";
                case PassiveStat.SkillCooldown: return "hourglass";
                case PassiveStat.SkillRepeat: return "crown";
                default: return "sword";
            }
        }
        static Color32 Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
        public static Sprite Get(string key)
        {
            if (cache.TryGetValue(key, out var cached)) return cached;
            string pattern, light, mid, shade;
            switch (key)
            {
                case "sword": pattern = "..........ww/.........whs/........whms/.......whms/......whms/.....whms/....whms/..gwhms/..sgms/...gg/..gs.g/..s/"; light = "ebf8ff"; mid = "a5c7e0"; shade = "4d6c95"; break;
                case "shield": pattern = "....gggg/..gghhhhgg/.ghhmmmmssg/.ghwmmmmssg/.ghwmgmmssg/.ghmgggmssg/..gmgggssg/..gmmgmssg/...gmssg/....gsg/.....g"; light = "abedff"; mid = "4697cc"; shade = "294671"; break;
                case "heart": pattern = "..hhhh..hhhh/.hwwmmhhmmmms/.hwmmmmmmmmss/.hmmmmmmmmsss/..hmmmmmmsss/...hmmmmsss/....hmmsss/.....hsss/......ss"; light = "ffb5aa"; mid = "ef5b76"; shade = "9c315b"; break;
                case "crystal": pattern = ".....w/....whh/...whhmm/..whhhmms/.whhhmmmss/..hhhmmss/...hhmss/....hss/.....s/...g...g/.....g"; light = "b6ffff"; mid = "47c9e6"; shade = "3c669a"; break;
                case "hourglass": pattern = ".gggggggggg/..gwwhhhsg/..gwhmmsg/...ghmsg/....gsg/....ghg/...ghmsg/..gwhmmsg/..ghmmmmssg/.gggggggggg"; light = "fff0b0"; mid = "d7b467"; shade = "94704b"; break;
                case "area": pattern = ".....hh/....hhhh/...hhwwhh/.....hh/..h..hh..h/.hh......hh/hhw......whh/.hh......hh/..h..hh..h/.....hh/...hhwwhh/....hhhh/.....hh"; light = "cfffe0"; mid = "6fcab9"; shade = "376e83"; break;
                case "crown": pattern = ".w....w....w/.h....h....h/.hh..hhh..hh/.hghhhhhgmmh/.hhghhhgmmmh/..hhhhmmmms/..gmgmgmgms/..mmmmmmsss/..ggggggggg"; light = "fff3b4"; mid = "e5b250"; shade = "9d613c"; break;
                case "compass": pattern = ".....w/.....h/....hhh/....hmh/..ggwhhgg/.gmmwhhmms/whhhwwmmmms/.gmhhmmsmg/..gghmsgg/....hss/....hss/.....s"; light = "dcfaff"; mid = "86cbd6"; shade = "527bb0"; break;
                case "flame": pattern = "......h/.....hh/.....hms/...h.hmms/..hmmhmms/..hmmmmmsh/..hmhmmmms/..hwhhmmms/...whhmms/....hmms/.....ss"; light = "ffea9a"; mid = "ff9756"; shade = "c84b60"; break;
                case "drop": pattern = ".....w/.....hh/....whhs/....hhmms/...hhmmms/..hhmmmmss/..hwmmmmss/..hwmmmmss/...hmmmss/....ssss"; light = "ffb7cd"; mid = "ee577b"; shade = "9c355e"; break;
                case "sun": pattern = ".....g/..g..g..g/...g...g/....hhhh/.g.hhwwmm.g/..hhwwmmmss/g.hhmmmmsss.g/..hmmmmsss/.g.mmmsss.g/....ssss/...g...g/..g..g..g/.....g"; light = "fff8be"; mid = "ffca70"; shade = "d27947"; break;
                case "boot": pattern = "....hhhh/....hwmss/....hmmss/....hmmss/....hmmss/....hgmsg/...hmmmms/..hmmmmmms/.hhmmmmmmss/.gmmmmmmmss/.gggggggggg"; light = "c9f5bc"; mid = "72b49e"; shade = "406976"; break;
                case "bolt": pattern = "......whh/.....whhm/....whhms/...whhmss/..whhhhhhh/.....hhms/....hhms/...hhms/..hhs/..hs/.s"; light = "fcf5b5"; mid = "eacd6b"; shade = "b38b4d"; break;
                case "moon": pattern = ".....hhhh/...hhwmm/..hwmm/..hwm/..hwm....g/.hwm....ggg/.hwm.....g/..hmm/..hmmm/...hmmmss/.....ssss"; light = "e2ceff"; mid = "a388e7"; shade = "655294"; break;
                case "leech": pattern = "...h/..hh/..hms.....g/.hhmms...ggg/.hwmmss...g/.hmmmss/..hmmss....g/...sss...gg/.....ggggg/.......g"; light = "bdd9ff"; mid = "6b8ee0"; shade = "494d91"; break;
                default: return Get("compass");
            }
            const int size = 16;
            var ink = new Color32(12, 19, 33, 255);
            var colors = new System.Collections.Generic.Dictionary<char, Color32> { ['h'] = Hex(light), ['m'] = Hex(mid), ['s'] = Hex(shade), ['w'] = Hex("f6fdff"), ['g'] = Hex("eac075") };
            var pixels = new Color32[size * size];
            var rows = pattern.Split('/'); int width = 0;
            foreach (var row in rows) width = Mathf.Max(width, row.Length);
            int ox = (size - width) / 2, oy = (size - rows.Length) / 2;
            for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++)
                if (colors.TryGetValue(rows[y][x], out var color)) pixels[(size - 1 - oy - y) * size + ox + x] = color;
            var outlined = (Color32[])pixels.Clone();
            for (int y = 1; y < size - 1; y++) for (int x = 1; x < size - 1; x++)
            {
                int p = y * size + x;
                if (pixels[p].a != 0) continue;
                if (pixels[p - 1].a != 0 || pixels[p + 1].a != 0 || pixels[p - size].a != 0 || pixels[p + size].a != 0) outlined[p] = ink;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "skill_emblem_" + key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(outlined); texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, size);
            sprite.name = texture.name; cache[key] = sprite; return sprite;
        }
    }
}
