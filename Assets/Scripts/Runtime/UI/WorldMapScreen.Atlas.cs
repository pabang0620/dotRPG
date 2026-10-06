using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UI] "전체 지도": the whole world as one painted route map (Art/worldmap_atlas) with a point per region, the
    /// roads between them and the player's region marked. Clicking a point shows that region in the normal map.
    /// Without the picture the button stays hidden.
    /// </summary>
    public partial class WorldMapScreen
    {
        Button atlasButton;
        RectTransform atlasPanel;
        readonly List<(string id, Image dot, Text label)> atlasDots = new List<(string, Image, Text)>();
        bool atlasBuilt;

        /// <summary>Towns get a name label and a bigger point.</summary>
        static readonly HashSet<string> Hubs = new HashSet<string> { "village", "canyon", "winter", MapRegistry.Sanctum };

        /// <summary>
        /// Where every region sits on worldmap_atlas.png (0..1 from the left / from the top), read off the picture: the
        /// village by the river, the skull forest's two lanes north of it, the red canyon and its mine to the east, the
        /// snow village and frozen lake in the north-east, the drowned sanctum in the north-west lake.
        /// </summary>
        static Dictionary<string, Vector2> AtlasPositions() => new Dictionary<string, Vector2>
        {
            ["village"] = new Vector2(.49f, .75f),
            ["forest"] = new Vector2(.46f, .64f),
            ["forest_ruins"] = new Vector2(.37f, .40f),
            ["forest_depths"] = new Vector2(.43f, .41f),
            ["forest_crossing"] = new Vector2(.39f, .26f),
            ["canyon"] = new Vector2(.67f, .58f),
            ["canyon_pass"] = new Vector2(.73f, .46f),
            ["canyon_mine"] = new Vector2(.78f, .36f),
            ["canyon_ridge"] = new Vector2(.88f, .46f),
            ["canyon_gate"] = new Vector2(.64f, .30f),
            ["winter"] = new Vector2(.77f, .14f),
            ["winter_edge"] = new Vector2(.67f, .21f),
            ["winter_lake"] = new Vector2(.86f, .26f),
            ["winter_peak"] = new Vector2(.89f, .07f),
            ["winter_reach"] = new Vector2(.46f, .08f),
            [MapRegistry.Sanctum] = new Vector2(.16f, .13f),
            ["sanctum_hall"] = new Vector2(.20f, .21f),
            ["sanctum_archive"] = new Vector2(.11f, .27f),
            ["sanctum_roots"] = new Vector2(.28f, .27f),
            ["sanctum_court"] = new Vector2(.20f, .33f),
        };

        void BuildAtlasButton(Transform frame)
        {
            atlasButton = Button(frame, "AtlasToggle", "전체 지도", "ui_btn", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-174, -10), new Vector2(140, 34), ToggleAtlas, 17);
            atlasButton.gameObject.SetActive(Game.Art.Optional("worldmap_atlas") != null);
        }

        void ToggleAtlas()
        {
            if (!atlasBuilt) BuildAtlas();
            if (atlasPanel == null) return;
            bool show = !atlasPanel.gameObject.activeSelf;
            atlasPanel.gameObject.SetActive(show);
            Game.Audio.PlaySfx("select");
            if (show) RefreshAtlas();
        }

        void BuildAtlas()
        {
            atlasBuilt = true;
            var art = Game.Art.Optional("worldmap_atlas");
            if (art == null) return;
            var frame = map.rectTransform.parent.parent; // viewport -> frame
            var img = UIFactory.Image(frame, "Atlas", art, Color.white);
            img.preserveAspect = false; // the box below keeps the picture's 16:10 so the points line up
            img.raycastTarget = true;
            atlasPanel = UIFactory.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(47, -54), new Vector2(666, 416));
            var pos = AtlasPositions();
            Vector2 size = atlasPanel.sizeDelta;
            Vector2 At(Vector2 p) => new Vector2(p.x * size.x, -p.y * size.y);
            // Roads first (under the points).
            var drawn = new HashSet<string>();
            foreach (var kv in pos)
                foreach (var n in WorldRoutes.Neighbors(kv.Key))
                {
                    if (!pos.TryGetValue(n, out var q)) continue;
                    string key = string.CompareOrdinal(kv.Key, n) < 0 ? kv.Key + "|" + n : n + "|" + kv.Key;
                    if (!drawn.Add(key)) continue;
                    Road(At(kv.Value), At(q));
                }
            foreach (var kv in pos)
            {
                var info = MapRegistry.Get(kv.Key);
                if (info == null) continue;
                bool hub = Hubs.Contains(kv.Key);
                string id = kv.Key;
                var dot = UIFactory.Image(atlasPanel, "Dot_" + id, Game.Art.Get("ui_circle"), Color.white);
                dot.raycastTarget = true;
                UIFactory.Place(dot.rectTransform, new Vector2(0, 1), new Vector2(.5f, .5f), At(kv.Value), Vector2.one * (hub ? 22f : 14f));
                var b = dot.gameObject.AddComponent<Button>();
                b.targetGraphic = dot;
                b.onClick.AddListener(() => { atlasPanel.gameObject.SetActive(false); SelectMap(id); });
                Text label = null;
                if (hub)
                {
                    label = UIFactory.Text(atlasPanel, "Name_" + id, id == MapRegistry.Village ? "작은 마을" : info.displayName, 16, Color.white, TextAnchor.UpperCenter, true);
                    label.raycastTarget = false;
                    UIFactory.Place(label.rectTransform, new Vector2(0, 1), new Vector2(.5f, 1f), At(kv.Value) + new Vector2(0, -14), new Vector2(170, 24));
                }
                atlasDots.Add((id, dot, label));
            }
            atlasPanel.gameObject.SetActive(false);
        }

        void Road(Vector2 a, Vector2 b)
        {
            var line = UIFactory.Image(atlasPanel, "Road", Game.Art.Get("ui_white"), new Color(1f, .93f, .75f, .75f));
            line.raycastTarget = false;
            var rt = line.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, .5f);
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2((b - a).magnitude, 3f);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        void RefreshAtlas()
        {
            if (atlasPanel == null || !atlasPanel.gameObject.activeSelf) return;
            string here = Game.World != null ? Game.World.MapId : null;
            var hereInfo = here != null ? MapRegistry.Get(here) : null;
            if (hereInfo != null && hereInfo.exteriorMap != null) here = hereInfo.exteriorMap;
            int lv = Game.Session.Progression.Level;
            foreach (var (id, dot, label) in atlasDots)
            {
                var zone = HuntingGrounds.Get(id);
                bool mine = id == here;
                // Here: blue. Hunting grounds for my level: green, too high: red, below: grey; towns: gold.
                dot.color = mine ? UiTheme.AccentBlue
                    : zone == null ? UiTheme.Accent
                    : lv < zone.minLevel ? UiTheme.Bad
                    : lv > zone.maxLevel ? UiTheme.TextMuted : UiTheme.Good;
                dot.rectTransform.localScale = Vector3.one * (mine ? 1.35f : 1f);
            }
        }
    }
}
