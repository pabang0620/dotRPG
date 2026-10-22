using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Story guests: a story character fighting beside the party for a while (쿠크 in the first two rooms of the
    /// 지바크 raid, 쿠크 and 카나 in the 사얍 방어전). Not saved and not part of the roster. When the party is full and
    /// <c>benchIfFull</c> is asked, the last hired AI sits out until the guest leaves; people are never pushed out.
    /// </summary>
    public sealed partial class PartyManager
    {
        readonly HashSet<string> guests = new HashSet<string>();
        readonly HashSet<string> benched = new HashSet<string>();

        public bool IsGuest(string mercId) => mercId != null && guests.Contains(mercId);

        /// <summary>Brings <paramref name="mercId"/> in as a guest (null: unknown, not in the world, or no seat).</summary>
        public PlayerController AddGuest(string mercId, bool benchIfFull)
        {
            var def = MercenaryDatabase.Get(mercId);
            if (def == null || Local == null || !LocalInWorld) return null;
            var have = Find(mercId);
            if (have != null) { guests.Add(mercId); return have; }
            if (members.Count >= MaxMembers && benchIfFull)
            {
                for (int i = members.Count - 1; i >= 1; i--)
                {
                    var m = members[i];
                    if (m == null || netMembers.Contains(m) || m.Data == null || guests.Contains(m.Data.MercenaryId)) continue;
                    benched.Add(m.Data.MercenaryId);
                    Despawn(m);
                    break;
                }
            }
            if (members.Count >= MaxMembers) return null;
            guests.Add(mercId);
            var member = Spawn(def, members.Count - 1);
            SyncCompanionLevels();
            Changed?.Invoke();
            return member;
        }

        /// <summary>Sends a guest away; a hired companion it replaced comes back.</summary>
        public void RemoveGuest(string mercId)
        {
            if (mercId == null || !guests.Remove(mercId)) return;
            var m = Find(mercId);
            if (m != null && !Game.Session.PartyRoster.Contains(mercId)) Despawn(m);
            if (benched.Count > 0)
            {
                benched.Clear();
                if (LocalInWorld) OnLocalSpawned(false);
            }
            Changed?.Invoke();
        }

        public void ClearGuests()
        {
            foreach (var id in new List<string>(guests)) RemoveGuest(id);
        }
    }
}
