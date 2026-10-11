using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Raid-wide mechanics shared by every raid boss (Docs/PLAN_RAID_MECHANICS.md): enrage at 40% HP and the
    /// "이대론 죽을 수 없다!" roar (safe circles, then damage + stun on everyone outside them). All tuning lives here.
    /// The boss logic is in BossBrain.Raid.cs (host / solo only); this class also draws the roar on any PC, so a
    /// party member renders the same thing from the host's <see cref="PartyMsg.RaidRoar"/>.
    /// </summary>
    public static class RaidMechanics
    {
        // ---------- Tuning ----------
        /// <summary>HP ratio at or under which a raid boss enrages (once).</summary>
        public const float EnrageAt = .4f;
        /// <summary>Boss damage while enraged (speed / cooldown use BossBrain.EnrageSpeed / EnrageCooldown).</summary>
        public const float EnrageDamage = 1.3f;
        /// <summary>First roar this long after the enrage, then every <see cref="RoarEvery"/> s.</summary>
        public const float RoarFirstDelay = 3f, RoarEvery = 30f;
        /// <summary>Readable cast: the speech line, the 3-2-1 count and the safe circles show for this long.</summary>
        public const float RoarCast = 3.5f;
        /// <summary>Penalty for anyone caught outside: share of max HP (unblockable) and stun seconds.</summary>
        public const float RoarDamageShare = .2f, RoarStun = 4f;
        /// <summary>Safe circles: how many, their radius (drawn at exactly this size) and how far from the boss.</summary>
        public const int SafeCircles = 3;
        public const float SafeRadius = 1.8f, SafeSpread = 8f;
        public const string RoarLine = "이대론 죽을 수 없다!";

        static readonly Color Gold = new Color(1f, .85f, .3f, .95f);
        static readonly Color Red = new Color(1f, .3f, .25f);

        /// <summary>Host: a roar started (boss, cast seconds, safe circle centres). PartyNet relays it to members.</summary>
        public static event Action<EnemyController, float, Vector2[]> RoarStarted;
        /// <summary>Host: a member was stunned by a raid mechanic (member, seconds). PartyNet relays it.</summary>
        public static event Action<PlayerController, float> MemberStunned;

        public static void RaiseRoar(EnemyController boss, float cast, Vector2[] circles) => RoarStarted?.Invoke(boss, cast, circles);
        public static void RaiseStun(PlayerController m, float seconds) => MemberStunned?.Invoke(m, seconds);

        /// <summary>True when <paramref name="p"/> stands inside one of the circles.</summary>
        public static bool InSafeCircle(Vector2 p, Vector2[] circles)
        {
            foreach (var c in circles) if (((Vector2)p - c).sqrMagnitude <= SafeRadius * SafeRadius) return true;
            return false;
        }

        /// <summary>Draws the roar on this PC: speech line, 3-2-1 announce, red screen edge and the safe circles.</summary>
        public static void ShowRoar(EnemyController boss, float cast, Vector2[] circles)
        {
            if (boss != null)
                WorldPopupText.Show(boss.transform, RoarLine, Red, cast, 1.4f + 1.1f * boss.Size);
            foreach (var c in circles)
            {
                SkillFx.Spawn("fx_ring", c, Gold, cast + .2f, SkillFx.GroundOrder + 14)
                    .Scale(new Vector2(SafeRadius, SafeRadius * .7f), new Vector2(SafeRadius, SafeRadius * .7f)).Fade(FxFade.Late);
                SkillFx.Spawn("fx_glow", c, new Color(1f, .9f, .45f, .35f), cast + .2f, SkillFx.GroundOrder + 13).Additive()
                    .Scale(new Vector2(SafeRadius * 1.7f, SafeRadius * 1.2f), new Vector2(SafeRadius * 1.9f, SafeRadius * 1.3f)).Fade(FxFade.Late);
            }
            var runner = new GameObject("RaidRoarFx").AddComponent<RoarPresentation>();
            runner.Begin(boss != null ? boss.GetComponent<BossBrain>() : null, cast);
            Game.Audio?.PlaySfx("enemy_windup");
        }

        /// <summary>The countdown announce and the pulsing red screen edge for one roar.</summary>
        sealed class RoarPresentation : MonoBehaviour
        {
            BossBrain brain;
            float cast, started;
            int lastCount = -1;
            readonly Image[] edges = new Image[4];

            public void Begin(BossBrain b, float seconds)
            {
                brain = b;
                cast = seconds;
                started = Time.time;
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 900;
                // Four edge bands (left, right, bottom, top) that pulse red.
                for (int i = 0; i < 4; i++)
                {
                    var img = UIFactory.Image(transform, "Edge" + i, Game.Art.Get("ui_white"), Color.clear);
                    img.preserveAspect = false;
                    img.raycastTarget = false;
                    var rt = img.rectTransform;
                    bool vertical = i < 2;
                    rt.anchorMin = i == 1 ? new Vector2(1f, 0f) : i == 3 ? new Vector2(0f, 1f) : Vector2.zero;
                    rt.anchorMax = i == 0 ? new Vector2(0f, 1f) : i == 2 ? new Vector2(1f, 0f) : Vector2.one;
                    rt.pivot = new Vector2(i == 1 ? 1f : 0f, i == 3 ? 1f : 0f);
                    rt.sizeDelta = vertical ? new Vector2(70f, 0f) : new Vector2(0f, 60f);
                    rt.anchoredPosition = Vector2.zero;
                    edges[i] = img;
                }
            }

            void Update()
            {
                float t = Time.time - started;
                if (t >= cast + .25f) { Destroy(gameObject); return; }
                int count = Mathf.Clamp(Mathf.CeilToInt(cast - t), 0, 9);
                if (count != lastCount && t < cast)
                {
                    lastCount = count;
                    if (brain != null) BossBrain.RaiseAnnounce(brain, $"{RoarLine}  안전지대로! {count}", Red);
                }
                float a = (.25f + .25f * Mathf.Sin(t * 12f)) * Mathf.Clamp01((cast + .25f - t) / .25f);
                foreach (var e in edges) e.color = new Color(1f, .1f, .08f, a);
            }
        }
    }
}
