using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ARMOR] The game-wide mark of a broken guard (방어 붕괴, whatever caused it): a shield split in two halves floats
    /// over the monster's head, bobbing, its halves drifting apart a little, for as long as the armour stays broken.
    /// Attached by <see cref="EnemyController.BreakArmor"/>.
    /// </summary>
    public class ArmorBreakMark : MonoBehaviour
    {
        const int W = 14, H = 16;
        static Sprite left, right;

        EnemyController enemy;
        SpriteRenderer a, b;
        float age;

        public static void Attach(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead || !enemy.ArmorBroken || enemy.GetComponent<ArmorBreakMark>() != null) return;
            var m = enemy.gameObject.AddComponent<ArmorBreakMark>();
            m.enemy = enemy;
            m.a = Half(enemy.transform, true);
            m.b = Half(enemy.transform, false);
            m.LateUpdate();
        }

        static SpriteRenderer Half(Transform parent, bool isLeft)
        {
            var sr = new GameObject(isLeft ? "BrokenShieldL" : "BrokenShieldR").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = isLeft ? (left ??= Draw(true)) : (right ??= Draw(false));
            return sr;
        }

        /// <summary>One half of a heater shield (16 px = 1 unit) cut by a jagged crack down the middle.</summary>
        static Sprite Draw(bool isLeft)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = isLeft ? "BrokenShieldL" : "BrokenShieldR" };
            var px = new Color32[W * H];
            var rim = new Color32(40, 30, 34, 255);
            var face = new Color32(214, 76, 62, 255);
            var light = new Color32(255, 150, 120, 255);
            var steel = new Color32(235, 225, 210, 255);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Heater shield: flat top, sides curving into a point at the bottom (y = 0 is the bottom row).
                int inset = y < 6 ? (6 - y) : 0;
                bool inside = x >= inset && x < W - inset && y < H;
                // The crack zig-zags down the middle; each half keeps its own side of it.
                int crack = W / 2 + ((y / 2) % 2 == 0 ? 0 : -1);
                bool mine = isLeft ? x < crack : x >= crack + 1;
                if (!inside || !mine) { px[y * W + x] = new Color32(0, 0, 0, 0); continue; }
                bool edge = x == inset || x == W - 1 - inset || y == H - 1 || y == 0 || x == crack - 1 || x == crack + 1;
                Color32 c = edge ? rim : face;
                if (!edge && y == H - 3) c = steel;                         // the top band
                if (!edge && x == inset + 2 && y > 6 && y < H - 4) c = light; // a highlight
                px[y * W + x] = c;
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 16f);
        }

        void LateUpdate()
        {
            if (enemy == null) { Destroy(this); return; }
            if (enemy.IsDead || !enemy.ArmorBroken)
            {
                if (a != null) Destroy(a.gameObject);
                if (b != null) Destroy(b.gameObject);
                Destroy(this);
                return;
            }
            age += Time.deltaTime;
            float lift = 1.45f * enemy.Size + .06f * Mathf.Sin(age * 3f);
            float split = .04f + .03f * (.5f + .5f * Mathf.Sin(age * 2.2f));
            int order = YSort.OrderFor(enemy.Position.y) + 25;
            a.transform.localPosition = new Vector3(-split, lift, 0f);
            b.transform.localPosition = new Vector3(split, lift - .02f, 0f);
            a.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
            b.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            a.sortingOrder = b.sortingOrder = order;
        }
    }
}
