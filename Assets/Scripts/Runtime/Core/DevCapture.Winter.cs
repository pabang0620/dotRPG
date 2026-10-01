using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// -dotrpgWinter: renders and checks of the 32px winter village. Starts a new game, walks the
        /// canyon's north portal into the winter map, renders the whole painted map at 32 px per tile with
        /// characters hidden, then 2x close-ups of the plaza, cabins + garden, camp, river bends + bridge +
        /// waterfall, north stairs + gate + lookout, farm and chest, plus in-game shots with the HUD and the
        /// minimap. Logs the ground paint time and confirms the south portal leads back to the canyon.
        /// </summary>
        IEnumerator WinterHdShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.2f);

            // Reach the winter village through the canyon's north portal (top edge, x 28-31).
            Game.Flow.TravelTo(MapRegistry.Canyon);
            yield return Wait(2f);
            yield return Teleport(new Vector2(29.5f, 58.3f));
            Game.Player.Spawn(new Vector2(29.5f, 59.4f), Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            yield return Wait(3f);
            Log($"via canyon north portal: map={Game.World.MapId} player={Game.Player.Position}");
            if (Game.World.MapId != MapRegistry.Winter)
            {
                Game.Flow.TravelTo(MapRegistry.Winter);
                yield return Wait(3f);
            }

            var b = Game.World.Bounds;
            Log($"winter: map={Game.World.MapId} size={b.width}x{b.height} spawn={Game.World.PlayerSpawn} objects={Game.World.ObjectsRoot.childCount} minimap={Game.World.Minimap.width}x{Game.World.Minimap.height}");

            // A fresh paint (cache cleared) times the whole painted ground.
            Log($"paint timing: winter={TimeLoad(MapRegistry.Winter)}ms (cached reload={TimeLoad(MapRegistry.Winter, false)}ms)");
            // Return to a clean state after the timing reloads.
            Game.World.Load(MapRegistry.Winter);
            yield return Wait(1.5f);
            Game.Player.Spawn(Game.World.PlayerSpawn, Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.6f);

            // The south portal must lead back to the canyon.
            Log($"south portal target (should be canyon): previousMap={Game.World.Map.previousMap}");

            SaveTexture(Game.World.Minimap, Path.Combine(folder, "winter_minimap_texture.png"));

            yield return RenderMap("winter", new[]
            {
                ("plaza", 23.5f, 25.5f),
                ("cabins", 13.4f, 23.5f),
                ("camp", 35.5f, 25.5f),
                ("bridge", 39.6f, 22.5f),
                ("north", 19.5f, 38.5f),
                ("lookout", 41.6f, 38.5f),
                ("farm", 14f, 7.5f),
                ("chest", 41.6f, 32.5f),
            });

            // In-game screenshots with the HUD and the minimap.
            yield return Shot("winter_ingame_start");
            yield return Teleport(new Vector2(23.5f, 21.5f));
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_plaza_hud");
            yield return Teleport(new Vector2(35.5f, 22.5f));
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_camp_hud");
            Log($"after walk: player={Game.Player.Position}");
        }
    }
}
