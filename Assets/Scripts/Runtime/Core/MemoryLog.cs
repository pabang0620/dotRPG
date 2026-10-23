using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace DotRPG
{
    /// <summary>
    /// [MEMORY] Development builds only: start with "-dotrpgMemLog" (optionally followed by seconds, default 60) and a line like
    /// "[MemLog] t=12.0m mono=85.2MB gc=80.1MB alloc=612.4MB reserved=700.0MB tex=812 sprites=2310 mats=40 meshes=12 objs=5321 map=village"
    /// goes to the player log every interval and after each cache sweep. Off (and free) in a normal run.
    /// </summary>
    public sealed class MemoryLog : MonoBehaviour
    {
        static MemoryLog instance;
        float interval = 60f, next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
#if !DOTRPG_RELEASE
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-dotrpgMemLog");
            if (at < 0) return;
            var go = new GameObject("MemoryLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MemoryLog>();
            if (at + 1 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                instance.interval = Mathf.Max(5f, seconds);
            instance.next = Time.realtimeSinceStartup + instance.interval;
            Line("start interval=" + instance.interval + "s");
#endif
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + interval;
            Line("tick");
        }

        /// <summary>Writes one memory line when the log is on (no-op otherwise).</summary>
        public static void Line(string reason)
        {
            if (instance == null) return;
            const float Mb = 1048576f;
            string map = Game.World != null ? Game.World.MapId : "-";
            Debug.Log($"[MemLog] t={Time.realtimeSinceStartup / 60f:F1}m {reason} mono={Profiler.GetMonoUsedSizeLong() / Mb:F1}MB gc={GC.GetTotalMemory(false) / Mb:F1}MB " +
                $"alloc={Profiler.GetTotalAllocatedMemoryLong() / Mb:F1}MB reserved={Profiler.GetTotalReservedMemoryLong() / Mb:F1}MB " +
                $"tex={Resources.FindObjectsOfTypeAll<Texture2D>().Length} sprites={Resources.FindObjectsOfTypeAll<Sprite>().Length} " +
                $"mats={Resources.FindObjectsOfTypeAll<Material>().Length} meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length} " +
                $"objs={Resources.FindObjectsOfTypeAll<GameObject>().Length} sweeps={MemoryJanitor.Sweeps} map={map}");
        }
    }
}
