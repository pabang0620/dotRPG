using UnityEngine;

namespace DotRPG
{
    /// <summary>Hides Rigidbody2D API renames between Unity versions (velocity → linearVelocity in Unity 6).</summary>
    public static class PhysicsCompat
    {
        public static Vector2 GetVelocity(this Rigidbody2D body)
        {
#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity;
#else
            return body.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody2D body, Vector2 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = velocity;
#else
            body.velocity = velocity;
#endif
        }

        /// <summary>Creates a top-down (no gravity, no rotation) rigidbody.</summary>
        public static Rigidbody2D AddTopDownBody(GameObject go, RigidbodyType2D type)
        {
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = type;
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            return body;
        }
    }
}
