using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>Local screenshot review only: never purchases, draws, equips or promotes.</summary>
        IEnumerator ArtReviewShowcase()
        {
            ApplyRequestedResolution();
            Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior, "전사 외형 검증");
            yield return Wait(.2f);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Game.Flow.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            if (Game.Flow.IsTransitioning) throw new Exception("Art review game start timed out");
            Game.Session.Progression.SetFromServer(15, 0);
            Game.UI.Hud.ClearToasts();
            Game.Flow.OpenInventory();
            yield return Wait(.5f);
            yield return Shot("01-approved-warrior-equipment");
            Game.Flow.CloseInventory();
            Game.Flow.OpenWindow(Game.UI.Cosmetics);
            yield return Wait(.5f);
            ReviewClick("Tab_WarriorSkin");
            yield return Wait(.3f);
            Game.UI.Hud.ClearToasts();
            yield return Shot("02-warrior-skin-shop");
            Game.UI.Pop();
            Game.Flow.OpenWindow(GachaScreen.Instance);
            yield return Wait(.6f);
            yield return Shot("03-skin-draw-banner");
            Game.UI.Pop();
            Game.Flow.OpenWindow(Game.UI.Skills);
            yield return Wait(.5f);
            ReviewClick("CareerA");
            yield return Wait(.2f);
            yield return Shot("04-fighter-card");
            ReviewClick("CareerB");
            yield return Wait(.2f);
            yield return Shot("05-guardian-card");
            Log("PASS: equipment, wardrobe, skin draw and both warrior career screens captured; no purchases or draws.");
        }

        static void ReviewClick(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            if (button == null) throw new Exception("Missing art review button: " + name);
            button.onClick.Invoke();
        }
    }
}
