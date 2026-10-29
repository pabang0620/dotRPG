using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 잿빛 낙인 of 잿빛 손 지휘관 카마츠 (Docs/ch3/RAIDS.md 2): from phase 2, every <see cref="GreyBrand.Every"/> s one member
    /// is branded. Within <see cref="GreyBrand.Seconds"/> s they must stand within <see cref="GreyBrand.Reach"/> m of another member:
    /// beside a person the brand is wiped, beside only an AI companion half of it lands (35% of max HP), alone 70%.
    /// The meeting point (halfway to the nearest other member) is a teal safe circle and is handed to both through
    /// <see cref="RaidDoom"/>, so the companions and the measure bot walk there with their existing safe-spot lookup.
    /// Runs only where the boss is simulated (solo / host).
    /// </summary>
    public sealed partial class BossBrain
    {
        float nextBrandAt = -1f, brandUntil = -1f;
        PlayerController branded;
        Telegraph brandCircle;

        public bool BrandActive => brandUntil > 0f;

        void BrandTick()
        {
            if (!GreyBrand.UsedBy(Def.id) || Phase < 2) return;
            float now = Time.time;
            if (nextBrandAt < 0f) nextBrandAt = now + GreyBrand.FirstDelay;
            if (BrandActive)
            {
                if (branded == null || branded.IsDead) { EndBrand(); return; }
                var partner = NearestPartner(branded, out float d, true);
                // Beside a person the brand goes at once; beside an AI it holds to the end (then half lands).
                if (partner != null && d <= GreyBrand.Reach && !GreyBrand.IsAi(partner)) { ResolveBrand(); return; }
                if (now >= brandUntil) ResolveBrand();
                return;
            }
            if (now < nextBrandAt || Sealed || RuinActive || FateActive) return;
            nextBrandAt = now + GreyBrand.Every * (Heroic ? DungeonDatabase.RaidHeroicPatternSpeed : 1f);
            StartBrand();
        }

        void StartBrand()
        {
            var members = MonsterHits.All();
            if (members.Count == 0) return;
            // The person is branded first when there is one (the AI walk to them); else anyone.
            var people = members.FindAll(m => m != null && !m.IsDead && !GreyBrand.IsAi(m));
            var pool = people.Count > 0 ? people : members;
            branded = pool[E.Rng.Next(pool.Count)];
            float time = GreyBrand.Seconds * (Heroic ? .85f : 1f);
            brandUntil = Time.time + time;
            var partner = NearestPartner(branded, out _, false);
            Vector2 meet = partner != null ? FreeNear((branded.Position + partner.Position) * .5f) : branded.Position;
            brandCircle = Telegraph.SafeCircle(E, meet, GreyBrand.Reach * .5f, time + .3f);
            if (partner != null)
            {
                RaidDoom.Begin(time);
                RaidDoom.Assign(branded, meet);
                RaidDoom.Assign(partner, meet);
            }
            GreyBrand.Started++;
            Shout(ShoutGreyBrand);
            // [UI 2026-10-29] The real nickname (the local player was "나") and the meeting spot numbered on the ground with
            // both names; the party members get the same line and spot through the gimmick line message.
            string line = L.F("잿빛 낙인! {0}, 동료 곁으로", RaidDoom.NameOf(branded));
            WorldPopupText.Show(branded.transform, L.T("낙인"), GreyBrand.Ash, time, 1.9f);
            Announce?.Invoke(this, line, GreyBrand.Ash);
            if (partner != null)
            {
                string packed = RaidDoom.Pack(line, time, new[] { meet }, new[] { RaidDoom.NameOf(branded) + ", " + RaidDoom.NameOf(partner) });
                RaidDoom.Unpack(packed); // the spot label here
                RaidSeal.RaiseLine(E, packed); // and on the members' PCs
            }
            Game.Audio.PlaySfx("enemy_windup");
        }

        void ResolveBrand()
        {
            var target = branded;
            float d = float.MaxValue;
            var partner = target != null ? NearestPartner(target, out d, true) : null;
            float share = GreyBrand.AloneShare;
            if (partner != null && d <= GreyBrand.Reach) share = GreyBrand.IsAi(partner) ? GreyBrand.AiShare : 0f;
            if (share <= 0f) GreyBrand.Cleared++;
            else if (share < GreyBrand.AloneShare) GreyBrand.Halved++;
            else GreyBrand.Missed++;
            EndBrand();
            if (target == null || target.IsDead) return;
            if (share <= 0f)
            {
                Announce?.Invoke(this, L.T("낙인 상쇄!"), new Color(.6f, 1f, .9f));
                SkillVisuals.Flash(target.Center, new Color(.8f, .9f, 1f, .8f), 3f, .3f);
                return;
            }
            Announce?.Invoke(this, share < GreyBrand.AloneShare ? L.T("낙인 일부 상쇄") : L.T("낙인 폭발!"), DoomRed);
            SkillVisuals.Flash(target.Center, new Color(.6f, .6f, .65f, .9f), 4f, .35f);
            Game.Audio.PlaySfx("rock_break");
            int damage = Mathf.Max(1, Mathf.RoundToInt(target.Health.Max * share));
            MonsterHits.Apply(new[] { target }, damage, E.Position, 3f, true, E.gameObject);
        }

        /// <summary>The nearest other living member (judged where remote members last reported themselves when <paramref name="net"/>).</summary>
        static PlayerController NearestPartner(PlayerController self, out float distance, bool net)
        {
            distance = float.MaxValue;
            PlayerController best = null;
            Vector2 at = net ? self.NetTarget : self.Position;
            foreach (var m in MonsterHits.All())
            {
                if (m == null || m == self || m.IsDead) continue;
                float d = Vector2.Distance(at, net ? m.NetTarget : m.Position) - (net && (m.NetDriven || self.NetDriven) ? FateNetGrace : 0f);
                // A person counts before an AI at the same reach (the brand is wiped only beside a person).
                if (d < distance - .01f || (d <= GreyBrand.Reach && distance <= GreyBrand.Reach && best != null && GreyBrand.IsAi(best) && !GreyBrand.IsAi(m)))
                {
                    distance = d;
                    best = m;
                }
            }
            return best;
        }

        void EndBrand()
        {
            if (!BrandActive && brandCircle == null) return;
            brandUntil = -1f;
            branded = null;
            if (brandCircle != null && !brandCircle.Cancelled) brandCircle.Cancel();
            brandCircle = null;
            RaidDoom.End();
        }
    }

    /// <summary>[CH3] 잿빛 낙인 numbers and the measure counters.</summary>
    public static class GreyBrand
    {
        // ---------- Tuning (Docs/ch3/RAIDS.md 2) ----------
        public const float Every = 30f, FirstDelay = 6f, Seconds = 6f, Reach = 2f;
        /// <summary>Share of max HP that lands: beside only an AI companion, and alone.</summary>
        public const float AiShare = .35f, AloneShare = .7f;

        public static readonly Color Ash = new Color(.78f, .78f, .84f);

        public static bool UsedBy(string bossId) => bossId == MonsterDatabase.KamatzBoss;

        public static bool IsAi(PlayerController m) => RaidRift.IsAi(m);

        /// <summary>[MEASURE] Brands given / wiped beside a person / halved beside an AI / landed whole (reset by the measure run).</summary>
        public static int Started, Cleared, Halved, Missed;

        public static void ResetCounters() => Started = Cleared = Halved = Missed = 0;
    }
}
