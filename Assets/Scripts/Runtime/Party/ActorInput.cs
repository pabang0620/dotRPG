using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// What a party member wants to do this frame. Produced by an <see cref="IActorInput"/> (keyboard,
    /// AI, later the network) and consumed by <see cref="PlayerController"/> / <see cref="PlayerCombat"/>.
    /// </summary>
    public struct ActorCommand
    {
        /// <summary>Movement, each axis -1..1 (length ≤ 1).</summary>
        public Vector2 move;
        /// <summary>Aim direction for attacks and skills (zero = keep the current facing).</summary>
        public Vector2 aim;
        /// <summary>Turn towards <see cref="aim"/> before attacking or casting (AI members; the local player faces where it walks).</summary>
        public bool faceAim;
        public bool attack;
        public bool mobility;
        public bool interact;
        /// <summary>Skill slot to cast (0-4), -1 = none.</summary>
        public int skillSlot;
        public bool useHealing;
        public bool useMana;
        public bool townScroll;

        public static ActorCommand None => new ActorCommand { skillSlot = -1 };
    }

    /// <summary>Source of a member's commands.</summary>
    public interface IActorInput
    {
        /// <summary>Called once per frame while the game is playing.</summary>
        ActorCommand Read(PlayerController self);
    }

    /// <summary>Keyboard / gamepad of this PC (exactly the keys the player used before parties existed).</summary>
    public sealed class LocalInput : IActorInput
    {
        public ActorCommand Read(PlayerController self)
        {
            var input = Game.Input;
            var cmd = ActorCommand.None;
            if (input == null) return cmd;
            cmd.move = input.Move;
            cmd.aim = input.Move;
            cmd.attack = input.AttackPressed;
            cmd.mobility = input.MobilityPressed;
            cmd.interact = input.InteractPressed;
            cmd.useHealing = input.UseItemPressed;
            cmd.useMana = input.UseManaPressed;
            cmd.townScroll = input.TownScrollPressed;
            for (int s = 0; s < SkillGems.Slots; s++)
                if (input.SkillPressed(s)) { cmd.skillSlot = s; break; }
            return cmd;
        }
    }

    /// <summary>
    /// Input driven by code (automated tests): set <see cref="Next"/> every frame, or a held
    /// <see cref="Move"/> plus one-shot presses that are cleared after being read.
    /// </summary>
    public sealed class ScriptedInput : IActorInput
    {
        public Vector2 Move;
        public Vector2 Aim;
        bool attack, mobility;
        int skill = -1;

        public void PressMobility() => mobility = true;
        public void PressAttack() => attack = true;
        public void PressSkill(int slot) => skill = slot;

        public ActorCommand Read(PlayerController self)
        {
            var cmd = ActorCommand.None;
            cmd.move = Vector2.ClampMagnitude(Move, 1f);
            cmd.aim = Aim.sqrMagnitude > 0.0001f ? Aim : Move;
            cmd.faceAim = Aim.sqrMagnitude > 0.0001f;
            cmd.attack = attack;
            cmd.mobility = mobility;
            mobility = false;
            cmd.skillSlot = skill;
            attack = false;
            skill = -1;
            return cmd;
        }
    }
}
