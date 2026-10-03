using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The unfinished workshop from the reference image. The player delivers wood and stone here;
    /// once everything is delivered it is built with a short hammer-and-sparkles sequence.
    /// Its state is read from / written to the quest progress so it survives save & load.
    /// </summary>
    public class ConstructionSite : Interactable
    {
        SpriteRenderer building;
        SpriteRenderer woodPile;
        SpriteRenderer stonePile;
        bool buildingInProgress;
        bool hd;

        QuestManager Quest => Game.Quest;

        public override string Prompt
        {
            get
            {
                if (Quest.Progress.workshopBuilt) return "살펴보기";
                return Quest.Stage == QuestStage.Active ? "재료 전달하기" : "살펴보기";
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
            site.woodPile = NewRenderer(go.transform, "WoodPile", new Vector2(-1.9f, -0.2f), 1);
            site.woodPile.sprite = Game.Art.Get(hd ? "town_pile_0" : "pile_wood");
            site.stonePile = NewRenderer(go.transform, "StonePile", new Vector2(1.9f, -0.2f), 1);
            site.stonePile.sprite = Game.Art.Get(hd ? "town_pile_1" : "pile_stone");

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
            woodPile.enabled = !p.workshopBuilt && p.woodDelivered > 0;
            stonePile.enabled = !p.workshopBuilt && p.stoneDelivered > 0;
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

            if (OnlineEconomy.On)
            {
                // [SERVER] The server moves what is missing from the bag and keeps the count.
                OnlineEconomy.Deliver((w, s, complete) =>
                {
                    if (w < 0) return;
                    Quest.SetDeliveredFromServer(w, s);
                    Game.Audio.PlaySfx("deliver");
                    GameEvents.RaiseToast($"재료 전달! 목재 {w}/{cfg.requiredWood} · 돌 {s}/{cfg.requiredStone}");
                    if (complete && !buildingInProgress) StartCoroutine(BuildSequence());
                });
                return;
            }
            Quest.DeliverMaterials(Game.Session.Inventory, out int wood, out int stone);
            if (wood + stone > 0)
            {
                Game.Audio.PlaySfx("deliver");
                Fx.Sparkle((Vector2)transform.position + new Vector2(0f, 1f), 2, 1f);
                GameEvents.RaiseToast($"재료 전달! 목재 {Quest.Progress.woodDelivered}/{cfg.requiredWood} · 돌 {Quest.Progress.stoneDelivered}/{cfg.requiredStone}");
            }
            else if (!Quest.MaterialsComplete)
            {
                GameEvents.RaiseToast($"재료가 부족하다. 목재 {Quest.WoodStillNeeded}개, 돌 {Quest.StoneStillNeeded}개가 더 필요하다.");
            }

            if (Quest.MaterialsComplete) StartCoroutine(BuildSequence());
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
            GameEvents.RaiseToast("공방이 완성되었다!");
        }
    }
}
