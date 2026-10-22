using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Search spots of the chapter 3 quests (chapter3.json objectives "interact"): 노호의 기록 조각 in B2, 발체의 조약돌 in
    /// 갈라진 협곡 바닥, three 수호석 잔해 and three 잿빛 손 야영지 in 공허의 틈. Each shows only while its quest is active and
    /// counts once (a story flag remembers it, so leaving and coming back never counts it twice). Placed on the field's
    /// spawn points so they always stand on open ground. Generated ART_LIST sprites replace the stand-ins when present
    /// (sized at import, GeneratedArtImport; Spot.scale only enlarges a stand-in icon).
    /// </summary>
    public sealed class Chapter3Spots : Interactable
    {
        sealed class Spot
        {
            public string map, interactId, quest, sprite, standIn, prompt;
            public int count = 1;
            public float scale = 1f;
        }

        static readonly Spot[] spots =
        {
            new Spot { map = "hollow_roots", interactId = "c3_record_1", quest = "c3_record", sprite = "c3_record_piece", standIn = "icon_scroll", prompt = "기록 조각 줍기" }, // i18n-ignore: translated at runtime via Prompt (L.T)
            new Spot { map = "hollow_roots", interactId = "c3_record_2", quest = "c3_record", sprite = "c3_record_piece", standIn = "icon_scroll", prompt = "기록 조각 줍기" }, // i18n-ignore: translated at runtime via Prompt (L.T)
            // 3-4: Ria's herb garden on the edge of Sayab (the reunion cutscene starts here).
            new Spot { map = MapRegistry.Sayab, interactId = "ria_garden", quest = "c3_trust", sprite = "c3_herb_garden", standIn = "town_planter", prompt = "약초밭으로 들어가기", scale = 1.6f }, // i18n-ignore: translated at runtime via Prompt (L.T)
            new Spot { map = MapRegistry.DryFloor, interactId = "c3_pebble", quest = "c3s_balche_toy", sprite = "c3_pebble", standIn = "icon_stone", prompt = "조약돌 줍기" }, // i18n-ignore: translated at runtime via Prompt (L.T)
            new Spot { map = MapRegistry.DryRift, interactId = "c3_guardian_wreck", quest = "c3_stones", count = 3, sprite = "c3_guardian_wreck", standIn = "icon_stone", prompt = "수호석 잔해 살펴보기", scale = 1.8f }, // i18n-ignore: translated at runtime via Prompt (L.T)
            new Spot { map = MapRegistry.DryRift, interactId = "c3_grey_camp", quest = "c3_trace", count = 3, sprite = "c3_grey_camp", standIn = "town_log", prompt = "야영지 뒤지기", scale = 1.2f }, // i18n-ignore: translated at runtime via Prompt (L.T)
        };

        string interactId, flag, prompt;

        public override string Prompt => L.T(prompt);

        /// <summary>World build: the spots of this map whose quest is active and not yet searched.</summary>
        public static void Spawn(WorldBuilder world)
        {
            if (world == null || Game.Session == null || Game.Quest == null) return;
            var j = Game.Session.Journal;
            var points = new List<Vector2>();
            foreach (Transform t in world.ObjectsRoot)
            {
                var spawner = t.GetComponent<EnemySpawner>();
                if (spawner != null) points.AddRange(spawner.Points);
            }
            if (points.Count == 0) return;
            int slot = 0;
            foreach (var s in spots)
            {
                if (s.map != world.MapId) { continue; }
                for (int n = 0; n < s.count; n++, slot++)
                {
                    string flag = "c3spot_" + s.interactId + (s.count > 1 ? "_" + n : "");
                    if (j.Status(s.quest) != QuestStatus.Active || j.HasFlag(flag)) continue;
                    // Spread over the field: every seventh spawn point, offset off the pack centre.
                    var at = points[(slot * 7 + 3) % points.Count] + new Vector2(1.2f, -0.6f);
                    if (!world.IsFree(at)) at = points[(slot * 7 + 3) % points.Count];
                    Create(world.ObjectsRoot, at, s, flag);
                }
            }
        }

        static void Create(Transform parent, Vector2 at, Spot s, string flag)
        {
            var go = new GameObject("QuestSpot_" + s.interactId);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var spot = go.AddComponent<Chapter3Spots>();
            spot.interactId = s.interactId; spot.flag = flag; spot.prompt = s.prompt;
            var art = Game.Art.Optional(s.sprite);
            float scale = art != null ? 1f : s.scale;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * scale;
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = art ?? Game.Art.Get(s.standIn);
            go.AddComponent<YSort>().Configure(true);
            WarmGlow.Attach(go.transform, new Vector2(0f, .6f), 1f, .25f); // findable from afar
            spot.ConfigureShape(new Vector2(0f, .2f), .8f, new Vector2(0f, art != null ? sr.sprite.bounds.size.y + .3f : 1.2f * scale));
        }

        public override void Interact(PlayerController player)
        {
            Game.Quest?.SetFlag(flag);
            Game.Audio?.PlaySfx("select");
            Fx.Sparkle(transform.position + Vector3.up * .5f, 4, .5f);
            GameEvents.RaiseInteracted(interactId);
            Destroy(gameObject);
        }
    }
}
