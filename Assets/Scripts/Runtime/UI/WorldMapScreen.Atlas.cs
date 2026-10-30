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
            // Clockwise progression: each town lies between the previous region's 4 and its own 1.
            [MapRegistry.Village] = new Vector2(0.165f, 0.56f),
            ["forest"] = new Vector2(0.145f, 0.43f),
            ["forest_ruins"] = new Vector2(0.11f, 0.26f),
            ["forest_depths"] = new Vector2(0.235f, 0.32f),
            ["forest_crossing"] = new Vector2(0.28f, 0.18f),
            [MapRegistry.Canyon] = new Vector2(0.425f, 0.245f),
            ["canyon_pass"] = new Vector2(0.44f, 0.105f),
            ["canyon_mine"] = new Vector2(0.56f, 0.06f),
            ["canyon_ridge"] = new Vector2(0.585f, 0.245f),
            ["canyon_gate"] = new Vector2(0.70f, 0.15f),
            [MapRegistry.Winter] = new Vector2(0.825f, 0.30f),
            ["winter_edge"] = new Vector2(0.865f, 0.495f),
            ["winter_lake"] = new Vector2(0.735f, 0.46f),
            ["winter_peak"] = new Vector2(0.91f, 0.62f),
            ["winter_reach"] = new Vector2(0.795f, 0.725f),
            [MapRegistry.Sanctum] = new Vector2(0.60f, 0.80f),
            ["sanctum_hall"] = new Vector2(0.43f, 0.87f),
            ["sanctum_archive"] = new Vector2(0.39f, 0.73f),
            ["sanctum_roots"] = new Vector2(0.30f, 0.89f),
            ["sanctum_court"] = new Vector2(0.215f, 0.73f),
            [MapRegistry.Undergate] = new Vector2(0.50f, 0.47f),
            // [CH3] 메말라 가는 협곡 and 사얍, west along the chart's top edge (hidden until the chapter opens).
            [MapRegistry.DryFloor] = new Vector2(0.335f, 0.075f),
            [MapRegistry.Sayab] = new Vector2(0.215f, 0.065f),
            [MapRegistry.DryStones] = new Vector2(0.11f, 0.075f),
            [MapRegistry.DryRift] = new Vector2(0.045f, 0.16f),
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
            atlasButton = Button(frame, "AtlasToggle", L.T("월드 지도"), "ui_btn", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-210, -10), new Vector2(140, 34), ToggleAtlas, 17);
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
            if (atlasButton != null) atlasButton.GetComponentInChildren<Text>().text = show ? L.T("지역 지도") : L.T("월드 지도");
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
            // [CH3] Closed chapter 3 regions are not drawn (no node, no road) until the server opens the chapter.
            if (!Chapter3.Enabled) foreach (var id in new List<string>(positions.Keys)) if (MapRegistry.IsChapter3(id)) positions.Remove(id);
            Vector2 At(Vector2 p) => new Vector2(p.x * 740, -p.y * 416);
            if (underground)
            {
                for (int floor = 1; floor <= 4; floor++)
                {
                    float y = -(.26f + (floor - 1) * .18f) * 416;
                    Road(new Vector2(65, y), new Vector2(675, y), new Color32(63, 76, 81, 110), 1);
                    AtlasLabel("Floor_" + floor, "B" + floor, new Vector2(40, y + 10), new Vector2(45, 30), 22, UiTheme.AccentWarm);
                }
                Button(atlasContents, "SurfaceReturn", L.T("↑  뿌리샘 마을 · 지상월드"), "ui_btngray", TL, C,
                    At(new Vector2(.5f, .10f)), new Vector2(420, 34), () => SelectWorld(WorldLayer.Surface), 17);
                Road(At(new Vector2(.50f, .14f)), At(positions["hollow_descent"]), new Color32(190, 151, 96, 220), 3, true);
                AtlasLabel("Strata", L.T("폐광  /  뿌리 묘실 · 푸른 수로  /  심연의 봉인"), new Vector2(370, -390), new Vector2(620, 26), 17, UiTheme.TextSecondary);
            }
            else
            {
                AtlasLabel("ChartTitle", L.T("네 마을의 순환로"), new Vector2(115, -12), new Vector2(190, 28), 19, UiTheme.AccentLight);
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
                bool field = !town && !underground;
                Vector2 point = At(kv.Value);
                // [CH3] A painted node (Art/node_<map id>, 사얍 and the dry canyon) replaces the drawn marker and the stage badge.
                var nodeArt = info.chapter >= 3 ? Game.Art.Optional("node_" + id) : null;
                bool badge = field && nodeArt == null;
                var halo = UIFactory.Image(atlasContents, "Current_" + id, WorldAtlasArt.Marker(badge ? "fieldhalo" : "halo"), UiTheme.AccentBlue);
                UIFactory.Place(halo.rectTransform, TL, C, point, badge ? new Vector2(68, 36) : Vector2.one * (nodeArt != null ? 54 : 46));
                var dot = UIFactory.Image(atlasContents, "Node_" + id, nodeArt ?? WorldAtlasArt.Marker(gateway ? "gate" : town ? "town" : underground ? "cave" : "fieldbadge"), Color.white);
                dot.raycastTarget = true;
                UIFactory.Place(dot.rectTransform, TL, C, point, nodeArt != null ? Vector2.one * 44 : badge ? new Vector2(60, 28) : Vector2.one * 34);
                var button = dot.gameObject.AddComponent<Button>(); button.targetGraphic = dot; button.colors = UiTheme.ButtonColors();
                button.onClick.AddListener(() => { if (gateway) SelectWorld(WorldLayer.Underground); else SelectMap(id); });
                var hover = dot.gameObject.AddComponent<PointerRelay>();
                hover.onEnter = () => DescribeAtlasMap(id); hover.onExit = () => RefreshAtlas();
                Text label = null;
                if (town || underground)
                {
                    string text = id == MapRegistry.Village ? L.T("작은 마을") : id == MapRegistry.Sanctum ? L.T("고대 성소") : gateway ? L.T("뿌리샘 · 지하 입구") : L.T(info.displayName);
                    // Leave the southern approach to the forest town visible beside its nameplate.
                    Vector2 offset = new Vector2(id == MapRegistry.Village ? 115 : 0, -22);
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
                else if (badge)
                {
                    var stage = AtlasLabel("Stage_" + id, FieldBadge(id), point, new Vector2(60, 28), 19, UiTheme.TextPrimary);
                    stage.alignment = TextAnchor.MiddleCenter;
                    stage.horizontalOverflow = HorizontalWrapMode.Overflow;
                    var textShadow = stage.GetComponent<Shadow>();
                    if (textShadow != null) textShadow.effectDistance = new Vector2(1, -1);
                    UIFactory.Place(stage.rectTransform, TL, C, point, new Vector2(60, 28));
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
            string[] names = { L.T("해골 숲"), L.T("바위 협곡"), L.T("눈꽃 숲"), L.T("고대 성소") };
            for (int r = 0; r < WorldRoutes.Regions.Length; r++)
                for (int stage = 1; stage <= 4; stage++)
                    if (WorldRoutes.Regions[r][stage] == id) return names[r];
            return "";
        }
        static string FieldBadge(string id)
        {
            string[] prefixes = { "숲", "협곡", "설원", "성소" }; // i18n-ignore
            for (int r = 0; r < WorldRoutes.Regions.Length; r++)
                for (int stage = 1; stage <= 4; stage++)
                    if (WorldRoutes.Regions[r][stage] == id) return L.T(prefixes[r]) + " " + stage;
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
                    DottedAtlasRoad(At(positions[region[0]]), At(positions[region[1]]), color, false, false);
                    return;
                }
                if (!Connects(region[4], region[5])) continue;
                SurfaceTransferRoad(region[4], region[5], At(positions[region[4]]), At(positions[region[5]]));
                return;
            }
            // Each split follows its biome's clockwise travel direction; stage 2 stays above stage 3.
            DottedAtlasRoad(At(positions[source]), At(positions[target]), color, false, false);
        }
        void SurfaceTransferRoad(string from, string to, Vector2 a, Vector2 b)
        {
            var group = UIFactory.Stretch(UIFactory.Rect(atlasContents, "RouteTransfer_" + from + "_" + to));
            Road(a, b, new Color32(18, 27, 29, 240), 5, false, group);
            Road(a, b, new Color32(245, 204, 117, 255), 2, false, group);
            // The arrow shows the progression order; the actual portal remains bidirectional.
            var arrow = UIFactory.Image(group, "TransferArrow", WorldAtlasArt.Marker("routearrow"), Color.white);
            arrow.raycastTarget = false;
            UIFactory.Place(arrow.rectTransform, TL, C, Vector2.Lerp(a, b, .55f), new Vector2(16, 16));
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }
        void AtlasTrailBead(Vector2 at, Color color, float size)
        {
            var dot = UIFactory.Image(atlasContents, "Trail bead", WorldAtlasArt.Marker("trail"), color);
            dot.raycastTarget = false;
            UIFactory.Place(dot.rectTransform, TL, C, at, Vector2.one * size);
        }
        Text AtlasLabel(string name, string text, Vector2 p, Vector2 size, int font, Color color)
        {
            var label = UIFactory.Text(atlasContents, name, text, font, color, TextAnchor.UpperCenter, true);
            UIFactory.Place(label.rectTransform, TL, new Vector2(.5f, 1), p, size); label.raycastTarget = false; return label;
        }
        void Road(Vector2 a, Vector2 b, Color color, float width, bool dashed = false, Transform parent = null)
        {
            float length = (b - a).magnitude; if (length < .1f) return;
            if (dashed)
            {
                var direction = (b - a) / length;
                for (float p = 0; p < length; p += 13) Road(a + direction * p, a + direction * Mathf.Min(p + 7, length), color, width, false, parent);
                return;
            }
            var line = UIFactory.Image(parent != null ? parent : atlasContents, "Route", Game.Art.Get("ui_white"), color); line.preserveAspect = false; line.raycastTarget = false;
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
            string name = stage.Length > 0 ? L.F("{0} · 사냥터 {1}  {2}", FieldRegion(id), stage, L.T(info.displayName)) : L.T(info.displayName);
            legend.text = name + (zone != null ? $"   Lv.{zone.minLevel}~{zone.maxLevel}" : L.T("   안전 지역")) + (id == MapRegistry.Undergate ? L.T("   ·  클릭하여 지하 던전 지도 열기") : L.T("   ·  클릭하여 지역 지도 보기"));
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
                int entryLevel=zone==null?0:WorldLayers.IsUnderground(entry.id)?Mathf.Min(Progression.LevelCap,zone.minLevel):zone.minLevel;
                entry.dot.color = level >= entryLevel ? Color.white : new Color32(223, 152, 145, 255);
                if (entry.label != null) entry.label.color = current ? UiTheme.AccentBlue : MapRegistry.IsTown(entry.id) ? UiTheme.AccentLight : UiTheme.TextPrimary;
            }
            caption.text = L.F("{0} <color=#b8c4d8>· 연결 지도</color>", L.T(WorldLayers.Name(selectedWorld)));
            legend.text = L.T("<color=#78c8ff>◎</color> 현재 위치   <color=#ffe066>◆</color> 선택 지역   금색: 사냥터   청록: 마을길   화살표: 다음 마을");
            detail.rectTransform.sizeDelta = new Vector2(718, 70); detail.fontSize = 17;
            detail.text = selectedWorld == WorldLayer.Surface
                ? L.T("마을 → 사냥터 1 → 위쪽 2 / 아래쪽 3 → 사냥터 4 → 다음 마을\n숲 → 바위 협곡 → 눈꽃 숲 → 고대 성소 → 숲 순서로 섬을 순환합니다.")
                : L.T("뿌리샘 마을의 하행 계단 → B1 → B2 → B3 → B4\n지역을 선택하면 지형과 출구를 확인합니다. 지도를 눌러도 캐릭터는 이동하지 않습니다.");
            roomButton.gameObject.SetActive(false); bossIcon.enabled = false;
            RefreshAtlasQuestPin(); // [UI] quest destination
        }
    }
}
