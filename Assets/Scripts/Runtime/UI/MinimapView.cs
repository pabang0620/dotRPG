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

            var ring = UIFactory.Image(root, "Frame", Game.Art.Get("ui_ring"), Color.white);
            ring.preserveAspect = false;
            UIFactory.Stretch(ring.rectTransform);

            view.playerDot = view.Dot(root, PlayerColor, 12f);

            // [UI] Region name on a translucent plate (it used to sit straight on the map / terrain).
            view.labelPlate = UIFactory.Image(root, "NamePlate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            view.labelPlate.preserveAspect = false;
            UIFactory.Place(view.labelPlate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(120f, 24f));
            view.label = UIFactory.Text(root, "Name", "", UiTheme.FontCaption, UIColors.Cream, TextAnchor.MiddleCenter, true);
            UIFactory.Place(view.label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(260f, 24f));
            return view;
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
            if (world == null || world.Minimap == null || player == null) return;

            if (world.Minimap != shownTexture)
            {
                shownTexture = world.Minimap;
                map.texture = shownTexture;
                mapRect.sizeDelta = new Vector2(world.Bounds.width * PixelsPerTile, world.Bounds.height * PixelsPerTile);
                label.text = world.Map != null ? world.Map.displayName : "";
                labelPlate.rectTransform.sizeDelta = new Vector2(Mathf.Min(260f, label.preferredWidth + 20f), 24f);
                labelPlate.enabled = label.text.Length > 0;
            }

            Vector2 p = player.Position;
            var b = world.Bounds;
            mapRect.anchoredPosition = new Vector2((b.width * 0.5f - p.x) * PixelsPerTile, (b.height * 0.5f - p.y) * PixelsPerTile);

            // Monsters inside the circle.
            int n = 0;
            foreach (var enemy in EnemyController.Active)
            {
                if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) continue;
                Vector2 offset = (enemy.Position - p) * PixelsPerTile;
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
                Vector2 offset = (portal - p) * PixelsPerTile;
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
            foreach (var npc in NpcController.Services)
            {
                if (npc == null || !npc.isActiveAndEnabled) continue;
                Vector2 offset = ((Vector2)npc.transform.position - p) * PixelsPerTile;
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
                var service = npc.Definition.service;
                img.sprite = Game.Art.Get(service == NpcService.Shop ? "icon_potion_hp" : service == NpcService.Blacksmith ? "icon_anvil"
                    : service == NpcService.Dungeon ? "menuicon_dungeon" : "icon_chest"); // [DUNGEON] guide icon
                img.rectTransform.anchoredPosition = offset;
            }
            for (int i = s; i < serviceIcons.Count; i++) serviceIcons[i].gameObject.SetActive(false);

            // Quest marks ("!" give, "?" report / talk): pinned to the rim when off the minimap.
            int q = 0;
            foreach (var npc in NpcController.All)
            {
                if (npc == null || !npc.isActiveAndEnabled || npc.ShownMark == QuestMark.None) continue;
                Vector2 offset = ((Vector2)npc.transform.position - p) * PixelsPerTile;
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
                mark.text = npc.ShownMark == QuestMark.Available ? "!" : "?";
                mark.color = npc.ShownMarkMain ? new Color32(255, 214, 64, 255) : new Color32(120, 214, 255, 255);
                mark.rectTransform.anchoredPosition = offset;
                mark.transform.SetAsLastSibling();
            }
            for (int i = q; i < questMarks.Count; i++) questMarks[i].gameObject.SetActive(false);

            playerDot.transform.SetAsLastSibling();
        }
    }
}
