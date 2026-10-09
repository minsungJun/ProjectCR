using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectCR
{
    /// <summary>
    /// 테이블 태블릿(보유 금액, 목표 금액 = 납부금, 회전·서비스·전체 회수 버튼). CRT_Tablet 프리팹 루트에 붙는다.
    /// 숫자 글자는 TableChipView가 채우고, 여기서는 버튼을 누를 수 있는지와 회전 버튼의 남은 회전 수 "회전 (5/5)"를 상태에 맞춘다.
    /// 누를 수 있는지의 판단은 GameFlowController 규칙을 그대로 따른다(HUD 버튼과 같다).
    /// </summary>
    public class TabletPanel : MonoBehaviour
    {
        [SerializeField] private TextMesh balanceValue;
        [FormerlySerializedAs("betValue")]
        [SerializeField] private TextMesh dueValue;
        [SerializeField] private TabletButton[] buttons = new TabletButton[0];

        public TextMesh BalanceValue => balanceValue;
        public TextMesh DueValue => dueValue;

        /// <summary>로봇 선반에서 음료를 하나 골라 둔 상태. 이때 [회전] 버튼이 [마시기]가 된다(RouletteGame이 넣는다).</summary>
        public bool DrinkReady { get; set; }

        public void Configure(TextMesh balance, TextMesh due, TabletButton[] tabletButtons)
        {
            balanceValue = balance;
            dueValue = due;
            buttons = tabletButtons;
        }

        public void Refresh(GameFlowController flow)
        {
            foreach (TabletButton button in buttons)
            {
                if (button != null)
                {
                    bool drinking = button.Action == TabletAction.Spin && flow.Phase == GamePhase.DrinkSelection;
                    button.SetInteractable(drinking ? DrinkReady : CanPress(flow, button.Action));
                    if (button.Action == TabletAction.Spin && button.Label != null)
                    {
                        button.Label.text = drinking ? "마시기" : SpinLabel(flow);
                    }
                }
            }
        }

        /// <summary>그 동작의 버튼을 깜빡이게 하거나 원래대로 돌린다(튜토리얼 서비스 안내).</summary>
        public void SetHighlighted(TabletAction action, bool value)
        {
            foreach (TabletButton button in buttons)
            {
                if (button != null && button.Action == action)
                {
                    button.SetHighlighted(value);
                }
            }
        }

        /// <summary>회전 버튼 글자: 이번 라운드 남은 회전 / 라운드 기본 회전 수(회전권으로 기본보다 많아질 수 있다).</summary>
        public static string SpinLabel(GameFlowController flow) =>
            $"회전 ({flow.Round.SpinsRemaining}/{flow.GameBalance.SpinsPerRound})";

        public static bool CanPress(GameFlowController flow, TabletAction action)
        {
            switch (action)
            {
                case TabletAction.Spin:
                    return flow.ValidateBet() == BetError.None;
                case TabletAction.Service:
                    return flow.CanEnterShop;
                default:
                    return flow.Phase == GamePhase.Betting && flow.Bet.HasBets;
            }
        }
    }
}
