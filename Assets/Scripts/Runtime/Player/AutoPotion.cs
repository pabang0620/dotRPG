using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [AUTO] 물약 자동 사용: when the local player's HP / MP drops below the percents in the settings, drinks a potion
    /// (the plain one first, then the greater one). One system for manual play and auto-progress (QuestAutoPilot leaves
    /// potions to it while it is on). Tells once when the bag runs out.
    /// </summary>
    public class AutoPotion : MonoBehaviour
    {
        const float CheckEvery = 0.25f, DrinkGap = 1.2f, OutOfPotionsToastGap = 60f;
        float nextCheck, nextHp, nextMp, hpWarnedAt = -999f, mpWarnedAt = -999f;

        /// <summary>The setting is on (default on).</summary>
        public static bool On => Game.Settings == null || Game.Settings.Data == null || Game.Settings.Data.autoPotion;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject("AutoPotion");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPotion>();
        }

        void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + CheckEvery;
            if (!On || !Game.IsWorldRunning || !(Game.IsPlaying || QuestAutoPilot.WindowOpenInRunningWorld)) return; // drinks behind an open window too (online)
            if (Game.MiniDungeon?.Active == true || Game.MiniDungeon?.Busy == true) return; // mini dungeons: no auto potions
            var p = Game.Player;
            if (p == null || !p.IsLocal || p.IsDead || p.Health == null || p.Health.Max <= 0) return;
            var data = Game.Settings != null ? Game.Settings.Data : null;
            int hpPct = data != null ? data.autoPotionHpPercent : 40, mpPct = data != null ? data.autoPotionMpPercent : 25;

            if (Time.time >= nextHp && p.Health.Current < p.Health.Max * hpPct / 100f)
            {
                nextHp = Time.time + DrinkGap;
                if (!p.AutoDrink(false) && Time.time - hpWarnedAt > OutOfPotionsToastGap)
                {
                    hpWarnedAt = Time.time;
                    GameEvents.RaiseToast(L.T("자동 물약: 체력 물약이 없습니다."));
                }
            }
            if (Time.time >= nextMp && p.MaxMana > 0 && p.Mana < p.MaxMana * mpPct / 100f)
            {
                nextMp = Time.time + DrinkGap;
                if (!p.AutoDrink(true) && Time.time - mpWarnedAt > OutOfPotionsToastGap)
                {
                    mpWarnedAt = Time.time;
                    GameEvents.RaiseToast(L.T("자동 물약: 마나 물약이 없습니다."));
                }
            }
        }
    }
}
