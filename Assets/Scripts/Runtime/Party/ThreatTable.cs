using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Per-monster aggro (added to an <see cref="EnemyController"/> the first time it looks for a target).
    /// Damage from a party member adds damage × <see cref="CharacterData.ThreatMultiplier"/>; a warrior's
    /// 전쟁 함성 adds a big flat amount. The monster keeps its current target until someone else has
    /// <see cref="SwitchRatio"/> times its threat; dead or removed members are dropped. Without any
    /// threat it goes for the nearest alive member (solo: the local player, exactly as before parties).
    /// Also remembers the last member that hit it, for kill credit.
    /// </summary>
    public sealed class ThreatTable : MonoBehaviour
    {
        // ---------- Tuning ----------
        /// <summary>A new target must have this many times the current target's threat.</summary>
        public const float SwitchRatio = 1.2f;
        /// <summary>Flat threat a war cry puts on every monster it reaches…</summary>
        public const float WarCryThreat = 60f;
        /// <summary>…and at least this multiple of the monster's highest threat, so the shout always takes aggro.</summary>
        public const float WarCryOverTop = 1.5f;
        /// <summary>Members this close count a little extra (근접 보정).</summary>
        public const float CloseRange = 1.6f, CloseBonus = 3f;

        readonly Dictionary<PlayerController, float> threat = new Dictionary<PlayerController, float>();
        readonly List<PlayerController> drop = new List<PlayerController>();
        EnemyController enemy;

        /// <summary>The member this monster is after (null = nobody alive).</summary>
        public PlayerController Current { get; private set; }
        PlayerController forced; float forcedUntil;
        public PlayerController Forced => Time.time<forcedUntil && Alive(forced) ? forced : null;
        public void Force(PlayerController target,float duration) { forced=target;forcedUntil=Time.time+duration;AddWarCry(target);Current=target; }
        /// <summary>The party member that hit this monster last (kill credit).</summary>
        public PlayerController LastAttacker { get; private set; }

        /// <summary>The table of a monster, created (and hooked to its health) on first use.</summary>
        public static ThreatTable For(EnemyController enemy)
        {
            if (enemy == null) return null;
            var t = enemy.GetComponent<ThreatTable>();
            if (t != null) return t;
            t = enemy.gameObject.AddComponent<ThreatTable>();
            t.enemy = enemy;
            var health = enemy.GetComponent<Health>();
            if (health != null) health.Damaged += t.OnDamaged;
            return t;
        }

        void OnDamaged(DamageInfo info)
        {
            var member = info.AttackerMember;
            if (member == null) return;
            LastAttacker = member;
            Add(member, info.amount * member.Data.ThreatMultiplier);
            Game.Party?.RecordDamageDealt(member, info.amount);
        }

        public float ThreatOf(PlayerController member) => member != null && threat.TryGetValue(member, out float v) ? v : 0f;

        public void Add(PlayerController member, float amount)
        {
            if (member == null || amount <= 0f) return;
            threat[member] = ThreatOf(member) + amount;
        }

        /// <summary>Highest threat on this monster (0 when nobody has any).</summary>
        public float TopThreat
        {
            get
            {
                float top = 0f;
                foreach (var v in threat.Values) top = Mathf.Max(top, v);
                return top;
            }
        }

        /// <summary>War cry: large threat for <paramref name="member"/> so the monster turns on it.</summary>
        public void AddWarCry(PlayerController member)
        {
            if (member == null) return;
            float now = ThreatOf(member);
            threat[member] = Mathf.Max(now + WarCryThreat, TopThreat * WarCryOverTop);
        }

        /// <summary>Puts war-cry threat on every living monster within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static void WarCry(PlayerController member, Vector2 center, float radius)
        {
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && Vector2.Distance(e.Center, center) <= radius)
                    For(e).AddWarCry(member);
        }

        /// <summary>Forgets a member (left the party).</summary>
        public void Forget(PlayerController member)
        {
            threat.Remove(member);
            if (Current == member) Current = null;
            if (LastAttacker == member) LastAttacker = null;
        }

        static bool Alive(PlayerController m) => m != null && !m.IsDead && m.isActiveAndEnabled;

        float Effective(PlayerController m, Vector2 from) =>
            ThreatOf(m) + (Vector2.Distance(m.Position, from) <= CloseRange ? CloseBonus : 0f);

        /// <summary>Re-evaluates <see cref="Current"/> against the alive party members and returns it.</summary>
        public PlayerController Select(PartyManager party)
        {
            if (party == null) return Current = null;
            if(Forced != null && party.Contains(Forced)) return Current=Forced;
            drop.Clear();
            foreach (var m in threat.Keys) if (!Alive(m) || !party.Contains(m)) drop.Add(m);
            foreach (var m in drop) threat.Remove(m);
            if (Current != null && (!Alive(Current) || !party.Contains(Current))) Current = null;

            Vector2 from = enemy != null ? enemy.Position : (Vector2)transform.position;
            if (threat.Count == 0)
            {
                // Nobody has hurt it yet: whoever is nearest (the local player alone = old behaviour).
                Current = party.NearestAlive(from);
                return Current;
            }
            PlayerController top = null;
            float topValue = -1f;
            foreach (var m in party.Members)
            {
                if (!Alive(m)) continue;
                float v = Effective(m, from);
                if (v > topValue) { topValue = v; top = m; }
            }
            if (Current == null) Current = top;
            else if (top != null && top != Current && topValue >= Effective(Current, from) * SwitchRatio) Current = top;
            return Current;
        }
    }
}
