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
    ///
    /// 번호판: 왼쪽 클릭 = 칩 하나 놓기(같은 곳이면 쌓임), 오른쪽 클릭 = 그 자리 베팅 빼기.
    /// 칩이 놓인 자리에는 금액 배지가 붙고, 마우스를 올리면 그 베팅에 포함되는 숫자 칸이 표시된다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RouletteHud : MonoBehaviour
    {
        private const int HistoryLength = 12;
        private const int BetListLength = 8;
        private const string SkullMark = "☠";

        // 번호판 칸 간격(px). RouletteHud.uss의 .cell 크기(37×44)와 margin(1)에 맞춘 값이다. 바꾸면 같이 바꿀 것.
        private const float ColumnPitch = 39f;
        private const float RowPitch = 46f;
        private const float ZeroCellPitch = 39f;
        private const float HotspotSize = 16f;

        public event Action<BetTarget> TargetClicked;
        public event Action<BetTarget> TargetRemoveRequested;
        public event Action UndoClicked;
        public event Action ClearClicked;
        public event Action<long> ChipAdjusted;
        public event Action ChipHalved;
        public event Action ChipDoubled;
        public event Action ChipMaxed;
        public event Action SpinClicked;
        public event Action PayClicked;
        public event Action RestartClicked;
        public event Action ViewToggled;

        private readonly Dictionary<BetTarget, Button> targetButtons = new Dictionary<BetTarget, Button>();
        private readonly Dictionary<BetTarget, Label> chipBadges = new Dictionary<BetTarget, Label>();
        private readonly Dictionary<int, Button> numberCells = new Dictionary<int, Button>();
        private readonly List<Button> betButtons = new List<Button>();
        private readonly List<SpinResult> history = new List<SpinResult>();

        private RouletteDefinition definition;
        private BetTarget hoverTarget = BetTarget.None;

        private Label roundLabel;
        private Label spinsLabel;
        private Label dueLabel;
        private Label moneyLabel;
        private Label seedLabel;
        private Label chipLabel;
        private Label betSummaryLabel;
        private Label betListLabel;
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
            this.definition = definition;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            roundLabel = root.Q<Label>("round");
            spinsLabel = root.Q<Label>("spins");
            dueLabel = root.Q<Label>("due");
            moneyLabel = root.Q<Label>("money");
            seedLabel = root.Q<Label>("seed");
            chipLabel = root.Q<Label>("chip-value");
            betSummaryLabel = root.Q<Label>("bet-summary");
            betListLabel = root.Q<Label>("bet-list");
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

            BindBetButton(root, "chip-minus10", () => ChipAdjusted?.Invoke(-10));
            BindBetButton(root, "chip-minus1", () => ChipAdjusted?.Invoke(-1));
            BindBetButton(root, "chip-plus1", () => ChipAdjusted?.Invoke(1));
            BindBetButton(root, "chip-plus10", () => ChipAdjusted?.Invoke(10));
            BindBetButton(root, "chip-half", () => ChipHalved?.Invoke());
            BindBetButton(root, "chip-double", () => ChipDoubled?.Invoke());
            BindBetButton(root, "chip-max", () => ChipMaxed?.Invoke());
            BindBetButton(root, "bet-undo", () => UndoClicked?.Invoke());
            BindBetButton(root, "bet-clear", () => ClearClicked?.Invoke());

            BuildBoard(root.Q("number-grid"));
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

            foreach (KeyValuePair<BetTarget, Button> pair in targetButtons)
            {
                long stake = bet.StakeOn(pair.Key);
                pair.Value.SetEnabled(betting);
                pair.Value.EnableInClassList("cell--bet", stake > 0);

                Label badge = chipBadges[pair.Key];
                badge.text = ShortAmount(stake);
                badge.style.display = stake > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (Button button in betButtons)
            {
                button.SetEnabled(betting);
            }

            // 미리보기와 정산이 같은 SpinConfig 배수를 쓴다.
            SpinConfig preview = flow.BuildSpinConfig();
            previewLabel.text = bet.HasBets ? DescribeBestCase(bet.Bets, preview, flow.Wallet.MaxMoney) : DescribeMultipliers(preview);

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
                    verdict = $"해골 낙첨 · 지급 0 (−{settlement.TotalStake:N0})";
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
                return "번호판을 누르면 칩이 놓입니다. 오른쪽 클릭은 빼기.";
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
                long payout = 0;
                foreach (ConfirmedBet bet in bets)
                {
                    if (bet.Target.CoversNumber(n, color))
                    {
                        payout += PayoutCalculator.ScaledPayout(bet.Stake, config.PayoutFor(bet.Target), maxMoney);
                    }
                }

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

        private static string DescribeMultipliers(SpinConfig preview)
        {
            string balls = preview.BallCount > 1 ? $" · 공 {preview.BallCount}개, 공마다 따로 지급" : string.Empty;
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

        /// <summary>칩 배지용 짧은 금액: 950, 1.2K, 3.4M, 5.6B.</summary>
        private static string ShortAmount(long value)
        {
            if (value < 1_000)
            {
                return value.ToString();
            }

            if (value < 1_000_000)
            {
                return $"{value / 1_000.0:0.#}K";
            }

            return value < 1_000_000_000 ? $"{value / 1_000_000.0:0.#}M" : $"{value / 1_000_000_000.0:0.#}B";
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

        // ---------- 번호판 ----------

        /// <summary>
        /// 테이블식 베팅판.
        /// 1행: 0(세로) | 12열 × 3행 번호(위 3·6·…·36, 아래 1·4·…·34) | 열 베팅(위 3열, 아래 1열)
        ///      번호 위에 안쪽 베팅 클릭 점(칸 경계·모서리·줄 아랫변, InsideBets 위치)
        /// 2행: 더즌 1–12 / 13–24 / 25–36
        /// 3행: 1–18 / 짝수 / 빨강 / 검정 / 홀수 / 19–36
        /// 해골 칸은 베팅할 수 없어 판에 없다.
        /// </summary>
        private void BuildBoard(VisualElement board)
        {
            board.Clear();
            targetButtons.Clear();
            chipBadges.Clear();
            numberCells.Clear();

            var numbersRow = Row(board);
            numbersRow.AddToClassList("board-row--numbers");

            Button zero = MakeNumberCell(0);
            zero.AddToClassList("cell--zero");
            numbersRow.Add(zero);

            var columns = new VisualElement();
            columns.AddToClassList("grid-columns");
            numbersRow.Add(columns);

            for (int column = 0; column < 12; column++)
            {
                var columnElement = new VisualElement();
                columnElement.AddToClassList("grid-column");
                columns.Add(columnElement);

                for (int row = 0; row < 3; row++)
                {
                    columnElement.Add(MakeNumberCell(column * 3 + (3 - row)));
                }
            }

            var columnBets = new VisualElement();
            columnBets.AddToClassList("grid-column");
            numbersRow.Add(columnBets);
            columnBets.Add(MakeOutsideCell(BetKind.Column3, "outside--column"));
            columnBets.Add(MakeOutsideCell(BetKind.Column2, "outside--column"));
            columnBets.Add(MakeOutsideCell(BetKind.Column1, "outside--column"));

            // 번호 칸 위에 겹쳐 놓는 안쪽 베팅 클릭 점. 마지막에 넣어 번호 칸보다 위에 그린다.
            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("inside-overlay");
            numbersRow.Add(overlay);

            foreach (InsideBetSpot spot in InsideBets.All)
            {
                // 누르는 영역은 16px로 두고, 보이는 건 가운데 작은 점만. 번호를 가리지 않게 한다.
                var hotspot = new Button();
                hotspot.AddToClassList("hotspot");
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("hotspot__dot");
                hotspot.Add(dot);
                hotspot.style.left = ZeroCellPitch + spot.X * ColumnPitch - HotspotSize * 0.5f;
                hotspot.style.top = spot.Y * RowPitch - HotspotSize * 0.5f;
                Bind(hotspot, spot.Target, "chip-badge--spot");
                overlay.Add(hotspot);
            }

            var dozensRow = Row(board);
            dozensRow.AddToClassList("board-row--indent");
            dozensRow.Add(MakeOutsideCell(BetKind.Dozen1, "outside--dozen"));
            dozensRow.Add(MakeOutsideCell(BetKind.Dozen2, "outside--dozen"));
            dozensRow.Add(MakeOutsideCell(BetKind.Dozen3, "outside--dozen"));

            var evenRow = Row(board);
            evenRow.AddToClassList("board-row--indent");
            evenRow.Add(MakeOutsideCell(BetKind.Low, "outside--even"));
            evenRow.Add(MakeOutsideCell(BetKind.Even, "outside--even"));
            evenRow.Add(MakeOutsideCell(BetKind.Red, "outside--even", "cell--red"));
            evenRow.Add(MakeOutsideCell(BetKind.Black, "outside--even", "cell--black"));
            evenRow.Add(MakeOutsideCell(BetKind.Odd, "outside--even"));
            evenRow.Add(MakeOutsideCell(BetKind.High, "outside--even"));
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("board-row");
            parent.Add(row);
            return row;
        }

        private Button MakeNumberCell(int number)
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
                Bind(button, BetTarget.Straight(number), "chip-badge--cell");
                numberCells[number] = button;
            }
            else
            {
                button.SetEnabled(false);
            }

            return button;
        }

        private Button MakeOutsideCell(BetKind kind, params string[] classes)
        {
            var button = new Button { text = BetTarget.OutsideLabel(kind) };
            button.AddToClassList("cell");
            button.AddToClassList("outside");
            foreach (string name in classes)
            {
                button.AddToClassList(name);
            }

            Bind(button, BetTarget.Outside(kind), "chip-badge--cell");
            return button;
        }

        /// <summary>왼쪽 클릭 = 칩 놓기, 오른쪽 클릭 = 빼기, 마우스 올림 = 포함 칸 표시. 칩 배지를 붙인다.</summary>
        private void Bind(Button button, BetTarget target, string badgeClass)
        {
            button.clicked += () => TargetClicked?.Invoke(target);

            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    TargetRemoveRequested?.Invoke(target);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            button.RegisterCallback<PointerEnterEvent>(_ => SetHover(target));
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (hoverTarget == target)
                {
                    SetHover(BetTarget.None);
                }
            });

            var badge = new Label { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("chip-badge");
            badge.AddToClassList(badgeClass);
            badge.style.display = DisplayStyle.None;
            button.Add(badge);

            targetButtons[target] = button;
            chipBadges[target] = badge;
        }

        private void SetHover(BetTarget target)
        {
            hoverTarget = target;
            foreach (KeyValuePair<int, Button> pair in numberCells)
            {
                bool covered = !target.IsNone && !target.IsStraight &&
                               target.CoversNumber(pair.Key, definition.ColorOfNumber(pair.Key));
                pair.Value.EnableInClassList("cell--covered", covered);
            }
        }
    }
}
