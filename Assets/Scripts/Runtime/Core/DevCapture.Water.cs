using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator WaterShowcase()
        {
            yield return Wait(1);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1);
            foreach (var map in MapRegistry.All)
            {
                Game.Flow.TravelTo(map.id);
                yield return Wait(1);
                var water = Game.World.GetComponentInChildren<LivingWater>();
                if (water == null) throw new Exception("WATER FAIL missing surface " + map.id);
                if (water.Field.SwimPoints.Count > 0 && water.FishCount == 0) throw new Exception("WATER FAIL missing koi " + map.id);
                var before = new List<Vector2>(water.FishPositions);
                var traveled = new float[before.Count];
                for (int i = 0; i < 160; i++)
                {
                    yield return Wait(.1f);
                    if (!water.FishWithinWater) throw new Exception("WATER FAIL fish escaped water/entered bridge " + map.id);
                    int index = 0;
                    foreach (var p in water.FishPositions)
                    {
                        traveled[index] += Vector2.Distance(before[index], p);
                        before[index++] = p;
                    }
                }
                int moved = 0;
                foreach (var distance in traveled) if (distance > .15f) moved++;
                if (moved != water.FishCount || moved == 0) throw new Exception("WATER FAIL stationary fish " + map.id);
                Log($"PASS {map.id}: {water.FishCount} koi, {moved} swimming, water/bridge clearance for 16 seconds; waterfall pixels={water.Field.FallPixels}");

                // Render ground alone twice at the native pixel grid. Every dry pixel must be identical.
                water.FreezeAnimation = true;
                var hidden = new List<SpriteRenderer>();
                foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    if (sr.name != "Ground" && !sr.forceRenderingOff) { sr.forceRenderingOff = true; hidden.Add(sr); }
                Rect region = new Rect(0, 0, water.Field.Width / 32, water.Field.Height / 32);
                string a = Path.Combine(folder, map.id + "_surface_a.png"), b = Path.Combine(folder, map.id + "_surface_b.png");
                water.SetAnimationTime(0); RenderRegion(a, region, 32);
                water.SetAnimationTime(1.25f); RenderRegion(b, region, 32);
                var ta = new Texture2D(2,2); ta.LoadImage(File.ReadAllBytes(a));
                var tb = new Texture2D(2,2); tb.LoadImage(File.ReadAllBytes(b));
                var ca = ta.GetPixels32(); var cb = tb.GetPixels32();
                int changed = 0, dryChanged = 0;
                for (int i = 0; i < ca.Length; i++)
                    if (!ca[i].Equals(cb[i])) { if (water.Field.Mask[i].r > 0) changed++; else dryChanged++; }
                Destroy(ta); Destroy(tb);
                foreach (var sr in hidden) sr.forceRenderingOff = false;
                if (dryChanged > 0 || changed < 50) throw new Exception($"WATER FAIL masking/flow {map.id}: wet changes={changed}, dry changes={dryChanged}");
                Log($"PASS {map.id}: {changed} animated water pixels; 0 changed land/bridge pixels");
                water.FreezeAnimation = false;
                foreach (var p in water.FishPositions)
                {
                    RenderRegion(Path.Combine(folder, map.id + "_koi_detail.png"), new Rect(p.x-1.5f, p.y-1.5f, 3, 3), 192);
                    break;
                }
                var center = map.id == MapRegistry.Village ? new Vector2(23, 8) : water.Field.SwimPoints[water.Field.SwimPoints.Count / 2];
                var detail = new Rect(center.x - 7, center.y - 4.5f, 14, 9);
                // A short frame sequence makes the actual in-game water and fish motion reviewable.
                for (int f = 0; f < (map.id == MapRegistry.Village ? 36 : 2); f++)
                {
                    RenderRegion(Path.Combine(folder, $"{map.id}_living_{f:D2}.png"), detail, 48);
                    yield return Wait(.1f);
                }
                float time = water.AnimationTime;
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(.3f);
                Time.timeScale = 1;
                if (water.AnimationTime != time) throw new Exception("WATER FAIL pause clock " + map.id);
                Game.World.Load(map.id);
                yield return Wait(.2f);
                if (Game.World.GetComponentsInChildren<LivingWater>().Length != 1) throw new Exception("WATER FAIL duplicate map effects");
                Log("PASS " + map.id + ": pause and cached map reload");
            }
            Game.Flow.TravelTo(MapRegistry.Village);
            yield return Wait(1);
            Log("WATER: all checks passed");
        }
    }
}
