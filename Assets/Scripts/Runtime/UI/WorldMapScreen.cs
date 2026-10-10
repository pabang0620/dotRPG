using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Atlas browser: selecting a region never changes the active world or character position.</summary>
    public partial class WorldMapScreen : WindowScreen
    {
        RawImage map;
        RectTransform mapRect, playerDot, tooltip;
        Text caption, legend, detail, tooltipText, npcPageText;
        Transform side;
        RectTransform regionsContent;
        ScrollRect regionsScroll;
        Button surfaceTab, undergroundTab;
        WorldLayer selectedWorld;
        Button roomButton, travelButton;
        Image bossIcon;
        readonly List<GameObject> pins = new List<GameObject>();
        readonly List<(string id, Button button)> regionButtons = new List<(string, Button)>();
        readonly List<(Button button, Text label)> npcRows = new List<(Button, Text)>();
        readonly List<(RectTransform pin, WorldAtlas.Marker marker)> npcPins = new List<(RectTransform, WorldAtlas.Marker)>();
        readonly List<Button> roomTabs = new List<Button>();
        List<WorldAtlas.Marker> npcs = new List<WorldAtlas.Marker>();
        WorldAtlas.Preview preview;
        string selectedMap;
        int selectedNpc = -1, npcPage;
        float mapScale;
        public string SelectedMap => selectedMap;
        public int NpcCount => npcs.Count;
        public WorldLayer SelectedWorld => selectedWorld;
        public int ListedRegionCount => regionButtons.Count;
        public bool AtlasVisible => atlasPanel != null && atlasPanel.gameObject.activeSelf;
        static readonly Vector2 TL = new Vector2(0, 1), C = new Vector2(.5f, .5f);

        public static WorldMapScreen Create(Transform canvas)
        {
            var w = CreateWindow<WorldMapScreen>(canvas, "WorldMap", "세계 지도", "menuicon_map");
            var frame = Panel(w.content, "Frame", TL, TL, Vector2.zero, new Vector2(760, 590), UiTheme.PanelDeep);
            w.caption = Label(frame.transform, "RegionTitle", "", 22, TL, TL, new Vector2(18, -10), new Vector2(GameFlow.DemoSlots ? 260 : 400, 32));
            w.BuildAtlasButton(frame.transform); // [UI] whole-world route map
            if (GameFlow.DemoSlots) // [DEMO] trial builds jump straight to any region
                w.travelButton = Button(frame.transform, "DemoTravel", "이 지역으로 이동", "ui_btn", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-324, -10), new Vector2(150, 34), () =>
                {
                    var target = w.selectedMap;
                    var info = MapRegistry.Get(target);
                    if (info == null || info.IsInterior || target == Game.World.MapId) return;
                    w.Close();
                    Game.Flow.TravelTo(target, true);
                }, 17);
            Button(frame.transform, "CurrentRegion", "현재 위치", "ui_btngray", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -10), new Vector2(150, 34), () => w.SelectMap(Game.World.MapId), 17);
            var viewport = UIFactory.Place(UIFactory.Rect(frame.transform, "MapViewport"), TL, TL, new Vector2(10, -54), new Vector2(740, 416));
            viewport.gameObject.AddComponent<RectMask2D>();
            w.map = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>(); w.map.transform.SetParent(viewport, false); w.map.raycastTarget = false;
            w.mapRect = w.map.rectTransform; UIFactory.Place(w.mapRect, C, C, Vector2.zero, Vector2.one);
            var dot = UIFactory.Image(w.mapRect, "Player", Game.Art.Get("ui_dot"), UiTheme.AccentBlue);
            w.playerDot = UIFactory.Place(dot.rectTransform, C, C, Vector2.zero, new Vector2(18, 18));
            var tip = Panel(viewport, "NpcTooltip", C, C, Vector2.zero, new Vector2(340, 35), UiTheme.PanelDeep);
            w.tooltip = tip.rectTransform;
            w.tooltipText = Label(tip.transform, "Text", "", 17, C, C, Vector2.zero, new Vector2(326, 31), TextAnchor.MiddleCenter);
            w.tooltip.gameObject.SetActive(false);
            w.legend = Label(frame.transform, "Legend", "", 16, TL, TL, new Vector2(18, -474), new Vector2(718, 26), TextAnchor.MiddleLeft);
            w.detail = Label(frame.transform, "Selection", "NPC 번호나 이름을 선택하세요.", 17, TL, TL, new Vector2(18, -502), new Vector2(530, 34), TextAnchor.UpperLeft);
            w.roomButton = Button(frame.transform, "ViewInterior", "실내 보기", "ui_btn", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 12), new Vector2(160, 34), () =>
            { if (w.selectedNpc >= 0 && w.npcs[w.selectedNpc].interior != null) w.SelectMap(w.npcs[w.selectedNpc].interior); }, 17);
            w.roomButton.gameObject.SetActive(false);
            // [FIELD BOSS] The region's boss face next to its schedule line.
            w.bossIcon = UIFactory.SharpIcon(frame.transform, "BossIcon", Color.white);
            w.bossIcon.raycastTarget = false;
            UIFactory.Place(w.bossIcon.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 14), new Vector2(56, 56));
            w.bossIcon.enabled = false;
            w.side = Panel(w.content, "Side", new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(420, 590), UiTheme.Panel).transform;
            Label(w.side, "RegionHeading", "월드 선택 · 지역을 눌러 자세히 보기", 20, TL, TL, new Vector2(16, -10), new Vector2(388, 30));
            w.surfaceTab = Button(w.side, "WorldSurface", "지상월드", "ui_btn", TL, TL, new Vector2(16, -44), new Vector2(190, 38), () => w.SelectWorld(WorldLayer.Surface), 19);
            w.undergroundTab = Button(w.side, "WorldUnderground", "지하월드", "ui_btngray", TL, TL, new Vector2(214, -44), new Vector2(190, 38), () => w.SelectWorld(WorldLayer.Underground), 19);
            var regions=Panel(w.side,"RegionsViewport",TL,TL,new Vector2(16,-94),new Vector2(388,244),UiTheme.PanelDeep);
            regions.gameObject.AddComponent<RectMask2D>();
            w.regionsContent=UIFactory.Place(UIFactory.Rect(regions.transform,"RegionsContent"),TL,TL,Vector2.zero,new Vector2(388,244));
            w.regionsScroll=regions.gameObject.AddComponent<ScrollRect>();w.regionsScroll.viewport=regions.rectTransform;w.regionsScroll.content=w.regionsContent;w.regionsScroll.horizontal=false;w.regionsScroll.vertical=true;w.regionsScroll.scrollSensitivity=32;w.regionsScroll.movementType=ScrollRect.MovementType.Clamped;
            w.RebuildRegions();
            for (int i = 0; i < MapRegistry.IndoorServices.Length; i++)
            {
                var service = MapRegistry.IndoorServices[i];
                var button = Button(w.side, "Room_" + service, service == NpcService.Blacksmith ? "대장간 실내" : MapRegistry.ServiceName(service) + " 실내", "ui_btngray", TL, TL,
                    new Vector2(16 + i * 131, -351), new Vector2(126, 30), () =>
                    { string town = MapRegistry.Get(w.selectedMap)?.exteriorMap ?? w.selectedMap; var id = MapRegistry.InteriorFor(town, service); if (id != null) w.SelectMap(id); }, 16);
                w.roomTabs.Add(button);
            }
            Label(w.side, "NpcHeading", "NPC 위치", 20, TL, TL, new Vector2(16, -394), new Vector2(220, 28));
            Button(w.side, "NpcPrev", "◀", "ui_btngray", TL, TL, new Vector2(264, -391), new Vector2(36, 28), () => w.PageNpcs(-1), 16);
            w.npcPageText = Label(w.side, "NpcPage", "", 16, TL, TL, new Vector2(300, -391), new Vector2(58, 28), TextAnchor.MiddleCenter);
            Button(w.side, "NpcNext", "▶", "ui_btngray", TL, TL, new Vector2(358, -391), new Vector2(36, 28), () => w.PageNpcs(1), 16);
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                var button = Button(w.side, "NpcRow_" + i, "", "ui_btngray", TL, TL, new Vector2(16, -428 - i * 25), new Vector2(388, 24), () => w.SelectNpc(w.npcPage * 6 + slot), 16);
                var text = button.GetComponentInChildren<Text>(); text.alignment = TextAnchor.MiddleLeft; UIFactory.Stretch(text.rectTransform, 10, 0, 8, 0);
                w.npcRows.Add((button, text));
            }
            return w;
        }

        /// <summary>Every listed region, grouped under its town or explore area (first in each group), in registry order.</summary>
        List<List<MapInfo>> RegionGroups()
        {
            var all = MapRegistry.All.Where(m => !m.instanced && m.worldLayer == selectedWorld).ToList();
            string Hub(MapInfo m)
            {
                var zone = HuntingGrounds.Get(m.id);
                if (zone != null && all.Any(x => x.id == zone.village)) return zone.village;
                return m.id;
            }
            var groups = new List<List<MapInfo>>();
            var byHub = new Dictionary<string, List<MapInfo>>();
            foreach (var m in all)
            {
                string hub = Hub(m);
                if (!byHub.TryGetValue(hub, out var g)) { g = new List<MapInfo>(); byHub[hub] = g; groups.Add(g); }
                if (hub == m.id) g.Insert(0, m); else g.Add(m);
            }
            foreach (var g in groups)
            {
                var head = g[0];
                var rest = g.Skip(1).OrderBy(m => HuntingGrounds.Get(m.id)?.minLevel ?? 0).ToList();
                g.Clear(); g.Add(head); g.AddRange(rest);
            }
            return groups;
        }

        void RebuildRegions()
        {
            if (regionsContent == null) return;
            foreach (var entry in regionButtons) { entry.button.gameObject.SetActive(false); Destroy(entry.button.gameObject); }
            regionButtons.Clear();
            const float rowH = 32;
            var groups = RegionGroups();
            regionsContent.sizeDelta = new Vector2(388, Mathf.Max(244, groups.Sum(g => g.Count) * rowH));
            regionsContent.anchoredPosition = Vector2.zero;
            int row = 0;
            foreach (var group in groups)
                for (int k = 0; k < group.Count; k++)
                {
                    var info = group[k]; string id = info.id; var zone = HuntingGrounds.Get(id);
                    string badge = selectedWorld == WorldLayer.Underground && !info.displayName.StartsWith("B" + info.depth) ? $"B{info.depth} · " : "";
                    string tag = zone == null ? (MapRegistry.IsTown(id) ? "<color=#b9dbab>마을</color>" : "<color=#91c9b0>탐험</color>") : $"<color=#ccb995>Lv.{zone.minLevel}~{zone.maxLevel}</color>";
                    string label = k == 0 ? $"<b>{badge}{info.displayName}</b>   {tag}" : $"<color=#8c96a8>{(k == group.Count - 1 ? "└" : "├")}</color> {badge}{info.displayName}   {tag}";
                    var button = Button(regionsContent, "Region_" + id, label, k == 0 && zone == null ? "ui_btn" : "ui_btngray", TL, TL,
                        new Vector2(k == 0 ? 0 : 14, -row * rowH), new Vector2(k == 0 ? 388 : 374, rowH - 2), () => SelectMap(id), 16);
                    var text = button.GetComponentInChildren<Text>(); text.alignment = TextAnchor.MiddleLeft; UIFactory.Stretch(text.rectTransform, 10, 0, 8, 0);
                    regionButtons.Add((id, button)); row++;
                }
            RefreshWorldTabs();
        }

        void RefreshWorldTabs()
        {
            if (surfaceTab == null || undergroundTab == null) return;
            surfaceTab.GetComponent<Image>().sprite = UiTheme.Tab(selectedWorld == WorldLayer.Surface);
            undergroundTab.GetComponent<Image>().sprite = UiTheme.Tab(selectedWorld == WorldLayer.Underground);
            surfaceTab.GetComponentInChildren<Text>().color = selectedWorld == WorldLayer.Surface ? UiTheme.AccentLight : UiTheme.TextSecondary;
            undergroundTab.GetComponentInChildren<Text>().color = selectedWorld == WorldLayer.Underground ? UiTheme.AccentBlue : UiTheme.TextSecondary;
        }

        public void SelectWorld(WorldLayer world)
        {
            selectedWorld = world;
            RebuildRegions();
            if (string.IsNullOrEmpty(selectedMap) || WorldLayers.Of(selectedMap) != world)
                selectedMap = Game.World != null && WorldLayers.Of(Game.World.MapId) == world ? Game.World.MapId
                    : MapRegistry.All.FirstOrDefault(m => !m.instanced && m.worldLayer == world)?.id;
            selectedNpc = -1; npcPage = 0;
            Refresh(); ShowAtlas();
        }

        protected override bool PadNavigation => true;

        public override void Show()
        {
            selectedMap = Game.World.MapId; selectedWorld = WorldLayers.Of(selectedMap);
            RebuildRegions(); SetAtlasVisible(false); base.Show(); ShowAtlas();
        }
        public void SelectMap(string id)
        {
            if (MapRegistry.Get(id) == null) return;
            selectedMap = id; selectedNpc = -1; npcPage = 0;
            if (selectedWorld != WorldLayers.Of(id)) { selectedWorld = WorldLayers.Of(id); RebuildRegions(); }
            SetAtlasVisible(false); Refresh();
        }
        protected override void Refresh()
        {
            if (Game.World == null) return;
            if (string.IsNullOrEmpty(selectedMap)) selectedMap = Game.World.MapId;
            preview = WorldAtlas.Get(selectedMap); if (preview == null) return;
            bool current = selectedMap == Game.World.MapId;
            var info = MapRegistry.Get(selectedMap);
            map.texture = current ? Game.World.Minimap : preview.texture;
            mapScale = Mathf.Min(720 / preview.bounds.width, 396 / preview.bounds.height);
            mapRect.sizeDelta = preview.bounds.size * mapScale;
            caption.text = info.displayName + (current ? " <color=#78dcff>· 현재</color>" : "");
            legend.text = "<color=#78dcff>●</color> 내 위치   <color=#dab785>①</color> NPC   <color=#bca5ff>◆</color> 출구   · " + (current || preview.rendered ? "실제 지형" : "미방문 지역 약도");
            var zone = HuntingGrounds.Get(selectedMap);
            detail.rectTransform.sizeDelta = new Vector2(718, 38);
            detail.fontSize = 16;
            bossIcon.enabled = false;
            detail.text = current ? "NPC 번호나 이름을 선택하세요." : "NPC 기본 위치 · 지역 선택은 캐릭터를 이동시키지 않습니다.";
            if (zone != null)
            {
                int lv = Game.Session.Progression.Level;
                detail.rectTransform.sizeDelta = new Vector2(718, 84);
                detail.text = WorldLayers.IsUnderground(selectedMap)
                    ? $"지역 등급 Lv.{zone.minLevel}~{zone.maxLevel} · 몬스터 Lv.{zone.monsterLevel} · 귀환: {MapRegistry.Get(zone.village).displayName}\n" +
                      "Lv.40 만렙 이후 도전 · 강화 장비·파티 권장 <color=#b8c4d8>(캐릭터 상한 Lv.40)</color>"
                    : $"권장 Lv.{zone.minLevel}~{zone.maxLevel} · 1마리 경험치 {Progression.XpPercent(zone.KillXp, lv)} · 귀환 마을 {MapRegistry.Get(zone.village).displayName}\n" +
                      $"1시간 사냥 시 경험치 <color=#8fe28f>약 {Progression.XpPercent((long)zone.KillXp * HuntingGrounds.KillsPerHourEstimate, lv)}</color> <color=#b8c4d8>(내 레벨 기준 예상치)</color>";
                string boss = FieldBosses.InfoLine(zone.id); // [FIELD BOSS]
                var face = boss != null ? Game.Art.Optional("fboss_icon_" + zone.id.Split('_')[0]) : null;
                bossIcon.enabled = face != null;
                bossIcon.sprite = face;
                if (boss != null) { detail.rectTransform.sizeDelta = new Vector2(face != null ? 650 : 718, 84); detail.text += "\n" + boss; }
            }
            if(zone!=null)detail.text += "\n" + WorldRoutes.Directions(info);
            tooltip.gameObject.SetActive(false); roomButton.gameObject.SetActive(false);
            foreach (var pin in pins) { pin.SetActive(false); Destroy(pin); } pins.Clear(); npcPins.Clear();
            foreach (var portal in preview.portals)
            {
                var exit = UIFactory.Image(mapRect, "Exit", MinimapView.PortalSprite(), Color.white);
                UIFactory.Place(exit.rectTransform, C, C, Place(portal.position), new Vector2(24, 28)); pins.Add(exit.gameObject);
                PortalTooltip.Hook(exit, portal.target);
            }
            npcs = WorldAtlas.Npcs(selectedMap).OrderByDescending(n => n.service != NpcService.None).ThenBy(n => n.name).ToList();
            for (int i = 0; i < npcs.Count; i++)
            {
                int index = i;
                var pin = UIFactory.Image(mapRect, "NpcPin_" + i, Game.Art.Get("ui_circle"), npcs[i].service == NpcService.None ? new Color32(129, 180, 150, 255) : new Color32(222, 184, 117, 255));
                pin.raycastTarget = true; UIFactory.Place(pin.rectTransform, C, C, Place(npcs[i].position), new Vector2(24, 24));
                var number = UIFactory.Text(pin.transform, "Number", (i + 1).ToString(), 16, new Color32(24, 30, 39, 255), TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(number.rectTransform); number.raycastTarget = false;
                var relay = pin.gameObject.AddComponent<PointerRelay>(); relay.onClick = _ => SelectNpc(index);
                relay.onEnter = () => ShowTip(index); relay.onExit = () => { if (selectedNpc < 0) tooltip.gameObject.SetActive(false); else ShowTip(selectedNpc); };
                pins.Add(pin.gameObject); npcPins.Add((pin.rectTransform, npcs[i]));
            }
            playerDot.gameObject.SetActive(current); playerDot.anchoredPosition = Place(Game.Player.Position); playerDot.SetAsLastSibling();
            foreach (var entry in regionButtons)
                entry.button.GetComponent<Image>().color = entry.id == (info.exteriorMap ?? selectedMap) ? new Color32(180, 208, 223, 255) : Color.white;
            bool town = info.IsInterior || (MapRegistry.IsTown(selectedMap) && selectedMap != MapRegistry.Sanctum);
            for (int i = 0; i < roomTabs.Count; i++) roomTabs[i].interactable = town;
            RenderNpcRows();
            RefreshWorldTabs();
            RefreshAtlas();
        }
        Vector2 Place(Vector2 pos) => (pos - preview.bounds.center) * mapScale;
        void PageNpcs(int delta) { npcPage = Mathf.Clamp(npcPage + delta, 0, Mathf.Max(0, (npcs.Count - 1) / 6)); RenderNpcRows(); }
        void RenderNpcRows()
        {
            npcPageText.text = (npcPage + 1) + "/" + Mathf.Max(1, (npcs.Count + 5) / 6);
            for (int i = 0; i < npcRows.Count; i++)
            {
                int index = npcPage * 6 + i; bool exists = index < npcs.Count;
                npcRows[i].button.gameObject.SetActive(exists);
                if (!exists) continue;
                npcRows[i].label.text = $"{index + 1:00}  {npcs[index].name}";
                npcRows[i].button.GetComponent<Image>().color = index == selectedNpc ? new Color32(182, 214, 232, 255) : Color.white;
            }
            if (npcs.Count == 0 && HuntingGrounds.Get(selectedMap) == null) detail.text = "이 지역에 표시할 NPC가 없습니다.";
        }
        public void SelectNpc(int index)
        {
            if (AtlasVisible) { SetAtlasVisible(false); Refresh(); }
            if (index < 0 || index >= npcs.Count) return;
            selectedNpc = index; npcPage = index / 6; RenderNpcRows();
            detail.rectTransform.sizeDelta = new Vector2(530, 38);
            detail.text = npcs[index].name;
            roomButton.gameObject.SetActive(npcs[index].interior != null);
            for (int i = 0; i < npcPins.Count; i++) npcPins[i].pin.localScale = Vector3.one * (i == index ? 1.3f : 1);
            ShowTip(index);
        }
        void ShowTip(int index)
        {
            var pos = Place(npcs[index].position) + new Vector2(0, 30);
            pos.x = Mathf.Clamp(pos.x, -195, 195); pos.y = Mathf.Clamp(pos.y, -206, 206);
            tooltip.anchoredPosition = pos; tooltipText.text = npcs[index].name; tooltip.gameObject.SetActive(true); tooltip.SetAsLastSibling();
        }
        protected override void Update()
        {
            base.Update();
            if (AtlasVisible || selectedMap != Game.World.MapId || preview == null) return;
            playerDot.anchoredPosition = Place(Game.Player.Position);
            foreach (var entry in npcPins)
            {
                if (entry.marker.interior != null) continue;
                foreach (var npc in NpcController.All)
                    if (npc != null && npc.Definition.npcId == entry.marker.id)
                    { entry.marker.position = npc.transform.position; entry.pin.anchoredPosition = Place(entry.marker.position); break; }
            }
            if (selectedNpc >= 0) ShowTip(selectedNpc);
        }
    }
}
