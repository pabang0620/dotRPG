using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Region-only presentation; network IDs, loot and combat definitions remain unchanged.</summary>
    public static class RegionalMonsterArt
    {
        public const string Rock = "cliff_rock", Yeti = "snow_yeti", Sanctum = "sanctum_sentinel";
        static readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();
        static readonly string[] directions = { "down", "downside", "side", "upside", "up" };
        static readonly string[] frames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt" };
        public static bool Supports(string id) => UnderworldMonsterArt.Supports(id) || id != null && (id == Rock || id == Yeti || id == Rock+"_guard" || id == Rock+"_thrower" || id == Yeti+"_guard" || id == Yeti+"_thrower" || id == Sanctum || id == Sanctum+"_guard" || id == Sanctum+"_thrower");
        public static string Species(string id) => id != null && id.StartsWith(Rock) ? Rock : id != null && id.StartsWith(Yeti) ? Yeti : id != null && id.StartsWith(Sanctum) ? Sanctum : null;
        public static string LookFor(string species, MonsterKind kind) => species + (kind == MonsterKind.ShieldGuard ? "_guard" : kind == MonsterKind.Archer ? "_thrower" : "");
        [Serializable] sealed class Atlas { public float pixelsPerUnit; public Cell[] cells; }
        [Serializable] sealed class Cell { public int x, y, width, height; public float pivotX, pivotY; }

        public static void ApplyFieldLook(MonsterDef def, string map)
        {
            var zone = HuntingGrounds.Get(map);
            if (zone == null || def.boss || def.raid || !Array.Exists(zone.monsters, id => id == def.id)) return;
            if(UnderworldMonsterArt.Supports(def.look?.id))return;
            string species = zone.theme == MapTheme.Canyon ? Rock : zone.theme == MapTheme.Winter ? Yeti : zone.theme == MapTheme.SanctumField ? Sanctum : null;
            if (species == null) return;
            def.look = new CharacterLook { id = LookFor(species,def.kind), body = BodyKind.Monster, hairStyle = HairStyle.Bald };
            string role = def.kind == MonsterKind.Archer ? " 투척병" : def.kind == MonsterKind.Charger ? " 돌격병" :
                def.kind == MonsterKind.ShieldGuard ? " 수호병" : def.kind == MonsterKind.Knight ? " 전사" : "";
            def.name = (species == Rock ? "암석 골렘" : species == Yeti ? "설산 예티" : "성소 석상") + role;
        }

        public static Sprite Get(string id, string direction, string frame)
        {
            if(UnderworldMonsterArt.Supports(id))return UnderworldMonsterArt.Get(id,direction,frame);
            if (!Supports(id)) return null;
            if (!sheets.TryGetValue(id, out var cells))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "RegionalMonsters", id + ".png");
                if (!File.Exists(path)) { Debug.LogError("Missing regional monster atlas: " + path); return null; }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = id, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)) { UnityEngine.Object.Destroy(texture); return null; }
                texture.filterMode = FilterMode.Point;
                var atlas = JsonUtility.FromJson<Atlas>(File.ReadAllText(Path.ChangeExtension(path, ".json")));
                cells = new Sprite[40];
                // Authored bounds isolate uneven source spacing; a common PPU preserves pose proportions.
                for (int row = 0; row < 5; row++) for (int col = 0; col < 8; col++)
                {
                    var cell = atlas.cells[row * 8 + col];
                    var sprite = Sprite.Create(texture, new Rect(cell.x, texture.height-cell.y-cell.height, cell.width, cell.height),
                        new Vector2(cell.pivotX, cell.pivotY), atlas.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = id + "_" + directions[row] + "_" + frames[col];
                    cells[row * 8 + col] = sprite;
                }
                sheets[id] = cells;
            }
            int d = Array.IndexOf(directions, direction), f = Array.IndexOf(frames, frame);
            return cells[Math.Max(0, d) * 8 + Math.Max(0, f)];
        }

        static readonly Dictionary<string, Sprite> projectiles = new Dictionary<string, Sprite>();
        public static Sprite Projectile(MonsterDef def)
        {
            if(def?.look?.id=="hollow_hexer")return UnderworldMonsterArt.Projectile();
            string id = Species(def?.look?.id);
            if (!Supports(id)) return null;
            if (projectiles.TryGetValue(id, out var sprite)) return sprite;
            // Small code-native pixel projectile; same gameplay radius and speed as the original shot.
            var p = new PixelCanvas(16, 16);
            Color32 dark = id == Rock ? new Color32(54,43,34,255) : new Color32(51,97,133,255);
            Color32 mid = id == Rock ? new Color32(135,108,73,255) : new Color32(164,207,222,255);
            Color32 light = id == Rock ? new Color32(197,164,111,255) : new Color32(244,251,248,255);
            for (int y=3;y<13;y++) for (int x=3;x<13;x++) {
                int dx=x-8,dy=y-8;if(dx*dx+dy*dy>25)continue;
                p.Set(x,y,dx*dx+dy*dy>15?dark:(x+y<15?light:mid));
            }
            if(id==Sanctum){dark=new Color32(20,61,58,255);mid=new Color32(66,171,149,255);light=new Color32(203,242,205,255);p.Rect(5,5,6,6,mid);p.Line(5,5,10,10,light);p.Line(10,5,5,10,light);}
            if(id==Rock){p.Line(6,5,9,8,dark);p.Line(9,8,8,11,dark);}
            sprite = SpriteOf(p, id + "_projectile", 24);
            sprite.name = id + "_projectile";
            return projectiles[id] = sprite;
        }

        static Sprite SpriteOf(PixelCanvas p, string name, float ppu)
        {
            var t = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(p.ToTexturePixels());
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, p.Width, p.Height), Vector2.one * .5f, ppu);
        }
    }
}
