using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The gate to the next room, built on the '@' cells of a room map. Closed: a solid door with a red seal.
    /// <see cref="Open"/> (room cleared) swaps the art, removes the collider and from then on the local player
    /// stepping into the gate calls <see cref="Entered"/> once.
    /// </summary>
    public sealed class DungeonDoor : MonoBehaviour
    {
        /// <summary>How far past the gate cells the player may stand and still count as "in the door".</summary>
        const float EnterMargin = 0.35f;

        SpriteRenderer sr;
        BoxCollider2D solid;
        Rect area;
        bool fired;

        public bool IsOpen { get; private set; }
        public Vector2 Center => area.center;
        /// <summary>Where a character should walk to go through (just inside the gate).</summary>
        public Vector2 Threshold => new Vector2(area.center.x, area.yMin + 0.3f);
        public event Action Entered;

        /// <summary>Builds one gate covering every cell centre in <paramref name="cells"/> (null if there are none).</summary>
        public static DungeonDoor Create(List<Vector2> cells, Transform parent)
        {
            if (cells == null || cells.Count == 0) return null;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in cells)
            {
                minX = Mathf.Min(minX, c.x - 0.5f);
                maxX = Mathf.Max(maxX, c.x + 0.5f);
                minY = Mathf.Min(minY, c.y - 0.5f);
                maxY = Mathf.Max(maxY, c.y + 0.5f);
            }
            var go = new GameObject("DungeonDoor");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((minX + maxX) * 0.5f, minY, 0f);
            var door = go.AddComponent<DungeonDoor>();
            door.area = Rect.MinMaxRect(minX, minY, maxX, maxY);
            door.sr = go.AddComponent<SpriteRenderer>();
            door.sr.sprite = Game.Art.Get("dgn_gate_closed");
            door.solid = go.AddComponent<BoxCollider2D>();
            door.solid.size = new Vector2(maxX - minX, maxY - minY);
            door.solid.offset = new Vector2(0f, (maxY - minY) * 0.5f);
            go.AddComponent<YSort>().Configure(true);
            return door;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Game.Audio.PlaySfx("door_open"); // [H3]
            sr.sprite = Game.Art.Get("dgn_gate_open");
            solid.enabled = false;
            Fx.Sparkle(area.center, 10, 1.2f);
            Fx.Burst("fx_dust", new Vector2(area.center.x, area.yMin + 0.2f), 6, 2f, 0.6f);
        }

        void Update()
        {
            if (!IsOpen || fired || !Game.IsPlaying) return;
            var p = Game.Player;
            if (p == null || p.IsDead) return;
            var r = new Rect(area.xMin - EnterMargin * 0.5f, area.yMin - EnterMargin, area.width + EnterMargin, area.height + EnterMargin);
            if (!r.Contains(p.Position)) return;
            fired = true;
            Entered?.Invoke();
        }
    }
}
