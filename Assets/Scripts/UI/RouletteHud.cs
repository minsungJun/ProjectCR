using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 룰렛 화면 UI(상태 바, 베팅 요약·회전 패널, 결과, 게임 오버 창). 음료 선택 창은 DrinkSelectionView가 그린다.
    /// 납부는 버튼 없이 라운드가 끝나면 자동으로 판정한다(RouletteGame). 상태를 읽어 그리고, 버튼 입력을 이벤트로 넘기기만 한다.
    /// 베팅 검증과 중복 방지는 GameFlowController가 한다. 여기서 버튼을 끄는 건 보기 좋으라고 하는 일이다.
    ///
    /// 칩을 놓는 건 3D 테이블에서 한다(TableBetInput). 3D 입력이 HUD 위 클릭을 무시하도록
    /// <see cref="IsPointerOverUi"/>를 제공한다. 입력을 막는 요소에는 USS 클래스 blocks-input을 붙인다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RouletteHud : MonoBehaviour
    {
        private const int HistoryLength = 12;
        private const int BetListLength = 6;
        private const string SkullMark = "☠";
        private const string BlocksInputClass = "blocks-input";

        public event Action UndoClicked;
        public event Action ClearClicked;
        public event Action SpinClicked;
        public event Action RestartClicked;
        public event Action ViewToggled;

        private readonly List<Button> betButtons = new List<Button>();
        private readonly List<SpinResult> history = new List<SpinResult>();

        private RouletteDefinition definition;
        private UIDocument document;

        private Label roundLabel;
        private Label spinsLabel;
        private Label dueLabel;
        private Label moneyLabel;
        private Label seedLabel;
        private Label chipLabel;
        private Label hoverLabel;
        private Label betSummaryLabel;
        private Label betListLabel;
        private Label previewLabel;
        private Label errorLabel;
        private Label resultLabel;
        private Label historyLabel;
        private Label gameOverLabel;
        private Button spinButton;
        private VisualElement gameOverPanel;

        /// <summary>RouletteGame.Start에서 부른다. UIDocument가 루트를 만든 뒤여야 한다.</summary>
        public void Initialize(RouletteDefinition definition)
        {
            this.definition = definition;
            document = GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;

            roundLabel = root.Q<Label>("round");
            spinsLabel = root.Q<Label>("spins");
            dueLabel = root.Q<Label>("due");
            moneyLabel = root.Q<Label>("money");
            seedLabel = root.Q<Label>("seed");
            chipLabel = root.Q<Label>("chip-value");
            hoverLabel = root.Q<Label>("hover-target");
            betSummaryLabel = root.Q<Label>("bet-summary");
            betListLabel = root.Q<Label>("bet-list");
            previewLabel = root.Q<Label>("payout-preview");
            errorLabel = root.Q<Label>("bet-error");
            resultLabel = root.Q<Label>("result");
            historyLabel = root.Q<Label>("history");
            gameOverLabel = root.Q<Label>("gameover-text");
            gameOverPanel = root.Q("gameover-panel");

            spinButton = root.Q<Button>("spin");
            spinButton.clicked += () => SpinClicked?.Invoke();
            root.Q<Button>("restart").clicked += () => RestartClicked?.Invoke();
            root.Q<Button>("view-toggle").clicked += () => ViewToggled?.Invoke();

            BindBetButton(root, "bet-undo", () => UndoClicked?.Invoke());
            BindBetButton(root, "bet-clear", () => ClearClicked?.Invoke());

            ShowHover(BetTarget.None, null);
        }

        /// <summary>화면 좌표(Input System, 왼쪽 아래 원점)가 입력을 막는 HUD 요소 위인가.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (document == null || document.rootVisualElement?.panel == null)
            {
                return false;
            }

            IPanel panel = document.rootVisualElement.panel;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            for (VisualElement element = panel.Pick(panelPosition); element != null; element = element.parent)
            {
                if (element.ClassListContains(BlocksInputClass))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(GameFlowController flow)
        {
            bool betting = flow.Phase == GamePhase.Betting;
            long balance = flow.Wallet.Balance;
            BetManager bet = flow.Bet;

            roundLabel.text = $"라운드 {flow.Round.Round}";
            spinsLabel.text = $"남은 회전 {flow.Round.SpinsRemaining}/{flow.GameBalance.SpinsPerRound}";
            dueLabel.text = $"납부금 {flow.Round.CurrentDue:N0}";
            moneyLabel.text = $"보유 {balance:N0}";
            seedLabel.text = $"seed {flow.Seed}";

            chipLabel.text = bet.ChipValue.ToString("N0");
            betSummaryLabel.text = bet.HasBets
                ? $"{bet.Count}개 · 합계 {bet.TotalStake:N0} · 남는 돈 {balance - bet.TotalStake:N0}"
                : "없음";
            betListLabel.text = DescribeBets(bet.Bets);

            foreach (Button button in betButtons)
            {
                button.SetEnabled(betting);
            }

            // 미리보기와 정산이 같은 SpinConfig 배수(음료 포함)를 쓴다.
            SpinConfig preview = flow.BuildSpinConfig();
            previewLabel.text = bet.HasBets
                ? DescribeBestCase(bet.Bets, preview, flow.Wallet.MaxMoney)
                : DescribeMultipliers(preview, flow.Drinks.TotalStacks);

            BetError error = flow.ValidateBet();
            errorLabel.text = betting && error != BetError.None ? error.ToKorean() : string.Empty;
            spinButton.SetEnabled(betting && error == BetError.None);
            spinButton.text = SpinButtonText(flow.Phase);

            RefreshGameOver(flow);
        }

        /// <summary>테이블에서 마우스가 가리키는 베팅 자리와 그 배수.</summary>
        public void ShowHover(BetTarget target, GameFlowController flow)
        {
            if (target.IsNone || flow == null)
            {
                hoverLabel.text = "판을 누르면 칩을 놓고, 오른쪽 클릭은 뺍니다. 트레이 칩을 누르면 칩 금액이 바뀝니다.";
                return;
            }

            // 기본 배수와 음료 적용 배수를 함께 보여 준다. 숫자는 표시용 반올림.
            SpinConfig config = flow.BuildSpinConfig();
            int basePayout = config.PayoutFor(target);
            decimal final = config.MultiplierFor(target);
            DrinkGroup group = DrinkGroups.Of(target.Kind);
            string payout = final > basePayout
                ? $"{basePayout}배 → {NumberText.Multiplier(final)}배 ({group.ToKorean()} 음료 ×{NumberText.Multiplier(config.GroupBoost(group))})"
                : $"{basePayout}배";

            // 공 하나 기준 실제 등장 확률(확률 음료 반영, 해골 1/38 고정). 추첨과 같은 식.
            string chance = $" · 등장 {NumberText.Percent(config.ProbabilityOf(target))}";

            long stake = flow.Bet.StakeOn(target);
            string placed = stake > 0 ? $" · 놓인 칩 {stake:N0}" : string.Empty;
            hoverLabel.text = $"{target.ToKorean(definition)} · {payout}{chance}{placed}";
        }

        public void ShowSpinning()
        {
            resultLabel.text = "회전 중…";
            resultLabel.RemoveFromClassList("result--skull");
        }

        public void ShowBetError(BetError error)
        {
            errorLabel.text = error.ToKorean();
        }

        public void ShowMessage(string message)
        {
            resultLabel.text = message;
            resultLabel.RemoveFromClassList("result--skull");
        }

        public void ShowSettlement(SettlementResult settlement)
        {
            var numbers = new StringBuilder();
            foreach (BallResult ball in settlement.Spin.Balls)
            {
                if (numbers.Length > 0)
                {
                    numbers.Append(" · ");
                }

                numbers.Append(ball.IsSkull ? $"{SkullMark} 해골" : $"{ball.Number} ({ball.Color.ToKorean()})");
            }

            string verdict;
            switch (settlement.Outcome)
            {
                case SpinOutcome.SkullBust:
                    // 맞힌 베팅이 있어도 해골이 하나라도 있으면 전부 낙첨. 이유를 일반 낙첨과 구분해 보여 준다.
                    // 베팅 전부 낙첨 + 남은 보유 금액 절반.
                    verdict = $"해골 낙첨 · 지급 0 (−{settlement.TotalStake:N0}) · 보유 절반 −{settlement.SkullPenalty:N0}";
                    break;
                case SpinOutcome.Win:
                    verdict = $"{DescribeWinners(settlement)} 적중! 지급 {settlement.TotalPayout:N0} (손익 {Signed(settlement.Net)})";
                    break;
                default:
                    verdict = $"모두 낙첨 · 지급 0 (−{settlement.TotalStake:N0})";
                    break;
            }

            resultLabel.text = $"결과 {numbers} — {verdict}";
            resultLabel.EnableInClassList("result--skull", settlement.IsSkullBust);

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

        // ---------- 문구 ----------

        private string DescribeBets(IReadOnlyList<ConfirmedBet> bets)
        {
            if (bets.Count == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            for (int i = 0; i < bets.Count && i < BetListLength; i++)
            {
                if (i > 0)
                {
                    text.Append(" · ");
                }

                text.Append($"{bets[i].Target.ToKorean(definition)} {bets[i].Stake:N0}");
            }

            if (bets.Count > BetListLength)
            {
                text.Append($" 외 {bets.Count - BetListLength}개");
            }

            return text.ToString();
        }

        /// <summary>공 하나가 들어갔을 때 지급이 가장 큰 숫자와 그 금액.</summary>
        private string DescribeBestCase(IReadOnlyList<ConfirmedBet> bets, SpinConfig config, long maxMoney)
        {
            long best = 0;
            int bestNumber = -1;

            for (int n = 0; n <= 36; n++)
            {
                if (!definition.ContainsNumber(n))
                {
                    continue;
                }

                PocketColor color = definition.ColorOfNumber(n);
                decimal exact = 0m;
                foreach (ConfirmedBet bet in bets)
                {
                    if (bet.Target.CoversNumber(n, color))
                    {
                        exact = Math.Min(exact + PayoutCalculator.ExactPayout(bet.Stake, config.MultiplierFor(bet.Target), maxMoney), maxMoney);
                    }
                }

                // 정산과 같이 합계를 한 번만 버린다.
                long payout = PayoutCalculator.ToMoney(exact, maxMoney);

                if (payout > best)
                {
                    best = payout;
                    bestNumber = n;
                }
            }

            string balls = config.BallCount > 1 ? $" · 공 {config.BallCount}개, 공마다 따로 지급" : string.Empty;
            return bestNumber < 0
                ? $"지급 가능한 칸이 없습니다{balls}"
                : $"공 하나 기준 최고 지급 {best:N0} ({bestNumber}이 나오면){balls}";
        }

        private static string DescribeMultipliers(SpinConfig preview, int drinkStacks)
        {
            string balls = preview.BallCount > 1 ? $" · 공 {preview.BallCount}개, 공마다 따로 지급" : string.Empty;
            if (drinkStacks > 0)
            {
                balls += $" · 음료 {drinkStacks}잔 적용 중(자리에 마우스를 올리면 강화 배수)";
            }

            return $"원금 포함 배수: 숫자 {preview.PayoutOf(PayoutClass.Straight)} · 스플릿 {preview.PayoutOf(PayoutClass.Split)} · " +
                   $"스트리트 {preview.PayoutOf(PayoutClass.Street)} · 코너 {preview.PayoutOf(PayoutClass.Corner)} · " +
                   $"식스 라인 {preview.PayoutOf(PayoutClass.SixLine)} · 더즌·열 {preview.PayoutOf(PayoutClass.DozenColumn)} · " +
                   $"색·홀짝·1–18/19–36 {preview.PayoutOf(PayoutClass.EvenMoney)}{balls}";
        }

        private string DescribeWinners(SettlementResult settlement)
        {
            var winners = new List<string>();
            foreach (BetPayout bet in settlement.Bets)
            {
                if (bet.IsWin)
                {
                    string times = bet.Hits > 1 ? $" ×{bet.Hits}" : string.Empty;
                    winners.Add(bet.Bet.Target.ToKorean(definition) + times);
                }
            }

            return winners.Count > 3
                ? $"{string.Join(", ", winners.GetRange(0, 3))} 외 {winners.Count - 3}개"
                : string.Join(", ", winners);
        }

        private static string Signed(long value) => value >= 0 ? $"+{value:N0}" : $"−{-value:N0}";

        private static string SpinButtonText(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Spinning:
                    return "회전 중…";
                case GamePhase.RoundPayment:
                    return "납부 판정 중…";
                case GamePhase.DrinkSelection:
                    return "음료 선택 중";
                default:
                    return "회전";
            }
        }

        private void RefreshGameOver(GameFlowController flow)
        {
            bool visible = flow.Phase == GamePhase.GameOver;
            gameOverPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            var text = new StringBuilder("게임 오버\n");
            text.AppendLine($"라운드 {flow.Round.Round}의 납부금 {flow.Round.CurrentDue:N0}을 내지 못했습니다. 보유 {flow.Wallet.Balance:N0}");
            if (flow.Round.ForfeitedSpins > 0)
            {
                text.AppendLine($"걸 돈이 없어 남은 회전 {flow.Round.ForfeitedSpins}회를 쓰지 못했습니다.");
            }

            text.Append($"마신 음료 {flow.Drinks.TotalStacks}잔");
            gameOverLabel.text = text.ToString();
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
                    text.Append(ball.IsSkull ? SkullMark : ball.Number.ToString()).Append(' ');
                }

                text.Append("| ");
            }

            historyLabel.text = text.ToString().TrimEnd(' ', '|');
        }

        private void BindBetButton(VisualElement root, string name, Action onClick)
        {
            Button button = root.Q<Button>(name);
            button.clicked += onClick;
            betButtons.Add(button);
        }
    }
}
