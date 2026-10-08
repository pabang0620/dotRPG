param(
    [string]$MapId = 'all',
    [string]$Output = (Join-Path $PSScriptRoot '../../work/underworld-composition-guides')
)
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Output = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Add-Type -AssemblyName System.Drawing
$source = @'
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace UnityEngine {
 public static class Mathf {
  public static float Sin(float v)=>(float)Math.Sin(v);
  public static float Abs(float v)=>Math.Abs(v);
  public static int FloorToInt(float v)=>(int)Math.Floor(v);
  public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v));
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp(t,0,1);
 }
}
namespace DotRPG {
 public static class WorldRoutes { public static bool Portal(char c)=>"<>[]^{}".IndexOf(c)>=0; }
}
'@
foreach ($file in @('UnderworldComposition.cs','UnderworldCompositionNav.cs','UnderworldLayouts.cs','UnderworldContour.cs')) {
    $part = Get-Content -Raw (Join-Path $repo "Assets/Scripts/Runtime/World/$file")
    $source += [regex]::Replace($part, '(?m)^using [^;]+;\s*', '')
}
$source += @'
namespace DotRPG {
 public sealed class GuideData {
  public string MapId,Layout;
  public int Width=1792,Height=1536,Camps,FloorCells,WaterCells,ReachableFloorCells;
  public int[] Pixels,Mask,FrontMask;
  public string[] Portals,Unreachable;
  public GuideData(string id) {
   MapId=id;Layout=UnderworldLayouts.Layout(id);var rows=Layout.TrimEnd().Split('\n');
   var a=new char[56,48];var ports=new List<string>();int sx=0,sy=0;
   for(int y=0;y<48;y++)for(int x=0;x<56;x++){
    char c=rows[47-y][x];a[x,y]=c;if(c=='k')Camps++;
    if(c=='P'){sx=x;sy=y;}if(WorldRoutes.Portal(c))ports.Add(c+":"+x+","+y);
   }
   Portals=ports.ToArray();var contour=new UnderworldContour(a,56,48,id);
   int cols=contour.Columns,rs=contour.Rows;
   var seen=new bool[cols*rs];var queue=new Queue<int>();
   int start=(sy*8+4)*cols+sx*8+4;seen[start]=true;queue.Enqueue(start);
   while(queue.Count>0){int n=queue.Dequeue();ReachableFloorCells++;int x=n%cols,y=n/cols;
    foreach(int d in new[]{-1,1,-cols,cols}){int v=n+d,xx=x+(d==-1?-1:d==1?1:0),yy=y+(d==-cols?-1:d==cols?1:0);
     if(xx<0||yy<0||xx>=cols||yy>=rs||seen[v]||contour.At(xx,yy)!=1)continue;seen[v]=true;queue.Enqueue(v);}
   }
   var unreachable=new List<string>();
   for(int y=0;y<rs;y++)for(int x=0;x<cols;x++)if(contour.At(x,y)==1&&!seen[y*cols+x]&&unreachable.Count<24)unreachable.Add(((x+.5f)/8)+","+((y+.5f)/8));
   Unreachable=unreachable.ToArray();
   var front=new bool[cols*rs];
   // Structural fronts descend south from the actual floor silhouette. Distant space stays open.
   for(int x=0;x<cols;x++){
    int above=-1000;
    for(int y=rs-1;y>=0;y--){byte k=contour.At(x,y);if(k==1)above=y;
     else if(k==0&&above>=y&&above-y<=24)front[y*cols+x]=true;
    }
   }
   Pixels=new int[Width*Height];Mask=new int[Pixels.Length];FrontMask=new int[Pixels.Length];
   int ground=unchecked((int)0xffb59c71),wall=unchecked((int)0xff554a48),water=unchecked((int)0xff224759),deep=unchecked((int)0xff171f32);
   for(int y=0;y<rs;y++)for(int x=0;x<cols;x++){
    byte kind=contour.At(x,y);if(kind==1)FloorCells++;if(kind==2)WaterCells++;
    bool face=front[y*cols+x];int color=kind==1?ground:kind==2?water:face?wall:deep;
    int mask=kind==1?unchecked((int)0xffff0000):kind==2?unchecked((int)0xff0000ff):face?unchecked((int)0xff00ff00):unchecked((int)0xff000000);
    for(int py=0;py<4;py++)for(int px=0;px<4;px++){
     int i=(Height-1-y*4-py)*Width+x*4+px;Pixels[i]=color;Mask[i]=mask;FrontMask[i]=face?unchecked((int)0xffffffff):0;
    }
   }
  }
 }
}
'@
Add-Type -TypeDefinition $source
function Save-Pixels([int[]]$pixels, [string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new(1792,1536,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bits = $bitmap.LockBits([System.Drawing.Rectangle]::new(0,0,1792,1536),[System.Drawing.Imaging.ImageLockMode]::WriteOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($pixels,0,$bits.Scan0,$pixels.Length)
    $bitmap.UnlockBits($bits)
    $bitmap.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
$ids = if ($MapId -eq 'all') { @('hollow_descent','hollow_roots','hollow_fungal','hollow_depths') } else { @($MapId) }
foreach ($id in $ids) {
    $data = [DotRPG.GuideData]::new($id)
    Save-Pixels $data.Pixels (Join-Path $Output "$id-guide.png")
    Save-Pixels $data.Mask (Join-Path $Output "$id-mask.png")
    Save-Pixels $data.FrontMask (Join-Path $Output "$id-front-mask.png")
    [System.IO.File]::WriteAllText((Join-Path $Output "$id-layout.txt"),$data.Layout)
    $report = [ordered]@{mapId=$id;width=1792;height=1536;pixelsPerTile=32;origin='northwest';camps=$data.Camps;floorSubcells=$data.FloorCells;reachableFloorSubcells=$data.ReachableFloorCells;waterSubcells=$data.WaterCells;portals=$data.Portals;guideColors=@{floor='#B59C71';structuralFront='#554A48';water='#224759';deepBackground='#171F32'};maskColors=@{floor='red';water='blue';structuralFront='green';deepBackground='black'}}
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $Output "$id-report.json")
    [pscustomobject]@{Map=$id;Camps=$data.Camps;AllFloorConnected=($data.FloorCells -eq $data.ReachableFloorCells);Guide=(Join-Path $Output "$id-guide.png")}
    if ($data.Camps -ne 18 -or $data.FloorCells -ne $data.ReachableFloorCells) { throw "Layout validation failed for $id; disconnected subcells: $($data.Unreachable -join '; ')" }
}
