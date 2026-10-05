using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Original RGBA art, loaded once. Cells stay at native resolution; no bilinear enlargement.</summary>
    public static class CareerPaintArt
    {
        static readonly Dictionary<Career,Sprite[]> sprites=new Dictionary<Career,Sprite[]>();
        static readonly Dictionary<Career,Color32[]> pixels=new Dictionary<Career,Color32[]>();
        public static Sprite Cell(Career career,int index)
        {
            if(!sprites.TryGetValue(career,out var sheet)){
                sheet=new Sprite[4];sprites[career]=sheet;
                string file=Path.Combine(Application.streamingAssetsPath,"CareerReborn",career+".png");
                if(!File.Exists(file)){Debug.LogWarning("Career artwork missing: "+file);return null;}
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="CareerReborn_"+career,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(file),false)){UnityEngine.Object.Destroy(texture);return null;}
                int w=texture.width/2,h=texture.height/2;
                for(int i=0;i<4;i++)sheet[i]=Sprite.Create(texture,new Rect(i%2*w,(1-i/2)*h,w,h),Vector2.one*.5f,w,0,SpriteMeshType.FullRect);
            }
            return sheet[Mathf.Clamp(index,0,3)];
        }
        public static int Motif(CareerSkill s)
        {
            if(s.career==Career.Fighter)return s.effect=="focus"||s.kind==CareerSkillKind.Passive?2:s.effect=="execute"||s.effect=="five"||s.effect=="rush"?1:s.effect=="flurry"?3:0;
            if(s.career==Career.Guardian)return s.effect=="ward"||s.effect=="citadel"?2:s.effect=="shield"?1:s.effect=="bash"||s.effect=="counter"?3:0;
            if(s.career==Career.Arcanist)return s.effect=="fire"?0:s.effect=="ice"?1:s.effect=="storm"?2:3;
            return s.effect=="wings"||s.effect=="dawn"?0:s.effect=="heal"?1:s.effect=="hot"||s.effect=="cleanse"?2:3;
        }
        public static PixelCanvas Icon(CareerSkill s)
        {
            var sprite=Cell(s.career,0);if(sprite==null)return CareerArt.Icon(s);
            if(!pixels.TryGetValue(s.career,out var rgba)){rgba=sprite.texture.GetPixels32();pixels[s.career]=rgba;}
            var p=new PixelCanvas(64,64);Color32 color=CareerCatalog.Color(s.career);
            p.Rect(1,1,62,62,new Color32(13,17,29,255));p.Rect(3,3,58,58,PixelCanvas.Shade(color,.75f));p.Rect(5,5,54,54,new Color32(20,25,43,255));
            int primary=Motif(s);float angle=s.career==Career.Fighter?(s.index==1?-30:s.index==7?28:0):0;
            PaintIcon(p,s.career,primary,32,30,48,angle,rgba);
            // A second contextual motif differentiates the skill from other uses of the same material.
            if(s.index==1&&s.career==Career.Fighter)PaintIcon(p,s.career,0,33,30,38,135,rgba);
            else if(s.index==3&&s.career==Career.Fighter){PaintIcon(p,s.career,0,18,27,28,-30,rgba);PaintIcon(p,s.career,0,43,33,28,30,rgba);}
            else if(s.index==8){PaintIcon(p,s.career,s.career==Career.Arcanist?0:s.career==Career.Bishop?3:2,46,45,27,0,rgba);}
            else if(s.index==0||s.index==4||s.index==2||s.index==6)PaintIcon(p,s.career,(primary+(s.index==2?2:1))%4,45,44,23,0,rgba);
            // Tier pips and the awakening crown remain legible even at the skill tree's smaller scale.
            for(int i=0;i<(s.index==8?5:s.index%4+1);i++){int x=24+i*5-(s.index==8?3:0);p.Rect(x,56,3,3,color);p.Set(x,56,new Color32(255,248,221,255));}
            if(s.kind==CareerSkillKind.Passive){p.Rect(8,8,5,5,color);p.Set(10,7,color);}
            return p;
        }
        static void PaintIcon(PixelCanvas p,Career career,int cell,int cx,int cy,int size,float angle,Color32[] rgba)
        {
            var s=Cell(career,cell);var rect=s.rect;int width=s.texture.width;
            float a=angle*Mathf.Deg2Rad,co=Mathf.Cos(a),si=Mathf.Sin(a);
            for(int y=5;y<55;y++)for(int x=5;x<59;x++){
                float dx=(x-cx)/(float)size,dy=(y-cy)/(float)size;
                float u=dx*co-dy*si+.5f,v=dx*si+dy*co+.5f;if(u<0||u>=1||v<0||v>=1)continue;
                var fg=rgba[((int)rect.y+(int)((1-v)*(rect.height-1)))*width+(int)rect.x+(int)(u*(rect.width-1))];
                if(fg.a==0)continue;var bg=p.Pixels[y*p.Width+x];float alpha=fg.a/255f;
                p.Set(x,y,new Color32((byte)Mathf.Lerp(bg.r,fg.r,alpha),(byte)Mathf.Lerp(bg.g,fg.g,alpha),(byte)Mathf.Lerp(bg.b,fg.b,alpha),255));
            }
        }
    }
}
