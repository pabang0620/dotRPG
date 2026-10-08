using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
namespace DotRPG {
 /// <summary>Deterministic, map-owned scenery. Solid-looking landmarks only occupy existing blocked ground.</summary>
 public static class HuntingScenery {
  static readonly Dictionary<string,Sprite> art=new Dictionary<string,Sprite>();
  static readonly Dictionary<string,Sprite> floors=new Dictionary<string,Sprite>();
  static int Hash(int x,int y)=>unchecked((x*73856093^y*19349663)&0x7fffffff);
  static Color32 C(int r,int g,int b)=>new Color32((byte)Mathf.Clamp(r,0,255),(byte)Mathf.Clamp(g,0,255),(byte)Mathf.Clamp(b,0,255),255);
  public static bool Solid(char c)=>c=='W'||c=='%';
  static Sprite Make(PixelCanvas p,string key,Vector2 pivot){var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false){name=key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.SetPixels32(p.ToTexturePixels());t.Apply();return Sprite.Create(t,new Rect(0,0,t.width,t.height),pivot,32,0,SpriteMeshType.FullRect);}
  public static Sprite Landmark(string key){
   if(art.TryGetValue(key,out var found))return found;string path=Path.Combine(Application.streamingAssetsPath,"HuntingScenery",key+".png");if(!StreamingFiles.Exists(path))return null;
   var t=new Texture2D(2,2,TextureFormat.RGBA32,false){name=key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};ImageConversion.LoadImage(t,StreamingFiles.ReadAllBytes(path));t.filterMode=FilterMode.Point;
   var px=t.GetPixels32();int l=t.width,b=t.height,rr=0,tt=0;for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)if(px[y*t.width+x].a>64){l=Math.Min(l,x);rr=Math.Max(rr,x);b=Math.Min(b,y);tt=Math.Max(tt,y);}
   return art[key]=Sprite.Create(t,new Rect(l,b,rr-l+1,tt-b+1),new Vector2(.5f,0),32,0,SpriteMeshType.FullRect);
  }
  static SpriteRenderer Put(Transform root,Sprite sprite,Vector2 foot,Vector2 size,int order,string name){if(sprite==null)return null;var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.position=foot;var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=order;go.transform.localScale=new Vector3(size.x/sprite.bounds.size.x,size.y/sprite.bounds.size.y,1);return sr;}
  public static void Dress(Transform root,char[,] a,int w,int h,HuntingZone z){
   if(z==null)return;bool snow=z.theme==MapTheme.Winter,forest=z.theme==MapTheme.Forest;
   var relic=Landmark(forest?"forest_relic":snow?"winter_relic":"canyon_relic");
   var used=new List<Vector2>();
   // Choose inward-facing solid shoulders. The foot and its full width are blocked; no new hidden collider.
   for(int y=h-7;y>=3;y--)for(int x=2;x<w-2;x++){
    if(used.Count>=3)continue;
    bool solid=true;for(int dx=-2;dx<=2;dx++)for(int dy=0;dy<2;dy++)if(!Solid(a[x+dx,y+dy]))solid=false;
    bool edge=false;for(int dx=-2;dx<=2;dx++)if(!Solid(a[x+dx,y-1])&&a[x+dx,y-1]!='~')edge=true;
    edge |= x<6||x>w-7;
    var pos=new Vector2(x+.5f,y+.1f);if(!solid||!edge||used.Exists(v=>Vector2.Distance(v,pos)<12))continue;
    var sr=Put(root,relic,pos,new Vector2(5,6.3f),YSort.OrderFor(pos.y),"Fantasy landmark "+z.id);if(sr!=null)TreeFade.Attach(sr.gameObject);used.Add(pos);
   }
   // Small bank clusters add rhythm without filling the battle floor with noisy details.
   var atmosphere=new GameObject("Quiet field atmosphere");atmosphere.transform.SetParent(root,false);atmosphere.AddComponent<HuntingAtmosphere>().Setup(a,w,h,z.theme);
   var crystal=Detail(snow?"ice":forest?"mushroom":"amber");int count=0;
   for(int y=3;y<h-3;y++)for(int x=3;x<w-3;x++){
    if(Hash(x+z.variant*11,y)%31!=0||count>=45)continue;
    bool bank=Solid(a[x,y])&&(a[x,y-1]=='.'||a[x,y-1]==','||a[x,y-1]=='~');if(!bank)continue;
    Put(root,crystal,new Vector2(x+.5f,y+.2f),new Vector2(.9f,1.1f),YSort.OrderFor(y+.2f),"Bank accent");count++;
   }
  }
  static Sprite Detail(string key){
   if(art.TryGetValue(key,out var s))return s;var p=new PixelCanvas(40,48);bool moss=key=="mushroom",ice=key=="ice";
   for(int k=0;k<3;k++){int x=9+k*11,y=35-k%2*7;
    if(moss){p.Rect(x-1,y-6,3,9,C(72,94,69));p.Ellipse(x,y-8,7,4,C(26,84,83));p.Ellipse(x-1,y-10,5,2,C(90,196,163));p.Set(x-3,y-11,C(200,231,180));}
    else {var dark=ice?C(43,77,116):C(104,55,44);var mid=ice?C(90,162,184):C(184,109,54);var light=ice?C(190,225,225):C(238,176,83);for(int yy=0;yy<22;yy++){int half=yy<7?yy/2:3;for(int xx=-half;xx<=half;xx++)p.Set(x+xx,y-22+yy,xx<0?light:xx<2?mid:dark);}p.Line(x,y-21,x,y-2,light);}
   }return art[key]=Make(p,key,new Vector2(.5f,.1f));
  }
  public static void BuildSanctum(Transform root,char[,] a,int w,int h,HuntingZone z){
   if(!floors.TryGetValue(z.id,out var terrain)){terrain=Paint(a,w,h,z.variant);floors[z.id]=terrain;}
   Put(root,terrain,Vector2.zero,new Vector2(w,h),-30000,"Layered sanctuary ground");
   void Building(string key,float x,float y,float ww,float hh,float shade=1){var sr=Put(root,SunkenSanctumArt.Asset(key),new Vector2(x,y),new Vector2(ww,hh),YSort.OrderFor(y),key);if(sr!=null){sr.color=new Color(shade,shade,shade);TreeFade.Attach(sr.gameObject);}}
   // The bases remain in the dark perimeter; only upper walls overlap the playable layer.
   Building("tower_left",2.5f,31,7,15,.72f);Building("tower_right",w-2.5f,31,7,15,.8f);
   Building("tower_left",2,7,8,17,.48f);Building("tower_right",w-2,6,8,16,.48f);
   if(z.variant==0||z.variant==3)Building("arch",z.variant==0?44:17,h-3,12,11,.9f);
   else {Building("tower_left",16,h-3,9,12,.86f);Building("tower_right",44,h-3,9,12,.9f);}
   if(z.variant==3)Put(root,SunkenSanctumArt.Asset("altar"),new Vector2(30,20),new Vector2(10,8),-27900,"Broken inner dais");
   for(int y=6;y<h-5;y+=5)for(int x=6;x<w-5;x+=4){
    if(a[x,y]!='~'||a[x,y-1]!='~'||Hash(x,y)%3!=0)continue;
    bool near=false;for(int dx=-2;dx<=2;dx++)for(int dy=-2;dy<=2;dy++)if(a[x+dx,y+dy]!='~'&&a[x+dx,y+dy]!='W')near=true;if(!near)continue;
    if(z.variant==1||z.variant==0){var sr=Put(root,SunkenSanctumArt.Asset("pillar"),new Vector2(x+.5f,y),new Vector2(1.1f,2.3f),YSort.OrderFor(y),"Submerged colonnade");if(sr!=null)TreeFade.Attach(sr.gameObject);}
    else Put(root,Detail("mushroom"),new Vector2(x,y),new Vector2(1.1f,1.3f),YSort.OrderFor(y),"Moss lit shore");
   }
   var water=new GameObject("Sanctuary quiet water");water.transform.SetParent(root,false);water.AddComponent<SanctumWater>().Setup(a,w,h,false);
   root.gameObject.AddComponent<HuntingAtmosphere>().Setup(a,w,h,MapTheme.SanctumField);
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(WorldRoutes.Portal(a[x,y]))Put(root,Game.Art.Get(a[x,y]=='<'?"arrow_left":a[x,y]=='>'?"arrow_right":a[x,y]=='['?"arrow_up":"arrow_down"),new Vector2(x+.5f,y+.5f),new Vector2(1,1),-27000,"Passage direction");
  }
  static Sprite Paint(char[,] a,int w,int h,int variant){
   var p=new PixelCanvas(w*32,h*32);var mat=SunkenSanctumArt.Asset("floor").texture;var pixels=mat.GetPixels32();
   char At(int x,int y)=>x<0||y<0||x>=w||y>=h?'W':a[x,h-1-y];bool Land(char c)=>c!='~'&&c!='W';
   for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++){
    int tx=x/32,ty=y/32,bx=x%32,by=y%32;char c=At(tx,ty);var stone=pixels[((y*3)%mat.height)*mat.width+(x*3)%mat.width];Color32 col;
    if(Land(c)){
     int shade=variant==2?-12:variant==1?5:0;col=C(stone.r+shade,stone.g+shade,stone.b+shade);
     if(!Land(At(tx,ty+1))&&by>24)col=by<28?C(160,157,118):C(53,69,51);
     bool edge=!Land(At(tx+1,ty))||!Land(At(tx-1,ty))||!Land(At(tx,ty-1));
     if(edge&&Hash(x/12,y/9)%7<2)col=C(stone.r*2/3,stone.g*4/5,stone.b/2);
     if(variant==1&&((x+32*(y/64%2))%96<2||y%64<2))col=C(58,70,60);
     // Long branching roots stay flat on stone; they are markings, not obstacles.
     if(variant==2&&edge&&Math.Abs((x+19*(y/80))%93-46-(int)(7*Mathf.Sin(y*.08f)))<3)col=C(77,62,39);
    }else if(c=='~'){
     bool shore=Land(At(tx-1,ty))||Land(At(tx+1,ty))||Land(At(tx,ty-1))||Land(At(tx,ty+1));
     float dist=2;for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++)if(Land(At(tx+ox,ty+oy))){float dx=Mathf.Max(0,Mathf.Abs(bx/32f-.5f-ox)-.5f),dy=Mathf.Max(0,Mathf.Abs(by/32f-.5f-oy)-.5f);dist=Mathf.Min(dist,Mathf.Sqrt(dx*dx+dy*dy));}
     float shallow=Mathf.Floor(Mathf.Clamp01(1-dist/1.5f)*6)/6;int v=Hash(x/28,y/23)%3;col=C((int)(21+20*shallow)+v,(int)(57+27*shallow)+v,(int)(48+18*shallow)+v);
    }else {bool face=Land(At(tx,ty+1));int bands=(x%43<2||y%31<2)?-8:0;col=face?C(57+bands,64+bands,51+bands):C(20+stone.r/9,28+stone.g/9,25+stone.b/10);}
    p.Set(x,y,col);
   }return Make(p,"sanctum_field_"+variant,Vector2.zero);
  }
 }
}
