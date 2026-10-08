using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public partial class SilverWarriorBaker : AssetPostprocessor
    {
        const string Root = "Assets/Resources/SilverWarrior/";
        const string SourceRoot = "Assets/ArtSources/SilverWarrior/";
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) && !assetPath.StartsWith(SourceRoot)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.isReadable = true; t.alphaIsTransparency = true; t.mipmapEnabled = false;
            t.npotScale = TextureImporterNPOTScale.None; t.filterMode = FilterMode.Point;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = TextureWrapMode.Clamp; t.maxTextureSize = 4096;
        }
        sealed class Blob
        {
            public int id, count, x0 = int.MaxValue, y0 = int.MaxValue, x1, y1;
            public float X => (x0 + x1) * .5f;
            public float Y => (y0 + y1) * .5f;
        }
        static readonly Color32[] Palette = Array.ConvertAll(new[] {
            "#101116", "#23242a", "#36373e", "#4c5057", "#71797e", "#9aa7a9", "#c0cbc6", "#dce5d7", "#f3f5e9",
            "#846378", "#bb91a0", "#dfb6b1", "#f5d6c1", "#fff0d8",
            "#42242e", "#7c303c", "#b64751", "#dd6a70", "#f7adb0"
        }, hex => PixelCanvas.Hex(hex));
        static Color32 Quantize(Color32 p)
        {
            if (p.a < 128) return default;
            int best = int.MaxValue; Color32 result = default;
            foreach (var c in Palette)
            {
                int dr = p.r - c.r, dg = p.g - c.g, db = p.b - c.b;
                int d = dr * dr + dg * dg + db * db;
                if (d < best) { best = d; result = c; }
            }
            return result;
        }
        static List<Blob> Blobs(Color32[] pixels, int w, int h, out int[] labels, int minimum)
        {
            var mark = new int[pixels.Length]; labels = mark;
            var list = new List<Blob>(); var stack = new Stack<int>(); int id = 0;
            for (int k = 0; k < pixels.Length; k++)
            {
                if (mark[k] != 0 || pixels[k].a < 128) continue;
                var b = new Blob { id = ++id }; mark[k] = id; stack.Push(k);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    b.count++; b.x0 = Math.Min(b.x0, x); b.x1 = Math.Max(b.x1, x);
                    b.y0 = Math.Min(b.y0, y); b.y1 = Math.Max(b.y1, y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                        int j = yy * w + xx;
                        if (mark[j] != 0 || pixels[j].a < 128) continue;
                        mark[j] = id; stack.Push(j);
                    }
                }
                if (b.count >= minimum) list.Add(b);
            }
            return list;
        }
        static bool Skin(Color32 p) => p.a > 0 && p.r > 145 && p.r > p.g + 6 && p.g > p.b + 5;
        static List<Blob> SheetCells(Color32[] pixels, int w, int h, int cols, out int[] labels)
        {
            // Atlas cells, not flood fill: a transparent collar can separate a head from
            // its torso, and tall neighboring poses can touch at a single pixel.
            labels = new int[pixels.Length];
            var cells = new List<Blob>();
            var bands = new List<Vector2Int>();
            int begin = -1;
            for (int x = 0; x <= w; x++)
            {
                bool filled = false;
                if (x < w) for (int y = 0; y < h && !filled; y++) filled = pixels[y * w + x].a >= 128;
                if (filled && begin < 0) begin = x;
                if (!filled && begin >= 0) { if (x - begin > 16) bands.Add(new Vector2Int(begin, x)); begin = -1; }
            }
            if (bands.Count != cols) throw new Exception("Expected " + cols + " atlas columns, found " + bands.Count);
            int[] cuts = new int[6]; cuts[5] = h;
            for (int r = 1; r < 5; r++)
            {
                int target = r * h / 5, best = int.MaxValue, cut = target;
                for (int y = target - h / 28; y <= target + h / 28; y++)
                {
                    int count = 0;
                    for (int x = 0; x < w; x++) if (pixels[y * w + x].a >= 128) count++;
                    if (count < best || count == best && Mathf.Abs(y - target) < Mathf.Abs(cut - target)) { best = count; cut = y; }
                }
                cuts[r] = cut;
            }
            for (int row = 0; row < 5; row++) for (int col = 0; col < cols; col++)
            {
                var b = new Blob { id = row * cols + col + 1 };
                for (int y = cuts[4 - row]; y < cuts[5 - row]; y++)
                for (int x = bands[col].x; x < bands[col].y; x++)
                {
                    if (pixels[y * w + x].a < 128) continue;
                    labels[y * w + x] = b.id; b.count++;
                    b.x0 = Math.Min(b.x0, x); b.x1 = Math.Max(b.x1, x);
                    b.y0 = Math.Min(b.y0, y); b.y1 = Math.Max(b.y1, y);
                }
                if (b.count < 800) throw new Exception("Empty character atlas cell " + row + "/" + col);
                cells.Add(b);
            }
            return cells;
        }
        static SilverFrame Landmarks(PixelCanvas c, int row, int col, bool attack)
        {
            const int waist = 42;
            string direction = SilverWarriorArt.Directions[row];
            string clip = attack ? "attack" + col : SilverWarriorArt.BaseFrames[col];
            var pose = WarriorRightHandRig.Sample(SilverWarriorArt.FacingOf(direction), clip, waist);
            return new SilverFrame { row = row, column = col, direction = direction,
                clip = clip, hand = pose.rightHand, waist = waist };
        }
        static PixelCanvas AlignObi(PixelCanvas source)
        {
            int obi = 46, most = 0;
            for (int y = 35; y <= 52; y++)
            {
                int count = 0;
                for (int x = 22; x < 43; x++)
                { var p = source.Get(x, y); if (p.a > 0 && p.r > 185 && p.g > 185 && p.b > 170 && Mathf.Abs(p.r - p.g) < 25) count++; }
                if (count > most) { most = count; obi = y; }
            }
            if (most < 3) throw new Exception("Missing white obi landmark");
            var result = new PixelCanvas(64, 64);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int sy = y <= 42 ? Mathf.RoundToInt(14 + (y - 14) * (obi - 14f) / 28f) : obi + y - 42;
                result.Set(x, y, source.Get(x, sy));
            }
            return result;
        }

        static void BakeSheet(string name, int cols, List<SilverFrame> frames)
        {
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + name + ".png");
            if (source == null) throw new Exception("Missing source " + name);
            var px = source.GetPixels32(); int w = source.width, h = source.height;
            var blobs = SheetCells(px, w, h, cols, out var labels);
            if (blobs.Count != cols * 5) throw new Exception(name + ": expected " + cols * 5 + " silhouettes, found " + blobs.Count);
            blobs.Sort((a, b) => b.Y.CompareTo(a.Y));
            for (int r = 0; r < 5; r++) blobs.Sort(r * cols, cols, Comparer<Blob>.Create((a, b) => a.X.CompareTo(b.X)));
            var heights = new List<int>(); foreach (var b in blobs) heights.Add(b.y1 - b.y0 + 1);
            heights.Sort(); float scale = 44f / heights[heights.Count / 2];
            var sheet = new Texture2D(cols * 64, 5 * 64, TextureFormat.RGBA32, false);
            sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
            for (int n = 0; n < blobs.Count; n++)
            {
                var b = blobs[n]; var c = new PixelCanvas(64, 64);
                // Center the head silhouette, independent of hair color, horns and arm reach.
                float sum = 0; int count = 0;
                for (int y = b.y0 + (int)((b.y1 - b.y0) * .50f); y <= b.y0 + (int)((b.y1 - b.y0) * .78f); y++)
                {
                    int left = w, right = -1;
                    for (int x = b.x0; x <= b.x1; x++)
                        if (labels[y * w + x] == b.id) { left = Math.Min(left, x); right = Math.Max(right, x); }
                    if (right >= left) { sum += (left + right) * .5f; count++; }
                }
                float anchorX = count > 0 ? sum / count : b.X;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    int sx = Mathf.RoundToInt(anchorX + (x - 32) / scale);
                    int sy = Mathf.RoundToInt(b.y0 + (57 - y) / scale);
                    if (sx < b.x0 || sx > b.x1 || sy < b.y0 || sy > b.y1 || labels[sy * w + sx] != b.id) continue;
                    c.Set(x, y, Quantize(px[sy * w + sx]));
                }
                int row = n / cols, col = n % cols;
                c = AlignObi(c);
                frames.Add(Landmarks(c, row, col, name == "attack"));
                sheet.SetPixels(col * 64, (4 - row) * 64, 64, 64, Array.ConvertAll(c.ToTexturePixels(), p => (Color)p));
            }
            sheet.Apply(); File.WriteAllBytes(Root + name + ".png", sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
        }
        static readonly Color32[] SwordPalette = Array.ConvertAll(new[] {
            "#101116", "#23242a", "#36373e", "#515a67", "#788899", "#a6b7c7", "#d6e1e7", "#f5f3e9",
            "#4b2d17", "#785025", "#aa7231", "#d3994c", "#e6be70", "#f5d993", "#8a7d65", "#c1b695", "#e4d9b9",
            "#401b27", "#772b38", "#b3333f", "#e25e58", "#563168", "#81429a", "#b379c6"
        }, hex => PixelCanvas.Hex(hex));
        static void BakeKatanas()
        {
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "katanas.png");
            var px = source.GetPixels32(); int w = source.width, h = source.height;
            var blobs = Blobs(px, w, h, out var labels, 800);
            if (blobs.Count != 4) throw new Exception("Expected four katana silhouettes, got " + blobs.Count);
            blobs.Sort((a, b) => a.X.CompareTo(b.X));
            var atlas = new Texture2D(256, 64, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[256 * 64]);
            for (int n = 0; n < 4; n++)
            {
                var b = blobs[n]; float scale = 48f / (b.y1 - b.y0 + 1);
                float sum = 0; int count = 0;
                for (int y = b.y0; y < b.y0 + (b.y1 - b.y0) / 5; y++) for (int x = b.x0; x <= b.x1; x++)
                    if (labels[y * w + x] == b.id) { sum += x; count++; }
                float gripX = sum / count;
                var canvas = new PixelCanvas(64, 64);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    int sx = Mathf.RoundToInt(gripX + (x - 32) / (scale * 1.45f)), sy = Mathf.RoundToInt(b.y0 + (57 - y) / scale);
                    if (sx < b.x0 || sx > b.x1 || sy < b.y0 || sy > b.y1 || labels[sy * w + sx] != b.id || px[sy * w + sx].a < 128) continue;
                    var pixel = px[sy * w + sx]; int best = int.MaxValue; Color32 color = default;
                    foreach (var candidate in SwordPalette)
                    {
                        int dr = pixel.r - candidate.r, dg = pixel.g - candidate.g, db = pixel.b - candidate.b;
                        int distance = dr * dr + dg * dg + db * db;
                        if (distance < best) { best = distance; color = candidate; }
                    }
                    canvas.Set(x, y, color);
                }
                atlas.SetPixels(n * 64, 0, 64, 64, Array.ConvertAll(canvas.ToTexturePixels(), c => (Color)c));
            }
            atlas.Apply(); File.WriteAllBytes(Root + "katanas.png", atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
        }
        public static void BakeAndBuild()
        {
            var frames = new List<SilverFrame>();
            BakeSheet("body", 8, frames); BakeSheet("attack", 6, frames); BakeKatanas();
            File.WriteAllText(Root + "frames.json", JsonUtility.ToJson(new SilverAtlas { frames = frames.ToArray() }, true));
            AssetDatabase.Refresh(); SilverWarriorArt.ResetCache();
            Validate(); BuildScript.BuildWindows();
        }
    }
}
