using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace DotRPG {
 public static class SanctumHunting {
  public static string Layout(HuntingZone z) {
   const int w=60,h=48;var a=new char[w,h];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)a[x,y]=(x<5||x>w-6||y<4||y>h-5)?'W':'~';
   void Ell(float cx,float cy,float rx,float ry,char c='.') {for(int y=2;y<h-2;y++)for(int x=2;x<w-2;x++)if((x-cx)*(x-cx)/(rx*rx)+(y-cy)*(y-cy)/(ry*ry)<1)a[x,y]=c;}
   void Line(int x,int y,int xx,int yy,int radius=3){int n=Math.Max(Math.Abs(xx-x),Math.Abs(yy-y));for(int i=0;i<=n;i++){int px=Mathf.RoundToInt(Mathf.Lerp(x,xx,i/(float)Math.Max(n,1))),py=Mathf.RoundToInt(Mathf.Lerp(y,yy,i/(float)Math.Max(n,1)));for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)if(px+dx>=0&&px+dx<w&&py+dy>=0&&py+dy<h)a[px+dx,py+dy]='.';}}
   if(z.variant==0){Ell(29,24,17,10);Ell(30,38,12,7);Ell(25,9,15,7);Line(0,24,29,24);Line(29,24,30,47);Line(29,24,25,0);Ell(39,25,5,4,'~');}
   if(z.variant==1){Ell(30,24,24,17);for(int y=14;y<=34;y++)for(int x=25;x<=33;x++)a[x,y]='~';Line(0,24,17,24);Line(42,24,59,24);Line(17,11,44,11);Line(17,38,44,38);}
   if(z.variant==2){Ell(13,24,11,12);Ell(25,12,13,9);Ell(40,22,14,10);Ell(45,36,10,8);Line(0,24,13,24);Line(13,24,25,12,4);Line(25,12,40,22,4);Line(40,22,45,36,4);Line(40,22,59,24);Ell(29,29,7,5,'~');}
   if(z.variant==3){Ell(30,24,23,18);Ell(30,24,16,11,'~');Ell(30,24,10,7);Line(30,0,30,47,3);Line(9,24,51,24,3);}
   // Traversal markers are single cells inside broad approaches; arrivals are inset by the existing portal code.
   if(z.variant==0){a[0,24]='<';a[30,47]='[';a[25,0]=']';}
   else if(z.variant<3){a[0,24]='<';a[59,24]='>';}
   else {a[30,47]='[';a[30,0]=']';}
   a[z.variant==3?30:7,24]='P';
   var candidates=new List<Vector2Int>();
   for(int y=6;y<h-6;y++)for(int x=7;x<w-7;x++){bool clear=true;for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)if(a[x+dx,y+dy]!='.')clear=false;if(clear)candidates.Add(new Vector2Int(x,y));}
   candidates.Sort((u,v)=>Hash(u.x,u.y,z.variant).CompareTo(Hash(v.x,v.y,z.variant)));var selected=new List<Vector2Int>();
   foreach(var c in candidates){if(selected.Exists(t=>Vector2Int.Distance(t,c)<5)||Vector2.Distance(c,new Vector2(z.variant==3?30:7,24))<6)continue;a[c.x,c.y]='k';selected.Add(c);if(selected.Count>=20)break;}
   var text=new StringBuilder();for(int y=h-1;y>=0;y--){for(int x=0;x<w;x++)text.Append(a[x,y]);text.Append('\n');}return text.ToString();
  }
  static int Hash(int x,int y,int v)=>unchecked((x*73856093^y*19349663^v*83492791)&0x7fffffff);
 }
}
