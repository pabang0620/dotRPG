param(
    [Parameter(Mandatory=$true)][string]$Original,
    [Parameter(Mandatory=$true)][string]$Edited,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
# Use Windows PowerShell 5.1 (powershell.exe); System.Drawing is supplied by .NET Framework.
Add-Type -AssemblyName System.Drawing
# Package the imagegen edit into the approved terrain plate. Only the old stair
# footprint is replaced; the rest of the existing 96PPU art stays pixel-identical.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class SanctumStairPatch {
    public static void Apply(string originalPath, string editedPath, string outputPath) {
        using (var original = new Bitmap(originalPath))
        using (var edited = new Bitmap(editedPath)) {
            if (original.Width != 4608 || original.Height != 6912 || edited.Size != original.Size)
                throw new ArgumentException("Both registered images must be 4608x6912 (96PPU).");
            var bounds = new Rectangle(0, 0, original.Width, original.Height);
            using (var result = original.Clone(bounds, PixelFormat.Format32bppArgb))
            using (var source = edited.Clone(bounds, PixelFormat.Format32bppArgb)) {
                var dst = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                var src = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    if (src.Stride <= 0 || dst.Stride <= 0) throw new InvalidOperationException("Unexpected bitmap stride.");
                    var from = new byte[src.Stride * src.Height];
                    var to = new byte[dst.Stride * dst.Height];
                    Marshal.Copy(src.Scan0, from, 0, from.Length);
                    Marshal.Copy(dst.Scan0, to, 0, to.Length);
                    // Bounds measured on the 1024x1536 source, scaled by 4.5.
                    // The whole old stair (468..555, 534..639) is in the opaque core.
                    const double scale = 4.5;
                    for (int y = (int)(518*scale); y < (int)(655*scale); y++)
                    for (int x = (int)(452*scale); x < (int)(573*scale); x++) {
                        double u = (x+.5)/scale, v = (y+.5)/scale;
                        double alpha = Math.Min(Math.Min((u-452)/12, (573-u)/14),
                            Math.Min((v-518)/13, (655-v)/13));
                        alpha = Math.Max(0, Math.Min(1, alpha));
                        int a = y*src.Stride+x*4, b = y*dst.Stride+x*4;
                        for (int c=0; c<3; c++) to[b+c]=(byte)Math.Round(to[b+c]*(1-alpha)+from[a+c]*alpha);
                    }
                    Marshal.Copy(to, 0, dst.Scan0, to.Length);
                } finally { source.UnlockBits(src); result.UnlockBits(dst); }
                result.Save(outputPath, ImageFormat.Png);
            }
        }
    }
}
'@
[SanctumStairPatch]::Apply([IO.Path]::GetFullPath($Original), [IO.Path]::GetFullPath($Edited), [IO.Path]::GetFullPath($Output))
