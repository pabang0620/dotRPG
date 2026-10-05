using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Other people in the same town. While this character stands in a town (a safe, shared map) its look and
    /// position go to the chat server a few times a second (town.pos); the server relays them to everyone in the same
    /// town and chat channel, and this draws each of them as a walking body with a name tag (party members in green).
    /// Display only: no collisions, no combat, nothing saved. Hunting grounds are shared by the field party instead.
    /// </summary>
    public sealed class TownPeers : MonoBehaviour
    {
        const float SendGap = 0.2f, IdleResend = 2f, StaleSeconds = 8f;

        sealed class Peer
        {
            public PlayerController body;
            public TextMesh label, shadow;
            public string map, skin, weapon;
            public int cls = -1, career;
            public float seenAt;
        }

        static TownPeers instance;
        readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        float sentAt = -10f;
        Vector2 sentPos;
        Facing sentFacing;
        string sentMap;

        static TownPeers Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("TownPeers");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TownPeers>();
                GameEvents.MapEntered += instance.OnMapEntered;
                return instance;
            }
        }

        /// <summary>Is this map a town where people see each other?</summary>
        public static bool IsTown(string map) => MapRegistry.Get(map) is MapInfo m && m.safe && !m.instanced;

        // ---------------- sending ----------------

        /// <summary>Called by the chat service every frame while the socket is ready: my look and spot, throttled.</summary>
        public static void Send(ChatSocket socket, float now)
        {
            var player = Game.Player;
            string map = Game.Session?.MapId;
            if (player == null || string.IsNullOrEmpty(map) || !IsTown(map) || !player.gameObject.activeInHierarchy) return;
            var self = Instance;
            var pos = player.Position;
            bool changed = map != self.sentMap || (pos - self.sentPos).sqrMagnitude > 0.0025f || player.Facing != self.sentFacing;
            if (now - self.sentAt < (changed ? SendGap : IdleResend)) return;
            bool moving = map == self.sentMap && (pos - self.sentPos).sqrMagnitude > 0.0025f;
            self.sentAt = now;
            self.sentPos = pos;
            self.sentFacing = player.Facing;
            self.sentMap = map;
            var cls = Game.Session.PlayerClass;
            var frame = new Dictionary<string, object>
            {
                ["map_id"] = map,
                ["x"] = System.Math.Round(pos.x, 2),
                ["y"] = System.Math.Round(pos.y, 2),
                ["f"] = (int)player.Facing,
                ["m"] = moving,
                ["cls"] = (int)cls,
                ["career"] = (int)Game.Session.Progression.Career,
                ["skin"] = Game.Cosmetics?.SkinFor(cls) ?? "",
                ["weapon"] = Game.Session.Equipment[EquipSlot.Weapon] ?? "",
                ["level"] = Game.Session.Progression.Level,
            };
            socket.Send("town.pos", frame);
        }

        /// <summary>The socket dropped or the character changed: everyone goes, the next send starts fresh.</summary>
        public static void Reset()
        {
            if (instance == null) return;
            instance.sentMap = null;
            instance.ClearAll();
        }

        // ---------------- receiving ----------------

        public static void Apply(Dictionary<string, object> d)
        {
            string id = MiniJson.Str(d, "id");
            if (string.IsNullOrEmpty(id) || id == OnlineSession.Current?.ActiveCharacter) return;
            string map = MiniJson.Str(d, "map_id");
            var self = Instance;
            if (Game.Session == null || map != Game.Session.MapId || Game.Party == null) { self.Remove(id); return; }
            int cls = MiniJson.Int(d, "cls");
            int career = MiniJson.Int(d, "career");
            string skin = MiniJson.Str(d, "skin", "");
            string weapon = MiniJson.Str(d, "weapon", "");
            var at = new Vector2((float)MiniJson.Num(d, "x"), (float)MiniJson.Num(d, "y"));
            var facing = (Facing)Mathf.Clamp(MiniJson.Int(d, "f"), 0, 7);
            self.peers.TryGetValue(id, out var p);
            // A changed look (class, promotion, skin, weapon) rebuilds the body.
            if (p != null && (p.body == null || p.map != map || p.cls != cls || p.career != career || p.skin != skin || p.weapon != weapon))
            {
                self.Remove(id);
                p = null;
            }
            if (p == null)
            {
                p = self.Make(id, MiniJson.Str(d, "name", "모험가"), MiniJson.Int(d, "level", 1), (CharacterClass)cls, career, skin, weapon, at, facing);
                if (p == null) return;
                p.map = map;
                self.peers[id] = p;
            }
            p.seenAt = Time.unscaledTime;
            p.body.NetMoveTo(at, facing);
        }

        public static void Gone(string id) => instance?.Remove(id);

        Peer Make(string id, string name, int level, CharacterClass cls, int career, string skin, string weapon, Vector2 at, Facing facing)
        {
            if (!System.Enum.IsDefined(typeof(CharacterClass), cls)) return null;
            var data = CharacterData.CreateCompanion("town_" + id, name, cls, 100);
            data.Progression.Restore(new SaveData { level = Mathf.Max(1, level), xp = 0, career = new CareerSave { career = (Career)career } }, cls);
            if (!string.IsNullOrEmpty(weapon) && EquipmentDatabase.Get(weapon) != null) data.Equipment.Set(EquipSlot.Weapon, weapon);
            data.DisplayName = name;
            data.SkinId = skin ?? "";
            var body = PlayerController.CreateCompanion(Game.Config, transform, data, null);
            if (body.BodyCollider != null) body.BodyCollider.enabled = false; // walk through everyone
            body.Spawn(at, facing, int.MaxValue, 0);
            body.SetNetDriven(true, true);
            var p = new Peer { body = body, cls = (int)cls, career = career, skin = skin ?? "", weapon = weapon ?? "" };
            p.shadow = Label(body.transform, name, new Color32(20, 16, 24, 220), new Vector3(0.03f, 1.47f, 0f), 40);
            p.label = Label(body.transform, name, InMyParty(id) ? new Color32(140, 240, 150, 255) : new Color32(240, 244, 255, 255), new Vector3(0f, 1.5f, 0f), 41);
            return p;
        }

        static bool InMyParty(string characterId)
        {
            var party = PartyClient.Instance;
            if (party == null || !party.InParty) return false;
            foreach (var m in party.Members) if (m.characterId == characterId) return true;
            return false;
        }

        static TextMesh Label(Transform parent, string text, Color32 color, Vector3 local, int order)
        {
            var go = new GameObject("Name");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var tm = go.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.045f;
            tm.fontSize = 48;
            tm.text = text;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            var font = UIFont.Get();
            if (font != null)
            {
                tm.font = font;
                mr.sharedMaterial = font.material;
            }
            mr.sortingOrder = 400 + order;
            return tm;
        }

        void Remove(string id)
        {
            if (!peers.TryGetValue(id, out var p)) return;
            peers.Remove(id);
            if (p.body != null) Destroy(p.body.gameObject);
        }

        void ClearAll()
        {
            foreach (var p in peers.Values) if (p.body != null) Destroy(p.body.gameObject);
            peers.Clear();
        }

        void OnMapEntered(string map)
        {
            ClearAll();
            sentMap = null; // tell the new town right away
        }

        void Update()
        {
            if (peers.Count == 0) return;
            float now = Time.unscaledTime;
            List<string> stale = null;
            foreach (var kv in peers)
                if (kv.Value.body == null || now - kv.Value.seenAt > StaleSeconds) (stale ??= new List<string>()).Add(kv.Key);
            if (stale != null) foreach (var id in stale) Remove(id);
        }

        void OnDestroy()
        {
            GameEvents.MapEntered -= OnMapEntered;
            if (instance == this) instance = null;
        }
    }
}
