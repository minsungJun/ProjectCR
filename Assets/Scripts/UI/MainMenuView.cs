using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 메인 메뉴(MainMenu 씬, MainMenu.uxml): 게임 제목 아래 [새 게임] [이어하기] [설정] [종료]를 세로로.
    /// - 새 게임: Roulette 씬을 새로 연다(새 게임 상태로 시작). 튜토리얼 기록은 지우지 않는다.
    /// - 이어하기: 저장·복원 기능이 아직 없어서 늘 비활성. 저장 기능이 생기면 <see cref="HasContinue"/>만 바꾼다(새 게임으로 대신하지 않는다).
    /// - 설정: 공용 옵션창. 닫으면 메인 메뉴로. Esc도 옵션창을 닫는다.
    /// - 종료: 게임 종료.
    /// 버튼을 연타해도 씬 이동·종료는 한 번만(SceneFlow).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuView : MonoBehaviour
    {
        [Tooltip("메인 메뉴 제목(게임 이름).")]
        [SerializeField] private string gameTitle = "ProjectCR";

        private VisualElement menu;
        private OptionsView options;

        /// <summary>이어할 저장이 있는가. 저장 기능이 없어서 지금은 늘 false.</summary>
        public static bool HasContinue => false;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            menu = root.Q("main-menu");
            root.Q<Label>("main-title").text = gameTitle;

            Button continueButton = root.Q<Button>("main-continue");
            continueButton.SetEnabled(HasContinue);

            root.Q<Button>("main-new-game").clicked += SceneFlow.LoadGame;
            continueButton.clicked += () =>
            {
                // 저장 기능이 생기면 여기서 복원 뒤 게임 씬으로. 지금은 버튼이 꺼져 있어 오지 않는다.
            };
            root.Q<Button>("main-settings").clicked += OpenOptions;
            root.Q<Button>("main-quit").clicked += SceneFlow.Quit;

            options = new OptionsView(root);
            options.Closed += () => menu.style.display = DisplayStyle.Flex;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && options != null && options.IsOpen)
            {
                options.Close();
            }
        }

        private void OpenOptions()
        {
            if (options == null || !options.IsValid)
            {
                return;
            }

            menu.style.display = DisplayStyle.None;
            options.Open();
        }
    }
}
