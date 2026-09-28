using System; using System.Collections.Generic; using System.IO; using DotRPG; using UnityEngine;
static class MapPreview {
  static char[,] cells; static int W,H;
  static char At(int x,int y)=> x>=0&&y>=0&&x<W&&y<H?cells[x,y]:'\0';
  static char Raw(char c)=> c=='~'||c=='='||c=='#'||c=='d'?c: c=='C'?'#':'.';
  static char G(int x,int y){ char c=At(x,y); if(c=='\0')return '\0'; char r=Raw(c); if(r!='.')return r; if(c=='.')return '.'; var a=new[]{Raw(At(x-1,y)),Raw(At(x+1,y)),Raw(At(x,y+1)),Raw(At(x,y-1))}; foreach(var cand in new[]{'=','#','d'}){int n=0; foreach(var q in a) if(q==cand)n++; if(n>=2)return cand;} return '.'; }
  public static void Run(string mapPath, string outPath, int scale, float cx0=-1, float cy0=-1, float vw=0, float vh=0){
    var rows=new List<string>(); foreach(var l in File.ReadAllLines(mapPath)){ if(l.StartsWith("//")||l.Trim().Length==0) continue; rows.Add(l);} H=rows.Count; W=0; foreach(var r in rows) W=Math.Max(W,r.Length);
    cells=new char[W,H]; for(int row=0;row<H;row++) for(int x=0;x<W;x++) cells[x,H-1-row]= x<rows[row].Length?rows[row][x]:'.';
    int PW=W*16, PH=H*16; var px=new Color32[PW*PH];
    void Draw(PixelCanvas c, float wx, float wy, bool flip=false){ if(c==null)return; int ox=(int)Math.Round(wx*16 - c.PivotX); int oyTop=(int)Math.Round((PH - wy*16) - (c.Height - c.PivotY)); for(int y=0;y<c.Height;y++) for(int x=0;x<c.Width;x++){ var s=c.Pixels[y*c.Width+(flip?c.Width-1-x:x)]; if(s.a==0)continue; int X=ox+x,Y=oyTop+y; if(X<0||Y<0||X>=PW||Y>=PH)continue; int i=Y*PW+X; var d=px[i]; float al=s.a/255f; px[i]=new Color32((byte)(s.r*al+d.r*(1-al)),(byte)(s.g*al+d.g*(1-al)),(byte)(s.b*al+d.b*(1-al)),255);} }
    var rng=new Random(1234);
    for(int y=0;y<H;y++) for(int x=0;x<W;x++){ char g=G(x,y); string key;
      if(g=='~'){ char ab=G(x,y+1); bool edge=ab!='~'&&ab!='d'&&ab!='\0'; key= edge?$"tile_water_edge_{(x*7+y)%4}":$"tile_water_{(x*3+y*5)%4}"; }
      else if(g=='d') key="tile_dock";
      else if(g=='='||g=='#'){ bool soil=g=='#'; Func<char,bool> same= soil? (ch=>ch=='#'): (ch=>ch=='='); int m=0; bool L(char ch)=> ch=='\0'||same(ch); if(!L(G(x,y+1)))m|=1; if(!L(G(x+1,y)))m|=2; if(!L(G(x,y-1)))m|=4; if(!L(G(x-1,y)))m|=8; key=$"tile_{(soil?"soil":"dirt")}_{m}_{(x*5+y*3)%3}"; }
      else key=$"tile_grass_{(x+y)%2}";
      Draw(ProceduralArt.Draw(key), x+0.5f, y+0.5f);
      if(cells[x,y]=='.' && rng.NextDouble()<0.07) Draw(ProceduralArt.Draw("deco_tuft"), x+0.5f,y+0.5f);
    }
    var objs=new List<(float y, PixelCanvas c, float wx, float wy, bool flip)>();
    var looks=new Dictionary<char,(CharacterLook,string)>{{'1',(CharacterLook.Chief,"down")},{'2',(CharacterLook.Farmer,"side")},{'3',(CharacterLook.Fisher,"down")},{'4',(CharacterLook.Builder,"side")},{'5',(CharacterLook.Lumberjack,"up")},{'6',(CharacterLook.Miner,"side")},{'7',(CharacterLook.Carrier,"side")},{'8',(new CharacterLook("kid",HairStyle.Spiky,new Color32(250,205,160,255),new Color32(90,60,40,255),new Color32(230,200,70,255),new Color32(80,110,160,255)),"down")},{'P',(CharacterLook.Player,"down")},{'k',(CharacterLook.Skeleton,"down")}};
    for(int y=0;y<H;y++) for(int x=0;x<W;x++){ char c=cells[x,y]; float fx=x+0.5f, fy=y+0.2f; PixelCanvas s=null; float oy=fy;
      switch(c){ case 'T': s=ProceduralArt.Draw("tree"); break; case 'O': s=ProceduralArt.Draw("tree_fruit"); break; case 'R': s=ProceduralArt.Draw("rock"); break; case 'S': s=ProceduralArt.Draw("stump"); break; case 'B': s=ProceduralArt.Draw("bush"); break; case 'c': s=ProceduralArt.Draw("crate"); break; case 'n': s=ProceduralArt.Draw("sign"); break; case 'C': s=ProceduralArt.Draw("crop_carrot"); break;
        case 'F': { int m=0; if(At(x-1,y)=='F')m|=1; if(At(x+1,y)=='F')m|=2; if(At(x,y+1)=='F')m|=4; if(At(x,y-1)=='F')m|=8; s=ProceduralArt.Draw("fence_"+m); oy=y+0.05f; break; }
        case 'H': s=ProceduralArt.Draw("house"); fx=x+1.5f; oy=y+0.1f; break;
        case 'X': s=ProceduralArt.Draw("site_blueprint"); fx=x+1.5f; oy=y+0.1f; objs.Add((oy-0.2f,ProceduralArt.Draw("pile_wood"),fx-1.9f,oy-0.2f,false)); break;
        case 'f': Draw(ProceduralArt.Draw("deco_flower_"+(x%2)), x+0.5f, y+0.2f); break; case 'r': Draw(ProceduralArt.Draw("deco_pebble"), x+0.5f,y+0.3f); break; }
      if(looks.TryGetValue(c,out var lk)){ fy=y+0.5f; objs.Add((fy+0.01f, ProceduralArt.Draw("shadow"), fx, fy+0.08f,false)); s=ProceduralArt.DrawCharacter(lk.Item1, lk.Item2, "idle0"); oy=fy; if(c=='7') objs.Add((oy-0.01f, ProceduralArt.Draw("tool_crate"), fx, oy+1.05f,false)); }
      if(s!=null) objs.Add((oy,s,fx,oy, c=='5'?false:false)); }
    objs.Sort((a,b)=>b.y.CompareTo(a.y)); foreach(var o in objs) Draw(o.c,o.wx,o.wy,o.flip);
    // crop
    int x0=0,y0=0,cw=PW,ch=PH; if(vw>0){ x0=(int)((cx0-vw/2)*16); y0=(int)((PH-(cy0+vh/2)*16)); cw=(int)(vw*16); ch=(int)(vh*16); }
    var outPx=new Color32[cw*scale*ch*scale]; for(int y=0;y<ch*scale;y++) for(int x=0;x<cw*scale;x++){ int sx=x0+x/scale, sy=y0+y/scale; outPx[y*cw*scale+x]= (sx>=0&&sy>=0&&sx<PW&&sy<PH)? px[sy*PW+sx]: new Color32(0,0,0,255);} 
    P.Png(outPath,cw*scale,ch*scale,outPx); Console.WriteLine($"map {W}x{H} -> {outPath}");
  }
}
