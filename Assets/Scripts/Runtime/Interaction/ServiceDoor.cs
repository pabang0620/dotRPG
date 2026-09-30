using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Front door of a town service building (general store, smithy, warehouse): using it opens the
    /// same window as talking to the NPC who runs it.
    /// </summary>
    public class ServiceDoor : Interactable
    {
        NpcService service;
        string prompt = "들어가기";

        public override string Prompt => prompt;

        public static ServiceDoor Attach(GameObject building, NpcService service, string prompt)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(building.transform, false);
            var door = go.AddComponent<ServiceDoor>();
            door.service = service;
            door.prompt = prompt;
            door.ConfigureShape(Vector2.zero, 0.9f, new Vector2(0f, 1.7f));
            return door;
        }

        public override void Interact(PlayerController player)
        {
            var def = WorldBuilder.ServiceNpc(service);
            if (def != null) Game.UI.OpenService(def);
        }
    }

    /// <summary>Plays a looping sprite animation from sprite keys (the plaza fountain's water).</summary>
    public class SpriteCycler : MonoBehaviour
    {
        SpriteRenderer sr;
        string[] keys;
        float fps = 6f, time;
        int frame;

        public void Setup(string[] frameKeys, float framesPerSecond)
        {
            keys = frameKeys;
            fps = framesPerSecond;
            sr = GetComponent<SpriteRenderer>();
            time = Random.value * 3f;
        }

        void Update()
        {
            if (sr == null || keys == null || keys.Length == 0) return;
            time += Time.deltaTime * fps;
            int f = (int)time % keys.Length;
            if (f == frame) return;
            frame = f;
            sr.sprite = Game.Art.Get(keys[f]);
        }
    }
}
