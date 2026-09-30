using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Orthographic follow camera for pixel art:
    /// - picks an integer pixel scale for the current resolution (crisp pixels at 720p/1080p/1440p/4K),
    /// - smooth-follows the target and clamps to the map bounds,
    /// - supports screen shake (respecting the accessibility setting),
    /// - drifts between points of interest on the title screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        Camera cam;
        GameConfig config;
        Transform target;
        Vector3 velocity;
        Vector2 focus;
        int lastScreenHeight;
        float shakeUntil;
        float shakeStrength;

        int titleIndex;
        bool hasFocus;
        float titleTimer;

        public Camera Camera => cam;

        public static CameraFollow Create(GameConfig config)
        {
            var existing = UnityEngine.Camera.main;
            GameObject go = existing != null ? existing.gameObject : new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = existing != null ? existing : go.AddComponent<Camera>();
            if (go.GetComponent<AudioListener>() == null) go.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = config.backgroundColor;
            cam.nearClipPlane = -50f;
            cam.farClipPlane = 50f;
            go.transform.position = new Vector3(0f, 0f, -10f);

            // Explicit null check: GetComponent returns a "fake null" object in the editor, so ?? would not work.
            var follow = go.GetComponent<CameraFollow>();
            if (follow == null) follow = go.AddComponent<CameraFollow>();
            follow.cam = cam;
            follow.config = config;
            follow.UpdateZoom(true);
            return follow;
        }

        public void SetTarget(Transform t, bool snap)
        {
            target = t;
            if (snap && t != null)
            {
                focus = t.position + new Vector3(0f, 0.5f, 0f);
                velocity = Vector3.zero;
                ApplyPosition(focus, Vector2.zero);
            }
        }

        // [PARTY] > 0 while a companion's skill effect runs: only the local player's actions shake the screen.
        public static int ShakeMute;

        public void Shake(float strength, float duration)
        {
            if (ShakeMute > 0) return; // [PARTY]
            if (Game.Settings != null && !Game.Settings.Data.screenShake) return;
            shakeStrength = Mathf.Max(shakeStrength * (Time.unscaledTime < shakeUntil ? 1f : 0f), strength);
            shakeUntil = Time.unscaledTime + duration;
        }

        void UpdateZoom(bool force)
        {
            if (!force && Screen.height == lastScreenHeight) return;
            lastScreenHeight = Screen.height;
            int ppu = config.pixelsPerUnit;
            int scale = Mathf.Max(1, Mathf.RoundToInt((float)Screen.height / config.targetPixelHeight));
            cam.orthographicSize = Screen.height / (2f * ppu * scale);
        }

        void LateUpdate()
        {
            UpdateZoom(false);

            var state = Game.State != null ? Game.State.Current : GameState.Boot;
            Vector2 desired;
            if (state == GameState.Title || target == null)
            {
                desired = TitleDrift();
                if (!hasFocus) focus = desired;
                focus = Vector2.Lerp(focus, desired, 1f - Mathf.Exp(-0.6f * Time.unscaledDeltaTime));
            }
            else
            {
                desired = (Vector2)target.position + new Vector2(0f, 0.5f);
                Vector3 smoothed = Vector3.SmoothDamp(focus, desired, ref velocity, config.cameraSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                focus = smoothed;
            }

            hasFocus = true;
            Vector2 shake = Vector2.zero;
            if (Time.unscaledTime < shakeUntil)
                shake = Random.insideUnitCircle * shakeStrength;
            ApplyPosition(focus, shake);
        }

        Vector2 TitleDrift()
        {
            var world = Game.World;
            if (world == null || world.PointsOfInterest.Count == 0) return focus;
            titleTimer += Time.unscaledDeltaTime;
            if (titleTimer > 7f)
            {
                titleTimer = 0f;
                titleIndex = (titleIndex + 1) % world.PointsOfInterest.Count;
            }
            return world.PointsOfInterest[titleIndex % world.PointsOfInterest.Count];
        }

        void ApplyPosition(Vector2 center, Vector2 shake)
        {
            Vector2 pos = ClampToBounds(center);
            pos += shake;
            // Snap to the art pixel grid to avoid shimmering.
            float unitsPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
            float step = Mathf.Max(unitsPerPixel, 0.0001f);
            pos.x = Mathf.Round(pos.x / step) * step;
            pos.y = Mathf.Round(pos.y / step) * step;
            transform.position = new Vector3(pos.x, pos.y, -10f);
        }

        Vector2 ClampToBounds(Vector2 p)
        {
            if (Game.World == null) return p;
            var b = Game.World.Bounds;
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float x = b.width > halfW * 2f ? Mathf.Clamp(p.x, b.xMin + halfW, b.xMax - halfW) : b.center.x;
            float y = b.height > halfH * 2f ? Mathf.Clamp(p.y, b.yMin + halfH, b.yMax - halfH) : b.center.y;
            return new Vector2(x, y);
        }
    }
}
