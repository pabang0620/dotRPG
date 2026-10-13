using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [0038] 이름 변경 요청: an operator replaced an unsuitable character name with a temporary one
    /// (운영정책 제5조). Before entering the game the player picks a new name once (POST /characters/{id}/rename).
    /// </summary>
    public class OnlineRenameScreen : OnlineMenuScreen
    {
        InputField nameField;
        Text body;
        OnlineSession.CharacterSummary target;
        Action onRenamed;
        string requestId, requestFor;

        protected override bool AnyFieldFocused => nameField.isFocused;

        public static OnlineRenameScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "OnlineRename", true);
            var screen = root.gameObject.AddComponent<OnlineRenameScreen>();
            screen.ui = ui;
            // Five body lines: the policy line and the operator's note, then the name box and the status line.
            screen.BuildPanel(root, L.T("캐릭터 이름 변경"), 640, new string('\n', 5), 20);
            screen.body = screen.panel.Find("Body").GetComponent<Text>();
            screen.nameField = screen.Field(L.T("새 이름"), -190f, L.T("한글·영문·숫자 2~8자"), 8, false);
            screen.Status(-238f);
            screen.menu.AddButton(L.T("변경"), () => screen.Submit());
            screen.menu.AddButton(L.T("취소"), () => ui.Pop());
            screen.menu.OnCancel = () => ui.Pop();
            screen.FitPanel();
            return screen;
        }

        /// <summary>Shows the dialog for one character; <paramref name="renamed"/> runs after the list reloaded.</summary>
        public void Open(OnlineSession.CharacterSummary character, Action renamed)
        {
            target = character;
            onRenamed = renamed;
            ui.Push(this);
        }

        public override void Show()
        {
            base.Show();
            nameField.text = "";
            requestId = null;
            string note = target != null && !string.IsNullOrEmpty(target.renameNote)
                ? "\n" + L.F("<size=18><color=#b8c4d8>사유: {0}</color></size>", target.renameNote)
                : "";
            body.text = L.T("운영정책에 따라 캐릭터 이름을 바꿔야 합니다.") + note;
            Say(L.T("이름은 다른 모험가와 겹칠 수 없습니다."));
        }

        protected override void Update()
        {
            base.Update();
            if (AnyFieldFocused && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Submit();
        }

        void Submit()
        {
            if (busy || target == null || OnlineSession.Current == null) return;
            string name = nameField.text;
            if (name.Length < 2) { Say(L.T("이름은 2자 이상이어야 합니다."), true); return; }
            // Same request_id while retrying the same name (a lost answer must not count twice).
            if (requestId == null || requestFor != name) { requestId = ApiClient.NewRequestId(); requestFor = name; }
            busy = true;
            Say(L.T("바꾸는 중..."));
            OnlineSession.Current.RenameCharacter(target.id, name, requestId, r =>
            {
                busy = false;
                if (!r.ok)
                {
                    if (r.code != "NETWORK") requestId = null; // a refused name can be retried with a new id
                    Say(Explain(r), true);
                    Game.Audio.PlaySfx("cancel");
                    return;
                }
                Game.Audio.PlaySfx("confirm");
                var done = onRenamed;
                onRenamed = null;
                ui.Pop();
                done?.Invoke();
            });
        }
    }
}
