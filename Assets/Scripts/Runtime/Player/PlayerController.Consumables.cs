using UnityEngine;

namespace DotRPG
{
    public partial class PlayerController
    {
        float lockedUntil;
        readonly System.Collections.Generic.Dictionary<string, float> potionReadyAt = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>Holds the character still (reading the return scroll).</summary>
        public void LockMovement(float seconds)
        {
            CancelMobility();
            lockedUntil = Time.time + seconds;
        }

        /// <summary>The potion key: the plain potion first, the greater one (cash) only once the plain ones run out.</summary>
        public void UseHealing()
        {
            if (!IsLocal) return;
            if (Game.Session.Inventory.Count(ConsumableDatabase.HpPotion) > 0) UseConsumable(ConsumableDatabase.HpPotion);
            else if (Game.Session.Inventory.Count(ConsumableDatabase.HpPotionHi) > 0) UseConsumable(ConsumableDatabase.HpPotionHi);
            else GameEvents.RaiseToast(L.T("체력 물약이 없습니다. 마을 잡화상인에게서 물약을 사세요."));
        }

        /// <summary>[AUTO] Auto-potion: the plain potion first, then the greater one; false (silently) when the bag has none.</summary>
        public bool AutoDrink(bool mana)
        {
            if (!IsLocal || IsDead || Game.Session == null) return false;
            var bag = Game.Session.Inventory;
            string plain = mana ? ConsumableDatabase.MpPotion : ConsumableDatabase.HpPotion;
            string greater = mana ? ConsumableDatabase.MpPotionHi : ConsumableDatabase.HpPotionHi;
            string id = bag.Count(plain) > 0 ? plain : bag.Count(greater) > 0 ? greater : null;
            return id != null && UseConsumable(id);
        }

        /// <summary>[PORTAL] True while a hit is shoving the character (knockback), not its own walking.</summary>
        public bool KnockedBack => Time.time < knockbackUntil;

        /// <summary>[PORTAL] Where the character itself is trying to walk (zero while standing still).</summary>
        public Vector2 MoveIntent => desiredVelocity;

        /// <summary>Uses one potion or scroll from the bag (local player only). Returns true when it was used up.</summary>
        public bool UseConsumable(string id)
        {
            var item = ConsumableDatabase.Get(id);
            if (item == null || IsDead || !IsLocal) return false;
            var bag = Game.Session.Inventory;
            if (bag.Count(id) <= 0)
            {
                string josa = L.Josa(item.name, "이", "가"); // i18n-ignore Korean particle (empty in English)
                GameEvents.RaiseToast(L.F("{0}{1} 없습니다. 마을 잡화상인에게서 살 수 있습니다.", L.T(item.name), josa));
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            switch (item.kind)
            {
                case ConsumableKind.HealHp:
                {
                    if (health.Current >= health.Max) { GameEvents.RaiseToast(L.T("체력이 가득 차 있습니다.")); return false; }
                    if (!PotionReady(id)) return false;
                    bag.Remove(id, 1);
                    if (OnlineEconomy.On) OnlineEconomy.UseItem(id); // [SERVER]
                    int amount = Mathf.Max(1, Mathf.RoundToInt(health.Max * item.power / 100f));
                    health.Heal(amount);
                    if (PartyNet.IsMember) PartyNet.Current.SendHeal(amount); // [PARTY NET] the host's copy heals too
                    Game.Audio.PlaySfx("heal");
                    SkillVisuals.Flash(Center, new Color(1f, 0.35f, 0.35f, 0.55f), 1.6f, 0.3f);
                    Fx.Sparkle(Center + Vector2.up * 0.4f, 3, 0.4f);
                    GameEvents.RaiseToast($"{item.name}  <color=#ff8a8a>+{amount} HP</color>");
                    return true;
                }
                case ConsumableKind.HealMp:
                {
                    int maxMp = Data.Stats.MaxMp;
                    if (Data.Mana >= maxMp - 0.5f) { GameEvents.RaiseToast(L.T("MP가 가득 차 있습니다.")); return false; }
                    if (!PotionReady(id)) return false;
                    bag.Remove(id, 1);
                    if (OnlineEconomy.On) OnlineEconomy.UseItem(id); // [SERVER]
                    int amount = Mathf.Max(1, Mathf.RoundToInt(maxMp * item.power / 100f));
                    Data.Mana += amount;
                    ClampMana();
                    GameEvents.RaisePlayerHealthChanged(health.Current, health.Max);
                    Game.Audio.PlaySfx("heal");
                    SkillVisuals.Flash(Center, new Color(0.4f, 0.6f, 1f, 0.6f), 1.6f, 0.3f);
                    Fx.Sparkle(Center + Vector2.up * 0.4f, 3, 0.4f);
                    GameEvents.RaiseToast($"{item.name}  <color=#8ab8ff>+{amount} MP</color>");
                    return true;
                }
                case ConsumableKind.TownScroll:
                    return Game.Flow.UseTownScroll();
                case ConsumableKind.Buff:
                {
                    // [CASH] 투지의 주문서: refreshes to the full time, never stacks.
                    bag.Remove(id, 1);
                    if (OnlineEconomy.On) OnlineEconomy.UseItem(id);
                    Data.ScrollPower = item.power;
                    Data.ScrollUntil = Time.time + item.minutes * 60f;
                    RefreshStats();
                    Game.Audio.PlaySfx("magic");
                    SkillVisuals.Flash(Center, new Color(1f, .55f, .25f, .6f), 1.8f, .35f);
                    GameEvents.RaiseToast(L.F("{0}: {1}분 동안 공격 피해 +{2}%", L.T(item.name), item.minutes, item.power));
                    return true;
                }
                case ConsumableKind.LuckBox:
                case ConsumableKind.SealedBox:
                    CashClient.OpenItem(id); // [CASH] the server opens boxes
                    return false;
                case ConsumableKind.DungeonReset:
                {
                    // [DUNGEON] 요일 던전 초기화권: only when an entry was used today (the server checks the same).
                    var progress = Game.Session.Dungeons;
                    if (progress.EntriesUsed(ResetClock.Now) <= 0)
                    {
                        GameEvents.RaiseToast(L.T("오늘 요일 던전 입장 횟수가 그대로 남아 있습니다."));
                        Game.Audio.PlaySfx("cancel");
                        return false;
                    }
                    bag.Remove(id, 1);
                    if (OnlineEconomy.On) OnlineEconomy.UseItem(id);
                    progress.ResetEntries(ResetClock.Now);
                    Game.Audio.PlaySfx("confirm");
                    GameEvents.RaiseToast(L.F("{0}: 오늘 요일 던전 입장 횟수가 {1}회로 돌아왔습니다.", L.T(item.name), DungeonDatabase.DailyEntries));
                    return true;
                }
            }
            return false;
        }

        bool PotionReady(string id)
        {
            if (potionReadyAt.TryGetValue(id, out float at) && Time.time < at) return false;
            potionReadyAt[id] = Time.time + 0.6f;
            return true;
        }

        /// <summary>Korean particle after a word: consonant ending → <paramref name="withFinal"/> (이/을/은), vowel → <paramref name="withoutFinal"/>.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word)) return withoutFinal;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return withFinal;
            return (c - 0xAC00) % 28 != 0 ? withFinal : withoutFinal;
        }
    }
}
