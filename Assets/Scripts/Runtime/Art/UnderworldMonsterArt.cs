using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Production pose atlases, aligned at the feet and sampled onto a crisp native pixel grid.</summary>
    public static class UnderworldMonsterArt
    {
        public static readonly string[] Ids = { "hollow_scarab", "hollow_guard", "hollow_hexer" };
        public static readonly string[] Directions = { "down", "downside", "side", "upside", "up" };
        public static readonly string[] Frames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt" };
        public const int Width=128, Height=128;
        const int FeetY=116;
        static readonly Dictionary<string,Sprite[]> cache=new Dictionary<string,Sprite[]>();
        static readonly Dictionary<string,PixelCanvas[]> canvases=new Dictionary<string,PixelCanvas[]>();
        static Sprite bolt;
        [Serializable] sealed class Atlas { public float scale; public Cell[] cells; }
        [Serializable] sealed class Cell { public int x,y,width,height; public float anchorX,anchorY; }
        static Color32 C(string c)=>PixelCanvas.Hex(c);
        public static bool Supports(string id)=>Array.IndexOf(Ids,id)>=0;
        public static Sprite Get(string id,string direction,string frame)
        {
            if(!Supports(id))return null;
            if(!cache.TryGetValue(id,out var sprites))
            {
                var texture=new Texture2D(Width*8,Height*5,TextureFormat.RGBA32,false)
                {name=id+"_production_sheet",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var pixels=new Color32[Width*8*Height*5];
                for(int d=0;d<5;d++)for(int f=0;f<8;f++)
                {
                    var p=Draw(id,d,f).ToTexturePixels();
                    for(int y=0;y<Height;y++)Array.Copy(p,y*Width,pixels,((4-d)*Height+y)*Width*8+f*Width,Width);
                }
                texture.SetPixels32(pixels);texture.Apply();sprites=new Sprite[40];
                for(int d=0;d<5;d++)for(int f=0;f<8;f++)
                {
                    var sprite=Sprite.Create(texture,new Rect(f*Width,(4-d)*Height,Width,Height),
                        new Vector2(.5f,(Height-FeetY-1f)/Height),56,0,SpriteMeshType.FullRect);
                    sprite.name=id+"_"+Directions[d]+"_"+Frames[f];sprites[d*8+f]=sprite;
                }
                cache[id]=sprites;
            }
            return sprites[Math.Max(0,Array.IndexOf(Directions,direction))*8+Math.Max(0,Array.IndexOf(Frames,frame))];
        }
        public static PixelCanvas Draw(string id,int direction,int frame)
        {
            if(!canvases.TryGetValue(id,out var poses))canvases[id]=poses=LoadPoses(id);
            return poses[Mathf.Clamp(direction,0,4)*8+Mathf.Clamp(frame,0,7)];
        }
        static PixelCanvas[] LoadPoses(string id)
        {
            string path=Path.Combine(Application.streamingAssetsPath,"Underworld",id+".png");
            if(!File.Exists(path))throw new FileNotFoundException("Missing production underground monster atlas",path);
            var atlas=JsonUtility.FromJson<Atlas>(File.ReadAllText(Path.ChangeExtension(path,".json")));
            if(atlas?.cells==null||atlas.cells.Length!=20)throw new InvalidDataException("Expected five facings and four authored poses: "+id);
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!ImageConversion.LoadImage(source,File.ReadAllBytes(path),false))throw new InvalidDataException(path);
            var input=source.GetPixels32();int sourceWidth=source.width,sourceHeight=source.height;
            UnityEngine.Object.Destroy(source);
            var output=new PixelCanvas[40];
            // Passing poses return to the grounded neutral stance between alternating authored steps.
            int[] keys={0,0,1,0,2,0,3,0};
            for(int d=0;d<5;d++)for(int f=0;f<8;f++)
            {
                var cell=atlas.cells[d*4+keys[f]];var p=new PixelCanvas(Width,Height);
                for(int y=2;y<Height-2;y++)for(int x=2;x<Width-2;x++)
                {
                    float rise=Mathf.Clamp01((FeetY-y)/55f);
                    float bob=(f==1||f==3||f==5)?rise:0;
                    float recoil=f==7?2*rise:0;
                    float sy=(y-FeetY+bob)/atlas.scale+cell.anchorY;
                    float sx=(x-Width*.5f+recoil)/atlas.scale+cell.anchorX;
                    int px=Mathf.RoundToInt(sx),py=Mathf.RoundToInt(sy);
                    if(px<0||py<0||px>=cell.width||py>=cell.height)continue;
                    var color=input[(sourceHeight-1-cell.y-py)*sourceWidth+cell.x+px];
                    // Hard alpha and discrete colour steps keep generated material detail pixel-sharp.
                    if(color.a<160)continue;
                    color.a=255;color.r=Step(color.r);color.g=Step(color.g);color.b=Step(color.b);
                    p.Set(x,y,color);
                }
                output[d*8+f]=p;
            }
            return output;
        }
        static byte Step(byte value)=>(byte)Mathf.Clamp(Mathf.RoundToInt(value/8f)*8,0,255);
        public static void ExportSheets(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach(var id in Ids)File.WriteAllBytes(Path.Combine(folder,id+".png"),ImageConversion.EncodeToPNG(Get(id,"down","idle0").texture));
        }
        public static Sprite Projectile()
        {
            if(bolt!=null)return bolt;
            var p=new PixelCanvas(24,24);
            p.Ellipse(12,12,10,5,C("211b38"));p.Ellipse(13,12,7,3,C("6d3aa1"));
            p.Line(7,12,20,12,C("bb74df"));p.Line(13,11,19,12,C("eee1f5"));
            p.Line(9,8,14,9,C("73409e"));p.Line(8,16,13,15,C("73409e"));
            p.Set(4,9,C("ba69d9"));p.Set(6,18,C("73409e"));
            var t=new Texture2D(24,24,TextureFormat.RGBA32,false){name="hollow_hexer_bolt",filterMode=FilterMode.Point};
            t.SetPixels32(p.ToTexturePixels());t.Apply();
            return bolt=Sprite.Create(t,new Rect(0,0,24,24),Vector2.one*.5f,32,0,SpriteMeshType.FullRect);
        }
    }
}
