using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>-dotrpgCanyon: renders and checks of the 32px canyon town.</summary>
        IEnumerator CanyonHdShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.2f);

            // Reach the canyon through the forest (canyon = forest.nextMap), or jump straight there.
            Game.Flow.TravelTo(MapRegistry.Canyon);
            yield return Wait(2.5f);
            if (Game.World.MapId != MapRegistry.Canyon)
            {
                Game.World.Load(MapRegistry.Canyon);
                Game.Player.Spawn(Game.World.PlayerSpawn, Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
                Game.Camera.SetTarget(Game.Player.transform, true);
                yield return Wait(1.5f);
            }

            var b = Game.World.Bounds;
            Log($"canyon: map={Game.World.MapId} size={b.width}x{b.height} spawn={Game.World.PlayerSpawn} objects={Game.World.ObjectsRoot.childCount} minimap={Game.World.Minimap.width}x{Game.World.Minimap.height}");
            Log($"monsters on canyon (must be 0): {EnemyController.Active.Count}");

            // Confirm both portals still lead to the forest / winter maps.
            var info = MapRegistry.Get(MapRegistry.Canyon);
            Log($"portals: south '<' -> {info.previousMap} (expect {MapRegistry.Forest}), north '>' -> {info.nextMap} (expect {MapRegistry.Winter})");

            // Clean map renders: overview at 32px/tile + 2x close-ups (1280x720 at 64px/tile).
            yield return RenderMap("canyon", new[]
            {
                ("plaza", 22f, 40f),        // central flagstone plaza + inn
                ("cliffs", 22f, 46f),       // north stairs cut into the cliff
                ("water", 15f, 16f),        // teal channel + wooden bridge
                ("inn", 12f, 38f),          // the inn landmark up close
                ("houses", 34f, 40f),       // green- and red-roof houses
                ("gate", 23f, 54f),         // north stone gate + upper terrace
                ("lookout", 42f, 40f),      // east lookout deck + treasure chest
                ("rest", 6f, 20f),          // west rest area: well, bench, garden
            });

            // In-game shots with the HUD and minimap.
            SaveTexture(Game.World.Minimap, Path.Combine(folder, "canyon_minimap_texture.png"));
            yield return Shot("canyon_ingame_start");
            yield return Teleport(new Vector2(22f, 40f));
            yield return Wait(0.3f);
            yield return Shot("canyon_ingame_plaza");
            yield return Teleport(new Vector2(15f, 17f));
            yield return Wait(0.3f);
            yield return Shot("canyon_ingame_bridge");

            // Ground paint timing (fresh vs cached).
            Log($"paint timing: canyon={TimeLoad(MapRegistry.Canyon)}ms (cached reload={TimeLoad(MapRegistry.Canyon, false)}ms)");
            Game.Player.Spawn(Game.World.PlayerSpawn, Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.4f);
        }
    }
}
