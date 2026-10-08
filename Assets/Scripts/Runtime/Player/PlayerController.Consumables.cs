using UnityEngine;

namespace DotRPG
{
    public partial class PlayerController
    {
        /// <summary>Eats one carrot to heal (Q key, or clicking a carrot in the bag).</summary>
        public void TryEatCarrot()
        {
            if (!IsLocal) return;
            var inventory = Game.Session.Inventory;
            if (health.Current >= health.Max)
            {
                GameEvents.RaiseToast("체력이 가득 차 있습니다.");
                return;
            }
            if (!inventory.Remove(ItemIds.Carrot, 1))
            {
                GameEvents.RaiseToast("체력 물약도 당근도 없습니다. 마을 잡화상인에게서 물약을 사세요.");
                return;
            }
            if (OnlineEconomy.On) OnlineEconomy.UseItem(ItemIds.Carrot); // [SERVER] consume there too
            health.Heal(stats.carrotHealAmount);
            if (PartyNet.IsMember) PartyNet.Current.SendHeal(stats.carrotHealAmount); // [PARTY NET]
            Game.Audio.PlaySfx("heal");
            Fx.Sparkle(Center + Vector2.up * 0.4f, 2, 0.3f);
        }

        float lockedUntil;
        readonly System.Collections.Generic.Dictionary<string, float> potionReadyAt = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>Holds the character still (reading the return scroll).</summary>
        public void LockMovement(float seconds)
        {
            CancelMobility();
            lockedUntil = Time.time + seconds;
        }

        /// <summary>Q: a health potion if there is one, otherwise a carrot.</summary>
        public void UseHealing()
        {
            if (!IsLocal) return;
            if (Game.Session.Inventory.Count(ConsumableDatabase.HpPotionHi) > 0) UseConsumable(ConsumableDatabase.HpPotionHi); // [CASH] the strong one first
            else if (Game.Session.Inventory.Count(ConsumableDatabase.HpPotion) > 0) UseConsumable(ConsumableDatabase.HpPotion);
            else TryEatCarrot();
        }

        /// <summary>Uses one potion or scroll from the bag (local player only). Returns true when it was used up.</summary>
        public bool UseConsumable(string id)
        {
            var item = ConsumableDatabase.Get(id);
            if (item == null || IsDead || !IsLocal) return false;
            var bag = Game.Session.Inventory;
            if (bag.Count(id) <= 0)
            {
                GameEvents.RaiseToast($"{item.name}{Josa(item.name, "이", "가")} 없습니다. 마을 잡화상인에게서 살 수 있습니다.");
                Game.Audio.PlaySfx("cancel");
                return false;
            }
            switch (item.kind)
            {
                case ConsumableKind.HealHp:
                {
                    if (health.Current >= health.Max) { GameEvents.RaiseToast("체력이 가득 차 있습니다."); return false; }
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
                    if (Data.Mana >= maxMp - 0.5f) { GameEvents.RaiseToast("MP가 가득 차 있습니다."); return false; }
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
                    GameEvents.RaiseToast($"{item.name}: {item.minutes}분 동안 공격 피해 +{item.power}%");
                    return true;
                }
                case ConsumableKind.LuckBox:
                case ConsumableKind.SealedBox:
                    CashClient.OpenItem(id); // [CASH] the server opens boxes
                    return false;
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
