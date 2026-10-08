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
        static readonly string[] RingTowns = { MapRegistry.Village, MapRegistry.Canyon, MapRegistry.Winter, MapRegistry.Sanctum };

        static Dictionary<string, Vector2> SurfacePositions() => new Dictionary<string, Vector2>
        {
            [MapRegistry.Village] = new Vector2(.50f, .17f),
            ["forest"] = new Vector2(.65f, .15f), ["forest_ruins"] = new Vector2(.77f, .17f),
            ["forest_depths"] = new Vector2(.70f, .30f), ["forest_crossing"] = new Vector2(.86f, .32f),
            [MapRegistry.Canyon] = new Vector2(.88f, .51f),
            ["canyon_pass"] = new Vector2(.86f, .68f), ["canyon_mine"] = new Vector2(.76f, .71f),
            ["canyon_ridge"] = new Vector2(.80f, .85f), ["canyon_gate"] = new Vector2(.65f, .87f),
            [MapRegistry.Winter] = new Vector2(.50f, .83f),
            ["winter_edge"] = new Vector2(.35f, .87f), ["winter_lake"] = new Vector2(.24f, .72f),
            ["winter_peak"] = new Vector2(.20f, .86f), ["winter_reach"] = new Vector2(.14f, .68f),
            [MapRegistry.Sanctum] = new Vector2(.12f, .51f),
            ["sanctum_hall"] = new Vector2(.14f, .32f), ["sanctum_archive"] = new Vector2(.23f, .17f),
            ["sanctum_roots"] = new Vector2(.30f, .30f), ["sanctum_court"] = new Vector2(.35f, .15f),
            [MapRegistry.Undergate] = new Vector2(.50f, .51f),
        };
        static Dictionary<string, Vector2> UndergroundPositions() => new Dictionary<string, Vector2>
        {
            ["hollow_descent"] = new Vector2(.50f, .27f), ["hollow_roots"] = new Vector2(.29f, .52f),
            ["hollow_fungal"] = new Vector2(.71f, .52f), ["hollow_depths"] = new Vector2(.50f, .78f),
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
                for (int floor = 1; floor <= 3; floor++)
                {
                    float y = floor == 1 ? -.27f * 416 : floor == 2 ? -.52f * 416 : -.78f * 416;
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
                AtlasLabel("WorldGateway", "지하 입구", At(new Vector2(.5f, .66f)), new Vector2(110, 23), 16, UiTheme.AccentBlue);
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
                        Road(a, b, new Color32(14, 22, 25, 210), spoke ? 5 : 7);
                        Road(a, b, line, spoke ? 2 : 3, spoke);
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
                button.onClick.AddListener(() => SelectMap(id));
                var hover = dot.gameObject.AddComponent<PointerRelay>();
                hover.onEnter = () => DescribeAtlasMap(id); hover.onExit = () => RefreshAtlas();
                Text label = null;
                if (town || underground)
                {
                    string text = info.displayName; int number = System.Array.IndexOf(RingTowns, id);
                    if (number >= 0) text = (number + 1) + "  " + (id == MapRegistry.Village ? "작은 마을" : id == MapRegistry.Sanctum ? "고대 성소" : info.displayName);
                    Vector2 offset = id == MapRegistry.Village ? new Vector2(0, 35) : new Vector2(0, -22);
                    float width = underground ? 240 : gateway ? 170 : 166;
                    var plate = UIFactory.Image(atlasContents, "NamePlate_" + id, Game.Art.Get("ui_white"), new Color32(14, 23, 28, 218));
                    plate.preserveAspect = false;
                    UIFactory.Place(plate.rectTransform, TL, new Vector2(.5f, 1), point + offset + new Vector2(0, 2), new Vector2(width, 27));
                    label = AtlasLabel("Name_" + id, text, point + offset, new Vector2(width, 27), 17, town ? UiTheme.AccentLight : UiTheme.TextPrimary);
                }
                else AtlasLabel("Stage_" + id, FieldIndex(id), point + new Vector2(0, 8), new Vector2(22, 20), 16, UiTheme.TextPrimary);
                atlasDots.Add((id, dot, halo, label));
            }
        }

        static string FieldIndex(string id)
        {
            if (id == "forest" || id == "canyon_pass" || id == "winter_edge" || id == "sanctum_hall") return "1";
            if (id == "forest_ruins" || id == "canyon_mine" || id == "winter_lake" || id == "sanctum_archive") return "2";
            if (id == "forest_depths" || id == "canyon_ridge" || id == "winter_peak" || id == "sanctum_roots") return "3";
            return "4";
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
            var line = UIFactory.Image(atlasContents, "Route", Game.Art.Get("ui_white"), color); line.preserveAspect = false;
            var rt = line.rectTransform; rt.anchorMin = rt.anchorMax = TL; rt.pivot = new Vector2(0, .5f);
            rt.anchoredPosition = a; rt.sizeDelta = new Vector2(length, width);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
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
            legend.text = info.displayName + (zone != null ? $"   Lv.{zone.minLevel}~{zone.maxLevel}" : "   안전 지역") + "   ·  클릭하여 지역 지도 보기";
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
            legend.text = "<color=#78c8ff>◎</color> 현재 위치   <color=#ffe066>◆</color> 선택 지역   실선: 사냥터   점선: 마을길";
            detail.rectTransform.sizeDelta = new Vector2(718, 70); detail.fontSize = 17;
            detail.text = selectedWorld == WorldLayer.Surface
                ? "1 작은 마을 → 2 바위 협곡 → 3 눈꽃 숲 → 4 고대 성소 → 1\n각 지역은 사냥터 1 → 위 2 / 아래 3 → 4로 이어집니다. 중앙 뿌리샘에서 지하로 내려갑니다."
                : "뿌리샘 마을의 하행 계단 → B1 → B2 두 갈래 동굴 → B3\n지역을 선택하면 지형과 출구를 확인합니다. 지도를 눌러도 캐릭터는 이동하지 않습니다.";
            roomButton.gameObject.SetActive(false); bossIcon.enabled = false;
        }
    }
}
