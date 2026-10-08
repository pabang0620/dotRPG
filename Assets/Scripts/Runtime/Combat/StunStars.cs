using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Three little stars circling over a stunned monster's head.</summary>
    public class StunStars : MonoBehaviour
    {
        EnemyController enemy;
        readonly SpriteRenderer[] stars = new SpriteRenderer[3];
        float age;

        public static void Attach(EnemyController enemy)
        {
            if (enemy == null || enemy.IsDead || !enemy.IsStunned || enemy.GetComponent<StunStars>() != null) return;
            var s = enemy.gameObject.AddComponent<StunStars>();
            s.enemy = enemy;
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Star");
                go.transform.SetParent(enemy.transform, false);
                s.stars[i] = go.AddComponent<SpriteRenderer>();
                s.stars[i].sprite = Game.Art.Get("fx_star");
            }
            s.LateUpdate();
        }

        void LateUpdate()
        {
            if (enemy == null) { Destroy(this); return; }
            if (enemy.IsDead || !enemy.IsStunned)
            {
                foreach (var s in stars) if (s != null) Destroy(s.gameObject);
                Destroy(this);
                return;
            }
            age += Time.deltaTime;
            int order = YSort.OrderFor(enemy.Position.y) + 20;
            for (int i = 0; i < 3; i++)
            {
                float a = age * 5f + i * Mathf.PI * 2f / 3f;
                stars[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.32f, 1.08f + Mathf.Sin(a) * 0.1f, 0f);
                stars[i].sortingOrder = order + (Mathf.Sin(a) < 0f ? 1 : -3);
            }
        }
    }
}
