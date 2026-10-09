using System;
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

        /// <summary>File size in bytes (0 when missing). On Android the bytes stay cached for the next read.</summary>
        public static long Length(string path)
        {
            if (!Android) return File.Exists(path) ? new FileInfo(path).Length : 0;
            return Load(path)?.Length ?? 0;
        }

        /// <summary>The first <paramref name="count"/> bytes (fewer when the file is shorter). On Android the bytes stay cached for the next read.</summary>
        public static byte[] ReadHeader(string path, int count)
        {
            byte[] header;
            if (!Android)
            {
                header = new byte[count];
                int read = 0;
                using (var stream = File.OpenRead(path))
                    while (read < count) { int n = stream.Read(header, read, count - read); if (n == 0) break; read += n; }
                if (read < count) Array.Resize(ref header, read);
                return header;
            }
            var bytes = Load(path) ?? throw new FileNotFoundException("StreamingAssets file not found: " + path);
            header = new byte[Math.Min(count, bytes.Length)];
            Array.Copy(bytes, header, header.Length);
            return header;
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
