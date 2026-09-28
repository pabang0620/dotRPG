using System; using System.IO; using System.IO.Compression; using System.Collections.Generic; using DotRPG; using UnityEngine;
static class P {
  static uint[] crcT;
  static uint Crc(byte[] b, int o, int n){ if(crcT==null){crcT=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xEDB88320u^(c>>1):c>>1;crcT[i]=c;}} uint r=0xFFFFFFFF; for(int i=o;i<o+n;i++) r=crcT[(r^b[i])&0xFF]^(r>>8); return r^0xFFFFFFFF;}
  static void Chunk(Stream s,string t,byte[] d){ var len=BitConverter.GetBytes(d.Length); Array.Reverse(len); s.Write(len); var td=new byte[4+d.Length]; System.Text.Encoding.ASCII.GetBytes(t).CopyTo(td,0); d.CopyTo(td,4); s.Write(td); var c=BitConverter.GetBytes(Crc(td,0,td.Length)); Array.Reverse(c); s.Write(c);}
  public static void Png(string path,int w,int h,Color32[] px){ // px top-down
    var raw=new MemoryStream(); for(int y=0;y<h;y++){ raw.WriteByte(0); for(int x=0;x<w;x++){var c=px[y*w+x]; raw.WriteByte(c.r);raw.WriteByte(c.g);raw.WriteByte(c.b);raw.WriteByte(c.a);} }
    var z=new MemoryStream(); using(var zs=new ZLibStream(z,CompressionLevel.Optimal,true)) { raw.Position=0; raw.CopyTo(zs);} 
    using var f=File.Create(path); f.Write(new byte[]{137,80,78,71,13,10,26,10});
    var ih=new byte[13]; var bw=BitConverter.GetBytes(w); Array.Reverse(bw); bw.CopyTo(ih,0); var bh=BitConverter.GetBytes(h); Array.Reverse(bh); bh.CopyTo(ih,4); ih[8]=8; ih[9]=6; Chunk(f,"IHDR",ih); Chunk(f,"IDAT",z.ToArray()); Chunk(f,"IEND",new byte[0]); }
  static void Main(string[] a){ if(a.Length>1 && a[1]=="map"){ MapPreview.Run(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/Resources/Maps/Village.txt"), a[0], int.Parse(a[2]), a.Length>3?float.Parse(a[3]):-1, a.Length>4?float.Parse(a[4]):-1, a.Length>5?float.Parse(a[5]):0, a.Length>6?float.Parse(a[6]):0); return; }
    var sheet = new List<(string,PixelCanvas)>();
    string[] keys = {"tile_grass_0","tile_grass_1","tile_dirt_0_0","tile_dirt_5_1","tile_soil_9_0","tile_water_0","tile_water_edge_1","tile_dock","deco_tuft","deco_flower_0","deco_flower_1","deco_pebble","tree","tree_fruit","stump","rock","bush","fence_3","fence_1","fence_2","fence_12","fence_0","sign","crate","house","site_blueprint","site_built","pile_wood","pile_stone","crop_carrot","crop_sprout","crop_hole","tool_sword","tool_axe","tool_pickaxe","tool_can","tool_rod","tool_hammer","fx_slash","fx_sparkle","fx_dust","fx_alert","icon_wood","icon_stone","icon_carrot","heart_full","heart_half","heart_empty","ui_panel","ui_dark","shadow"};
    foreach(var k in keys){ var c=ProceduralArt.Draw(k); if(c==null){Console.WriteLine("NULL "+k); continue;} sheet.Add((k,c)); }
    var looks = new[]{CharacterLook.Player, CharacterLook.Skeleton, CharacterLook.Chief, CharacterLook.Farmer, CharacterLook.Fisher, CharacterLook.Builder, CharacterLook.Miner, CharacterLook.Carrier};
    foreach(var L in looks) foreach(var d in new[]{"down","up","side"}) foreach(var fr in new[]{"idle0","walk0","walk1","walk2","attack"}) sheet.Add((L.id+d+fr, ProceduralArt.DrawCharacter(L,d,fr)));
    // layout
    int scale=4, W=1400, x=4,y=4,rowH=0; var items=new List<(int,int,PixelCanvas)>();
    foreach(var (k,c) in sheet){ int w=c.Width*scale+6, h=c.Height*scale+6; if(x+w>W){x=4;y+=rowH;rowH=0;} items.Add((x,y,c)); x+=w; rowH=Math.Max(rowH,h);} int H=y+rowH+4;
    var px=new Color32[W*H]; for(int i=0;i<px.Length;i++) px[i]=new Color32(70,120,80,255);
    foreach(var (ox,oy,c) in items) for(int yy=0;yy<c.Height*scale;yy++) for(int xx=0;xx<c.Width*scale;xx++){ var s=c.Pixels[(yy/scale)*c.Width+xx/scale]; if(s.a==0) continue; int i=(oy+yy)*W+ox+xx; var d=px[i]; float al=s.a/255f; px[i]=new Color32((byte)(s.r*al+d.r*(1-al)),(byte)(s.g*al+d.g*(1-al)),(byte)(s.b*al+d.b*(1-al)),255);} 
    Png(a[0],W,H,px); Console.WriteLine("ok "+W+"x"+H);
  }
}
