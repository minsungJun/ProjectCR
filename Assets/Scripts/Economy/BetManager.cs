namespace ProjectCR
{
    public enum BetError
    {
        None,
        NotInBettingPhase,
        NoTargetSelected,
        InvalidNumber,
        SkullNotBettable,
        InvalidTarget,
        NonPositive,
        BelowMinimum,
        InsufficientFunds,
    }

    public static class BetErrorMessages
    {
        public static string ToKorean(this BetError error)
        {
            switch (error)
            {
                case BetError.NotInBettingPhase:
                    return "지금은 베팅할 수 없습니다.";
                case BetError.NoTargetSelected:
                    return "번호나 바깥 베팅을 먼저 고르세요.";
                case BetError.InvalidNumber:
                    return "휠에 없는 번호입니다.";
                case BetError.SkullNotBettable:
                    return "해골에는 베팅할 수 없습니다.";
                case BetError.InvalidTarget:
                    return "번호판에서 걸 수 없는 조합입니다.";
                case BetError.NonPositive:
                    return "베팅 금액은 0보다 커야 합니다.";
                case BetError.BelowMinimum:
                    return "최소 베팅 금액보다 적습니다.";
                case BetError.InsufficientFunds:
                    return "보유 금액이 부족합니다.";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>회전 시작 시 확정되어 이미 차감된 베팅.</summary>
    public readonly struct ConfirmedBet
    {
        public ConfirmedBet(BetTarget target, long stake)
        {
            Target = target;
            Stake = stake;
        }

        /// <summary>숫자 하나 베팅. 테스트와 디버그에서 짧게 쓰려고 둔다.</summary>
        public ConfirmedBet(int number, long stake)
            : this(BetTarget.Straight(number), stake)
        {
        }

        public BetTarget Target { get; }
        public long Stake { get; }
    }

    /// <summary>
    /// 회전 하나에 거는 베팅(숫자 하나, 안쪽 베팅 하나, 또는 바깥 베팅 하나)의 선택·검증·확정.
    /// 확정하면 잠기고, 정산이 끝나 <see cref="Unlock"/>될 때까지 대상과 금액을 바꿀 수 없다.
    /// </summary>
    public sealed class BetManager
    {
        public const int NoSelection = -1;

        private readonly RouletteDefinition definition;
        private readonly long minBet;

        public BetManager(RouletteDefinition definition, long minBet)
        {
            this.definition = definition;
            this.minBet = minBet;
        }

        public BetTarget Selection { get; private set; } = BetTarget.None;

        /// <summary>숫자 베팅이면 그 번호, 아니면 <see cref="NoSelection"/>.</summary>
        public int SelectedNumber => Selection.IsStraight ? Selection.Number : NoSelection;

        /// <summary>입력된 금액. 검증 전 값이라 잘못된 값일 수 있다.</summary>
        public long Stake { get; private set; }

        public bool IsLocked { get; private set; }

        public BetError SelectNumber(int number) => SelectTarget(BetTarget.Straight(number));

        public BetError SelectTarget(BetTarget target)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            BetError error = ValidateTarget(target);
            if (error != BetError.None)
            {
                return error;
            }

            Selection = target;
            return BetError.None;
        }

        public BetError SetStake(long stake)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            Stake = stake;
            return BetError.None;
        }

        public BetError Validate(Wallet wallet)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            BetError targetError = ValidateTarget(Selection);
            if (targetError != BetError.None)
            {
                return targetError;
            }

            if (Stake <= 0)
            {
                return BetError.NonPositive;
            }

            if (Stake < minBet)
            {
                return BetError.BelowMinimum;
            }

            if (!wallet.CanAfford(Stake))
            {
                return BetError.InsufficientFunds;
            }

            return BetError.None;
        }

        /// <summary>검증하고, 통과하면 지갑에서 한 번 차감하고 잠근다.</summary>
        public BetError TryConfirm(Wallet wallet, out ConfirmedBet bet)
        {
            bet = default;

            BetError error = Validate(wallet);
            if (error != BetError.None)
            {
                return error;
            }

            if (!wallet.TrySpend(Stake))
            {
                return BetError.InsufficientFunds;
            }

            bet = new ConfirmedBet(Selection, Stake);
            IsLocked = true;
            return BetError.None;
        }

        private BetError ValidateTarget(BetTarget target)
        {
            switch (target.Kind)
            {
                case BetKind.None:
                    return BetError.NoTargetSelected;
                case BetKind.Straight:
                    if (target.Number == RouletteDefinition.SkullNumber)
                    {
                        return BetError.SkullNotBettable;
                    }

                    // 해골은 번호 조회에서 빠져 있어서 여기서도 걸러진다.
                    return definition.ContainsNumber(target.Number) ? BetError.None : BetError.InvalidNumber;
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    // 번호판에서 실제로 붙어 있는 조합만 받는다.
                    return InsideBets.IsValid(target) ? BetError.None : BetError.InvalidTarget;
                default:
                    return BetError.None;
            }
        }

        /// <summary>정산 후 호출. 다음 베팅을 위해 번호와 금액은 남겨 두고, 금액만 잔액에 맞춰 줄인다.</summary>
        public void Unlock(long balance)
        {
            IsLocked = false;
            if (Stake > balance)
            {
                Stake = balance;
            }
        }
    }
}
