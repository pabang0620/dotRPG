using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 설정 > 줍기 설정: which dropped items are left on the ground (Pickup.Skipped). Its own screen so the settings
    /// list stays short enough for every screen height. Gold and rare-or-better gear are always picked up.
    /// </summary>
    public class LootFilterScreen : MenuScreen
    {
        UIRoot ui;

        public static LootFilterScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "LootFilter", true);
            var screen = root.gameObject.AddComponent<LootFilterScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "줍기 설정", 600, "안 줍기로 둔 아이템은 바닥에 흐리게 남습니다.\n골드와 레어 이상 장비는 항상 줍습니다.", 16);
            var s = Game.Settings;
            var menu = screen.menu;
            menu.AddOption("커먼 장비", () => s.Data.skipCommonGear ? "안 줍기" : "줍기", d => { s.Data.skipCommonGear = !s.Data.skipCommonGear; s.Apply(); });
            menu.AddOption("언커먼 장비", () => s.Data.skipUncommonGear ? "안 줍기" : "줍기", d => { s.Data.skipUncommonGear = !s.Data.skipUncommonGear; s.Apply(); });
            menu.AddOption("소비 아이템", () => s.Data.skipConsumables ? "안 줍기" : "줍기", d => { s.Data.skipConsumables = !s.Data.skipConsumables; s.Apply(); });
            menu.AddOption("재료", () => s.Data.skipMaterials ? "안 줍기" : "줍기", d => { s.Data.skipMaterials = !s.Data.skipMaterials; s.Apply(); });
            menu.AddButton("돌아가기", () => screen.Close());
            menu.OnCancel = screen.Close;
            screen.FitPanel();
            return screen;
        }

        void Close()
        {
            Game.Settings.Save();
            ui.Pop();
        }
    }
}
