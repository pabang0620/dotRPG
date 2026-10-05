using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    // Two original four-by-four atlases: eight motifs, four authored temporal cels each.
    public static class FighterReforgeArt
    {
        [System.Serializable] sealed class Cel {public int row,frame,x,y,width,height;}
        [System.Serializable] sealed class Layout {public Cel[] rects;}
        static readonly Sprite[] frames=new Sprite[32];
        static readonly bool[] loaded=new bool[2];
        static readonly Dictionary<string,Sprite> icons=new Dictionary<string,Sprite>();
        public static int Row(CareerSkill s)=>new[]{7,0,1,2,7,3,4,5,6}[s.index];
        public static Color Tint(int row)=>row==0||row==3?new Color32(75,193,255,255):row==2||row==6?new Color32(219,113,245,255):new Color32(255,192,74,255);
        public static Sprite Frame(int row,int frame)
        {
            row=Mathf.Clamp(row,0,7);int part=row/4;
            if(!loaded[part])Load(part);
            return frames[row*4+Mathf.Clamp(frame,0,3)];
        }
        public static Sprite Icon(CareerSkill s){Frame(Row(s),2);return icons.TryGetValue(s.id,out var icon)?icon:null;}
        static void Load(int part)
        {
            loaded[part]=true;string file=Path.Combine(Application.streamingAssetsPath,"CareerRenewal",part==0?"FighterReforgeA":"FighterReforgeB");
            if(!File.Exists(file+".png")||!File.Exists(file+".frames.json")){Debug.LogError("Missing Fighter reforge atlas: "+file);return;}
            var t=new Texture2D(2,2,TextureFormat.RGBA32,false){name="FighterReforge"+part,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            ImageConversion.LoadImage(t,File.ReadAllBytes(file+".png"),false);
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(file+".frames.json"));
            foreach(var cel in layout.rects){int row=cel.row+part*4;var s=Sprite.Create(t,new Rect(cel.x,cel.y,cel.width,cel.height),Vector2.one*.5f,t.width/4f,0,SpriteMeshType.FullRect);s.name="Fighter_"+row+"_"+cel.frame;frames[row*4+cel.frame]=s;}
            var pixels=t.GetPixels32();
            foreach(var s in CareerCatalog.For(Career.Fighter))if(Row(s)/4==part){
                var rect=frames[Row(s)*4+2].rect;var p=new PixelCanvas(64,64);Color32 c=Tint(Row(s));
                p.Rect(1,1,62,62,new Color32(13,19,35,255));p.Rect(3,3,58,58,PixelCanvas.Shade(c,.65f));p.Rect(5,5,54,54,new Color32(19,27,44,255));
                float scale=48/Mathf.Max(rect.width,rect.height);int w=Mathf.RoundToInt(rect.width*scale),h=Mathf.RoundToInt(rect.height*scale);
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                    var fg=pixels[((int)rect.y+Mathf.Min((int)rect.height-1,(int)((h-1-y)/scale)))*t.width+(int)rect.x+Mathf.Min((int)rect.width-1,(int)(x/scale))];
                    if(fg.a<32)continue;p.Set(32-w/2+x,31-h/2+y,fg);
                }
                for(int i=0;i<(s.kind==CareerSkillKind.Awakening?5:s.tier+1);i++)p.Rect(20+i*5,56,3,3,c);
                if(s.kind==CareerSkillKind.Passive){for(int i=0;i<(s.index==0?3:1);i++)p.Rect(9+i*5,9,3,5,c);}
                icons[s.id]=CareerArt.SpriteOf(p,s.Icon,64);
            }
            t.Apply(false,true);
        }
    }
}
