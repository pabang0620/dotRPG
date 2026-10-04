using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // Service entrances share the same placement with the atlas; existing village art keeps its custom door pivot.
        void ConfigureTownServices()
        {
            if (!MapRegistry.IsTown(MapId)) return;
            if (MapId != MapRegistry.Village)
            {
                var houses = objectsRoot.Cast<Transform>().Where(t => t.name == "House")
                    .OrderBy(t => Vector2.Distance(t.position, PlayerSpawn)).ToList();
                if (Winter)
                {
                    var barn = objectsRoot.Cast<Transform>().FirstOrDefault(t => t.name == "Barn");
                    if (barn != null) houses.Insert(Mathf.Min(1, houses.Count), barn);
                    houses.Add(StaticProp("Warehouse", "wnt_house", new Vector2(29.5f, 10.1f), new Vector2(2.8f, 1.9f), new Vector2(0, 1.2f)).transform);
                }
                for (int i = 0; i < MapRegistry.IndoorServices.Length && i < houses.Count; i++)
                {
                    var building = houses[i]; var service = MapRegistry.IndoorServices[i];
                    var old = building.GetComponentInChildren<DialogueInteractable>();
                    Vector3 doorPos = old != null ? old.transform.localPosition : Vector3.zero;
                    if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                    ServiceDoor.Attach(building.gameObject, service, MapRegistry.ServiceName(service) + " 들어가기").transform.localPosition = doorPos;
                }
            }
            foreach (var door in objectsRoot.GetComponentsInChildren<ServiceDoor>())
            {
                var sign = new GameObject("ServiceSign").AddComponent<SpriteRenderer>();
                sign.transform.SetParent(door.transform, false); sign.transform.localPosition = new Vector3(0, .9f, 0);
                sign.sprite = Game.Art.Get(door.Service == NpcService.Shop ? "icon_potion_hp" : door.Service == NpcService.Blacksmith ? "icon_anvil" : "icon_chest");
                float extent = sign.sprite != null ? sign.sprite.bounds.size.y : 1;
                sign.transform.localScale = Vector3.one * (.5f / extent); sign.sortingOrder = 25000;
            }
        }

        static readonly Dictionary<NpcService, Sprite> roomFloors = new Dictionary<NpcService, Sprite>();

        void BuildInterior()
        {
            PlayerSpawn = new Vector2(2.5f, 1.35f);
            DungeonGuidePosition = null;
            var service = map.interiorService;
            if (!roomFloors.TryGetValue(service, out var floor))
            {
                // 32 pixels per tile, raised back wall, individual floorboards/stone slabs and a woven rug.
                var c = new PixelCanvas(160, 160);
                var ink = PixelCanvas.Hex("282731"); var wood = PixelCanvas.Hex("79563e"); var edge = PixelCanvas.Hex("bc9362");
                bool smith = service == NpcService.Blacksmith;
                c.Rect(0, 0, 160, 160, ink);
                for (int y = 28; y < 156; y += 8)
                    for (int x = 4; x < 156; x++)
                    {
                        var color = smith ? PixelCanvas.Hex((x / 24 + y / 8) % 2 == 0 ? "737783" : "676b78")
                            : PixelCanvas.Hex((y / 8) % 2 == 0 ? "95714e" : "896447");
                        c.Rect(x, y, 1, 7, color);
                        if ((x + ((y / 8) % 2) * 17) % (smith ? 24 : 40) == 0) c.VLine(x, y, y + 7, ink);
                        if (!smith && (x * 7 + y) % 37 == 0) c.HLine(x, x + 5, y + 3, wood);
                    }
                c.Rect(4, 4, 152, 22, smith ? PixelCanvas.Hex("555765") : wood);
                for (int x = 8; x < 152; x += 16) { c.VLine(x, 5, 25, ink); c.VLine(x + 1, 5, 25, edge); }
                c.Rect(4, 25, 152, 3, ink); c.HLine(4, 155, 27, edge);
                c.Rect(0, 0, 4, 160, wood); c.Rect(156, 0, 4, 160, wood);
                c.Rect(4, 0, 152, 4, edge);
                var rug = PixelCanvas.Hex(service == NpcService.Shop ? "793f48" : smith ? "344b61" : "426458");
                c.Rect(51, 79, 58, 65, ink); c.Rect(53, 81, 54, 61, edge); c.Rect(55, 83, 50, 57, rug);
                for (int y = 87; y < 140; y += 8) { c.Rect(57, y, 2, 2, edge); c.Rect(101, y, 2, 2, edge); }
                c.Line(69, 112, 80, 100, edge); c.Line(80, 100, 91, 112, edge);
                c.Line(91, 112, 80, 124, edge); c.Line(80, 124, 69, 112, edge);
                c.Rect(60, 155, 40, 5, edge); c.HLine(60, 99, 158, ink);
                var tex = new Texture2D(160, 160, TextureFormat.RGBA32, false) { name = "Interior_" + service, filterMode = FilterMode.Point };
                tex.SetPixels32(c.ToTexturePixels()); tex.Apply();
                floor = Sprite.Create(tex, new Rect(0, 0, 160, 160), Vector2.zero, 32); roomFloors[service] = floor;
            }
            var floorGo = new GameObject("InteriorFloor"); floorGo.transform.SetParent(objectsRoot, false);
            var renderer = floorGo.AddComponent<SpriteRenderer>(); renderer.sprite = floor; renderer.sortingOrder = -30000;
            // Thin side walls and back wall match the drawn footprint, leaving a central aisle to the keeper.
            RoomBlock("BackWall", new Vector2(2.5f, 4.65f), new Vector2(5, .7f));
            RoomBlock("LeftWall", new Vector2(.05f, 2.5f), new Vector2(.1f, 5));
            RoomBlock("RightWall", new Vector2(4.95f, 2.5f), new Vector2(.1f, 5));
            var counter = RoomProp("Counter", "town_bench", new Vector2(2.5f, 2.55f), 1.6f, new Vector2(1.5f, .4f));
            var counterArt = counter.transform.Find("Visual").GetComponent<SpriteRenderer>();
            counterArt.sprite = CounterSprite(); counterArt.transform.localScale = Vector3.one;
            counterArt.sortingOrder = YSort.OrderFor(2.55f);
            if (service == NpcService.Shop)
            {
                RoomProp("StockCrate", "town_crate", new Vector2(.65f, 3.65f), .85f, new Vector2(.8f, .5f));
                RoomProp("StockBarrel", "town_barrel", new Vector2(4.2f, 3.6f), .75f, new Vector2(.7f, .5f));
                RoomProp("FlowerPot", "town_pot", new Vector2(.7f, 1.2f), .55f, new Vector2(.4f, .3f));
                var redPotion = RoomProp("Potion", "icon_potion_hp", new Vector2(2, 3.22f), .28f, Vector2.zero);
                var bluePotion = RoomProp("PotionBlue", "icon_potion_mp", new Vector2(2.9f, 3.22f), .28f, Vector2.zero);
                foreach (var bottle in new[] { redPotion, bluePotion })
                    bottle.transform.Find("Visual").GetComponent<YSort>().Configure(true, 30);
            }
            else if (service == NpcService.Blacksmith)
            {
                RoomProp("Anvil", "town_anvil", new Vector2(.8f, 2.6f), .85f, new Vector2(.75f, .4f));
                var forge = RoomProp("Forge", "wnt_fire_0", new Vector2(.85f, 3.8f), .75f, new Vector2(.6f, .3f));
                WarmGlow.Attach(forge.transform, new Vector2(0, .3f), 1.1f, .36f);
                RoomProp("Woodpile", "town_woodpile", new Vector2(4.1f, 3.6f), 1, new Vector2(.8f, .5f));
                RoomProp("Materials", "town_crate", new Vector2(4.15f, 1.7f), .65f, new Vector2(.6f, .4f));
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    RoomProp("Supply" + i, "town_crate", new Vector2(.7f, 1.7f + i * .9f), .8f, new Vector2(.7f, .5f));
                    RoomProp("Barrel" + i, i == 0 ? "town_sacks" : "town_barrel", new Vector2(4.2f, 1.7f + i * .9f), .75f, new Vector2(.7f, .5f));
                }
            }
            var def = ServiceNpc(service)?.Clone();
            if (def != null)
            {
                def.behaviour = NpcBehaviour.Idle; def.tool = NpcTool.None; def.initialFacing = Facing.Down;
                var npc = NpcController.Create(def, new Vector2(2.5f, 3.35f), objectsRoot);
                npc.ConfigureShape(new Vector2(0, -.3f), .7f, new Vector2(0, 1.5f));
            }
            var exit = new Vector2(2.5f, .25f);
            MapPortal.Create(exit, map.exteriorMap, objectsRoot);
            portalCells[map.exteriorMap] = new List<Vector2> { exit };
            Decoration("arrow_down", new Vector2(2.5f, .48f), -20000);
            CreateBoundaryWalls(); PointsOfInterest.Add(PlayerSpawn);
            Game.Cutscenes?.OnWorldRebuilt(); BuildMinimap();
        }

        static Sprite counterSprite;
        static Sprite CounterSprite()
        {
            if (counterSprite != null) return counterSprite;
            var c = new PixelCanvas(52, 26); var ink = PixelCanvas.Hex("302936"); var gold = PixelCanvas.Hex("cda768");
            c.Rect(1, 4, 50, 22, ink); c.Rect(3, 11, 46, 12, PixelCanvas.Hex("79503a"));
            c.Rect(0, 0, 52, 11, ink); c.Rect(1, 1, 50, 7, PixelCanvas.Hex("bc8a54"));
            c.HLine(2, 49, 1, gold); c.HLine(1, 50, 9, PixelCanvas.Hex("674535"));
            for (int x = 4; x < 49; x += 16)
            { c.Rect(x, 13, 12, 8, ink); c.Rect(x+1, 14, 10, 6, PixelCanvas.Hex("916447")); c.Rect(x+5, 16, 3, 2, gold); }
            c.HLine(3, 48, 23, gold); c.VLine(13, 2, 7, PixelCanvas.Hex("9b6d43")); c.VLine(34, 2, 7, PixelCanvas.Hex("9b6d43"));
            var tex = new Texture2D(52, 26, TextureFormat.RGBA32, false) { name = "RoomCounter", filterMode = FilterMode.Point };
            tex.SetPixels32(c.ToTexturePixels()); tex.Apply();
            counterSprite = Sprite.Create(tex, new Rect(0, 0, 52, 26), new Vector2(.5f, 0), 32);
            return counterSprite;
        }

        void RoomBlock(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.transform.SetParent(objectsRoot, false); go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
        }
        GameObject RoomProp(string name, string key, Vector2 position, float width, Vector2 collision)
        {
            var go = StaticProp(name, key, position, Vector2.zero, Vector2.zero);
            var sr = go.GetComponent<SpriteRenderer>();
            // Scale only the graphic, keeping collision and foot anchors in world units.
            var visual = new GameObject("Visual").AddComponent<SpriteRenderer>(); visual.transform.SetParent(go.transform, false);
            visual.sprite = sr.sprite; visual.transform.localScale = Vector3.one * (width / Mathf.Max(.1f, sr.sprite.bounds.size.x));
            Destroy(sr); visual.gameObject.AddComponent<YSort>().Configure(true);
            if (collision != Vector2.zero) { var col = go.AddComponent<BoxCollider2D>(); col.size = collision; col.offset = new Vector2(0, collision.y * .5f); }
            return go;
        }
    }
}
