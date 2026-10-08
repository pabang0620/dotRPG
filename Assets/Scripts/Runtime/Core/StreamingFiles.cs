using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DotRPG
{
    /// <summary>
    /// [ANDROID] StreamingAssets files. On a PC build they are plain files (File.*, unchanged). On Android they live inside
    /// the apk, where File.* cannot reach them, so they are read with UnityWebRequest and the call waits for the result.
    /// </summary>
    public static class StreamingFiles
    {
        const float WaitSeconds = 10f;
        static readonly Dictionary<string, byte[]> androidCache = new Dictionary<string, byte[]>();

        static bool Android => Application.platform == RuntimePlatform.Android;

        public static bool Exists(string path)
        {
            if (!Android) return File.Exists(path);
            return Load(path) != null;
        }

        public static byte[] ReadAllBytes(string path)
        {
            if (!Android) return File.ReadAllBytes(path);
            var bytes = Load(path);
            androidCache.Remove(path); // the caller keeps the data; the next Exists/Read asks again
            if (bytes == null) throw new FileNotFoundException("StreamingAssets file not found: " + path);
            return bytes;
        }

        public static string ReadAllText(string path)
        {
            if (!Android) return File.ReadAllText(path);
            return Encoding.UTF8.GetString(ReadAllBytes(path)).TrimStart('\uFEFF'); // File.ReadAllText drops a BOM too
        }

        // Exists then Read is the common order: the bytes of the first request are kept until the read takes them.
        static byte[] Load(string path)
        {
            if (androidCache.TryGetValue(path, out var cached)) return cached;
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                float until = Time.realtimeSinceStartup + WaitSeconds;
                while (!op.isDone && Time.realtimeSinceStartup < until) { }
                if (!op.isDone || req.result != UnityWebRequest.Result.Success) return null;
                var bytes = req.downloadHandler.data;
                if (bytes != null) androidCache[path] = bytes;
                return bytes;
            }
        }
    }
}
