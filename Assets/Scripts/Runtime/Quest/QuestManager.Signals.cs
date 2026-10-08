using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class QuestManager
    {
        /// <summary>Sets a story flag (world change); flag objectives and quest requirements react.</summary>
        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !Journal.Flags.Add(flag)) return;
            Signal(ObjectiveTypes.Flag, flag, 1);
            Refresh();
        }

        public void ClearFlag(string flag)
        {
            if (Journal.Flags.Remove(flag ?? "")) Refresh();
        }

        /// <summary>A cutscene finished: cutscene objectives naming it complete.</summary>
        public void OnCutsceneFinished(string cutsceneId) => Signal(ObjectiveTypes.Cutscene, cutsceneId, 1);

        /// <summary>Automated checks: raise a gameplay signal directly (dungeon / raid clears).</summary>
        public void DevSignal(string type, string target, int amount) => Signal(type, target, amount);

        void Signal(string type, string target, int amount)
        {
            bool changed = false;
            foreach (var q in db.All)
            {
                if (Journal.Status(q.id) != QuestStatus.Active) continue;
                var step = CurrentStep(q);
                if (step == null) continue;
                for (int i = 0; i < step.objectives.Count; i++)
                {
                    var o = step.objectives[i];
                    if (o.type != type || IsObjectiveDone(q, i)) continue;
                    if (o.target != "*" && o.target != target) continue;
                    Advance(q, i, amount);
                    changed = true;
                    if (type == ObjectiveTypes.Kill)
                    {
                        var counts = Journal.State(q.id).counts;
                        GameEvents.RaiseToast($"{q.title}: {Mathf.Min(counts[i], o.count)}/{o.count}");
                    }
                }
            }
            if (changed) Refresh();
        }

        // =============================== Rules ===============================

        int Need(ObjectiveDef o) => Mathf.Max(1, o.count);

        /// <summary>How many of an errand board's listed quests are completed.</summary>
        public int QuestsDone(ObjectiveDef o)
        {
            int n = 0;
            foreach (var id in o.target.Split(',')) if (Journal.Status(id.Trim()) == QuestStatus.Completed) n++;
            return n;
        }

        bool IsObjectiveDone(QuestDef q, int index)
        {
            var step = CurrentStep(q);
            if (step == null || index >= step.objectives.Count) return false;
            var o = step.objectives[index];
            switch (o.type)
            {
                case ObjectiveTypes.Collect: return Game.Session.Inventory.Count(o.target) >= Need(o) || Count(q, index) >= Need(o);
                case ObjectiveTypes.Level: return Game.Session.Progression.Level >= o.count;
                case ObjectiveTypes.Quests: return QuestsDone(o) >= Need(o);
                case ObjectiveTypes.Flag: return Journal.HasFlag(o.target) || Count(q, index) >= Need(o);
                default: return Count(q, index) >= Need(o);
            }
        }

        int Count(QuestDef q, int index)
        {
            var counts = Journal.State(q.id).counts;
            return index < counts.Count ? counts[index] : 0;
        }

        int TalkObjectiveIndex(QuestDef q, string npcId)
        {
            var step = CurrentStep(q);
            if (step == null) return -1;
            for (int i = 0; i < step.objectives.Count; i++)
            {
                var o = step.objectives[i];
                if (o.type == ObjectiveTypes.Talk && o.target == npcId && !IsObjectiveDone(q, i)) return i;
            }
            return -1;
        }

        void Advance(QuestDef q, int index, int amount)
        {
            var s = Journal.State(q.id);
            while (s.counts.Count <= index) s.counts.Add(0);
            s.counts[index] = Mathf.Min(Need(CurrentStep(q).objectives[index]), s.counts[index] + amount);
        }

        bool Unlocked(QuestDef q)
        {
            foreach (var r in q.requires) if (!Journal.IsCompleted(r)) return false;
            foreach (var f in q.requiresFlags) if (!Journal.HasFlag(f)) return false;
            return Game.Session.Progression.Level >= q.minLevel;
        }

        void Accept(QuestDef q)
        {
            var s = Journal.State(q.id);
            s.status = (int)QuestStatus.Active;
            s.step = 0;
            s.counts.Clear();
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast(q.Kind == QuestKind.Main ? $"메인 퀘스트: {q.DisplayTitle}" : $"새 퀘스트: {q.title}");
            if (q.Kind == QuestKind.Sub && string.IsNullOrEmpty(Journal.Tracked)) Journal.Tracked = q.id;
            if (!string.IsNullOrEmpty(q.startCutscene)) PlayCutscene(q.startCutscene, null);
            GameEvents.RaiseQuestAccepted(q.id);
            Game.Flow.Autosave();
        }

        void Complete(QuestDef q)
        {
            var s = Journal.State(q.id);
            if (s.status == (int)QuestStatus.Completed) return;
            s.status = (int)QuestStatus.Completed;
            if (Journal.Tracked == q.id) Journal.Tracked = "";
            GiveReward(q.reward);
            if (OnlineEconomy.On) OnlineEconomy.ClaimQuest(q.id, (ok, bonus) => { }); // [SERVER] XP, gold and items come from the claim
            Game.Audio.PlaySfx("quest");
            GameEvents.RaiseToast($"퀘스트 완료: {q.title}");
            GameEvents.RaiseQuestCompleted(q.id);
            Game.Flow.Autosave();
        }

        void GiveReward(QuestRewardDef r)
        {
            if (r == null) return;
            var parts = new List<string>();
            bool online = OnlineEconomy.On; // [SERVER] online the claim's delta adds these
            if (r.xp > 0)
            {
                int xp = Progression.QuestXp(r.xp); // scaled with the level curve (the server pays the same exported value)
                if (!online) Game.Session.Progression.AddXp(xp);
                parts.Add($"경험치 {xp:N0}");
            }
            if (r.gold > 0)
            {
                if (!online) Game.Session.Inventory.Add(ConsumableDatabase.Gold, r.gold);
                parts.Add($"골드 {r.gold:N0}");
            }
            if (r.items != null)
                foreach (var it in r.items)
                {
                    if (string.IsNullOrEmpty(it.id) || it.count <= 0) continue;
                    if (!online) Game.Session.Inventory.Add(it.id, it.count);
                    parts.Add(it.count > 1 ? $"{ItemName(it.id)} x{it.count}" : ItemName(it.id));
                }
            if (r.maxHealth > 0 && Game.Player != null)
            {
                Game.Player.AddMaxHealth(r.maxHealth);
                parts.Add($"최대 체력 +{EquipmentDatabase.Hearts(r.maxHealth)}");
            }
            if (r.setFlags != null) foreach (var f in r.setFlags) Journal.Flags.Add(f);
            if (parts.Count > 0) GameEvents.RaiseToast("보상: " + string.Join(", ", parts));
        }

        public static string ItemName(string id)
        {
            switch (id)
            {
                case ItemIds.Wood: return "목재";
                case ItemIds.Stone: return "돌";
                case ItemIds.Carrot: return "당근";
            }
            var eq = EquipmentDatabase.Get(EquipmentDatabase.BaseId(id));
            if (eq != null) return eq.name;
            var c = ConsumableDatabase.Get(id);
            return c != null ? c.name : id;
        }
    }
}
