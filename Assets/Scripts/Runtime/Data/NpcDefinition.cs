using System;
using UnityEngine;

namespace DotRPG
{
    public enum NpcBehaviour
    {
        /// <summary>Stands still, looks around.</summary>
        Idle,
        /// <summary>Strolls around its home position.</summary>
        Wander,
        /// <summary>Walks back and forth between home and home + patrolOffset.</summary>
        Patrol,
        /// <summary>Stands in place and periodically uses its tool (chopping, mining, watering...).</summary>
        Work,
    }

    public enum NpcTool
    {
        None,
        Axe,
        Pickaxe,
        WateringCan,
        FishingRod,
        Hammer,
        Crate,
    }

    /// <summary>
    /// One NPC placed on the map. The map file references it by <see cref="mapSymbol"/>.
    /// </summary>
    [Serializable]
    public class NpcDefinition
    {
        [Tooltip("Single character used in the map text file.")]
        public string mapSymbol = "1";
        public string npcId = "villager";
        public string displayName = "주민";
        public CharacterLook look = new CharacterLook();
        public NpcBehaviour behaviour = NpcBehaviour.Idle;
        public NpcTool tool = NpcTool.None;
        public Facing initialFacing = Facing.Down;
        public Vector2 patrolOffset = Vector2.zero;
        [Tooltip("Dialogue id from Dialogues.json. Quest NPCs are overridden by the QuestManager.")]
        public string dialogueId = "";
        [Tooltip("Optional dialogue after the main quest is complete.")]
        public string dialogueIdAfterQuest = "";

        public NpcDefinition() { }

        public NpcDefinition(string symbol, string id, string name, CharacterLook look, NpcBehaviour behaviour,
            NpcTool tool, Facing facing, string dialogue, string afterQuest = "", Vector2 patrol = default)
        {
            mapSymbol = symbol;
            npcId = id;
            displayName = name;
            this.look = look;
            this.behaviour = behaviour;
            this.tool = tool;
            initialFacing = facing;
            dialogueId = dialogue;
            dialogueIdAfterQuest = afterQuest;
            patrolOffset = patrol;
        }
    }
}
