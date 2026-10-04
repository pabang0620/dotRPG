using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Round minimap in the top-right corner. The map picture (the map itself rendered from above by
    /// <see cref="WorldBuilder"/>) scrolls under a circular mask so the player stays in the centre;
    /// monsters are red dots, map exits small portal swirls, quest NPCs "!"/"?" and town services small icons, the last two
    /// pinned to the rim when they are off the minimap.
    /// </summary>
    public class MinimapView : MonoBehaviour
    {
        public const float Diameter = 176f;
        const float PixelsPerTile = 6f; // ≈29 tiles across the circle
        const float Radius = Diameter * 0.5f;

        RectTransform mapRect;
        RawImage map;
        RectTransform markers;
        Image playerDot;
        Text label;
        Image labelPlate; // [UI]
        Texture shownTexture;
        readonly List<Image> enemyDots = new List<Image>();
        readonly List<Image> exitDots = new List<Image>();
        readonly List<Image> serviceIcons = new List<Image>();
        readonly List<Text> questMarks = new List<Text>();
        static Sprite portalSprite;

        static readonly Color EnemyColor = new Color32(255, 80, 80, 255);
        static readonly Color PlayerColor = new Color32(120, 220, 255, 255);

        public static MinimapView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Minimap"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -16), new Vector2(Diameter, Diameter));
            var view = root.gameObject.AddComponent<MinimapView>();

            // Circular mask; the mask graphic itself is the dark background outside the map edges.
            var maskImage = UIFactory.Image(root, "Mask", Game.Art.Get("ui_circle"), new Color(0.12f, 0.1f, 0.09f, 0.92f));
            maskImage.preserveAspect = false;
            UIFactory.Stretch(maskImage.rectTransform, 4, 4, 4, 4);
            var mask = maskImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            view.map = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>();
            view.map.transform.SetParent(maskImage.transform, false);
            view.map.raycastTarget = false;
            view.mapRect = view.map.rectTransform;
            view.mapRect.anchorMin = view.mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            view.mapRect.pivot = new Vector2(0.5f, 0.5f);

            view.markers = UIFactory.Place(UIFactory.Rect(maskImage.transform, "Markers"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var ring = UIFactory.Image(root, "Frame", CompassFrame(), Color.white);
            ring.preserveAspect = false;
            UIFactory.Place(ring.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(192, 192));


            view.playerDot = view.Dot(root, PlayerColor, 12f);

            // [UI] Region name on a translucent plate (it used to sit straight on the map / terrain).
            view.labelPlate = UIFactory.Image(root, "NamePlate", Game.Art.Get("ui_dark"), Color.white);
            view.labelPlate.preserveAspect = false;
            UIFactory.Place(view.labelPlate.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(7f, -8f), new Vector2(190f, 30f));
            view.label = UIFactory.Text(root, "Name", "", UiTheme.FontCaption, UIColors.Cream, TextAnchor.MiddleCenter, true);
            UIFactory.Place(view.label.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -11f), new Vector2(176f, 24f));
            view.label.fontSize = 16;
            return view;
        }

        static Sprite compassFrame;
        static Sprite CompassFrame()
        {
            if (compassFrame != null) return compassFrame;
            const int size = 192;
            var c = new PixelCanvas(size, size);
            var dark = PixelCanvas.Hex("17222b"); var gold = PixelCanvas.Hex("bd9862"); var light = PixelCanvas.Hex("f4d8a0");
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = x + .5f - 96, dy = y + .5f - 96, r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < 83 || r > 94) continue;
                bool lit = dx + dy < -5;
                var color = r > 92 ? dark : r > 90 ? (lit ? light : gold) : r > 86 ? PixelCanvas.Hex("39434b")
                    : r > 84 ? (lit ? gold : PixelCanvas.Hex("765b40")) : PixelCanvas.Hex("84b3bb");
                c.Set(x, y, color);
            }
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16;
                int x = 96 + Mathf.RoundToInt(Mathf.Sin(a) * 88), y = 96 + Mathf.RoundToInt(Mathf.Cos(a) * 88);
                c.Rect(x - 1, y - 1, 2, 2, i % 4 == 0 ? light : gold);
            }
            foreach (var p in new[] { new Vector2Int(96, 8), new Vector2Int(96, 183), new Vector2Int(8, 96), new Vector2Int(183, 96) })
            {
                for (int y = -7; y <= 7; y++) for (int x = -7; x <= 7; x++)
                {
                    int d = Mathf.Abs(x) + Mathf.Abs(y);
                    if (d <= 7) c.Set(p.x + x, p.y + y, d == 7 ? dark : d >= 5 ? gold : PixelCanvas.Hex("203642"));
                }
                if (p.y != 8) { c.Rect(p.x - 1, p.y - 1, 3, 3, PixelCanvas.Hex("88d5df")); c.Set(p.x - 1, p.y - 1, light); }
            }
            c.VLine(94, 5, 11, light); c.VLine(98, 5, 11, light); c.Line(94, 5, 98, 11, light);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MinimapCompass", filterMode = FilterMode.Point };
            tex.SetPixels32(c.ToTexturePixels()); tex.Apply();
            compassFrame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
            return compassFrame;
        }

        /// <summary>A small swirling portal (violet rim, bright core) drawn once in code: no art asset exists for it.</summary>
        public static Sprite PortalSprite()
        {
            if (portalSprite != null) return portalSprite;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            Color rim = new Color32(150, 90, 255, 255), mid = new Color32(90, 170, 255, 255), core = new Color32(225, 245, 255, 255), edge = new Color32(25, 15, 45, 255);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // Upright oval like a gate, with a spiral band inside.
                    float dx = (x + 0.5f - S * 0.5f) / (S * 0.36f), dy = (y + 0.5f - S * 0.5f) / (S * 0.48f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = Color.clear;
                    if (d <= 1f)
                    {
                        float spiral = 0.5f + 0.5f * Mathf.Sin(Mathf.Atan2(dy, dx) * 2f + d * 9f);
                        c = d > 0.82f ? rim : Color.Lerp(core, Color.Lerp(mid, rim, spiral), Mathf.Clamp01(d * 1.3f));
                        if (d > 0.93f) c = edge;
                    }
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            portalSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return portalSprite;
        }

        Image Dot(Transform parent, Color color, float size)
        {
            var dot = UIFactory.Image(parent, "Dot", Game.Art.Get("ui_dot"), color);
            UIFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            return dot;
        }

        void LateUpdate()
        {
            var world = Game.World;
            var player = Game.Player;
            float pixelsPerTile = world != null && world.Map.IsInterior ? 28f : PixelsPerTile;
            if (world == null || world.Minimap == null || player == null) return;

            if (world.Minimap != shownTexture)
            {
                shownTexture = world.Minimap;
                map.texture = shownTexture;
                mapRect.sizeDelta = new Vector2(world.Bounds.width * pixelsPerTile, world.Bounds.height * pixelsPerTile);
                label.text = world.Map != null ? world.Map.displayName : "";
                float plateWidth = Mathf.Clamp(label.preferredWidth + 24f, 150f, 260f);
                labelPlate.rectTransform.sizeDelta = new Vector2(plateWidth, 30f);
                label.rectTransform.sizeDelta = new Vector2(plateWidth - 14f, 24f);
                labelPlate.enabled = label.text.Length > 0;
            }

            Vector2 p = player.Position;
            var b = world.Bounds;
            mapRect.anchoredPosition = new Vector2((b.width * 0.5f - p.x) * pixelsPerTile, (b.height * 0.5f - p.y) * pixelsPerTile);

            // Monsters inside the circle.
            int n = 0;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) continue;
                Vector2 offset = (enemy.Position - p) * pixelsPerTile;
                if (offset.magnitude > Radius - 10f) continue;
                if (n >= enemyDots.Count) enemyDots.Add(Dot(markers, EnemyColor, 9f));
                var dot = enemyDots[n++];
                dot.gameObject.SetActive(true);
                dot.rectTransform.anchoredPosition = offset;
            }
            for (int i = n; i < enemyDots.Count; i++) enemyDots[i].gameObject.SetActive(false);

            // Map exits: always visible, clamped to the rim so they point the way.
            int e = 0;
            foreach (var portal in world.PortalCenters)
            {
                Vector2 offset = (portal - p) * pixelsPerTile;
                if (offset.magnitude > Radius - 12f) offset = offset.normalized * (Radius - 12f);
                if (e >= exitDots.Count)
                {
                    var icon = UIFactory.Image(markers, "Portal", PortalSprite(), Color.white);
                    UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 20f));
                    exitDots.Add(icon);
                }
                var dot = exitDots[e++];
                dot.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.08f);
                dot.gameObject.SetActive(true);
                dot.rectTransform.anchoredPosition = offset;
            }
            for (int i = e; i < exitDots.Count; i++) exitDots[i].gameObject.SetActive(false);

            // Town services: potion (general store), anvil (blacksmith), chest (storage).
            int s = 0;
            void ServiceIcon(NpcService service, Vector2 position)
            {
                Vector2 offset = (position - p) * pixelsPerTile;
                if (offset.magnitude > Radius - 14f) offset = offset.normalized * (Radius - 14f);
                if (s >= serviceIcons.Count)
                {
                    var icon = UIFactory.SharpIcon(markers, "Service", Color.white);
                    UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
                    icon.gameObject.AddComponent<Outline>().effectColor = new Color(0.1f, 0.07f, 0.05f, 0.9f);
                    serviceIcons.Add(icon);
                }
                var img = serviceIcons[s++];
                img.gameObject.SetActive(true);
                img.sprite = Game.Art.Get(service == NpcService.Shop ? "icon_potion_hp" : service == NpcService.Blacksmith ? "icon_anvil"
                    : service == NpcService.Dungeon ? "menuicon_dungeon" : "icon_chest"); // [DUNGEON] guide icon
                img.rectTransform.anchoredPosition = offset;
            }
            foreach (var npc in NpcController.Services)
                if (npc != null && npc.isActiveAndEnabled) ServiceIcon(npc.Definition.service, npc.transform.position);
            foreach (var it in Interactable.All)
                if (it is ServiceDoor door) ServiceIcon(door.Service, door.transform.position);
            for (int i = s; i < serviceIcons.Count; i++) serviceIcons[i].gameObject.SetActive(false);

            // Quest marks ("!" give, "?" report / talk): pinned to the rim when off the minimap.
            int q = 0;
            void QuestIcon(QuestMark kind, bool main, Vector2 position)
            {
                if (kind == QuestMark.None) return;
                Vector2 offset = (position - p) * pixelsPerTile;
                if (offset.magnitude > Radius - 14f) offset = offset.normalized * (Radius - 14f);
                if (q >= questMarks.Count)
                {
                    var t = UIFactory.Text(markers, "QuestMark", "!", 22, Color.white, TextAnchor.MiddleCenter, true);
                    UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 26f));
                    t.fontStyle = FontStyle.Bold;
                    t.raycastTarget = false;
                    t.gameObject.AddComponent<Outline>().effectColor = new Color(0.08f, 0.05f, 0.1f, 1f);
                    questMarks.Add(t);
                }
                var mark = questMarks[q++];
                mark.gameObject.SetActive(true);
                mark.text = kind == QuestMark.Available ? "!" : "?";
                mark.color = main ? new Color32(255, 214, 64, 255) : new Color32(120, 214, 255, 255);
                mark.rectTransform.anchoredPosition = offset;
                mark.transform.SetAsLastSibling();
            }
            foreach (var npc in NpcController.All)
                if (npc != null && npc.isActiveAndEnabled) QuestIcon(npc.ShownMark, npc.ShownMarkMain, npc.transform.position);
            if (Game.Quest != null)
                foreach (var it in Interactable.All)
                    if (it is ServiceDoor door)
                    {
                        string id = WorldBuilder.ServiceNpc(door.Service)?.npcId;
                        if (id != null) QuestIcon(Game.Quest.MarkFor(id), Game.Quest.MarkIsMain(id), door.transform.position);
                    }
            for (int i = q; i < questMarks.Count; i++) questMarks[i].gameObject.SetActive(false);

            playerDot.transform.SetAsLastSibling();
        }
    }
}
