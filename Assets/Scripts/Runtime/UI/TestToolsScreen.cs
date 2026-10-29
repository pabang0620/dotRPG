using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TEST TOOLS] Pause menu entry shown only on a test server (/meta test_tools): push the main quest or the level forward.
    /// The server refuses anything but a dev-login account on a test server, so the buttons are only shortcuts.
    /// </summary>
    public class TestToolsScreen : MenuScreen
    {
        UIRoot ui;
        bool busy;

        public static TestToolsScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "TestTools", true);
            var screen = root.gameObject.AddComponent<TestToolsScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("시험 도구"), 520, L.T("시험 서버 전용입니다. 진행이 서버에 바로 반영됩니다."), 18);
            var menu = screen.menu;
            menu.AddButton(L.T("메인 퀘스트 한 칸 진행"), () => screen.Run(done => OnlineEconomy.TestPushMainQuest(false, done)), () => !screen.busy);
            menu.AddButton(L.T("메인 퀘스트 챕터 끝까지"), () => ui.Confirm(L.T("열린 챕터의 메인 퀘스트를 모두 완료할까요?"),
                () => screen.Run(done => OnlineEconomy.TestPushMainQuest(true, done)), overlay: true), () => !screen.busy);
            menu.AddButton(L.T("레벨 한 칸 올리기"), () => screen.Run(done => OnlineEconomy.TestPushLevel(false, done)), () => !screen.busy);
            menu.AddButton(L.T("만렙까지 올리기"), () => ui.Confirm(L.T("레벨 상한까지 올릴까요?"),
                () => screen.Run(done => OnlineEconomy.TestPushLevel(true, done)), overlay: true), () => !screen.busy);
            menu.AddButton(L.T("돌아가기"), () => ui.Pop());
            menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        /// <summary>One request at a time: the buttons stay disabled until the server answers.</summary>
        void Run(System.Action<System.Action<bool>> request)
        {
            if (busy) return;
            busy = true;
            menu.Refresh();
            request(_ =>
            {
                busy = false;
                if (this != null && gameObject.activeInHierarchy) menu.Refresh();
            });
        }
    }
}
