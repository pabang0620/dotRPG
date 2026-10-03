using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY NET] Network identity and puppet mode. The host gives every monster a <see cref="NetId"/>
    /// and sends where it is; on a member PC the same monster is a puppet: no AI, no damage taken, it
    /// slides to the reported spot, plays the reported pose and dies when the host says so (the member
    /// then reports the kill to the server under its own account, through the normal death path).
    /// </summary>
    public partial class EnemyController
    {
        /// <summary>Host-assigned id within a run (0 = not yet assigned).</summary>
        public int NetId { get; set; }
        /// <summary>Member PC: driven by host snapshots.</summary>
        public bool Puppet { get; private set; }

        Vector2 puppetTarget;
        CharacterAnim puppetAnim;
        int puppetGoldHits = -1;

        /// <summary>Pose sent in snapshots.</summary>
        public CharacterAnim NetAnim => state == State.Windup ? CharacterAnim.Attack : animator != null ? animator.Current : CharacterAnim.Idle;
        public Facing NetFacing => facing;
        public bool NetFrozen => IsFrozen;

        public void MakePuppet()
        {
            Puppet = true;
            puppetTarget = Position;
            desiredVelocity = Vector2.zero;
            alertIcon.enabled = false;
            if (body != null) body.bodyType = RigidbodyType2D.Kinematic;
        }

        /// <summary>Host handover: this PC now runs the monster from where the last snapshot left it.</summary>
        public void ReleasePuppet()
        {
            if (!Puppet) return;
            Puppet = false;
            if (body != null && (Def == null || Def.kind != MonsterKind.Totem)) body.bodyType = RigidbodyType2D.Dynamic;
            EnterState(State.Chase);
        }

        /// <summary>One snapshot entry.</summary>
        public void ApplyNet(Vector2 position, Facing dir, CharacterAnim anim, int hp, int maxHp, bool frozen)
        {
            if (!Puppet || state == State.Dead) return;
            puppetTarget = position;
            if ((position - Position).sqrMagnitude > 9f) { transform.position = position; body.position = position; }
            facing = dir;
            puppetAnim = anim;
            if (maxHp > 0 && (health.Max != maxHp || health.Current != hp))
            {
                bool hurt = hp < health.Current;
                health.Init(maxHp, Mathf.Max(1, hp), 0f); // death comes as its own event
                if (hurt) flash.Flash(0.12f);
            }
            frozenUntil = frozen ? Time.time + 0.25f : 0f;
        }

        /// <summary>The host's monster died: same death as offline (loot report included).</summary>
        public void PuppetDie(int goldHits)
        {
            if (state == State.Dead) return;
            puppetGoldHits = goldHits;
            health.Init(health.Max, 0, 0f);
            Die();
        }

        /// <summary>Gold-runner hits for the kill report (the host counted them).</summary>
        int GoldHitsForReport => puppetGoldHits >= 0 ? puppetGoldHits : Behaviour is GoldRunnerBehaviour runner ? runner.GoldSpilled : 0;

        void PuppetUpdate()
        {
            animator.Renderer.color = IsFrozen ? new Color(0.6f, 0.85f, 1f, 1f) : Color.white;
            animator.Play(puppetAnim, facing);
            visual.localPosition = Vector3.zero;
        }

        void PuppetFixedStep()
        {
            Vector2 delta = puppetTarget - Position;
            float speed = Mathf.Max(4f, delta.magnitude * 10f);
            body.MovePosition(Vector2.MoveTowards(Position, puppetTarget, speed * Time.fixedDeltaTime));
        }
    }
}
