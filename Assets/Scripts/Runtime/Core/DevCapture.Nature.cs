using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator NatureShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1f);
            var counts = new Dictionary<string, int>();
            foreach (var sr in Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!VillageNatureArt.IsGenerated(sr.sprite)) continue;
                string key = sr.sprite.name;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                if (sr.sprite.texture.filterMode != FilterMode.Point || !sr.sprite.texture.isReadable)
                    throw new Exception("NATURE FAIL: filtering / canopy sampling " + key);
            }
            foreach (var art in VillageNatureArt.All)
            {
                if (!counts.ContainsKey(art.Name)) throw new Exception("NATURE FAIL: no placement for " + art.Name);
                Log($"PASS generated {art.Name}: {counts[art.Name]} placed, point sampling, readable alpha");
            }
            // Find a live tree with room behind its canopy and verify the existing fade still works.
            TreeFade faded = null;
            foreach (var tree in Game.World.ObjectsRoot.GetComponentsInChildren<TreeFade>())
            {
                var sr = tree.GetComponent<SpriteRenderer>();
                if (sr == null || !VillageNatureArt.IsGenerated(sr.sprite) || tree.name != "Tree") continue;
                var behind = (Vector2)tree.transform.position + Vector2.up * 2f;
                if (!Game.World.IsFree(behind)) continue;
                Game.Player.Place(behind, Facing.Down);
                yield return Wait(.4f);
                if (tree.Alpha < .6f) { faded = tree; break; }
            }
            if (faded == null) throw new Exception("NATURE FAIL: canopy transparency");
            Log("PASS canopy fades behind generated tree");
            RenderRegion(Path.Combine(folder, "nature_canopy.png"),
                new Rect(faded.transform.position.x - 4, faded.transform.position.y - 1, 8, 7), 128);
            yield return Teleport(Game.World.PlayerSpawn);
            if (faded.Alpha < .95f) throw new Exception("NATURE FAIL: canopy did not recover");
            Log("PASS canopy recovers when player leaves");

            int chopped = 0, mined = 0;
            foreach (var node in Game.World.ObjectsRoot.GetComponentsInChildren<ResourceNode>())
            {
                var sr = node.GetComponentInChildren<SpriteRenderer>();
                if (sr.sprite.name == "broadleaf" && chopped == 0)
                {
                    node.TakeDamage(new DamageInfo(999, Game.Player.Position, 0, Team.Player));
                    if (sr.sprite.name != "town_stump") throw new Exception("NATURE FAIL: tree depletion");
                    chopped++;
                }
                else if (sr.sprite.name == "rock" && mined == 0)
                {
                    node.TakeDamage(new DamageInfo(999, Game.Player.Position, 0, Team.Player));
                    if (sr.enabled || node.GetComponent<Collider2D>().enabled) throw new Exception("NATURE FAIL: rock depletion");
                    mined++;
                }
            }
            if (chopped != 1 || mined != 1) throw new Exception("NATURE FAIL: harvestable art not connected");
            Log("PASS wood and stone harvesting retain depletion/collision behavior");
            // Rebuild fresh before checking all house routes and capturing the whole village.
            yield return HouseScaleShowcase();
            yield return Teleport(new Vector2(8.5f, 39f));
            yield return Shot("village_nature_gameplay");
            RenderRegion(Path.Combine(folder, "village_nature_detail.png"), new Rect(3, 36, 17, 13), 96);
            Log("NATURE: all checks passed");
        }
    }
}
