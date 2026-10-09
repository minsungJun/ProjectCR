using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 게임 클리어·게임 오버 공용 결과 창(GameMenus.uxml의 result-panel). 제목과 결과 문구만 상태에 맞게 바꾸고 버튼은 [메인 메뉴로] 하나.
    /// 떠 있는 동안 일반 게임 입력과 일시정지 메뉴가 막힌다(RouletteGame·PauseMenuView가 <see cref="IsShown"/>을 본다).
    /// 저장 기능이 없어서 끝난 게임을 이어하기에서 지울 저장도 없다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameResultView : MonoBehaviour
    {
        private VisualElement panel;
        private Label title;
        private Label body;

        public bool IsShown { get; private set; }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("result-panel");
            title = root.Q<Label>("result-title");
            body = root.Q<Label>("result-text");
            root.Q<Button>("result-main-menu").clicked += SceneFlow.LoadMainMenu;
            Hide();
        }

        public void Show(GameFlowController flow)
        {
            if (panel == null || flow == null)
            {
                return;
            }

            bool cleared = flow.Phase == GamePhase.GameClear;
            title.text = cleared ? "게임 클리어" : "게임 오버";
            title.EnableInClassList("result-title--clear", cleared);
            body.text = cleared
                ? $"죽음의 문턱을 넘었습니다.\n마지막 라운드 {flow.Round.Round}의 납부금 {flow.LastPaidDue:N0}까지 냈습니다.\n보유 {flow.Wallet.Balance:N0} · 마신 음료 {flow.Drinks.TotalStacks}잔"
                : $"라운드 {flow.Round.Round}의 납부금 {flow.Round.CurrentDue:N0}을 내지 못했습니다.\n"
                  + (flow.Round.ForfeitedSpins > 0 ? $"걸 돈이 없어 남은 회전 {flow.Round.ForfeitedSpins}회를 쓰지 못했습니다.\n" : string.Empty)
                  + $"보유 {flow.Wallet.Balance:N0} · 마신 음료 {flow.Drinks.TotalStacks}잔";
            panel.style.display = DisplayStyle.Flex;
            IsShown = true;
        }

        public void Hide()
        {
            IsShown = false;
            if (panel != null)
            {
                panel.style.display = DisplayStyle.None;
            }
        }
    }
}
