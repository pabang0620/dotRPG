using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        static Texture2D masonryJoinery;
        static readonly Dictionary<int,Sprite> masonryParts=new Dictionary<int,Sprite>();
        static readonly Dictionary<string,Sprite> fittedMasonry=new Dictionary<string,Sprite>();
        static void BuildMonumentFooting(Transform root,int variant,Vector2 at,UnderworldContour contour,float width)
        {
            string key="Monument contact shade "+variant;
            if(!fittedMasonry.TryGetValue(key,out var sprite))
            {
                int w=Mathf.CeilToInt((width+2)*32),h=80;var pixels=new Color32[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    float wx=at.x+(x+.5f-w*.5f)/32,wy=at.y-2+(y+.5f)/32;
                    if(!contour.Land(wx,wy))continue;
                    float dx=(wx-at.x)/(width*.52f),dy=(wy-at.y+.15f)/1.65f,r=dx*dx+dy*dy;
                    if(r<1)pixels[y*w+x]=new Color32(13,19,19,(byte)(r<.38f?62:r<.70f?38:18));
                }
                sprite=fittedMasonry[key]=Make(pixels,w,h,key,new Vector2(.5f,.8f));
            }
            Put(root,key,sprite,at,sprite.bounds.size.x,-27900);
        }
        static Sprite MasonryPart(int index)
        {
            if(masonryParts.TryGetValue(index,out var cached))return cached;
            if(masonryJoinery==null)masonryJoinery=DepthTexture("masonry-joinery.png");
            if(masonryJoinery==null)return null;
            // The authored sheet has tall upper piers and shorter lower wall pieces.
            int half=masonryJoinery.width/2,split=Mathf.RoundToInt(masonryJoinery.height*730f/1254);
            int ox=index%2*half,top=index<2?0:split,bottom=index<2?split:masonryJoinery.height;
            int l=ox+half,r=ox,b=masonryJoinery.height,t=0;var pixels=masonryJoinery.GetPixels32();
            for(int y=top;y<bottom;y++)for(int x=ox;x<ox+half;x++)
            {
                int yy=masonryJoinery.height-1-y;if(pixels[yy*masonryJoinery.width+x].a<128)continue;
                l=Math.Min(l,x);r=Math.Max(r,x);b=Math.Min(b,yy);t=Math.Max(t,yy);
            }
            var sprite=Sprite.Create(masonryJoinery,new Rect(l,b,r-l+1,t-b+1),new Vector2(.5f,0),32,0,SpriteMeshType.FullRect);
            return masonryParts[index]=sprite;
        }
        static Sprite FittedMasonry(Sprite source,string key,Vector2 anchor,float width,bool hangs,UnderworldContour contour,Color tint,bool clipToRock=true)
        {
            if(fittedMasonry.TryGetValue(key,out var cached))return cached;
            int w=Mathf.RoundToInt(width*32),h=Mathf.RoundToInt(width*32*source.rect.height/source.rect.width);
            var pixels=new Color32[w*h];var input=source.texture.GetPixels32();var rect=source.rect;
            // Rasterize only these new modules to the same 32 pixels per world unit as the terrain.
            // Clamp them to existing blocked rock so a decorative support never covers a real path.
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float wx=anchor.x+(x+.5f-w*.5f)/32,wy=anchor.y+(y+.5f-(hangs?h:0))/32;
                if(clipToRock&&(contour.Land(wx,wy)||contour.Water(wx,wy)))continue;
                int sx=(int)(rect.x+(x+.5f)*rect.width/w),sy=(int)(rect.y+(y+.5f)*rect.height/h);
                var c=input[sy*source.texture.width+sx];if(c.a<128)continue;
                float depth=hangs?Mathf.Lerp(.40f,.94f,y/(float)h):.85f;
                c.r=(byte)(c.r*tint.r*depth);c.g=(byte)(c.g*tint.g*depth);c.b=(byte)(c.b*tint.b*depth);c.a=255;
                pixels[y*w+x]=c;
            }
            return fittedMasonry[key]=Make(pixels,w,h,key,new Vector2(.5f,hangs?1:0));
        }
        static Vector2[] InteriorMasonrySites(int variant)
        {
            // These bases sit on the two authored rock islands in UnderworldLayouts.
            // Their upper silhouettes use normal Y sorting instead of flattening the blocked islands.
            Vector2[][] centers={
                new[]{new Vector2(25,28),new Vector2(35,39)},
                new[]{new Vector2(14,31),new Vector2(41,29)},
                new[]{new Vector2(27,13),new Vector2(10,26)},
                new[]{new Vector2(40,17),new Vector2(15,33)}};
            return new[]{centers[variant][0]+new Vector2(.5f,-.5f),centers[variant][1]+new Vector2(.5f,-.5f)};
        }
        static bool NearInteriorMasonry(int variant,Vector2 at)
        {
            foreach(var site in InteriorMasonrySites(variant))if(Vector2.Distance(at,site)<3.8f)return true;
            return false;
        }
        static void BuildStructuralReturns(Transform root,int variant,char[,] cells,UnderworldContour contour,int w,int h,Vector2 primary)
        {
            var sites=new List<Vector2>();var candidates=new List<Vector2>();
            for(int y=8;y<h-4;y++)for(int x=4;x<w-4;x++)
            {
                if(cells[x,y]=='W'||cells[x,y]=='~'||cells[x,y-1]!='W')continue;
                float boundary=y+.5f;
                for(int n=0;n<16;n++){boundary-=.0625f;if(!contour.Land(x+.5f,boundary))break;}
                var at=new Vector2(x+.5f,boundary+.13f);bool safe=true;
                for(float dy=.7f;dy<6;dy+=.6f)for(float dx=-1.1f;dx<=1.1f;dx+=.55f)
                    if(contour.Land(at.x+dx,at.y-dy)||contour.Water(at.x+dx,at.y-dy))safe=false;
                if(safe)candidates.Add(at);
            }
            // Asymmetrical retained supports link the near ledge to the distant ruin silhouette.
            candidates.Sort((a,b)=>Hash((int)a.x+variant*7,(int)a.y).CompareTo(Hash((int)b.x+variant*7,(int)b.y)));
            Color[] tints={new Color(.68f,.64f,.54f),new Color(.55f,.62f,.49f),new Color(.47f,.63f,.64f),new Color(.52f,.56f,.69f)};
            int island=0;
            foreach(var site in InteriorMasonrySites(variant))
            {
                if(contour.Land(site.x,site.y)||contour.Water(site.x,site.y))continue;
                var art=MasonryPart(3);if(art==null)continue;
                string key="Interior column remnant "+variant+" "+island++;
                var fitted=FittedMasonry(art,key,site,3.1f,false,contour,tints[variant]*1.3f,false);
                var sr=Put(root,key,fitted,site,fitted.bounds.size.x,YSort.OrderFor(site.y));
                if(sr!=null)TreeFade.Attach(sr.gameObject);
            }
            foreach(var site in candidates)
            {
                if(sites.Count>=3)break;if(sites.Exists(p=>Vector2.Distance(p,site)<10))continue;
                int index=sites.Count%2;var art=MasonryPart(index);if(art==null)continue;
                string key="Cave underpinning "+variant+" "+sites.Count;
                var fitted=FittedMasonry(art,key,site,3.15f,true,contour,tints[variant]);
                Put(root,key,fitted,site,fitted.bounds.size.x,-29990);sites.Add(site);
            }
            // Two low masonry returns continue the primary structure onto its existing shelf.
            for(int n=0;n<2;n++)
            {
                var desired=primary+new Vector2(n==0?-7:8,n==0?-4:1);
                var at=DepthSite(cells,w,h,contour,desired,3.8f,3.5f);
                if(Vector2.Distance(at,primary)<5||sites.Exists(p=>Vector2.Distance(p,at)<4))continue;
                var art=MasonryPart(2+n);if(art==null)continue;
                string key="Cave masonry return "+variant+" "+n;
                var fitted=FittedMasonry(art,key,at,3.8f,false,contour,tints[variant]*1.2f);
                var sr=Put(root,key,fitted,at,fitted.bounds.size.x,YSort.OrderFor(at.y));
                if(sr!=null)TreeFade.Attach(sr.gameObject);sites.Add(at);
            }
        }
    }
}
