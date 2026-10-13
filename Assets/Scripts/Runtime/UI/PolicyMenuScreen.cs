using UnityEngine;

namespace DotRPG
{
    /// <summary>[POLICY] 설정 > 약관 및 정책: each document opens in the browser (pages on the game server, PolicyClient.Url).</summary>
    public class PolicyMenuScreen : MenuScreen
    {
        UIRoot ui;

        public static PolicyMenuScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "PolicyMenu", true);
            var screen = root.gameObject.AddComponent<PolicyMenuScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("약관 및 정책"), 600, L.T("문서는 브라우저에서 열립니다."), 16);
            var menu = screen.menu;
            menu.AddButton(L.T("이용약관"), () => PolicyClient.Open(PolicyClient.Terms));
            menu.AddButton(L.T("개인정보처리방침"), () => PolicyClient.Open(PolicyClient.Privacy));
            menu.AddButton(L.T("운영정책"), () => PolicyClient.Open(PolicyClient.Operation));
            menu.AddButton(L.T("유료 서비스·환불 정책"), () => PolicyClient.Open(PolicyClient.Paid));
            menu.AddButton(L.T("청소년 보호 정책"), () => PolicyClient.Open(PolicyClient.Youth));
            menu.AddButton(L.T("확률 정보"), () => PolicyClient.Open(PolicyClient.Probability));
            menu.AddButton(L.T("돌아가기"), () => ui.Pop());
            menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }
    }
}
