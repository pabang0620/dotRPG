using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>A projectile's look (core sprite + glow) that gameplay code moves every frame.</summary>
    public sealed class MovingFx
    {
        readonly List<SkillFx> parts = new List<SkillFx>();

        public MovingFx Add(SkillFx fx) { parts.Add(fx); return this; }

        public void MoveTo(Vector2 p)
        {
            foreach (var f in parts) if (f != null) f.transform.position = p;
        }

        public void Kill()
        {
            foreach (var f in parts) if (f != null) f.Kill();
            parts.Clear();
        }
    }
}
