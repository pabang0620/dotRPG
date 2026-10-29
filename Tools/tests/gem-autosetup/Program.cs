using System;
using System.Diagnostics;
using DotRPG;

// Runs GemAutoSetup.SelfCheck (the real Assets/Scripts/Runtime/Progression/GemAutoSetup.cs, no Unity types) and
// prints the three presets on the sample bar.
static class Program
{
    static int Main()
    {
        var issues = GemAutoSetup.SelfCheck();
        foreach (var i in issues) Console.WriteLine("FAIL " + i);
        string[] names = { "sustain", "area", "single" };
        var bar = GemAutoSetup.CheckBar();
        var gems = GemAutoSetup.CheckGemList();
        for (int p = 0; p < 3; p++)
        {
            var w = GemAutoSetup.Presets[p];
            var watch = Stopwatch.StartNew();
            var r = GemAutoSetup.Solve(bar, gems, w[0], w[1], w[2], true);
            watch.Stop();
            Console.WriteLine($"{names[p]} {w[0]}/{w[1]}/{w[2]}: sustain {r.Percent(GemAutoSetup.Axis.Sustain)}% area {r.Percent(GemAutoSetup.Axis.Aoe)}% single {r.Percent(GemAutoSetup.Axis.Single)}%  MP {r.mpUsed:0.00}/{r.mpBudget:0.00}  {watch.Elapsed.TotalMilliseconds:0.0} ms");
            foreach (var plan in r.plans) Console.WriteLine($"   slot {plan.slot}: {plan.gem1} + {plan.gem2}");
        }
        Console.WriteLine(issues.Count == 0 ? "GemAutoSetup self-check: OK" : $"GemAutoSetup self-check: {issues.Count} problem(s)");
        return issues.Count == 0 ? 0 : 1;
    }
}
