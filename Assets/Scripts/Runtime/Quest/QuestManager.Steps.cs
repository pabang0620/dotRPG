using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class QuestManager
    {
        /// <summary>
        /// Re-checks everything: unlocks quests, accepts giver-less ones, finishes steps whose objectives
        /// are all done (consuming collected items, playing the step cutscene) and quests whose last step ended.
        /// </summary>
        public void Refresh()
        {
            if (evaluating || Game.Session == null || !StoryEnabled)
            {
                Changed?.Invoke();
                return;
            }
            evaluating = true;
            try
            {
                for (int pass = 0; pass < 8; pass++)
                {
                    bool again = false;
                    foreach (var q in db.All)
                    {
                        var s = Journal.State(q.id);
                        var st = (QuestStatus)s.status;
                        if (st == QuestStatus.Locked && Unlocked(q))
                        {
                            s.status = (int)QuestStatus.Available;
                            if (string.IsNullOrEmpty(q.giver)) Accept(q);
                            again = true;
                        }
                        else if (st == QuestStatus.Active && !cutscenePlaying && StepDone(q))
                        {
                            FinishStep(q);
                            again = true;
                        }
                    }
                    if (!again) break;
                }
            }
            finally { evaluating = false; }
            Changed?.Invoke();
            TryAutoCutscenes();
        }

        bool StepDone(QuestDef q)
        {
            var step = CurrentStep(q);
            if (step == null) return true;
            for (int i = 0; i < step.objectives.Count; i++) if (!IsObjectiveDone(q, i)) return false;
            return true;
        }

        void FinishStep(QuestDef q)
        {
            var step = CurrentStep(q);
            if (step != null)
            {
                // [SERVER] Online the quest claim consumes the items (questService processClaim) and its delta sets the stacks.
                if (!OnlineEconomy.On)
                    foreach (var o in step.objectives)
                        if (o.type == ObjectiveTypes.Collect && o.consume) Game.Session.Inventory.Remove(o.target, Need(o));
                foreach (var f in step.setFlags) Journal.Flags.Add(f);
            }
            var s = Journal.State(q.id);
            s.step++;
            s.counts.Clear();
            if (step != null && !string.IsNullOrEmpty(step.cutscene))
            {
                // The step's scene plays first; the next step (or the report) follows when it ends.
                PlayCutscene(step.cutscene, () => AfterStep(q));
                return;
            }
            AfterStep(q);
        }

        void AfterStep(QuestDef q)
        {
            var s = Journal.State(q.id);
            if (s.step < q.steps.Count)
            {
                Game.Audio.PlaySfx("select");
                if (q.Kind == QuestKind.Main) GameEvents.RaiseToast(q.steps[s.step].text);
                return;
            }
            if (string.IsNullOrEmpty(q.turnIn)) Complete(q);
            else
            {
                s.status = (int)QuestStatus.ReadyToTurnIn;
                Game.Audio.PlaySfx("quest");
                GameEvents.RaiseToast($"{NpcName(q.turnIn)}에게 보고하세요.");
            }
        }

        // =============================== Cutscenes ===============================

        bool cutscenePlaying;

        void PlayCutscene(string id, Action done)
        {
            if (Game.Cutscenes == null || Game.Cutscenes.Get(id) == null)
            {
                if (Game.Cutscenes != null) Debug.LogWarning($"[dotRPG] Cutscene '{id}' not found.");
                OnCutsceneFinished(id);
                done?.Invoke();
                return;
            }
            cutscenePlaying = true;
            Game.Cutscenes.Play(id, () =>
            {
                cutscenePlaying = false;
                OnCutsceneFinished(id);
                done?.Invoke();
                Refresh();
            });
        }

        /// <summary>Starts cutscene objectives of the current steps whose map is the one the player is on.</summary>
        void TryAutoCutscenes()
        {
            if (cutscenePlaying || Game.Cutscenes == null || Game.Cutscenes.IsPlaying || !Game.IsPlaying) return;
            // Story scenes wait until the party is back on the map (not in the raid boss room before the results).
            if (Game.Dungeon != null && Game.Dungeon.InRun) return;
            string map = Game.Session.MapId;
            foreach (var q in db.All)
            {
                if (Journal.Status(q.id) != QuestStatus.Active) continue;
                var step = CurrentStep(q);
                if (step == null) continue;
                for (int i = 0; i < step.objectives.Count; i++)
                {
                    var o = step.objectives[i];
                    if (o.type != ObjectiveTypes.Cutscene || IsObjectiveDone(q, i)) continue;
                    if (!string.IsNullOrEmpty(o.map) && o.map != map) continue;
                    PlayCutscene(o.target, null);
                    return;
                }
            }
        }

        /// <summary>Called every frame by the cutscene player's host so queued scenes start once gameplay resumes.</summary>
        public void Tick()
        {
            if (!cutscenePlaying && Game.IsPlaying && Game.Cutscenes != null && !Game.Cutscenes.IsPlaying) TryAutoCutscenes();
            StoryRespawn.Tick(this);
        }

        // =============================== Text ===============================

        static string DefaultObjectiveText(ObjectiveDef o)
        {
            switch (o.type)
            {
                case ObjectiveTypes.Talk: return $"{Game.Quest.NpcName(o.target)}와(과) 이야기한다";
                case ObjectiveTypes.Kill: return o.target == "*" ? "몬스터 처치" : $"{MonsterName(o.target)} 처치";
                case ObjectiveTypes.Collect: return $"{ItemName(o.target)} 모으기";
                case ObjectiveTypes.Reach: return $"{(MapRegistry.Get(o.target)?.displayName ?? o.target)}(으)로 간다";
                case ObjectiveTypes.Dungeon: return "던전 클리어";
                case ObjectiveTypes.Raid: return "레이드 클리어";
                case ObjectiveTypes.Level: return $"레벨 {o.count} 달성";
                case ObjectiveTypes.Quests: return "부탁 해결";
                default: return o.target;
            }
        }

        static string MonsterName(string id)
        {
            var def = MonsterDatabase.Get(id);
            return def != null ? def.name : id;
        }

        public string FormatTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            return text
                .Replace("{name}", Journal.PlayerName)
                .Replace("{wood_required}", config.requiredWood.ToString())
                .Replace("{stone_required}", config.requiredStone.ToString())
                .Replace("{wood_left}", WoodStillNeeded.ToString())
                .Replace("{stone_left}", StoneStillNeeded.ToString())
                .Replace("{attack_key}", Game.Input.GetBindingLabel(GameAction.Attack))
                .Replace("{interact_key}", Game.Input.GetBindingLabel(GameAction.Interact))
                .Replace("{item_key}", Game.Input.GetBindingLabel(GameAction.UseItem))
                .Replace("{mobility_key}", Game.Input.GetBindingLabel(GameAction.Mobility))
                .Replace("{skill1_key}", Game.Input.GetBindingLabel(GameAction.Skill1))
                .Replace("{skill2_key}", Game.Input.GetBindingLabel(GameAction.Skill2))
                .Replace("{inventory_key}", Game.Input.GetBindingLabel(GameAction.Inventory))
                .Replace("{pause_key}", Game.Input.GetBindingLabel(GameAction.Pause));
        }
    }
}
