using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ART] <c>-dotrpgVillageArt</c>: screenshots of the generated village art in place (plaza fountain and
    /// props, the stable before and after the attack, graves, festival lanterns, story props) for a size check.
    /// </summary>
    public partial class DevCapture
    {
        IEnumerator VillageArtShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.5f);
            var j = Game.Session.Journal;
            j.Flags.Add("festival_eve");
            Game.World.Load(MapRegistry.Village);
            yield return Wait(0.5f);
            yield return ShotAt(10f, 13f, "art_01_stable_morning");
            yield return ShotAt(27f, 33f, "art_02_plaza_festival");
            yield return ShotAt(20f, 27f, "art_03_well_props");
            yield return ShotAt(45f, 30f, "art_04_east_street");
            yield return ShotAt(33f, 37f, "art_05_board");
            j.Flags.Add("attack_done");
            j.Flags.Add("stable_burned");
            j.Flags.Add("family_buried");
            Game.World.Load(MapRegistry.Village);
            yield return Wait(0.5f);
            yield return ShotAt(12f, 12f, "art_06_stable_burned");
            yield return ShotAt(20f, 10f, "art_07_graves");
            // The regenerated mage next to the warrior-made village (character select first).
            Game.Flow.ReturnToTitle();
            yield return Wait(1.5f);
            Game.UI.Push(Game.UI.CharacterSelect);
            yield return Wait(0.8f);
            yield return Shot("art_08_character_select");
            Game.UI.Pop();
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.5f);
            yield return ShotAt(27f, 36f, "art_09_mage_plaza");
            // Party of the four regenerated mercenaries, and the story cast lined up.
            foreach (var m in MercenaryDatabase.All) Game.Party.AddCompanion(m.id);
            yield return Wait(1.5f);
            yield return ShotAt(27f, 38f, "art_10_party");
            string[] cast = { "kael", "ria", "bram", "hanna", "leona", "orban", "grah", "bargas", "knight_dorn", "herbalist" };
            for (int i = 0; i < cast.Length; i++)
            {
                var def = StoryCast.Find(cast[i]);
                if (def != null) NpcController.Create(def, CutscenePlayer.CellToWorld(21f + i * 1.3f, 40f), Game.World.ObjectsRoot);
            }
            yield return ShotAt(27f, 41f, "art_11_story_cast");
        }

        /// <summary>Puts the player on a map-text cell (column, row) and takes a screenshot there.</summary>
        IEnumerator ShotAt(float col, float row, string name)
        {
            var pos = CutscenePlayer.CellToWorld(col, row);
            Game.Player.Place(pos, Facing.Down);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.6f);
            yield return Shot(name);
        }
    }
}
