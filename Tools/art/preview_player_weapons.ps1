param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Docs/Art/PlayerWeapons'),
    [switch]$BakeProject
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$core = Get-Content -Raw -LiteralPath (Join-Path $repository 'Assets/Scripts/Runtime/Art/PixelCanvas.cs')
$art = Get-Content -Raw -LiteralPath (Join-Path $repository 'Assets/Scripts/Runtime/Art/PlayerWeaponArt.cs')
$prefix = @'
using System;
using UnityEngine;
namespace UnityEngine {
 public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
}
'@
$exporter = @'
public static class PlayerWeaponsPreview {
 static System.Drawing.Bitmap Bitmap(DotRPG.PixelCanvas p) {
  var b = new System.Drawing.Bitmap(p.Width,p.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  var pixels=new int[p.Pixels.Length];
  for(int i=0;i<pixels.Length;i++){var c=p.Pixels[i];pixels[i]=(c.a<<24)|(c.r<<16)|(c.g<<8)|c.b;}
  var data=b.LockBits(new System.Drawing.Rectangle(0,0,p.Width,p.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  System.Runtime.InteropServices.Marshal.Copy(pixels,0,data.Scan0,pixels.Length);b.UnlockBits(data);return b;
 }
 public static void Save(string root,string folder) {
  System.IO.Directory.CreateDirectory(folder);
  using(var sheet=new System.Drawing.Bitmap(1120,470))
  using(var g=System.Drawing.Graphics.FromImage(sheet))
  using(var title=new System.Drawing.Font("Segoe UI",22,System.Drawing.FontStyle.Bold))
  using(var text=new System.Drawing.Font("Segoe UI",12))
  using(var white=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(225,232,223)))
  using(var muted=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(143,160,164)))
  using(var card=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(29,39,48)))
  using(var gold=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(192,159,99)))
  using(var oldKatanas=new System.Drawing.Bitmap(System.IO.Path.Combine(root,"Assets/Resources/SilverWarrior/katanas_v2.png"))) {
   g.Clear(System.Drawing.Color.FromArgb(17,24,31));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
   g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
   g.DrawString("WARRIOR KATANAS  /  APPEARANCE STUDY",title,white,26,20);
   g.DrawString("Same size, grip and combat rig. New readable metal and wrapping. Mage staffs remain unchanged.",text,muted,28,60);
   string[] names={"OAK / TRAINING","SILVER / STEEL","BONE / IVORY","GOLD / RUBY"};
   for(int tier=0;tier<4;tier++) {
    string key="wpn_sword_"+tier;
    var p=DotRPG.PlayerWeaponArt.Draw(key);
    using(var fresh=Bitmap(p)) {
     fresh.Save(System.IO.Path.Combine(folder,key+".png"),System.Drawing.Imaging.ImageFormat.Png);
     int x=24+tier*273,y=100;g.FillRectangle(card,x,y,258,332);
     g.DrawString(names[tier],text,gold,x+12,y+12);
     g.DrawString("KATANA",text,muted,x+12,y+35);
     // Both versions use precisely the same scale and source canvas; new art isn't made
     // to appear better by enlarging it independently of the old sprite.
      var oldRect=new System.Drawing.Rectangle(tier*64,0,64,64);
      g.DrawImage(oldKatanas,new System.Drawing.Rectangle(x-35,y+72,192,p.Height*3),oldRect,System.Drawing.GraphicsUnit.Pixel);
      g.DrawImage(fresh,new System.Drawing.Rectangle(x+85,y+72,192,p.Height*3),0,0,64,p.Height,System.Drawing.GraphicsUnit.Pixel);
     g.DrawString("BEFORE",text,muted,x+24,y+296);g.DrawString("AFTER",text,white,x+147,y+296);
    }
   }
   sheet.Save(System.IO.Path.Combine(folder,"weapon-before-after.png"),System.Drawing.Imaging.ImageFormat.Png);
  }
 }
}
'@
$source = $prefix + ($core -replace '(?m)^using [^;]+;\s*','') + ($art -replace '(?m)^using [^;]+;\s*','') + $exporter
Add-Type -TypeDefinition $source -ReferencedAssemblies 'System.Drawing.Common','System.Drawing.Primitives','System.Runtime','System.Runtime.InteropServices','System.Private.Windows.GdiPlus','System.Private.Windows.Core'
[PlayerWeaponsPreview]::Save($repository,$OutputDirectory)
if ($BakeProject) {
    $destination = Join-Path $repository 'Assets/Resources/Art/PlayerWeapons'
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $template = Get-Content -Raw -LiteralPath (Join-Path $repository 'Assets/Resources/Art/Weapons/wpn_staff_1.png.meta')
    foreach ($family in @('sword')) {
        foreach ($tier in 0..3) {
            $name = 'wpn_' + $family + '_' + $tier + '.png'
            Copy-Item -LiteralPath (Join-Path $OutputDirectory $name) -Destination (Join-Path $destination $name)
            $metaPath = Join-Path $destination ($name + '.meta')
            $guid = if (Test-Path -LiteralPath $metaPath) { ([regex]::Match((Get-Content -Raw -LiteralPath $metaPath),'guid: ([a-f0-9]+)')).Groups[1].Value } else { [guid]::NewGuid().ToString('N') }
            $meta = $template -replace '(?m)^guid: .*', ('guid: '+$guid)
            if ($family -eq 'sword') { $meta = $meta.Replace('spritePivot: {x: 0.5, y: 0.0833}','spritePivot: {x: 0.5, y: 0.1796875}').Replace('spritePixelsToUnits: 72','spritePixelsToUnits: 40') }
            [IO.File]::WriteAllText($metaPath,$meta)
        }
    }
    if (!(Test-Path -LiteralPath ($destination+'.meta'))) { [IO.File]::WriteAllText(($destination+'.meta'),"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nfolderAsset: yes`nDefaultImporter:`n  externalObjects: {}`n") }
}
Write-Output (Join-Path $OutputDirectory 'weapon-before-after.png')
