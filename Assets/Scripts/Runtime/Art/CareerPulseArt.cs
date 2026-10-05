using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Four independently drawn temporal cels per motif. Loaded once at native resolution.</summary>
    public static class CareerPulseArt
    {
        static readonly Dictionary<Career,Sprite[]> sheets=new Dictionary<Career,Sprite[]>();
        static Sprite shard,glint;
        [System.Serializable] sealed class Cel {public int row,frame,x,y,width,height;}
        [System.Serializable] sealed class Layout {public Cel[] rects;}
        public static Vector3 Scale(Career c,int row,Vector2 size)
        {var peak=Frame(c,row,2).bounds.size;return new Vector3(size.x/Mathf.Max(.001f,peak.x),size.y/Mathf.Max(.001f,peak.y),1);}
        public static Sprite Frame(Career career,int row,int frame)
        {
            if(career==Career.Fighter)return FighterReforgeArt.Frame(row,frame);
            if(!sheets.TryGetValue(career,out var sprites)){
                sprites=new Sprite[16];sheets[career]=sprites;
                string asset=career==Career.Fighter?"FighterConcept":career==Career.Guardian?"GuardianAoe":career.ToString();
                string file=Path.Combine(Application.streamingAssetsPath,"CareerRenewal",asset+".png");
                if(!File.Exists(file))return null;
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="CareerRenewal_"+asset,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(file),true)){Object.Destroy(texture);return null;}
                string layoutFile=Path.Combine(Application.streamingAssetsPath,"CareerRenewal",asset+".frames.json");
                var layout=File.Exists(layoutFile)?JsonUtility.FromJson<Layout>(File.ReadAllText(layoutFile)):null;
                if(layout?.rects!=null&&layout.rects.Length==16){
                    // The generated originals have irregular gutters. Exact native crop rectangles prevent adjacent cels bleeding.
                    foreach(var cel in layout.rects){
                        var s=Sprite.Create(texture,new Rect(cel.x,cel.y,cel.width,cel.height),Vector2.one*.5f,texture.width/4f,0,SpriteMeshType.FullRect);
                        s.name=career+"_"+cel.row+"_"+cel.frame;sprites[cel.row*4+cel.frame]=s;
                    }
                    return sprites[Mathf.Clamp(row,0,3)*4+Mathf.Clamp(frame,0,3)];
                }
                // Rounded proportional boundaries include the complete 1254px original, including the last two pixels.
                for(int y=0;y<4;y++)for(int x=0;x<4;x++){
                    int x0=Mathf.RoundToInt(x*texture.width/4f),x1=Mathf.RoundToInt((x+1)*texture.width/4f);
                    int y0=Mathf.RoundToInt((3-y)*texture.height/4f),y1=Mathf.RoundToInt((4-y)*texture.height/4f);
                    var s=Sprite.Create(texture,new Rect(x0,y0,x1-x0,y1-y0),Vector2.one*.5f,x1-x0,0,SpriteMeshType.FullRect);
                    s.name=career+"_"+y+"_"+x;sprites[y*4+x]=s;
                }
            }
            return sprites[Mathf.Clamp(row,0,3)*4+Mathf.Clamp(frame,0,3)];
        }
        public static int Row(CareerSkill s)
        {
            if(s.career==Career.Fighter)return FighterReforgeArt.Row(s);
            if(s.career==Career.Guardian)return s.id=="g_awake"?3:s.effect=="ward"||s.effect=="shield"?2:s.effect=="bash"||s.effect=="counter"||s.effect=="taunt"?1:0;
            if(s.career==Career.Arcanist)return s.effect=="fire"?0:s.effect=="ice"?1:s.effect=="storm"?2:3;
            return s.effect=="wings"?1:s.effect=="dawn"?3:s.effect=="hot"||s.effect=="cleanse"||s.effect=="bless"?2:0;
        }
        public static Sprite Shard
        {
            get{if(shard==null){var p=new PixelCanvas(24,12);p.Line(1,5,21,5,new Color32(153,184,214,255));p.Line(3,6,18,6,new Color32(255,250,231,255));p.Set(23,5,Color.white);shard=CareerArt.SpriteOf(p,"pulse_shard",24);}return shard;}
        }
        public static Sprite Glint
        {
            get{if(glint==null){var p=new PixelCanvas(24,24);p.Line(2,12,21,12,Color.white);p.Line(12,4,12,20,Color.white);p.Rect(10,10,5,5,Color.white);glint=CareerArt.SpriteOf(p,"pulse_glint",24);}return glint;}
        }
    }
}
