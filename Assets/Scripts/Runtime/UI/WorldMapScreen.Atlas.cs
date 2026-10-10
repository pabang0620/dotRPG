using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Read-only layer charts: every road comes from actual gameplay connections.</summary>
    public partial class WorldMapScreen
    {
        Button atlasButton;
        RectTransform atlasPanel, atlasContents;
        WorldLayer builtWorld;
        bool atlasBuilt;
        readonly List<(string id, Image dot, Image halo, Text label)> atlasDots = new List<(string, Image, Image, Text)>();

        static Dictionary<string, Vector2> SurfacePositions() => new Dictionary<string, Vector2>
        {
            [MapRegistry.Village] = new Vector2(0.22f, 0.48f),
            ["forest"] = new Vector2(0.08f, 0.31f),
            ["forest_ruins"] = new Vector2(0.20f, 0.22f),
            ["forest_depths"] = new Vector2(0.20f, 0.40f),
            ["forest_crossing"] = new Vector2(0.32f, 0.31f),
            [MapRegistry.Canyon] = new Vector2(0.79f, 0.48f),
            ["canyon_pass"] = new Vector2(0.70f, 0.28f),
            ["canyon_mine"] = new Vector2(0.81f, 0.18f),
            ["canyon_ridge"] = new Vector2(0.81f, 0.38f),
            ["canyon_gate"] = new Vector2(0.92f, 0.28f),
            [MapRegistry.Winter] = new Vector2(0.50f, 0.20f),
            ["winter_edge"] = new Vector2(0.34f, 0.10f),
            ["winter_lake"] = new Vector2(0.45f, 0.035f),
            ["winter_peak"] = new Vector2(0.45f, 0.165f),
            ["winter_reach"] = new Vector2(0.56f, 0.10f),
            [MapRegistry.Sanctum] = new Vector2(0.50f, 0.78f),
            ["sanctum_hall"] = new Vector2(0.33f, 0.70f),
            ["sanctum_archive"] = new Vector2(0.44f, 0.63f),
            ["sanctum_roots"] = new Vector2(0.44f, 0.77f),
            ["sanctum_court"] = new Vector2(0.55f, 0.70f),
            [MapRegistry.Undergate] = new Vector2(0.50f, 0.47f),
        };
        static Dictionary<string, Vector2> UndergroundPositions() => new Dictionary<string, Vector2>
        {
            ["hollow_descent"] = new Vector2(.50f, 0.26f),
            ["hollow_roots"] = new Vector2(.50f, 0.44f),
            ["hollow_fungal"] = new Vector2(.50f, 0.62f),
            ["hollow_depths"] = new Vector2(.50f, 0.80f),
        };

        void BuildAtlasButton(Transform frame)
        {
            atlasButton = Button(frame, "AtlasToggle", "월드 지도", "ui_btn", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-174, -10), new Vector2(140, 34), ToggleAtlas, 17);
        }
        void ToggleAtlas()
        {
            if (!AtlasVisible) ShowAtlas();
            else { SetAtlasVisible(false); Refresh(); }
            Game.Audio.PlaySfx("select");
        }
        public void ShowAtlas()
        {
            if (!atlasBuilt || builtWorld != selectedWorld) BuildAtlas();
            SetAtlasVisible(true); RefreshAtlas();
        }
        void SetAtlasVisible(bool show)
        {
            if (atlasPanel != null) atlasPanel.gameObject.SetActive(show);
            if (atlasButton != null) atlasButton.GetComponentInChildren<Text>().text = show ? "지역 지도" : "월드 지도";
            if (travelButton != null) travelButton.gameObject.SetActive(!show);
        }

        void BuildAtlas()
        {
            if (atlasPanel == null)
            {
                var frame = map.rectTransform.parent.parent;
                var img = UIFactory.Image(frame, "WorldAtlas", null, Color.white);
                img.preserveAspect = false; img.raycastTarget = true;
                atlasPanel = UIFactory.Place(img.rectTransform, TL, TL, new Vector2(10, -54), new Vector2(740, 416));
                atlasPanel.gameObject.AddComponent<RectMask2D>();
            }
            if (atlasContents != null) { atlasContents.gameObject.SetActive(false); Destroy(atlasContents.gameObject); }
            atlasContents = UIFactory.Stretch(UIFactory.Rect(atlasPanel, "Chart_" + selectedWorld));
            atlasPanel.GetComponent<Image>().sprite = WorldAtlasArt.Chart(selectedWorld);
            builtWorld = selectedWorld; atlasBuilt = true; atlasDots.Clear();
            bool underground = selectedWorld == WorldLayer.Underground;
            var positions = underground ? UndergroundPositions() : SurfacePositions();
            Vector2 At(Vector2 p) => new Vector2(p.x * 740, -p.y * 416);
            if (underground)
            {
                for (int floor = 1; floor <= 4; floor++)
                {
                    float y = -(.26f + (floor - 1) * .18f) * 416;
                    Road(new Vector2(65, y), new Vector2(675, y), new Color32(63, 76, 81, 110), 1);
                    AtlasLabel("Floor_" + floor, "B" + floor, new Vector2(40, y + 10), new Vector2(45, 30), 22, UiTheme.AccentWarm);
                }
                Button(atlasContents, "SurfaceReturn", "↑  뿌리샘 마을 · 지상월드", "ui_btngray", TL, C,
                    At(new Vector2(.5f, .10f)), new Vector2(244, 34), () => SelectWorld(WorldLayer.Surface), 17);
                Road(At(new Vector2(.50f, .14f)), At(positions["hollow_descent"]), new Color32(190, 151, 96, 220), 3, true);
                AtlasLabel("Strata", "폐광  /  뿌리 묘실 · 푸른 수로  /  심연의 봉인", new Vector2(370, -390), new Vector2(620, 26), 17, UiTheme.TextSecondary);
            }
            else
            {
                AtlasLabel("ChartTitle", "네 마을의 순환로", new Vector2(135, -12), new Vector2(235, 28), 19, UiTheme.AccentLight);
                AtlasLabel("Compass", "N  ↑", new Vector2(693, -12), new Vector2(70, 26), 17, UiTheme.TextSecondary);
            }
            var drawn = new HashSet<string>();
            foreach (var kv in positions)
                foreach (var target in WorldRoutes.Neighbors(kv.Key))
                {
                    if (!positions.TryGetValue(target, out var destination)) continue;
                    string key = string.CompareOrdinal(kv.Key, target) < 0 ? kv.Key + "|" + target : target + "|" + kv.Key;
                    if (!drawn.Add(key)) continue;
                    bool spoke = kv.Key == MapRegistry.Undergate || target == MapRegistry.Undergate;
                    Color line = spoke ? new Color32(95, 150, 140, 190) : new Color32(222, 190, 123, 245);
                    Vector2 a = At(kv.Value), b = At(destination);
                    if (underground)
                    {
                        Vector2 control = (a + b) * .5f + new Vector2((a.x + b.x) * .5f < 370 ? -18 : 18, 0);
                        CurvedRoad(a, control, b, new Color32(14, 22, 25, 210), 7);
                        CurvedRoad(a, control, b, line, 3);
                    }
                    else
                    {
                        SurfaceRoad(kv.Key, target, positions, line, spoke);
                    }
                }
            foreach (var kv in positions)
            {
                string id = kv.Key; var info = MapRegistry.Get(id); if (info == null) continue;
                bool town = MapRegistry.IsTown(id), gateway = id == MapRegistry.Undergate;
                Vector2 point = At(kv.Value);
                var halo = UIFactory.Image(atlasContents, "Current_" + id, WorldAtlasArt.Marker("halo"), UiTheme.AccentBlue);
                UIFactory.Place(halo.rectTransform, TL, C, point, Vector2.one * (town || underground ? 46 : 32));
                var dot = UIFactory.Image(atlasContents, "Node_" + id, WorldAtlasArt.Marker(gateway ? "gate" : town ? "town" : underground ? "cave" : "field"), Color.white);
                dot.raycastTarget = true;
                UIFactory.Place(dot.rectTransform, TL, C, point, Vector2.one * (town || underground ? 34 : 24));
                var button = dot.gameObject.AddComponent<Button>(); button.targetGraphic = dot; button.colors = UiTheme.ButtonColors();
                button.onClick.AddListener(() => { if (gateway) SelectWorld(WorldLayer.Underground); else SelectMap(id); });
                var hover = dot.gameObject.AddComponent<PointerRelay>();
                hover.onEnter = () => DescribeAtlasMap(id); hover.onExit = () => RefreshAtlas();
                Text label = null;
                if (town || underground)
                {
                    string text = id == MapRegistry.Village ? "작은 마을" : id == MapRegistry.Sanctum ? "고대 성소" : gateway ? "뿌리샘 · 지하 입구" : info.displayName;
                    Vector2 offset = new Vector2(0, -22);
                    float width = underground ? 240 : gateway ? 190 : 166;
                    var plate = UIFactory.Image(atlasContents, "NamePlate_" + id, Game.Art.Get("ui_white"), new Color32(14, 23, 28, 218));
                    plate.preserveAspect = false;
                    plate.raycastTarget = true;
                    var nameButton = plate.gameObject.AddComponent<Button>(); nameButton.targetGraphic = plate;
                    nameButton.onClick.AddListener(() => { if (gateway) SelectWorld(WorldLayer.Underground); else SelectMap(id); });
                    var nameHover = plate.gameObject.AddComponent<PointerRelay>(); nameHover.onEnter = () => DescribeAtlasMap(id); nameHover.onExit = () => RefreshAtlas();
                    UIFactory.Place(plate.rectTransform, TL, new Vector2(.5f, 1), point + offset + new Vector2(0, 2), new Vector2(width, 27));
                    label = AtlasLabel("Name_" + id, text, point + offset, new Vector2(width, 27), 17, town ? UiTheme.AccentLight : UiTheme.TextPrimary);
                }
                else
                {
                    var stage = AtlasLabel("Stage_" + id, FieldIndex(id), point, new Vector2(24, 24), 16, UiTheme.TextPrimary);
                    stage.alignment = TextAnchor.MiddleCenter;
                    UIFactory.Place(stage.rectTransform, TL, C, point, new Vector2(24, 24));
                }
                atlasDots.Add((id, dot, halo, label));
            }
        }

        static string FieldIndex(string id)
        {
            foreach (var region in WorldRoutes.Regions)
                for (int stage = 1; stage <= 4; stage++)
                    if (region[stage] == id) return stage.ToString();
            return "";
        }
        static string FieldRegion(string id)
        {
            string[] names = { "해골 숲", "바위 협곡", "눈꽃 숲", "고대 성소" };
            for (int r = 0; r < WorldRoutes.Regions.Length; r++)
                for (int stage = 1; stage <= 4; stage++)
                    if (WorldRoutes.Regions[r][stage] == id) return names[r];
            return "";
        }
        void SurfaceRoad(string source, string target, Dictionary<string, Vector2> positions, Color color, bool spoke)
        {
            Vector2 At(Vector2 p) => new Vector2(p.x * 740, -p.y * 416);
            if (spoke) { DottedAtlasRoad(At(positions[source]), At(positions[target]), color, true); return; }
            for (int r = 0; r < WorldRoutes.Regions.Length; r++)
            {
                var region = WorldRoutes.Regions[r];
                bool Connects(string a, string b) => (source == a && target == b) || (source == b && target == a);
                if (Connects(region[0], region[1]))
                {
                    var town = positions[region[0]]; var first = positions[region[1]];
                    float laneX = first.x - .035f;
                    float approachY = r == 1 ? town.y - .045f : town.y;
                    var approach = new[] { town, new Vector2(town.x, approachY), new Vector2(laneX, approachY), new Vector2(laneX, first.y), first };
                    RoundedSurfaceRoad(approach, color, true);
                    return;
                }
                if (!Connects(region[4], region[5])) continue;
                Vector2[] lane;
                switch (r)
                {
                    case 0: lane = new[] { new Vector2(.39f, .355f), new Vector2(.65f, .355f), new Vector2(.70f, .43f) }; break;
                    case 1: lane = new[] { new Vector2(.97f, .28f), new Vector2(.97f, .065f), new Vector2(.66f, .065f), new Vector2(.66f, .235f), new Vector2(.58f, .235f) }; break;
                    case 2: lane = new[] { new Vector2(.62f, .10f), new Vector2(.62f, .32f), new Vector2(.945f, .54f), new Vector2(.945f, .90f), new Vector2(.64f, .90f), new Vector2(.64f, .78f) }; break;
                    default: lane = new[] { new Vector2(.67f, .70f), new Vector2(.67f, .95f), new Vector2(.10f, .95f), new Vector2(.025f, .78f), new Vector2(.025f, .52f) }; break;
                }
                Color transfer = new Color32(163, 181, 187, 130);
                var transferPath = new List<Vector2> { positions[region[4]] };
                transferPath.AddRange(lane); transferPath.Add(positions[region[5]]);
                RoundedSurfaceRoad(transferPath, transfer, false);
                return;
            }
            // Branch edges stay inside their biome's 1 → upper 2 / lower 3 → 4 diamond.
            DottedAtlasRoad(At(positions[source]), At(positions[target]), color, false, false);
        }
        void RoundedSurfaceRoad(IList<Vector2> waypoints, Color color, bool dotted)
        {
            var corners = new List<Vector2>();
            foreach (var point in waypoints)
            {
                var at = new Vector2(point.x * 740, -point.y * 416);
                if (corners.Count == 0 || (at - corners[corners.Count - 1]).sqrMagnitude > .01f) corners.Add(at);
            }
            if (corners.Count < 2) return;
            var path = new List<Vector2> { corners[0] };
            for (int n = 1; n < corners.Count - 1; n++)
            {
                Vector2 corner = corners[n], incoming = corner - corners[n - 1], outgoing = corners[n + 1] - corner;
                float trim = Mathf.Min(12, incoming.magnitude * .45f, outgoing.magnitude * .45f);
                Vector2 entry = corner - incoming.normalized * trim, exit = corner + outgoing.normalized * trim;
                path.Add(entry);
                for (int sample = 1; sample <= 6; sample++)
                {
                    float t = sample / 6f;
                    path.Add((1 - t) * (1 - t) * entry + 2 * (1 - t) * t * corner + t * t * exit);
                }
            }
            path.Add(corners[corners.Count - 1]);
            float distance = 0, nextBead = 9;
            for (int n = 1; n < path.Count; n++)
            {
                Vector2 delta = path[n] - path[n - 1];
                float length = delta.magnitude;
                if (length < .001f) continue;
                Vector2 direction = delta / length;
                if (dotted)
                {
                    while (nextBead < distance + length)
                    {
                        AtlasTrailBead(path[n - 1] + direction * (nextBead - distance), color, 5);
                        nextBead += 9;
                    }
                }
                else
                {
                    // Carry dash phase across straight sections and corner samples.
                    float along = 0;
                    while (along < length - .001f)
                    {
                        float phase = Mathf.Repeat(distance + along, 13);
                        bool visible = phase < 7;
                        float span = Mathf.Min(length - along, (visible ? 7 : 13) - phase);
                        if (span < .001f) { along += .001f; continue; }
                        if (visible) Road(path[n - 1] + direction * along, path[n - 1] + direction * (along + span), color, 1.5f);
                        along += span;
                    }
                }
                distance += length;
            }
        }
        void AtlasTrailBead(Vector2 at, Color color, float size)
        {
            var dot = UIFactory.Image(atlasContents, "Trail bead", WorldAtlasArt.Marker("field"), color);
            dot.raycastTarget = false;
            UIFactory.Place(dot.rectTransform, TL, C, at, Vector2.one * size);
        }
        Text AtlasLabel(string name, string text, Vector2 p, Vector2 size, int font, Color color)
        {
            var label = UIFactory.Text(atlasContents, name, text, font, color, TextAnchor.UpperCenter, true);
            UIFactory.Place(label.rectTransform, TL, new Vector2(.5f, 1), p, size); label.raycastTarget = false; return label;
        }
        void Road(Vector2 a, Vector2 b, Color color, float width, bool dashed = false)
        {
            float length = (b - a).magnitude; if (length < .1f) return;
            if (dashed)
            {
                var direction = (b - a) / length;
                for (float p = 0; p < length; p += 13) Road(a + direction * p, a + direction * Mathf.Min(p + 7, length), color, width);
                return;
            }
            var line = UIFactory.Image(atlasContents, "Route", Game.Art.Get("ui_white"), color); line.preserveAspect = false; line.raycastTarget = false;
            var rt = line.rectTransform; rt.anchorMin = rt.anchorMax = TL; rt.pivot = new Vector2(0, .5f);
            rt.anchoredPosition = a; rt.sizeDelta = new Vector2(length, width);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }
        void DottedAtlasRoad(Vector2 a, Vector2 b, Color color, bool spoke, bool curved = true)
        {
            Vector2 delta = b - a;
            Vector2 control = (a + b) * .5f + (curved ? new Vector2(-delta.y, delta.x).normalized * Mathf.Min(12, delta.magnitude * .10f) : Vector2.zero);
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (spoke ? 13 : 9)));
            for (int n = 1; n < steps; n++)
            {
                float t = n / (float)steps;
                Vector2 at = (1-t)*(1-t)*a + 2*(1-t)*t*control + t*t*b;
                AtlasTrailBead(at, color, spoke ? 3.5f : 5f);
            }
        }
        void CurvedRoad(Vector2 a, Vector2 control, Vector2 b, Color color, float width)
        {
            Vector2 last = a;
            for (int n = 1; n <= 12; n++)
            {
                float t = n / 12f;
                Vector2 next = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
                Road(last, next, color, width); last = next;
            }
        }
        void DescribeAtlasMap(string id)
        {
            var info = MapRegistry.Get(id); if (info == null) return;
            var zone = HuntingGrounds.Get(id);
            string stage = FieldIndex(id);
            string name = stage.Length > 0 ? FieldRegion(id) + " · 사냥터 " + stage + "  " + info.displayName : info.displayName;
            legend.text = name + (zone != null ? $"   Lv.{zone.minLevel}~{zone.maxLevel}" : "   안전 지역") + (id == MapRegistry.Undergate ? "   ·  클릭하여 지하 던전 지도 열기" : "   ·  클릭하여 지역 지도 보기");
        }
        void RefreshAtlas()
        {
            if (!AtlasVisible) return;
            if (builtWorld != selectedWorld) { BuildAtlas(); SetAtlasVisible(true); }
            string here = Game.World != null ? Game.World.MapId : null;
            var hereInfo = MapRegistry.Get(here); if (hereInfo?.exteriorMap != null) here = hereInfo.exteriorMap;
            int level = Game.Session?.Progression?.Level ?? 1;
            foreach (var entry in atlasDots)
            {
                bool current = entry.id == here; var zone = HuntingGrounds.Get(entry.id);
                entry.halo.enabled = current || entry.id == selectedMap;
                entry.halo.color = current ? UiTheme.AccentBlue : UiTheme.AccentWarm;
                int entryLevel=zone==null?0:WorldLayers.IsUnderground(entry.id)?Mathf.Min(Progression.MaxLevel,zone.minLevel):zone.minLevel;
                entry.dot.color = level >= entryLevel ? Color.white : new Color32(223, 152, 145, 255);
                if (entry.label != null) entry.label.color = current ? UiTheme.AccentBlue : MapRegistry.IsTown(entry.id) ? UiTheme.AccentLight : UiTheme.TextPrimary;
            }
            caption.text = WorldLayers.Name(selectedWorld) + " <color=#b8c4d8>· 연결 지도</color>";
            legend.text = "<color=#78c8ff>◎</color> 현재 위치   <color=#ffe066>◆</color> 선택 지역   금색: 사냥터   청록: 마을길   회색 점선: 다음 지역";
            detail.rectTransform.sizeDelta = new Vector2(718, 70); detail.fontSize = 17;
            detail.text = selectedWorld == WorldLayer.Surface
                ? "작은 마을 → 바위 협곡 → 눈꽃 숲 → 고대 성소 → 작은 마을\n각 지역의 사냥터: 1 → 위 2 / 아래 3 → 4. 숫자에 마우스를 올리면 지역과 이름을 확인합니다."
                : "뿌리샘 마을의 하행 계단 → B1 → B2 → B3 → B4\n지역을 선택하면 지형과 출구를 확인합니다. 지도를 눌러도 캐릭터는 이동하지 않습니다.";
            roomButton.gameObject.SetActive(false); bossIcon.enabled = false;
        }
    }
}
