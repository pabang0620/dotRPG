using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Original cartographic pixel art. Two cached charts and five small symbols; no live world rendering.</summary>
    public static class WorldAtlasArt
    {
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Color32 C(string hex) => PixelCanvas.Hex(hex);

        public static Sprite Chart(WorldLayer layer)
        {
            string key = "chart_" + layer;
            if (sprites.TryGetValue(key, out var ready)) return ready;
            string path = Path.Combine(Application.streamingAssetsPath, "WorldAtlas", layer == WorldLayer.Underground ? "underground.png" : "surface.png");
            if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true))
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                    sprite.name = "IllustratedAtlas_" + layer; sprites[key] = sprite; return sprite;
                }
                Object.Destroy(texture);
            }
            return Store(key, layer == WorldLayer.Underground ? Underground() : Surface());
        }

        static Sprite Store(string key, PixelCanvas c)
        {
            var texture = new Texture2D(c.Width, c.Height, TextureFormat.RGBA32, false)
            { name = "WorldAtlas_" + key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(c.ToTexturePixels()); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, c.Width, c.Height), new Vector2(.5f, .5f), 16);
            sprite.name = texture.name; sprites[key] = sprite; return sprite;
        }

        static PixelCanvas Surface()
        {
            var c = new PixelCanvas(370, 208);
            for (int y = 0; y < c.Height; y++) for (int x = 0; x < c.Width; x++)
            {
                float nx = (x - 185f) / 167f, ny = (y - 105f) / 87f;
                float angle = Mathf.Atan2(ny, nx);
                float shore = Mathf.Sqrt(nx * nx + ny * ny) + .025f * Mathf.Sin(angle * 11) + .019f * Mathf.Cos(angle * 17);
                string color = shore > 1.09f ? "162c33" : shore > 1.04f ? "214048" : shore > 1 ? "355356" : "415347";
                if (shore < 1)
                {
                    // Four geographically distinct districts meet around the central root spring.
                    if (x > 190 && y < 112) color = shore > .94f ? "486454" : "4c6551";
                    if (x > 190 && y >= 112) color = shore > .94f ? "725f49" : "655742";
                    if (x <= 190 && y > 112) color = shore > .94f ? "708381" : "607773";
                    if (x <= 190 && y <= 112) color = shore > .94f ? "496b62" : "38584e";
                    float contour = Mathf.Sin(x * .06f + Mathf.Sin(y * .08f) * 2) + Mathf.Cos(y * .12f + x * .018f);
                    if (contour > 1.45f) c.Set(x, y, PixelCanvas.Shade(C(color), 1.07f));
                    else c.Set(x, y, C(color));
                }
                else c.Set(x, y, C(color));
            }
            // Engraver's contours, a winding watercourse, and a reed-filled western ruin lake.
            for (int ring = 0; ring < 3; ring++) Ring(c, 185, 105, 174 + ring * 10, 91 + ring * 7, C("2a4147"), 2);
            var stream = new[] { new Vector2(166, 18), new Vector2(174, 56), new Vector2(183, 88), new Vector2(178, 112), new Vector2(196, 143), new Vector2(190, 190) };
            Stroke(c, stream, 5, C("2b4d53")); Stroke(c, stream, 2, C("456a6b"));
            c.Ellipse(88, 57, 37, 24, C("2e4648")); c.Ellipse(88, 55, 31, 21, C("3b615b"));
            for (int n = 0; n < 7; n++) { int x = 63 + n * 8, y = 50 + (n % 3) * 7; c.HLine(x, x + 6, y, C("53796e")); }
            for (int n = 0; n < 13; n++) Tree(c, 230 + (n * 19) % 87, 36 + (n * 13) % 60, false);
            for (int n = 0; n < 7; n++) Mountain(c, 280 + (n * 19) % 52, 131 + (n * 13) % 42, false);
            for (int n = 0; n < 7; n++) Mountain(c, 85 + (n * 23) % 86, 137 + (n * 13) % 33, true);
            for (int n = 0; n < 6; n++) Tree(c, 49 + n * 17, 127 + (n % 2) * 13, true);
            Ruin(c, 75, 47); Ruin(c, 114, 68); Ruin(c, 47, 83);
            // Central great tree and root well remain understated beneath the gateway symbol.
            c.Ellipse(185, 104, 36, 26, C("31443d")); c.Ellipse(183, 103, 29, 21, C("53664c"));
            c.Ellipse(185, 103, 17, 10, C("244043")); c.Ellipse(185, 102, 11, 6, C("648b7a"));
            for (int n = 0; n < 6; n++)
            {
                float a = n * Mathf.PI / 3;
                c.Line(185, 103, 185 + Mathf.RoundToInt(Mathf.Cos(a) * 27), 103 + Mathf.RoundToInt(Mathf.Sin(a) * 18), C("837c51"));
            }
            Frame(c); return c;
        }

        static PixelCanvas Underground()
        {
            var c = new PixelCanvas(370, 208);
            var field = new float[c.Width * c.Height];
            for (int i = 0; i < field.Length; i++) field[i] = 99;
            // Overlapping, uneven lobes form continuous grotto silhouettes; walls belong to the
            // same contour as the tunnels, so no separate ellipse is pasted over a passage.
            void Chamber(float x, float y, float rx, float ry)
            {
                for (int yy = Mathf.Max(0, (int)(y - ry - 15)); yy < Mathf.Min(c.Height, y + ry + 16); yy++)
                    for (int xx = Mathf.Max(0, (int)(x - rx - 15)); xx < Mathf.Min(c.Width, x + rx + 16); xx++)
                    {
                        float nx = (xx - x) / rx, ny = (yy - y) / ry;
                        float d = (Mathf.Sqrt(nx * nx + ny * ny) - 1) * Mathf.Min(rx, ry);
                        field[yy * c.Width + xx] = Mathf.Min(field[yy * c.Width + xx], d);
                    }
            }
            void Passage(Vector2 a, Vector2 control, Vector2 b, float radius)
            {
                for (int n = 0; n <= 40; n++)
                {
                    float t = n / 40f;
                    Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
                    Chamber(p.x, p.y, radius, radius);
                }
            }
            Chamber(185, 55, 35, 19); Chamber(172, 49, 22, 15); Chamber(201, 60, 22, 15);
            Chamber(107, 107, 45, 25); Chamber(80, 109, 23, 17); Chamber(111, 91, 25, 16); Chamber(128, 117, 24, 14);
            Chamber(263, 108, 45, 26); Chamber(287, 99, 25, 19); Chamber(236, 116, 26, 19); Chamber(271, 126, 23, 13);
            Chamber(183, 162, 49, 22); Chamber(158, 158, 28, 20); Chamber(207, 164, 25, 17);
            Passage(new Vector2(185, 20), new Vector2(187, 37), new Vector2(185, 56), 8);
            Passage(new Vector2(185, 56), new Vector2(137, 82), new Vector2(107, 108), 9);
            Passage(new Vector2(185, 56), new Vector2(233, 82), new Vector2(263, 108), 10);
            Passage(new Vector2(107, 108), new Vector2(137, 135), new Vector2(185, 162), 9);
            Passage(new Vector2(263, 108), new Vector2(233, 135), new Vector2(185, 162), 9);
            for (int y = 0; y < c.Height; y++) for (int x = 0; x < c.Width; x++)
            {
                float d = field[y * c.Width + x];
                float stratum = y + 5 * Mathf.Sin(x * .027f) + 2 * Mathf.Sin(x * .067f + y * .018f);
                int seam = Mathf.FloorToInt(stratum) % 15;
                Color32 color;
                if (d < 0)
                {
                    color = C(y < 77 ? "665c48" : y > 139 ? "354d55" : x < 185 ? "4c5b45" : "355f64");
                    float planes = Mathf.Sin(x * .069f + y * .033f) + Mathf.Cos(y * .11f - x * .024f);
                    color = PixelCanvas.Shade(color, planes > 1.05f ? 1.1f : planes < -1.1f ? .88f : 1);
                    bool lip = y + 2 < c.Height && field[(y + 2) * c.Width + x] >= 0;
                    bool back = y >= 3 && field[(y - 3) * c.Width + x] >= 0;
                    if (back) color = PixelCanvas.Shade(color, .65f);
                    else if (lip) color = C(y > 140 ? "87918a" : "97927b");
                    else if (d > -2.5f) color = PixelCanvas.Shade(color, .85f);
                    if (d > -8 && d < -3 && seam == 0) color = PixelCanvas.Shade(color, .79f);
                }
                else
                {
                    // Thick downward-facing cut rock belongs to the floor above it, rather
                    // than tracing every chamber with an equally bright island outline.
                    int above = 0;
                    for (int drop = 1; drop <= 13 && y >= drop; drop++)
                        if (field[(y - drop) * c.Width + x] < 0) { above = drop; break; }
                    if (above > 0)
                    {
                        color = C(above < 3 ? "626857" : above < 7 ? "414d47" : above < 11 ? "2b3d3d" : "17282c");
                        if ((x + y / 5) % 14 < 2) color = PixelCanvas.Shade(color, .72f);
                        if (seam == 0) color = PixelCanvas.Shade(color, .76f);
                    }
                    else
                    {
                        color = C(y < 75 ? "292b2b" : y < 139 ? "202d31" : "15252e");
                        if (seam < 2) color = PixelCanvas.Shade(color, .75f);
                        else if (seam == 3) color = PixelCanvas.Shade(color, 1.12f);
                        if (d < 5) color = PixelCanvas.Shade(color, .63f);
                    }
                }
                c.Set(x, y, color);
            }
            // The cutaway has uninterrupted bedrock strata, deep fractures and a few fossils.
            // These are engraved into the rock mass; routes and location labels are drawn above.
            CavePoly(c, C("1c2529"), 13, 32, 68, 29, 99, 46, 82, 66, 36, 77, 12, 63);
            CavePoly(c, C("313634"), 14, 32, 68, 30, 87, 40, 40, 48, 14, 57);
            CavePoly(c, C("263036"), 275, 20, 343, 24, 357, 41, 332, 61, 304, 62, 282, 46);
            CavePoly(c, C("18272e"), 303, 145, 355, 128, 361, 175, 317, 191, 274, 181);
            CavePoly(c, C("24333a"), 308, 148, 354, 132, 354, 147, 324, 160, 294, 163);
            CavePoly(c, C("132127"), 12, 141, 58, 136, 98, 168, 83, 190, 22, 179);
            Stroke(c, new[] {new Vector2(53, 9),new Vector2(63, 22),new Vector2(59, 40),new Vector2(71, 52),new Vector2(63, 67)}, 1.5f, C("111f24"));
            Stroke(c, new[] {new Vector2(340, 70),new Vector2(327, 87),new Vector2(336, 104),new Vector2(327, 122)}, 1.2f, C("0f2129"));
            c.Line(62, 25, 75, 20, C("142226")); c.Line(331, 116, 346, 122, C("0f2129"));
            CaveFossil(c, 43, 111); CaveFossil(c, 317, 53);

            // B1: disused mine with a timber gallery, rail approach and a hand-operated hoist.
            for (int n = 0; n < 6; n++)
            {
                c.HLine(177, 193, 32 + n * 3, C(n % 2 == 0 ? "aaa58a" : "727862"));
                c.HLine(177, 193, 33 + n * 3, C("424c43"));
            }
            MineGallery(c, 156, 57); MineGallery(c, 211, 54);
            c.Line(154, 52, 172, 65, C("252b2c")); c.Line(158, 50, 176, 63, C("252b2c"));
            c.Line(154, 51, 172, 64, C("879194")); c.Line(158, 49, 176, 62, C("9ba09a"));
            for (int n = 0; n < 5; n++) c.Line(153+n*4, 50+n*3, 160+n*4, 48+n*3, C("6e5039"));
            c.Rect(204, 31, 3, 20, C("3a3029")); c.VLine(204, 31, 49, C("968165"));
            c.Rect(199, 31, 15, 3, C("74533d")); c.HLine(199, 213, 31, C("b19973"));
            c.VLine(212, 34, 44, C("898168")); c.Ellipse(212, 47, 5, 4, C("222b2d"));
            c.Rect(208, 45, 8, 4, C("626e6b")); c.HLine(208, 215, 45, C("a1a18a"));
            CaveLamp(c, 147, 44, false); CaveLamp(c, 224, 59, false);

            // B2 west: an ancient tree's roots grow through a broken burial gallery.
            RootTomb(c, 108, 98);
            // B2 east: large luminous caps frame a half-buried, leaking stone aqueduct.
            CaveAqueduct(c, 268, 104);
            // B3: a sunken seal monument with a narrow luminous seam, not a magic circle.
            CaveSeal(c, 186, 158);
            // Sparse attached mineral veins tie the layers together without filling the void.
            CrystalVein(c, 140, 131); CrystalVein(c, 224, 138); CrystalVein(c, 327, 175);
            Frame(c); return c;
        }

        static void CavePoly(PixelCanvas c, Color32 color, params int[] p)
        {
            int minX=c.Width,maxX=0,minY=c.Height,maxY=0;
            for(int i=0;i<p.Length;i+=2){minX=Mathf.Min(minX,p[i]);maxX=Mathf.Max(maxX,p[i]);minY=Mathf.Min(minY,p[i+1]);maxY=Mathf.Max(maxY,p[i+1]);}
            for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
            {
                bool inside=false;
                for(int i=0,j=p.Length-2;i<p.Length;j=i,i+=2)
                    if((p[i+1]>y+.5f)!=(p[j+1]>y+.5f)&&x+.5f<(p[j]-p[i])*(y+.5f-p[i+1])/(p[j+1]-p[i+1])+p[i])inside=!inside;
                if(inside)c.Set(x,y,color);
            }
        }

        static void CaveLamp(PixelCanvas c, int x, int y, bool blue)
        {
            c.Ellipse(x,y+3,6,3,C(blue?"436f6c":"7c6946"));
            c.Rect(x-2,y-4,5,8,C("1b2729"));c.Rect(x-1,y-3,3,5,C(blue?"75c5b8":"dabf77"));
            c.VLine(x,y-3,y,C(blue?"d3ede0":"f5e3b2"));c.HLine(x-2,x+2,y-5,C("707665"));
        }

        static void MineGallery(PixelCanvas c,int x,int y)
        {
            c.Ellipse(x+2,y+3,14,5,C("263332"));
            CavePoly(c,C("182629"),x-12,y+1,x-12,y-17,x-6,y-24,x+9,y-21,x+13,y-9,x+11,y+3);
            c.Rect(x-10,y-15,4,17,C("4a372b"));c.Rect(x+7,y-15,4,18,C("4a372b"));
            c.VLine(x-10,y-15,y,C("af956c"));c.VLine(x+7,y-15,y+1,C("9b825e"));
            c.Rect(x-13,y-18,27,5,C("42372d"));c.Rect(x-12,y-18,25,3,C("9d7d54"));
            c.HLine(x-12,x+11,y-19,C("baab80"));c.HLine(x-11,x+10,y-17,C("73523b"));
            c.Line(x-8,y-9,x-3,y-15,C("b39469"));c.Line(x+8,y-8,x+2,y-15,C("907249"));
            c.Rect(x-11,y-12,5,2,C("393e3b"));c.Set(x-9,y-12,C("bbc0a6"));
            c.Rect(x+6,y-11,5,2,C("393e3b"));c.Set(x+8,y-11,C("bbc0a6"));
            c.Rect(x-2,y-5,7,5,C("645947"));c.HLine(x-2,x+4,y-5,C("a28e69"));c.VLine(x,y-4,y-1,C("373c35"));
        }

        static void RootTomb(PixelCanvas c,int x,int y)
        {
            // Ruined gallery sits behind the tree; root arches then wrap its masonry.
            int gx=x+20,gy=y+4;
            c.Ellipse(gx+1,gy+6,15,5,C("27372c"));
            c.Rect(gx-10,gy-15,21,19,C("3b473b"));c.Rect(gx-8,gy-14,4,20,C("878972"));c.Rect(gx+5,gy-14,4,20,C("616f59"));
            c.Ellipse(gx,gy-10,9,9,C("8f927b"));c.Rect(gx-8,gy-10,17,7,C("8f927b"));
            c.Ellipse(gx,gy-8,5,7,C("1a2a26"));c.Rect(gx-5,gy-8,10,12,C("1a2a26"));
            c.HLine(gx-10,gx+10,gy-17,C("c0b795"));c.VLine(gx-8,gy-10,gy+3,C("aca68b"));
            c.Line(gx+3,gy-18,gx+1,gy-12,C("344336"));c.Rect(gx-11,gy+3,6,3,C("616d52"));
            var main=new[]{new Vector2(x-14,y-31),new Vector2(x-26,y-17),new Vector2(x-24,y-4),new Vector2(x-31,y+8),new Vector2(x-27,y+17)};
            Stroke(c,main,6,C("26322a"));Stroke(c,main,4,C("655e43"));
            Stroke(c,new[]{main[0]+new Vector2(-2,0),main[1]+new Vector2(-2,0),main[2]+new Vector2(-2,0),main[3]+new Vector2(-2,0)},1.3f,C("a09868"));
            Stroke(c,new[]{new Vector2(x-23,y-7),new Vector2(x-10,y-11),new Vector2(x+2,y-22),new Vector2(x+4,y-31)},3,C("36432f"));
            Stroke(c,new[]{new Vector2(x-24,y-9),new Vector2(x-11,y-13),new Vector2(x,y-23),new Vector2(x+3,y-31)},1.2f,C("8a8d5f"));
            Stroke(c,new[]{new Vector2(x-29,y+7),new Vector2(x-14,y+12),new Vector2(x-5,y+8),new Vector2(x+1,y+10)},2,C("7e7d50"));
            Stroke(c,new[]{new Vector2(x-25,y-3),new Vector2(x-38,y-10),new Vector2(x-41,y-21)},2,C("5c6745"));
            c.Line(x-39,y-14,x-46,y-16,C("8b9864"));c.Line(x-16,y+10,x-16,y+19,C("465439"));
            foreach(var p in new[]{new Vector2(x-17,y-26),new Vector2(x-33,y+5),new Vector2(x+21,y-20)})
            {c.Ellipse(p.x,p.y,6,3,C("465b35"));c.HLine((int)p.x-3,(int)p.x+2,(int)p.y-2,C("86995c"));}
            CaveLamp(c,x+23,y-4,true);
        }

        static void DeepMushroom(PixelCanvas c,int x,int y,int r,bool violet)
        {
            c.Ellipse(x+2,y+2,r,4,C("20353b"));
            CavePoly(c,C("273c41"),x-3,y,x-2,y-r-3,x+1,y-r-3,x+4,y);
            c.VLine(x-1,y-r-1,y,C(violet?"b0a7bb":"8cbbb1"));c.VLine(x+1,y-r+1,y,C("526f79"));
            c.Ellipse(x,y-r-3,r,r*.55f,C(violet?"354057":"215966"));
            c.Ellipse(x-1,y-r-5,r-1,r*.36f,C(violet?"746488":"3f949a"));
            c.HLine(x-r+2,x+r-2,y-r-2,C(violet?"b3a0bd":"9ccfc0"));
            for(int n=0;n<3;n++)c.Set(x-r/2+n*3,y-r-6+n%2,C(violet?"d8cad9":"b8e2cd"));
            c.Line(x-r+2,y-r-1,x-2,y-r+1,C("496777"));
        }

        static void CaveAqueduct(PixelCanvas c,int x,int y)
        {
            DeepMushroom(c,x+17,y-9,14,false);DeepMushroom(c,x-24,y-1,8,true);
            c.Ellipse(x+1,y+4,24,7,C("233d43"));
            // A connected series of old aqueduct arches, partly lost in collapsed stone.
            c.Rect(x-20,y-14,43,13,C("465957"));c.Rect(x-19,y-14,42,3,C("899584"));
            for(int a=-14;a<=14;a+=14)
            {
                c.Ellipse(x+a,y-4,5,6,C("18323a"));c.Rect(x+a-5,y-4,10,8,C("18323a"));
                c.VLine(x+a-6,y-8,y+4,C("819589"));c.VLine(x+a+5,y-7,y+4,C("566d67"));
                c.HLine(x+a-4,x+a+3,y-10,C("a8b3a0"));
            }
            c.HLine(x-20,x+22,y-15,C("bfbea0"));c.HLine(x-17,x+20,y-13,C("496c68"));
            c.VLine(x-3,y-12,y+2,C("659a97"));c.VLine(x-2,y-10,y+5,C("83b9b2"));
            c.Ellipse(x-2,y+6,6,2,C("6a9796"));c.HLine(x-5,x+1,y+5,C("aacabd"));
            CavePoly(c,C("637c73"),x+13,y+4,x+17,y-3,x+22,y-1,x+25,y+5);
            c.Line(x+17,y-3,x+21,y-2,C("a6b3a0"));
            DeepMushroom(c,x+30,y+18,11,false);DeepMushroom(c,x-27,y+22,6,false);
            c.Set(x+35,y-18,C("a5d4c4"));c.Set(x-26,y-14,C("679fa2"));
        }

        static void CaveSeal(PixelCanvas c,int x,int y)
        {
            c.Ellipse(x,y+13,41,11,C("152d39"));c.Ellipse(x,y+11,35,8,C("245264"));
            c.Ellipse(x-3,y+10,27,5,C("397084"));c.HLine(x-25,x-13,y+11,C("65979d"));c.HLine(x+12,x+27,y+9,C("5a8998"));
            c.Ellipse(x,y-1,27,7,C("142b34"));
            CavePoly(c,C("1b2d35"),x-25,y-3,x-23,y-24,x-14,y-35,x+11,y-35,x+23,y-25,x+25,y-3);
            c.Rect(x-20,y-25,40,25,C("4b6669"));
            CavePoly(c,C("78928e"),x-23,y-25,x-14,y-33,x+11,y-33,x+21,y-25);
            c.HLine(x-14,x+11,y-34,C("a3b3a0"));c.HLine(x-22,x+20,y-25,C("bac1a5"));
            c.Rect(x-24,y-23,8,23,C("536c69"));c.Rect(x+16,y-23,8,25,C("3b575b"));
            c.VLine(x-23,y-22,y-2,C("9cae9b"));c.VLine(x-19,y-20,y-1,C("7e958a"));
            c.VLine(x+17,y-21,y,C("829a90"));c.VLine(x+22,y-19,y,C("263e48"));
            c.Ellipse(x,y-17,13,12,C("102830"));c.Rect(x-13,y-17,26,18,C("102830"));
            CavePoly(c,C("35515b"),x-11,y-17,x-7,y-26,x+6,y-26,x+11,y-16,x+10,y,x-11,y);
            c.Line(x-8,y-24,x-1,y-15,C("6c878b"));c.Line(x-1,y-15,x-5,y-2,C("536f77"));
            c.VLine(x+1,y-24,y,C("69aaa4"));c.VLine(x+2,y-22,y-3,C("c1e0c5"));
            c.Line(x-1,y-15,x+6,y-12,C("97c5b6"));
            for(int n=0;n<3;n++)
            {c.HLine(x-23-n*3,x+23+n*3,y+n*3,C("7c9290"));c.HLine(x-23-n*3,x+23+n*3,y+1+n*3,C("334f57"));}
            CaveLamp(c,x-29,y-2,true);CaveLamp(c,x+29,y+2,true);
            // Carved relief fragments at the pediment read as a buried monument, not a door.
            c.Rect(x-6,y-32,3,3,C("324d51"));c.Rect(x+3,y-31,3,2,C("324d51"));
            c.Line(x+13,y-31,x+10,y-25,C("263e44"));c.Set(x-3,y-28,C("92b5a4"));
        }

        static void CrystalVein(PixelCanvas c,int x,int y)
        {
            CavePoly(c,C("19353b"),x-6,y+3,x-5,y-6,x-2,y-10,x+1,y-3,x+4,y-5,x+7,y+4);
            CavePoly(c,C("4b6c70"),x-4,y+1,x-3,y-8,x,y-2,x+1,y+3);
            c.Line(x-3,y-7,x-3,y,C("89b1a2"));c.Line(x+3,y-3,x+5,y+2,C("658c91"));
        }

        static void CaveFossil(PixelCanvas c,int x,int y)
        {
            for(int a=0;a<620;a+=12)
            {float r=a/620f*6;float t=a*Mathf.Deg2Rad;c.Set(x+Mathf.RoundToInt(Mathf.Cos(t)*r),y+Mathf.RoundToInt(Mathf.Sin(t)*r*.65f),C("44504b"));}
            c.Line(x-5,y+5,x+6,y+1,C("15242a"));
        }

        static void Stroke(PixelCanvas c, Vector2[] points, float width, Color32 color)
        {
            for (int n = 1; n < points.Length; n++)
            {
                int count = Mathf.CeilToInt(Vector2.Distance(points[n - 1], points[n]));
                for (int k = 0; k <= count; k++) { Vector2 p = Vector2.Lerp(points[n - 1], points[n], count == 0 ? 0 : k / (float)count); c.Circle(p.x, p.y, width, color); }
            }
        }
        static void Ring(PixelCanvas c, int x, int y, int rx, int ry, Color32 color, int skip = 1)
        {
            for (int a = 0; a < 360; a += skip)
            {
                float t = a * Mathf.Deg2Rad;
                c.Set(x + Mathf.RoundToInt(Mathf.Cos(t) * rx), y + Mathf.RoundToInt(Mathf.Sin(t) * ry), color);
            }
        }
        static void Tree(PixelCanvas c, int x, int y, bool snow)
        {
            c.Ellipse(x + 2, y + 5, 8, 4, C("34483e")); c.Rect(x - 1, y - 1, 2, 8, C("7c7351"));
            c.Ellipse(x, y - 4, 7, 7, C(snow ? "405a56" : "304d3e"));
            c.Ellipse(x - 1, y - 6, 5, 5, C(snow ? "94a89b" : "6c7b51")); c.HLine(x - 4, x, y - 7, C(snow ? "bcc5aa" : "809063"));
        }
        static void Mountain(PixelCanvas c, int x, int y, bool snow)
        {
            for (int row = 0; row < 14; row++)
            {
                c.HLine(x - row, x, y + row, C(snow ? "a0aaa0" : "8d7959")); c.HLine(x + 1, x + row, y + row, C(snow ? "526b6b" : "52483a"));
                if (row < 5) c.HLine(x - row, x + row / 2, y + row, C(snow ? "d0cbb5" : "bba47b"));
            }
        }
        static void Ruin(PixelCanvas c, int x, int y)
        {
            c.Ellipse(x + 2, y + 5, 10, 5, C("253e39"));
            c.Rect(x - 7, y - 9, 4, 15, C("829277")); c.Rect(x + 4, y - 9, 4, 15, C("687a68"));
            c.Rect(x - 9, y - 11, 19, 4, C("a3aa86")); c.HLine(x - 8, x + 8, y - 12, C("c3bc95"));
            c.Rect(x - 6, y - 4, 2, 8, C("586b58"));
        }
        static void Mushroom(PixelCanvas c, int x, int y)
        {
            c.Ellipse(x, y, 7, 3, C("264047")); c.Rect(x - 1, y - 8, 2, 10, C("6b9b97"));
            c.Ellipse(x, y - 8, 6, 4, C("4d9496")); c.HLine(x - 4, x + 2, y - 10, C("9fd1bd"));
        }
        static void Frame(PixelCanvas c)
        {
            for (int inset = 0; inset < 3; inset++)
            {
                var color = C(inset == 1 ? "a28f60" : "2e3734");
                c.HLine(inset, c.Width - 1 - inset, inset, color); c.HLine(inset, c.Width - 1 - inset, c.Height - 1 - inset, color);
                c.VLine(inset, inset, c.Height - 1 - inset, color); c.VLine(c.Width - 1 - inset, inset, c.Height - 1 - inset, color);
            }
            foreach (int x in new[] { 6, c.Width - 7 }) foreach (int y in new[] { 6, c.Height - 7 })
            { c.Rect(x - 2, y - 2, 5, 5, C("947d50")); c.Rect(x - 1, y - 1, 2, 2, C("d7c292")); }
        }

        public static Sprite Marker(string kind)
        {
            if (sprites.TryGetValue(kind, out var ready)) return ready;
            var c = new PixelCanvas(24, 24);
            if (kind == "halo")
            {
                for (int a = 0; a < 360; a++) { float r = a * Mathf.Deg2Rad; c.Set(12 + Mathf.RoundToInt(Mathf.Cos(r) * 10), 12 + Mathf.RoundToInt(Mathf.Sin(r) * 10), C("ffffff")); }
                c.Rect(10, 0, 4, 2, C("ffffff")); c.Rect(10, 22, 4, 2, C("ffffff")); return Store(kind, c);
            }
            c.Circle(12, 12, 11, C("17252b")); c.Circle(12, 11, 9, C(kind == "field" ? "806b47" : "b8985f")); c.Circle(12, 11, 7, C("2b4146"));
            if (kind == "town")
            {
                for (int y = 0; y < 6; y++) c.HLine(12 - y, 12 + y, 5 + y, C("e8d19a"));
                c.Rect(8, 10, 9, 7, C("c4ab7d")); c.Rect(11, 12, 3, 5, C("38473f")); c.HLine(6, 18, 10, C("f8e6b4"));
            }
            else if (kind == "gate")
            {
                c.Ellipse(12, 9, 6, 6, C("bfe1cd")); c.Rect(6, 9, 12, 9, C("bfe1cd")); c.Ellipse(12, 9, 4, 4, C("284145")); c.Rect(8, 9, 8, 9, C("284145"));
                for (int n = 0; n < 3; n++) c.HLine(8 - n, 15 + n, 14 + n * 2, C("91bfaa"));
            }
            else if (kind == "cave")
            {
                for (int y = 0; y < 10; y++) c.HLine(12 - y / 2 - 2, 12 + y / 2 + 2, 5 + y, C("809d99"));
                c.Ellipse(12, 13, 4, 5, C("172d38")); c.Rect(9, 13, 7, 5, C("172d38")); c.HLine(7, 18, 18, C("b5cfc0"));
            }
            return Store(kind, c);
        }
    }
}
