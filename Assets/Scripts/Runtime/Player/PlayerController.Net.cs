using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY NET] A member whose position comes from the network: on the host the copy of a remote human
    /// (the member moves on its own PC and reports where it stands), on a member PC everyone but the local
    /// player (puppets drawn from the host's snapshots). Attacks and skills still run on the body:
    /// the host copy reads them from its <see cref="NetworkInput"/>, a puppet gets them from
    /// <see cref="NetAct"/> events (visual only, puppet monsters take no damage there).
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>Teleport instead of walking when the reported spot is this far away (room change, blink).</summary>
        const float NetSnapDistance = 3f;

        Vector2 netTarget;
        bool netMoving;

        /// <summary>Position, facing and HP come from the network.</summary>
        public bool NetDriven { get; private set; }
        /// <summary>Member PC: drawn from snapshots only (HP and death are the host's).</summary>
        public bool NetPuppet { get; private set; }

        public void SetNetDriven(bool driven, bool puppet)
        {
            NetDriven = driven;
            NetPuppet = driven && puppet;
            netTarget = Position;
            netMoving = false;
            if (!driven) return;
            desiredVelocity = Vector2.zero;
            CancelMobility();
        }

        /// <summary>Where the network says this member stands now.</summary>
        public void NetMoveTo(Vector2 target, Facing facing)
        {
            if (!NetDriven) return;
            netMoving = (target - netTarget).sqrMagnitude > 0.0004f;
            netTarget = target;
            if ((target - Position).sqrMagnitude > NetSnapDistance * NetSnapDistance)
            {
                transform.position = target;
                body.position = target;
            }
            if (!combat.IsAttacking && !skills.IsCasting)
            {
                Facing = facing;
                AimDirection = facing.ToVector();
            }
        }

        /// <summary>HP as the host computed it (flash on loss, down at 0, up again when it rises from 0).</summary>
        public void NetSetHealth(int current, int max)
        {
            if (max <= 0) return;
            int before = health.Current;
            if (health.Max != max) health.SetMax(max, false);
            if (current <= 0)
            {
                if (!IsDead)
                {
                    health.Init(max, 0, stats.invulnerableTime);
                    Die();
                }
                return;
            }
            if (IsDead)
            {
                Revive(Mathf.Clamp01(current / (float)max));
                return;
            }
            if (current == before) return;
            health.Init(max, current, stats.invulnerableTime);
            if (current < before)
            {
                flash.Flash(0.1f);
                Game.Audio.PlaySfx("hurt", IsLocal ? 1f : 0.5f);
                if (IsLocal) Game.Camera?.Shake(0.12f, 0.18f);
            }
        }

        /// <summary>A puppet repeats an action the host saw (0 attack, 1 skill slot).</summary>
        public void NetAct(int kind, int skillSlot, Facing facing)
        {
            if (IsDead) return;
            Facing = facing;
            AimDirection = facing.ToVector();
            if (kind == 0) combat.TryAttack();
            else if (kind == 1 && skillSlot >= 0 && skillSlot < SkillGems.Slots)
            {
                Data.Mana = Data.Stats.MaxMp; // the cast already happened on the host; only the look is replayed
                skills.NetResetCooldown(skillSlot);
                skills.TryCast(skillSlot);
            }
        }

        /// <summary>Update for a network-driven member: no own movement, actions from the command (host copy).</summary>
        void NetUpdate(ActorCommand cmd)
        {
            desiredVelocity = Vector2.zero;
            bool stunned = Time.time < knockbackUntil;
            if (!NetPuppet && !stunned)
            {
                bool acting = cmd.attack || cmd.skillSlot >= 0;
                if (acting && cmd.aim.sqrMagnitude > 0.0001f && !combat.IsAttacking && !skills.IsCasting)
                    FaceTowards(Position + cmd.aim);
                if (cmd.attack) combat.TryAttack();
                if (cmd.skillSlot >= 0 && cmd.skillSlot < SkillGems.Slots) skills.TryCast(cmd.skillSlot);
            }
            if (combat.IsAttacking || skills.IsCasting) animator.Play(CharacterAnim.Attack, Facing);
            else if (netMoving && (netTarget - Position).sqrMagnitude > 0.0025f) animator.Play(CharacterAnim.Walk, Facing);
            else animator.Play(CharacterAnim.Idle, Facing);
        }

        void NetFixedStep()
        {
            Vector2 delta = netTarget - Position;
            float speed = Mathf.Max(stats.moveSpeed * 1.5f, delta.magnitude * 10f);
            body.SetVelocity(Vector2.zero);
            body.MovePosition(Vector2.MoveTowards(Position, netTarget, speed * Time.fixedDeltaTime));
        }
    }
}
