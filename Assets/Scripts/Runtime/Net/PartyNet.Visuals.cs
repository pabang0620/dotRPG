using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class PartyNet
    {
        readonly Dictionary<int, Telegraph> remoteWarnings = new Dictionary<int, Telegraph>();
        readonly HashSet<int> visibleWarnings = new HashSet<int>();
        readonly List<int> expiredWarnings = new List<int>();
        readonly Dictionary<int, MonsterProjectile> remoteProjectiles = new Dictionary<int, MonsterProjectile>();

        void WriteProjectiles(BinaryWriter w)
        {
            int count = 0;
            foreach (var p in MonsterProjectile.Active) if (p != null) count++;
            w.Write((ushort)Mathf.Min(count, 128));
            int sent = 0;
            foreach (var p in MonsterProjectile.Active)
            {
                if (p == null) continue;
                if (sent++ >= 128) break;
                p.WriteVisual(w);
            }
        }

        void ReadProjectiles(BinaryReader r)
        {
            int count = r.ReadUInt16();
            if (count > 128) throw new IOException("Too many projectiles");
            visibleWarnings.Clear();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                remoteProjectiles.TryGetValue(id, out var previous);
                remoteProjectiles[id] = MonsterProjectile.ReadVisual(r, previous, puppets);
                visibleWarnings.Add(id);
            }
            expiredWarnings.Clear();
            foreach (var pair in remoteProjectiles)
                if (!visibleWarnings.Contains(pair.Key)) expiredWarnings.Add(pair.Key);
            foreach (int id in expiredWarnings)
            {
                if (remoteProjectiles[id] != null) Destroy(remoteProjectiles[id].gameObject);
                remoteProjectiles.Remove(id);
            }
        }

        void WriteWarnings(BinaryWriter w)
        {
            int count = 0;
            foreach (var t in Telegraph.Active) if (t != null && !t.Cancelled && !t.Resolved) count++;
            w.Write((ushort)Mathf.Min(count, 64));
            int sent = 0;
            foreach (var t in Telegraph.Active)
            {
                if (t == null || t.Cancelled || t.Resolved) continue;
                if (sent++ >= 64) break;
                t.WriteVisual(w);
            }
        }

        void ReadWarnings(BinaryReader r)
        {
            int count = r.ReadUInt16();
            if (count > 64) throw new IOException("Too many warnings");
            visibleWarnings.Clear();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                remoteWarnings.TryGetValue(id, out var previous);
                remoteWarnings[id] = Telegraph.ReadVisual(r, previous);
                visibleWarnings.Add(id);
            }
            expiredWarnings.Clear();
            foreach (var pair in remoteWarnings)
                if (!visibleWarnings.Contains(pair.Key)) expiredWarnings.Add(pair.Key);
            foreach (int id in expiredWarnings)
            {
                if (remoteWarnings[id] != null) Destroy(remoteWarnings[id].gameObject);
                remoteWarnings.Remove(id);
            }
        }

        void ClearWarnings()
        {
            foreach (var t in remoteWarnings.Values) if (t != null) Destroy(t.gameObject);
            remoteWarnings.Clear();
            foreach (var p in remoteProjectiles.Values) if (p != null) Destroy(p.gameObject);
            remoteProjectiles.Clear();
        }
    }
}
