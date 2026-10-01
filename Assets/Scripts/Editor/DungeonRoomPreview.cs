using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Renders every dungeon room map to Logs/dgn_rooms/&lt;room id&gt;.png in edit mode (no player build).
    /// Run with graphics (no -nographics):
    /// Unity.exe -batchmode -quit -projectPath &lt;WT&gt; -executeMethod DotRPG.EditorTools.DungeonRoomPreview.RenderAll
    /// The closed gate is placed on the '@' cells so its layering over the ground can be checked.
    /// </summary>
    public static class DungeonRoomPreview
    {
        const int PixelsPerTile = 32;

        [MenuItem("dotRPG/Render Dungeon Room Previews")]
        public static void RenderAll()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "dgn_rooms");
            Directory.CreateDirectory(folder);

            Game.Config = GameConfig.LoadOrDefault(null);
            Game.Art = new SpriteLibrary(Game.Config.pixelsPerUnit);
            var world = WorldBuilder.Create(Game.Config);
            Game.World = world;

            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = -50f;
            cam.farClipPlane = 50f;

            int count = 0;
            foreach (var room in MapRegistry.Rooms)
            {
                // Clear the previous map ourselves (WorldBuilder.Rebuild uses play-mode Destroy).
                for (int i = world.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(world.transform.GetChild(i).gameObject);
                world.Load(room.id);
                DungeonDoor.Create(world.DungeonDoorCells, world.ObjectsRoot);
                Render(cam, world.Bounds, Path.Combine(folder, room.id + ".png"));
                Debug.Log($"[DGNPREVIEW] {room.id} {world.Bounds.width}x{world.Bounds.height} doors={world.DungeonDoorCells.Count} spawns={world.DungeonSpawns.Count}");
                count++;
            }

            foreach (var leftover in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (leftover.name == "MinimapCamera") Object.DestroyImmediate(leftover.gameObject);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(world.gameObject);
            Debug.Log($"[DGNPREVIEW] rendered {count} rooms to {folder}");
        }

        static void Render(Camera cam, Rect region, string file)
        {
            int w = Mathf.RoundToInt(region.width * PixelsPerTile), h = Mathf.RoundToInt(region.height * PixelsPerTile);
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.aspect = region.width / region.height;
            cam.orthographicSize = region.height * 0.5f;
            cam.transform.position = new Vector3(region.center.x, region.center.y, -10f);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
