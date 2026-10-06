using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace DotRPG
{
    /// <summary>
    /// [ANTI-ABUSE] The host checks each member's card against the server's view of that character (phase9 19.6):
    /// level, gear fingerprint and career. A card that disagrees is overwritten with the server values and the member
    /// is flagged (card_mismatch) in the host or observe report. The display name always comes from the server.
    /// </summary>
    public static class MemberCardCheck
    {
        sealed class View
        {
            public string name;
            public int level, career;
            public string gearHash;
            public readonly List<KeyValuePair<int, string>> worn = new List<KeyValuePair<int, string>>();
        }

        static readonly Dictionary<string, View> views = new Dictionary<string, View>();
        static readonly HashSet<string> mismatched = new HashSet<string>();
        static string room;

        /// <summary>Reads one entry of a field session or party run members[] (only the full views carry worn).</summary>
        public static void Read(string roomId, object m)
        {
            if (roomId != room) { room = roomId; views.Clear(); mismatched.Clear(); }
            string id = MiniJson.Str(m, "character_id");
            if (string.IsNullOrEmpty(id) || !MiniJson.Has(m, "level")) return;
            if (!views.TryGetValue(id, out var v)) views[id] = v = new View();
            v.level = MiniJson.Int(m, "level", v.level);
            v.gearHash = MiniJson.Str(m, "gear_hash", v.gearHash);
            if (MiniJson.Has(m, "name")) v.name = MiniJson.Str(m, "name");
            if (MiniJson.Has(m, "career")) v.career = MiniJson.Int(m, "career");
            var worn = MiniJson.Arr(m, "worn");
            if (worn != null)
            {
                v.worn.Clear();
                foreach (var w in worn) v.worn.Add(new KeyValuePair<int, string>(MiniJson.Int(w, "slot"), MiniJson.Str(w, "item_key")));
            }
        }

        public static bool Mismatched(string characterId) => !string.IsNullOrEmpty(characterId) && mismatched.Contains(characterId);

        /// <summary>
        /// Brings a human's card to the server values; true when anything had to change. A card sent later in the run
        /// (<paramref name="joining"/> false) may be a level or two ahead of a view read at the start, and its gear may
        /// have changed since, so only the join card's gear is checked and later levels get that margin.
        /// </summary>
        public static bool Correct(MemberCard card, bool joining)
        {
            if (card == null || card.IsAi || string.IsNullOrEmpty(card.characterId) || !views.TryGetValue(card.characterId, out var v)) return false;
            if (!string.IsNullOrEmpty(v.name)) card.name = v.name;
            bool wrong = false;
            int margin = joining ? 0 : 2;
            if (v.level > 0 && (card.level < v.level || card.level > v.level + margin)) { card.level = v.level; wrong = true; }
            if (joining && !string.IsNullOrEmpty(v.gearHash) && GearHash(card.gear) != v.gearHash)
            {
                wrong = true;
                var gear = new string[Equipment.SlotCount];
                foreach (var w in v.worn)
                    if (w.Key >= 0 && w.Key < gear.Length && EquipmentDatabase.Get(w.Value) != null) gear[w.Key] = w.Value;
                for (int i = 0; i < gear.Length; i++) card.gear[i] = gear[i] ?? "";
            }
            int career = card.career != null ? (int)card.career.career : 0;
            if (career != v.career)
            {
                wrong = true;
                card.career = new CareerSave { career = (Career)v.career };
            }
            if (wrong) mismatched.Add(card.characterId);
            return wrong;
        }

        /// <summary>Same as the server: worn keys sorted, joined by commas, SHA-256, first 16 hex characters.</summary>
        static string GearHash(string[] gear)
        {
            var keys = new List<string>();
            foreach (var g in gear) if (!string.IsNullOrEmpty(g)) keys.Add(g);
            keys.Sort(string.CompareOrdinal);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",", keys)));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
