using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>Counters of one measured stretch (kills, XP, gold, drops, damage taken, potions, deaths).</summary>
        sealed class MeasureTally
        {
            public int kills, gold, drops, potions, deaths, xp0;
            public float damage;
            PlayerController tracked;
            readonly Inventory bag = Game.Session.Inventory;

            public MeasureTally()
            {
                xp0 = TotalXp();
                GameEvents.EnemyKilled += OnKill;
                bag.Changed += OnBag;
                Track();
            }

            void OnKill(string id) => kills++;

            void OnBag(string id, int total, int delta)
            {
                bool potion = id == ConsumableDatabase.HpPotion || id == ConsumableDatabase.HpPotionHi || id == ConsumableDatabase.MpPotion || id == ConsumableDatabase.MpPotionHi;
                if (potion) { if (delta < 0) potions -= delta; return; }
                if (delta <= 0) return;
                if (id == ConsumableDatabase.Gold) gold += delta;
                else drops += delta;
            }

            void OnDamaged(DamageInfo info) => damage += info.amount;

            /// <summary>Follows the hero's body (rebuilt on map and room changes).</summary>
            public void Track()
            {
                var p = Game.Player;
                if (p == tracked) return;
                if (tracked != null && tracked.Health != null) tracked.Health.Damaged -= OnDamaged;
                tracked = p;
                if (p != null && p.Health != null) p.Health.Damaged += OnDamaged;
            }

            public int Xp => TotalXp() - xp0;

            public void Close()
            {
                GameEvents.EnemyKilled -= OnKill;
                bag.Changed -= OnBag;
                if (tracked != null && tracked.Health != null) tracked.Health.Damaged -= OnDamaged;
            }
        }
    }
}
