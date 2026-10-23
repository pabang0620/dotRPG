using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 닌자 casts split out of CareerCombat.Ninja.cs (500-line rule): 회수, 풍마 수리검, 기폭 쿠나이 (Docs/PLAN_CLASS_ROGUE.md 2.4).
    /// </summary>
    public sealed partial class CareerCombat
    {
        /// <summary>회수: every kunai of mine left within 8 m (up to 12) flies back to my hand, cutting what it passes; 1% MP each.</summary>
        IEnumerator Recall(Run c)
        {
            var mine = new List<KunaiProjectile>();
            foreach (var k in KunaiProjectile.Grounded)
                if (k != null && k.Owner == owner && Vector2.Distance(k.Position, owner.Center) <= c.n.range) mine.Add(k);
            mine.Sort((a, b) => (a.Position - owner.Center).sqrMagnitude.CompareTo((b.Position - owner.Center).sqrMagnitude));
            if (mine.Count > RecallMax) mine.RemoveRange(RecallMax, mine.Count - RecallMax);
            Pose(.2f, 1);
            Sound("c_dash", .6f);
            if (mine.Count == 0) { if (owner.IsLocal) GameEvents.RaiseToast(L.T("회수할 쿠나이가 없습니다.")); yield break; }
            EnemyController mark = c.n.spec == 'a' ? Target(c.dir, c.n.range, out _) : null; // [ROGUE SPEC] 표적 회수
            foreach (var k in mine) StartCoroutine(RecallOne(c, k, mark));
            yield return null;
        }

        IEnumerator RecallOne(Run c, KunaiProjectile k, EnemyController mark = null)
        {
            var hit = new HashSet<EnemyController>();
            Vector2 at = k.Position;
            float width = c.n.spec == 'b' && c.n.radius > 0f ? RecallWideHalf : .5f; // [ROGUE SPEC] 회오리 회수
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? RecallTargetMul : c.n.spec == 'b' ? RecallWideMul : 1f)));
            if (mark != null) { yield return RecallVia(c, k, mark, damage); at = k != null ? k.Position : at; hit.Add(mark); }
            k.Lift(owner.Center - at);
            while (k != null)
            {
                if (!Live(c)) { k.Vanish(); yield break; }
                Vector2 hand = owner.Center;
                Vector2 to = hand - at;
                float step = RecallSpeed * Time.deltaTime;
                Vector2 next = to.magnitude <= step ? hand : at + to.normalized * step;
                if (mark == null) foreach (var e in Corridor(at, next, width)) if (hit.Add(e)) Strike(c, e, damage, at, 1.5f, 0, "c_slash");
                k.Aim(to);
                k.Place(next);
                at = next;
                if (next == hand) break;
                yield return null;
            }
            if (k != null) k.Vanish();
            if (c.authority && owner != null && !owner.IsDead)
                owner.Data.Mana = Mathf.Min(owner.MaxMana, owner.Data.Mana + owner.MaxMana * RecallMpShare);
        }

        /// <summary>풍마 수리검: a big star out 7 m and back to me, piercing; the same monster is cut every 0.17 s, 6 times at most.</summary>
        IEnumerator Fuma(Run c)
        {
            Pose(.2f, 2);
            Sound("c_slash", .8f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? FumaBigMul : c.n.spec == 'b' ? FumaHoverMul : 1f))); // [ROGUE SPEC]
            StartCoroutine(FumaFlight(c, null, damage));
            ForClones(clone => StartCoroutine(FumaFlight(c, clone, CloneDamage(damage))));
            yield return null;
        }

        IEnumerator FumaFlight(Run c, NinjaClone clone, int damage)
        {
            Vector2 Origin() => clone != null ? clone.Center : owner.Center;
            Vector2 at = Origin() + c.dir * .4f;
            float size = c.n.radius * 2.2f;
            var star = CareerFx.Gen("n_fuma", "f_wave", at, Vector2.zero, size, size, 20f, VfxLayer.Top, false,
                clone != null ? CloneTint : SpecTint(c), 30f, true);
            float hoverFor = c.n.spec == 'b' && !float.IsNaN(c.n.duration) ? c.n.duration : FumaHover; // [ROGUE SPEC] 회전 풍마
            var count = new Dictionary<EnemyController, int>();
            var next = new Dictionary<EnemyController, float>();
            float travelled = 0f, hover = 0f;
            bool back = false;
            int hits = Mathf.Max(1, c.n.spec != '\0' && c.n.hits > 0 ? c.n.hits : c.s.hits);
            for (float guard = 0f; guard < 4f + hoverFor; guard += Time.deltaTime)
            {
                if (!Live(c)) break;
                Vector2 pos;
                if (!back)
                {
                    travelled += FumaSpeed * Time.deltaTime;
                    if (travelled >= c.n.range) { travelled = c.n.range; hover += Time.deltaTime; if (hover >= hoverFor) back = true; }
                    pos = Origin() + c.dir * (.4f + travelled);
                    if (ProjectileBlocked(pos)) back = true;
                }
                else
                {
                    Vector2 to = Origin() - at;
                    float step = FumaSpeed * 1.2f * Time.deltaTime;
                    if (to.magnitude <= step + .2f) break;
                    pos = at + to.normalized * step;
                }
                at = pos;
                star?.Place(at);
                foreach (var e in Enemies(at, c.n.radius))
                {
                    count.TryGetValue(e, out int n);
                    if (n >= hits || (next.TryGetValue(e, out float t) && Time.time < t)) continue;
                    if (Strike(c, e, damage, at, 1.5f, 0, "c_slash") || !c.authority) { count[e] = n + 1; next[e] = Time.time + CutGap; }
                }
                yield return null;
            }
            star?.Stop();
        }

        /// <summary>기폭 쿠나이: five tagged kunai around the target spot; each blows when a monster comes close, all after 2 s.</summary>
        IEnumerator Bomb(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            Pose(.2f, 2);
            Sound("swing", .8f);
            int count = Mathf.Max(1, c.n.spec != '\0' && c.n.projectiles > 0 ? c.n.projectiles : c.s.hits); // [ROGUE SPEC] 대형 기폭 1 / 기폭 지뢰밭 8
            float ring = count == 1 ? 0f : c.n.spec == 'b' ? BombFieldRing : BombRing;
            var tags = new List<(Vector2 at, VfxPlayer view)>();
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + Mathf.PI * .5f;
                Vector2 spot = point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                var view = CareerFx.Gen("n_tag", "f_spark", spot, Vector2.zero, .7f, .6f, 10f, VfxLayer.AtFeet, false, null, BombFuse + 1.5f, true);
                tags.Add((spot, view));
            }
            float fuse = Time.time + BombFuse;
            var left = new List<int>();
            for (int i = 0; i < tags.Count; i++) left.Add(i);
            while (left.Count > 0)
            {
                if (!Live(c)) { foreach (int i in left) tags[i].view?.Stop(); yield break; }
                for (int j = left.Count - 1; j >= 0; j--)
                {
                    int i = left[j];
                    if (Enemies(tags[i].at, BombTrigger).Count == 0) continue;
                    BombBlast(c, tags[i].at);
                    tags[i].view?.Stop();
                    left.RemoveAt(j);
                }
                if (Time.time >= fuse && left.Count > 0)
                {
                    int i = left[0];
                    BombBlast(c, tags[i].at);
                    tags[i].view?.Stop();
                    left.RemoveAt(0);
                    yield return new WaitForSeconds(BombChain);
                    continue;
                }
                yield return null;
            }
        }

        void BombBlast(Run c, Vector2 at)
        {
            CareerFx.Gen("m2_fire_explosion", "impact", at, Vector2.zero, c.n.radius * 2f, c.n.radius / 1.7f, 26f, VfxLayer.Top, false, Tint(c, NinjaOrange));
            ImpactSound("c_heavy", .7f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? BombOneMul : c.n.spec == 'b' ? BombFieldMul : 1f))); // [ROGUE SPEC]
            float root = c.n.spec == 'a' && !float.IsNaN(c.n.duration) ? c.n.duration : BombRoot;
            foreach (var e in Enemies(at, c.n.radius))
            {
                if (!Strike(c, e, damage, at, 3f, 1, "c_heavy") || !c.authority || e.IsDead) continue;
                e.Root(e.IsBoss ? root * BombRootBoss / BombRoot : root);
                CareerTrials.Record(owner, "rooted", 1);
            }
        }
    }
}
