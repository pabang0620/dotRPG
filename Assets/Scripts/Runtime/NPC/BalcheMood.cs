using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 발체 (the 사얍 child, npc id "balche") in three steps (Docs/ch3/WORLD_AND_STORY.md 4절):
    /// 1. after 3-3 until 3-5 (노호의 기록) she runs from the hero (keeps a few tiles away, comes back home when left alone);
    /// 2. after 3-5 she stands still and hands out her pebble errand (c3s_balche_toy, quest data);
    /// 3. after 3-11 (구출) she walks beside the hero in 사얍 (the festival of 3-끝 shows her lantern in the cutscene).
    /// Between 3-7 and 3-11 she is not in the village (the placement data hides her; this hides her too if it does not).
    /// </summary>
    public sealed class BalcheMood : MonoBehaviour
    {
        public const string NpcId = "balche";
        const string QuestSayab = "c3_sayab", QuestRecord = "c3_record", QuestDefense = "c3_defense", QuestKamatz = "c3_kamatz";
        const float FleeRange = 3.2f, FleeSpeed = 3.4f, HomeRadius = 5f, ReturnSpeed = 1.6f, FollowRange = 9f, FollowGap = 1.4f, FollowSpeed = 2.8f;

        enum Mood { Plain, Shy, Taken, Beside }

        Vector3 home;
        bool homeSet;

        /// <summary>Gives every 발체 in the world this component (the story watcher calls it once a second).</summary>
        public static void AttachAll()
        {
            foreach (var npc in NpcController.All)
                if (npc != null && npc.Definition != null && npc.Definition.npcId == NpcId && npc.GetComponent<BalcheMood>() == null)
                    npc.gameObject.AddComponent<BalcheMood>();
        }

        static Mood Current()
        {
            var j = Game.Session?.Journal;
            if (j == null || !Chapter3.Enabled) return Mood.Plain;
            if (j.IsCompleted(QuestKamatz)) return Mood.Beside;
            if (j.IsCompleted(QuestDefense)) return Mood.Taken;
            if (j.IsCompleted(QuestSayab) && !j.IsCompleted(QuestRecord)) return Mood.Shy;
            return Mood.Plain;
        }

        void Update()
        {
            if (!homeSet) { home = transform.position; homeSet = true; }
            var mood = Current();
            if (mood == Mood.Taken)
            {
                gameObject.SetActive(false); // back with the next map load once she is rescued
                return;
            }
            var player = Game.Player;
            if (player == null || !Game.IsWorldRunning) return;
            Vector2 me = transform.position, hero = player.Position;
            float dist = Vector2.Distance(me, hero);
            Vector2 target = me;
            float speed = 0f;
            if (mood == Mood.Shy)
            {
                if (dist < FleeRange)
                {
                    Vector2 away = (me - hero).sqrMagnitude > 0.001f ? (me - hero).normalized : Vector2.right;
                    target = me + away;
                    speed = FleeSpeed;
                }
                else if (dist > FleeRange * 2f) { target = home; speed = ReturnSpeed; }
            }
            else if (mood == Mood.Beside && dist < FollowRange && dist > FollowGap)
            {
                target = hero + (me - hero).normalized * FollowGap;
                speed = FollowSpeed;
            }
            if (speed <= 0f) return;
            Vector2 next = Vector2.MoveTowards(me, target, speed * Time.deltaTime);
            if (Vector2.Distance(next, home) > HomeRadius && mood == Mood.Shy) return; // she never runs off her corner of the square
            if (Game.World != null && !Game.World.IsFree(next)) return;
            transform.position = new Vector3(next.x, next.y, transform.position.z);
        }
    }
}
