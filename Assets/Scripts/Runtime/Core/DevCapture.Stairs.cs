using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// -dotrpgStairs: close-ups of every stone stairway at 4x (128 px per tile, one texel = 4x4 screen
        /// pixels): the canyon's north and east stairs and the winter village's north stairs, plus an in-game
        /// shot with the player standing on each flight.
        /// </summary>
        IEnumerator StairsShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.2f);
            foreach (var (mapId, prefix, shots, stand) in new[]
            {
                (MapRegistry.Canyon, "canyon", new[] { ("north", new Rect(20.5f, 42.5f, 7f, 9f)), ("east", new Rect(41.5f, 31.5f, 5f, 6.5f)) }, new Vector2(23.5f, 46.4f)),
                (MapRegistry.Winter, "winter", new[] { ("north", new Rect(15.5f, 31.5f, 7f, 7f)) }, new Vector2(19f, 34.9f)),
            })
            {
                if (Game.World.MapId != mapId)
                {
                    Game.Flow.TravelTo(mapId);
                    yield return Wait(2.5f);
                }
                Log($"--- map {Game.World.MapId}");
                SetPlayerVisible(false);
                SetExitMarkersVisible(false);
                yield return new WaitForEndOfFrame();
                foreach (var (name, rect) in shots)
                {
                    RenderRegion(Path.Combine(folder, $"stairs_{prefix}_{name}.png"), rect, 128);
                    Log($"rendered stairs_{prefix}_{name}");
                }
                SetExitMarkersVisible(true);
                SetPlayerVisible(true);
                yield return Teleport(stand);
                Game.Camera.SetTarget(Game.Player.transform, true);
                yield return Wait(0.5f);
                yield return Shot($"stairs_{prefix}_ingame");
            }
        }
    }
}
