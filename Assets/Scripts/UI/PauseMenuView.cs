using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 일시정지 메뉴(GameMenus.uxml의 pause-panel): [계속하기] [다시하기] [설정] [메인메뉴로] [종료]를 세로로.
    /// Esc로 열고 닫는다(옵션창이 열려 있으면 Esc는 옵션창만 닫는다). HUD의 [옵션] 버튼도 이 메뉴를 연다.
    /// 여는 동안 GamePause가 켜져 회전·로봇·카메라·튜토리얼 대사 출력이 멈추고, RouletteGame이 게임 입력을 막는다.
    /// 결과 창이 떠 있거나 씬을 옮기는 중이면 열지 않는다. 옵션창을 닫으면 일시정지 메뉴로 돌아온다(게임은 그대로 멈춰 있다).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenuView : MonoBehaviour
    {
        [SerializeField] private GameResultView resultView;

        private VisualElement panel;
        private OptionsView options;

        /// <summary>[다시하기]: 일시정지를 푼 뒤 부른다(RouletteGame.StartNewRun).</summary>
        public event Action RestartRequested;

        public bool IsOpen { get; private set; }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("pause-panel");
            root.Q<Button>("pause-resume").clicked += Close;
            root.Q<Button>("pause-restart").clicked += Restart;
            root.Q<Button>("pause-settings").clicked += OpenOptions;
            root.Q<Button>("pause-main-menu").clicked += SceneFlow.LoadMainMenu;
            root.Q<Button>("pause-quit").clicked += SceneFlow.Quit;

            options = new OptionsView(root);
            options.Closed += () =>
            {
                if (IsOpen)
                {
                    panel.style.display = DisplayStyle.Flex;
                }
            };

            panel.style.display = DisplayStyle.None;
        }

        private void OnDisable()
        {
            // 씬을 떠나거나 꺼질 때 멈춘 채로 남지 않게.
            if (IsOpen)
            {
                IsOpen = false;
                GamePause.Set(false);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (options != null && options.IsOpen)
            {
                options.Close();
            }
            else if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public bool CanOpen => !SceneFlow.IsLoading && (resultView == null || !resultView.IsShown);

        public void Open()
        {
            if (IsOpen || panel == null || !CanOpen)
            {
                return;
            }

            IsOpen = true;
            panel.style.display = DisplayStyle.Flex;
            GamePause.Set(true);
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            if (options != null && options.IsOpen)
            {
                options.Close();
            }

            IsOpen = false;
            panel.style.display = DisplayStyle.None;
            GamePause.Set(false);
        }

        private void Restart()
        {
            if (!IsOpen)
            {
                return;
            }

            Close();
            RestartRequested?.Invoke();
        }

        private void OpenOptions()
        {
            if (options == null || !options.IsValid)
            {
                return;
            }

            panel.style.display = DisplayStyle.None;
            options.Open();
        }
    }
}
