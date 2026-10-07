using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// [PERF] How long the first use of each generated effect costs (batch: -executeMethod DotRPG.EditorTools.PerfProbe.Run).
    /// Writes Logs/perf_probe.txt: clip build times (VfxLibrary) and HD fx sprite draw times (ProceduralArt).
    /// </summary>
    public static class PerfProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var sw = new Stopwatch();
            double clipTotal = 0, fxTotal = 0;
            foreach (var name in VfxPreview.Clips)
            {
                sw.Restart();
                var frames = VfxArt.Frames(name);
                sw.Stop();
                clipTotal += sw.Elapsed.TotalMilliseconds;
                sb.AppendLine($"clip {name,-14} {sw.Elapsed.TotalMilliseconds,8:0.0} ms  frames={frames?.Length}");
            }
            ProceduralArt.HdEffects = true;
            foreach (var key in FxKeys)
            {
                sw.Restart();
                ProceduralArt.Draw(key);
                sw.Stop();
                fxTotal += sw.Elapsed.TotalMilliseconds;
                sb.AppendLine($"fx   {key,-14} {sw.Elapsed.TotalMilliseconds,8:0.0} ms");
            }
            sb.AppendLine($"TOTAL clips {clipTotal:0} ms, fx {fxTotal:0} ms");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Logs", "perf_probe.txt"), sb.ToString());
        }

        static readonly string[] FxKeys =
        {
            "fx_glow", "fx_shock", "fx_ring", "fx_rune", "fx_swoosh", "fx_blade", "fx_zap", "fx_frost", "fx_crack", "fx_spike",
            "fx_ice", "fx_shard", "fx_snow", "fx_spark", "fx_streak", "fx_cut", "fx_slash", "fx_crescent", "fx_arc", "fx_bigsword",
            "fx_frostorb", "fx_flame", "fx_meteor", "fx_scorch", "fx_star", "fx_aegis", "fx_feather", "fx_holy", "fx_sparkle", "fx_bolt", "fx_magic", "fx_dust",
        };
    }
}
