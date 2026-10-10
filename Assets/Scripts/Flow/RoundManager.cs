namespace ProjectCR
{
    public enum PaymentOutcome
    {
        Paid,
        Insufficient,
    }

    /// <summary>
    /// 라운드 번호, 남은 회전 수, 납부금.
    /// 납부(<see cref="Pay"/>)와 다음 라운드(<see cref="AdvanceRound"/>)는 따로 부른다. 그 사이에 음료 선택(나중에는 상점)이 들어간다.
    /// 납부에 성공하면 납부금만 빠지고 남은 돈은 그대로 다음 라운드로 넘어간다.
    /// </summary>
    public sealed class RoundManager
    {
        private readonly GameBalance balance;

        public RoundManager(GameBalance balance)
        {
            this.balance = balance;
            Round = 1;
            SpinsRemaining = balance.SpinsPerRound;
        }

        /// <summary>1부터 센다.</summary>
        public int Round { get; private set; }

        public int SpinsRemaining { get; private set; }

        /// <summary>이번 라운드에서 베팅할 돈이 없어 버려진 회전 수. 안내 문구용.</summary>
        public int ForfeitedSpins { get; private set; }

        /// <summary>이번 라운드 납부금을 이미 냈다. 다음 라운드로 넘어가면 다시 false.</summary>
        public bool IsPaid { get; private set; }

        public bool HasSpins => SpinsRemaining > 0;

        public long CurrentDue => balance.GetDue(Round);

        /// <summary>이번 라운드에 시작한 회전 수(버린 회전은 세지 않는다). 상점은 0일 때만 열린다.</summary>
        public int SpinsTaken { get; private set; }

        public bool TryConsumeSpin()
        {
            if (SpinsRemaining <= 0)
            {
                return false;
            }

            SpinsRemaining--;
            SpinsTaken++;
            return true;
        }

        public void ForfeitRemainingSpins()
        {
            ForfeitedSpins += SpinsRemaining;
            SpinsRemaining = 0;
        }

        /// <summary>
        /// 이번 라운드 납부금을 낸다. 돈이 모자라면 아무것도 빼지 않고 Insufficient.
        /// 이미 냈으면 다시 빼지 않고 Paid(이중 차감 방지). 라운드는 넘기지 않는다.
        /// </summary>
        public PaymentOutcome Pay(Wallet wallet)
        {
            if (IsPaid)
            {
                return PaymentOutcome.Paid;
            }

            if (!wallet.TrySpend(CurrentDue))
            {
                return PaymentOutcome.Insufficient;
            }

            IsPaid = true;
            return PaymentOutcome.Paid;
        }

        /// <summary>여분의 회전권: 이번 라운드 남은 회전 +1. 납부했으면(라운드 종료 확정) false.</summary>
        public bool AddSpin()
        {
            if (IsPaid)
            {
                return false;
            }

            SpinsRemaining++;
            return true;
        }

        /// <summary>납부한 뒤 다음 라운드로 넘어가 회전 수를 다시 채운다. 납부 전이면 아무것도 하지 않고 false.</summary>
        public bool AdvanceRound()
        {
            if (!IsPaid)
            {
                return false;
            }

            Round++;
            SpinsRemaining = balance.SpinsPerRound;
            ForfeitedSpins = 0;
            SpinsTaken = 0;
            IsPaid = false;
            return true;
        }
    }
}
