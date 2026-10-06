using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Pluggable fighting style of a dungeon monster (added by <see cref="EnemyController.ApplyDefinition"/>).
    /// The base EnemyController keeps idle / wander / notice / return-home / hit stagger; once the monster
    /// is chasing, <see cref="Tick"/> owns the frame. Actions run as coroutines through <see cref="Run"/>:
    /// their waits (<see cref="Hold"/>) pause while the monster is stunned, frozen or the game is paused,
    /// and <see cref="Interrupt"/> stops the action and cancels its telegraphs (stagger, groggy, death).
    /// Decisions use <see cref="EnemyController.Rng"/> (seeded per spawn) so they can be replayed.
    /// </summary>
    public abstract class MonsterBehaviour : MonoBehaviour
    {
        protected EnemyController E;
        protected MonsterDef Def;
        readonly List<Telegraph> telegraphs = new List<Telegraph>();
        Coroutine action;
        protected float nextSkillAt;

        /// <summary>How fast the body reaches its wanted velocity (dashes use a lot more).</summary>
        public virtual float Acceleration => dashing ? 400f : 30f;
        /// <summary>Hits never stagger / push it right now (attacks, guard).</summary>
        public virtual bool SuperArmorNow => false;
        /// <summary>This particular hit is shrugged off (e.g. taken on the shield).</summary>
        public virtual bool ArmorAgainst(DamageInfo info) => SuperArmorNow;
        /// <summary>An action (attack, skill) is running.</summary>
        public bool Busy => action != null;

        public static MonsterBehaviour Attach(EnemyController e, MonsterDef def)
        {
            MonsterBehaviour b;
            switch (def.kind)
            {
                case MonsterKind.GoldRunner: b = e.gameObject.AddComponent<GoldRunnerBehaviour>(); break;
                case MonsterKind.Charger: b = e.gameObject.AddComponent<ChargerBehaviour>(); break;
                case MonsterKind.Necro: b = e.gameObject.AddComponent<NecroBehaviour>(); break;
                case MonsterKind.Archer: b = e.gameObject.AddComponent<ArcherBehaviour>(); break;
                case MonsterKind.ShieldGuard: b = e.gameObject.AddComponent<ShieldGuardBehaviour>(); break;
                case MonsterKind.Knight: b = e.gameObject.AddComponent<KnightBehaviour>(); break;
                case MonsterKind.Totem: b = e.gameObject.AddComponent<TotemBehaviour>(); break;
                case MonsterKind.Boss: b = e.gameObject.AddComponent<BossBrain>(); break;
                default: return null; // plain melee: the base EnemyController AI
            }
            b.E = e;
            b.Def = def;
            e.Died += b.OnDied;
            b.OnInit();
            return b;
        }

        protected virtual void OnInit() { }

        /// <summary>Called every frame the monster may act. <paramref name="engaged"/> = in the chase state. True = frame handled.</summary>
        public virtual bool Tick(bool engaged)
        {
            // A running action always owns the frame (even if it was started from idle by a script).
            if (Busy) return true;
            if (!engaged) return false;
            var target = E.MonsterTarget;
            if (target == null)
            {
                E.GiveUp();
                return false;
            }
            // [BALANCE] Just spotted the player: walk in and size them up before the first attack.
            if (E.HoldingFirstStrike && !(E.Def != null && E.Def.boss)) { E.ApproachOnly(target); return true; }
            Engaged(target, Vector2.Distance(E.Position, target.Position));
            return true;
        }

        /// <summary>Chasing <paramref name="target"/> and no action running: move / start an action.</summary>
        protected abstract void Engaged(PlayerController target, float dist);

        /// <summary>Before HP is taken: may change the hit (shield) or refuse it (false).</summary>
        public virtual bool BeforeDamage(ref DamageInfo info) => true;

        public virtual void OnHit(DamageInfo info) { }

        /// <summary>The groggy gauge broke: stop what it was doing. Returns the groggy length.</summary>
        public virtual float OnGroggyBroken()
        {
            Interrupt();
            return EnemyController.GroggySeconds;
        }

        /// <summary>Stops the running action and cancels its unresolved telegraphs.</summary>
        public virtual void Interrupt()
        {
            if (action != null) StopCoroutine(action);
            action = null;
            dashing = false;
            if (E != null && !E.IsDead) E.MonsterAnim(CharacterAnim.Idle);
            foreach (var t in telegraphs) if (t != null) t.Cancel();
            telegraphs.Clear();
            OnInterrupted();
        }

        protected virtual void OnInterrupted() { }

        protected virtual void OnDied(EnemyController e) => Interrupt();

        // =============================== Helpers ===============================

        protected void Run(IEnumerator routine)
        {
            if (action != null) StopCoroutine(action);
            action = StartCoroutine(Pump(routine));
        }

        /// <summary>Runs nested IEnumerators itself, so stopping the outer coroutine stops everything.</summary>
        IEnumerator Pump(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator sub)
                {
                    stack.Push(sub);
                    continue;
                }
                yield return top.Current;
            }
            action = null;
        }

        /// <summary>Waits <paramref name="seconds"/> of time in which the monster could act.</summary>
        protected IEnumerator Hold(float seconds, CharacterAnim anim = CharacterAnim.Idle, bool windup = false)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (E.CanAct)
                {
                    t += Time.deltaTime;
                    E.MonsterHalt();
                    E.MonsterAnim(anim, windup);
                }
                yield return null;
            }
            E.MonsterAnim(anim, false);
        }

        protected Telegraph Track(Telegraph t)
        {
            telegraphs.RemoveAll(x => x == null || x.Resolved || x.Cancelled);
            telegraphs.Add(t);
            return t;
        }

        protected float NextFloat(float min, float max) => min + (float)E.Rng.NextDouble() * (max - min);

        protected static Vector2 Dir(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.down;
        }

        protected static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        protected Vector2 ChestOf(EnemyController e) => e.Center;

        /// <summary>A free point near <paramref name="p"/> (falls back to the monster's own position).</summary>
        protected Vector2 FreeNear(Vector2 p)
        {
            if (Game.World == null || Game.World.IsFree(p)) return p;
            for (int i = 1; i <= 8; i++)
            {
                Vector2 q = Vector2.Lerp(p, E.Position, i / 8f);
                if (Game.World.IsFree(q)) return q;
            }
            return E.Position;
        }

        /// <summary>Walks in <paramref name="dir"/>, sliding along walls (tries ±60° / ±100°).</summary>
        protected void MoveAvoiding(Vector2 dir, float speed)
        {
            if (Game.World != null)
            {
                foreach (float a in new[] { 0f, 60f, -60f, 100f, -100f })
                {
                    Vector2 d = Rotate(dir, a);
                    if (Game.World.IsFree(E.Position + d * 0.7f))
                    {
                        E.MonsterMove(d, speed);
                        return;
                    }
                }
                E.MonsterHalt();
                return;
            }
            E.MonsterMove(dir, speed);
        }

        /// <summary>Plain melee swing (circle in front), like the base skeleton's but through a telegraph-free hit.</summary>
        protected IEnumerator Swing(PlayerController target, float windup, float reach, float mul, float recover)
        {
            E.MonsterFace(target.Position);
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(windup, CharacterAnim.Attack, true);
            Vector2 dir = E.MonsterFacing.ToVector();
            Vector2 hit = E.Center + dir * 0.55f * E.Size;
            Game.Audio.PlaySfx("enemy_attack");
            Fx.Spawn("fx_slash", hit, Vector2.zero, 0f, 0.15f, 0f, 10);
            MonsterHits.Apply(MonsterHits.InCircle(hit, reach), E.MonsterDamage(mul), E.Position, E.Stats.attackKnockback, false, E.gameObject);
            yield return Hold(recover, CharacterAnim.Attack);
        }

        /// <summary>
        /// Line telegraph, then a dash along it. Members touched by the body during the dash take an
        /// unblockable hit (once each). Stops early at walls.
        /// </summary>
        protected IEnumerator Charge(Vector2 dir, float length, float width, float windup, float speed, float contact, float mul, float knock, float recover)
        {
            E.MonsterFace(E.Position + dir);
            var t = Track(Telegraph.Rect(E, E.Position, dir, length, width, windup, 0));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(windup, CharacterAnim.Attack, true);
            if (t != null && !t.Cancelled) t.ResolveNow();
            dashing = true;
            Game.Audio.PlaySfx("enemy_attack");
            var hit = new HashSet<PlayerController>();
            Vector2 start = E.Position;
            float stuck = 0f, time = 0f, maxTime = length / speed + 0.3f;
            Vector2 last = E.Position;
            while (Vector2.Distance(start, E.Position) < length && time < maxTime)
            {
                if (E.CanAct)
                {
                    time += Time.deltaTime;
                    E.MonsterMove(dir, speed);
                    E.MonsterAnim(CharacterAnim.Walk);
                    foreach (var m in MonsterHits.InCircle(E.Center, contact))
                    {
                        if (!hit.Add(m)) continue;
                        MonsterHits.Apply(new[] { m }, E.MonsterDamage(mul), E.Position - dir, knock, true, E.gameObject);
                    }
                    if (Time.frameCount % 3 == 0) Fx.Dust(E.Position);
                    stuck = Vector2.Distance(last, E.Position) < speed * Time.deltaTime * 0.15f ? stuck + Time.deltaTime : 0f;
                    last = E.Position;
                    if (stuck > 0.12f) break;
                }
                yield return null;
            }
            dashing = false;
            E.MonsterHalt();
            ChargeHits = hit.Count;
            yield return Hold(recover, CharacterAnim.Idle);
        }

        protected bool dashing;
        /// <summary>Members hit by the last charge (tests).</summary>
        public int ChargeHits { get; protected set; }
    }
}
