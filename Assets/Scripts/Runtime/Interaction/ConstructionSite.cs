using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The unfinished workshop from the reference image. While the workshop quest is active the player helps the
    /// builder here and it is built with a short hammer-and-sparkles sequence (wood/stone gathering was removed
    /// on 2026-10-12, Docs/PLAN_REMOVE_GATHERING.md).
    /// Its state is read from / written to the quest progress so it survives save & load.
    /// </summary>
    public class ConstructionSite : Interactable
    {
        SpriteRenderer building;
        bool buildingInProgress;
        bool hd;

        QuestManager Quest => Game.Quest;

        public override string Prompt
        {
            get
            {
                if (Quest.Progress.workshopBuilt) return L.T("살펴보기");
                return Quest.Stage == QuestStage.Active ? L.T("재건 돕기") : L.T("살펴보기");
            }
        }

        public override bool CanInteract => !buildingInProgress;

        public static ConstructionSite Create(Vector2 position, Transform parent, bool hd = false)
        {
            var go = new GameObject("ConstructionSite");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var site = go.AddComponent<ConstructionSite>();
            site.hd = hd;
            site.building = NewRenderer(go.transform, "Building", Vector2.zero, 0);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(2.6f, 1.6f);
            col.offset = new Vector2(0f, 1.0f);
            site.ConfigureShape(new Vector2(0f, 0.1f), 1.0f, new Vector2(0f, 3.4f));
            go.AddComponent<YSort>().Configure(true);
            site.RefreshVisuals();
            return site;
        }

        static SpriteRenderer NewRenderer(Transform parent, string name, Vector2 localPos, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.transform.localPosition = localPos;
            sr.sortingOrder = order;
            return sr;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Game.Quest != null) Game.Quest.Changed += RefreshVisuals;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Game.Quest != null) Game.Quest.Changed -= RefreshVisuals;
        }

        void RefreshVisuals()
        {
            if (building == null || Quest == null) return;
            var p = Quest.Progress;
            building.sprite = Game.Art.Get(hd ? (p.workshopBuilt ? "town_site_1" : "town_site_0") : p.workshopBuilt ? "site_built" : "site_blueprint");
        }

        public override void Interact(PlayerController player)
        {
            var cfg = Quest.Config;
            if (Quest.Progress.workshopBuilt)
            {
                Game.Dialogue.Play(cfg.siteBuiltDialogue);
                return;
            }
            if (Quest.Stage == QuestStage.NotStarted)
            {
                Game.Dialogue.Play(cfg.siteLockedDialogue);
                return;
            }

            // The flag goes to the server with the story flags (online) or the save (offline).
            if (!buildingInProgress) StartCoroutine(BuildSequence());
        }

        IEnumerator BuildSequence()
        {
            buildingInProgress = true;
            Vector2 center = (Vector2)transform.position + new Vector2(0f, 1.3f);
            for (int i = 0; i < 4; i++)
            {
                Game.Audio.PlaySfx("hammer");
                Fx.Sparkle(center + Random.insideUnitCircle * 1.2f, 3, 0.4f);
                Fx.Burst("fx_dust", center + new Vector2(Random.Range(-1f, 1f), -0.8f), 3, 1.5f, 0.5f);
                Game.Camera?.Shake(0.05f, 0.12f);
                yield return new WaitForSeconds(0.35f);
            }
            Game.Audio.PlaySfx("build_complete");
            Fx.Sparkle(center, 10, 1.6f);
            Game.Camera?.Shake(0.15f, 0.3f);
            Quest.MarkWorkshopBuilt();
            RefreshVisuals();
            buildingInProgress = false;
            GameEvents.RaiseToast(L.T("공방이 완성되었습니다!"));
        }
    }
}
