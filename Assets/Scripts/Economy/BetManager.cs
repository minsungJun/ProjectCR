namespace ProjectCR
{
    public enum BetError
    {
        None,
        NotInBettingPhase,
        NoNumberSelected,
        InvalidNumber,
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
                case BetError.NoNumberSelected:
                    return "번호를 먼저 고르세요.";
                case BetError.InvalidNumber:
                    return "휠에 없는 번호입니다.";
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
        public ConfirmedBet(int number, long stake)
        {
            Number = number;
            Stake = stake;
        }

        public int Number { get; }
        public long Stake { get; }
    }

    /// <summary>
    /// 숫자 하나 베팅의 선택·검증·확정.
    /// 확정하면 잠기고, 정산이 끝나 <see cref="Unlock"/>될 때까지 번호와 금액을 바꿀 수 없다.
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

        public int SelectedNumber { get; private set; } = NoSelection;

        /// <summary>입력된 금액. 검증 전 값이라 잘못된 값일 수 있다.</summary>
        public long Stake { get; private set; }

        public bool IsLocked { get; private set; }

        public BetError SelectNumber(int number)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (!definition.ContainsNumber(number))
            {
                return BetError.InvalidNumber;
            }

            SelectedNumber = number;
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

            if (SelectedNumber == NoSelection)
            {
                return BetError.NoNumberSelected;
            }

            if (!definition.ContainsNumber(SelectedNumber))
            {
                return BetError.InvalidNumber;
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

            bet = new ConfirmedBet(SelectedNumber, Stake);
            IsLocked = true;
            return BetError.None;
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
