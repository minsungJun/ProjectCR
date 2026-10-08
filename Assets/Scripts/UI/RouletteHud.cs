using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 룰렛 화면 UI. 상태를 읽어 그리고, 버튼 입력을 이벤트로 넘기기만 한다.
    /// 베팅 검증과 중복 방지는 GameFlowController가 한다. 여기서 버튼을 끄는 건 보기 좋으라고 하는 일이다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RouletteHud : MonoBehaviour
    {
        private const int HistoryLength = 12;

        public event Action<int> NumberClicked;
        public event Action<long> StakeAdjusted;
        public event Action StakeHalved;
        public event Action StakeDoubled;
        public event Action StakeMaxed;
        public event Action SpinClicked;
        public event Action PayClicked;
        public event Action RestartClicked;
        public event Action ViewToggled;

        private readonly Dictionary<int, Button> numberButtons = new Dictionary<int, Button>();
        private readonly List<Button> stakeButtons = new List<Button>();
        private readonly List<SpinResult> history = new List<SpinResult>();

        private Label roundLabel;
        private Label spinsLabel;
        private Label dueLabel;
        private Label moneyLabel;
        private Label seedLabel;
        private Label selectedLabel;
        private Label stakeLabel;
        private Label previewLabel;
        private Label errorLabel;
        private Label resultLabel;
        private Label historyLabel;
        private Label paymentLabel;
        private Label gameOverLabel;
        private Button spinButton;
        private Button payButton;
        private VisualElement paymentPanel;
        private VisualElement gameOverPanel;

        /// <summary>RouletteGame.Start에서 부른다. UIDocument가 루트를 만든 뒤여야 한다.</summary>
        public void Initialize(RouletteDefinition definition)
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            roundLabel = root.Q<Label>("round");
            spinsLabel = root.Q<Label>("spins");
            dueLabel = root.Q<Label>("due");
            moneyLabel = root.Q<Label>("money");
            seedLabel = root.Q<Label>("seed");
            selectedLabel = root.Q<Label>("selected");
            stakeLabel = root.Q<Label>("stake");
            previewLabel = root.Q<Label>("payout-preview");
            errorLabel = root.Q<Label>("bet-error");
            resultLabel = root.Q<Label>("result");
            historyLabel = root.Q<Label>("history");
            paymentLabel = root.Q<Label>("payment-text");
            gameOverLabel = root.Q<Label>("gameover-text");
            paymentPanel = root.Q("payment-panel");
            gameOverPanel = root.Q("gameover-panel");

            spinButton = root.Q<Button>("spin");
            payButton = root.Q<Button>("pay");
            spinButton.clicked += () => SpinClicked?.Invoke();
            payButton.clicked += () => PayClicked?.Invoke();
            root.Q<Button>("restart").clicked += () => RestartClicked?.Invoke();
            root.Q<Button>("view-toggle").clicked += () => ViewToggled?.Invoke();

            BindStake(root, "stake-minus10", () => StakeAdjusted?.Invoke(-10));
            BindStake(root, "stake-minus1", () => StakeAdjusted?.Invoke(-1));
            BindStake(root, "stake-plus1", () => StakeAdjusted?.Invoke(1));
            BindStake(root, "stake-plus10", () => StakeAdjusted?.Invoke(10));
            BindStake(root, "stake-half", () => StakeHalved?.Invoke());
            BindStake(root, "stake-double", () => StakeDoubled?.Invoke());
            BindStake(root, "stake-max", () => StakeMaxed?.Invoke());

            BuildNumberGrid(root.Q("number-grid"), definition);
        }

        public void Refresh(GameFlowController flow)
        {
            bool betting = flow.Phase == GamePhase.Betting;
            long balance = flow.Wallet.Balance;

            roundLabel.text = $"라운드 {flow.Round.Round}";
            spinsLabel.text = $"남은 회전 {flow.Round.SpinsRemaining}/{flow.GameBalance.SpinsPerRound}";
            dueLabel.text = $"납부금 {flow.Round.CurrentDue:N0}";
            moneyLabel.text = $"보유 {balance:N0}";
            seedLabel.text = $"seed {flow.Seed}";

            int selected = flow.Bet.SelectedNumber;
            selectedLabel.text = selected == BetManager.NoSelection
                ? "—"
                : $"{selected} ({flow.Definition.ColorOfNumber(selected).ToKorean()})";
            stakeLabel.text = flow.Bet.Stake.ToString("N0");

            foreach (KeyValuePair<int, Button> pair in numberButtons)
            {
                pair.Value.SetEnabled(betting);
                pair.Value.EnableInClassList("cell--selected", pair.Key == selected);
            }

            foreach (Button button in stakeButtons)
            {
                button.SetEnabled(betting);
            }

            SpinConfig preview = flow.BuildSpinConfig();
            long payout = PayoutCalculator.StraightPayout(flow.Bet.Stake, preview.PayoutMultiplier, flow.Wallet.MaxMoney);
            string ballNote = preview.BallCount > 1 ? $" · 공 {preview.BallCount}개, 맞힌 공마다 지급" : string.Empty;
            previewLabel.text = selected == BetManager.NoSelection || flow.Bet.Stake <= 0
                ? $"숫자 적중 시 원금 포함 {preview.PayoutMultiplier}배{ballNote}"
                : $"적중 시 지급 {payout:N0} ({preview.PayoutMultiplier}배, 원금 포함) · 순이익 +{payout - flow.Bet.Stake:N0}{ballNote}";

            BetError error = flow.ValidateBet();
            errorLabel.text = betting && error != BetError.None ? error.ToKorean() : string.Empty;
            spinButton.SetEnabled(betting && error == BetError.None);
            spinButton.text = flow.Phase == GamePhase.Spinning ? "회전 중…" : "회전";

            RefreshPayment(flow);
            RefreshGameOver(flow);
        }

        public void ShowSpinning()
        {
            resultLabel.text = "회전 중…";
        }

        public void ShowBetError(BetError error)
        {
            errorLabel.text = error.ToKorean();
        }

        public void ShowMessage(string message)
        {
            resultLabel.text = message;
        }

        public void ShowSettlement(SettlementResult settlement)
        {
            var numbers = new StringBuilder();
            foreach (BallPayout ball in settlement.Balls)
            {
                if (numbers.Length > 0)
                {
                    numbers.Append(" · ");
                }

                numbers.Append($"{ball.Ball.Number} ({ball.Ball.Color.ToKorean()})");
            }

            int wins = settlement.WinCount;
            string verdict;
            if (wins == 0)
            {
                verdict = $"낙첨 −{settlement.Bet.Stake:N0}";
            }
            else
            {
                string count = settlement.Balls.Count > 1 ? $"공 {wins}개 " : string.Empty;
                verdict = $"{count}적중! 지급 {settlement.TotalPayout:N0} (순이익 +{settlement.Net:N0})";
            }

            resultLabel.text = $"결과 {numbers} — {verdict}";

            history.Insert(0, settlement.Spin);
            if (history.Count > HistoryLength)
            {
                history.RemoveAt(history.Count - 1);
            }

            RefreshHistory();
        }

        public void ClearHistory()
        {
            history.Clear();
            RefreshHistory();
        }

        private void RefreshPayment(GameFlowController flow)
        {
            bool visible = flow.Phase == GamePhase.RoundPayment;
            paymentPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            long due = flow.Round.CurrentDue;
            long balance = flow.Wallet.Balance;
            var text = new StringBuilder();
            text.AppendLine($"라운드 {flow.Round.Round} 종료");
            text.AppendLine($"납부금 {due:N0} / 보유 {balance:N0}");

            if (flow.Round.ForfeitedSpins > 0)
            {
                text.AppendLine($"베팅할 돈이 없어 남은 회전 {flow.Round.ForfeitedSpins}회가 소멸했습니다.");
            }

            text.Append(balance >= due
                ? $"납부 후 {balance - due:N0}을 다음 라운드로 가져갑니다."
                : "납부금이 부족합니다. 납부하면 게임이 끝납니다.");

            paymentLabel.text = text.ToString();
            payButton.text = balance >= due ? "납부" : "납부 (부족)";
        }

        private void RefreshGameOver(GameFlowController flow)
        {
            bool visible = flow.Phase == GamePhase.GameOver;
            gameOverPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
            {
                gameOverLabel.text =
                    $"게임 오버\n라운드 {flow.Round.Round}의 납부금 {flow.Round.CurrentDue:N0}을 내지 못했습니다.\n보유 {flow.Wallet.Balance:N0}";
            }
        }

        private void RefreshHistory()
        {
            var text = new StringBuilder("최근 ");
            if (history.Count == 0)
            {
                text.Append("—");
            }

            foreach (SpinResult spin in history)
            {
                foreach (BallResult ball in spin.Balls)
                {
                    text.Append(ball.Number).Append(' ');
                }

                text.Append("| ");
            }

            historyLabel.text = text.ToString().TrimEnd(' ', '|');
        }

        private void BindStake(VisualElement root, string name, Action onClick)
        {
            Button button = root.Q<Button>(name);
            button.clicked += onClick;
            stakeButtons.Add(button);
        }

        /// <summary>테이블식 배치: 0이 왼쪽 세로 칸, 그 오른쪽에 12열 × 3행(위 3·6·…·36, 아래 1·4·…·34).</summary>
        private void BuildNumberGrid(VisualElement grid, RouletteDefinition definition)
        {
            grid.Clear();
            numberButtons.Clear();

            Button zero = MakeCell(0, definition);
            zero.AddToClassList("cell--zero");
            grid.Add(zero);

            var columns = new VisualElement();
            columns.AddToClassList("grid-columns");
            grid.Add(columns);

            for (int column = 0; column < 12; column++)
            {
                var columnElement = new VisualElement();
                columnElement.AddToClassList("grid-column");
                columns.Add(columnElement);

                for (int row = 0; row < 3; row++)
                {
                    columnElement.Add(MakeCell(column * 3 + (3 - row), definition));
                }
            }
        }

        private Button MakeCell(int number, RouletteDefinition definition)
        {
            var button = new Button { text = number.ToString() };
            button.AddToClassList("cell");

            switch (definition.ColorOfNumber(number))
            {
                case PocketColor.Red:
                    button.AddToClassList("cell--red");
                    break;
                case PocketColor.Black:
                    button.AddToClassList("cell--black");
                    break;
                default:
                    button.AddToClassList("cell--green");
                    break;
            }

            if (definition.ContainsNumber(number))
            {
                button.clicked += () => NumberClicked?.Invoke(number);
                numberButtons[number] = button;
            }
            else
            {
                button.SetEnabled(false);
            }

            return button;
        }
    }
}
