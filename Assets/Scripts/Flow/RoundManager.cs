namespace ProjectCR
{
    public enum PaymentOutcome
    {
        Paid,
        Insufficient,
    }

    /// <summary>
    /// 라운드 번호, 남은 회전 수, 납부금.
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

        public bool HasSpins => SpinsRemaining > 0;

        public long CurrentDue => balance.GetDue(Round);

        public bool TryConsumeSpin()
        {
            if (SpinsRemaining <= 0)
            {
                return false;
            }

            SpinsRemaining--;
            return true;
        }

        public void ForfeitRemainingSpins()
        {
            ForfeitedSpins += SpinsRemaining;
            SpinsRemaining = 0;
        }

        /// <summary>
        /// 납부금을 낸다. 돈이 모자라면 아무것도 빼지 않고 Insufficient.
        /// 성공하면 다음 라운드로 넘어가 회전 수를 다시 채운다.
        /// </summary>
        public PaymentOutcome SubmitPayment(Wallet wallet)
        {
            if (!wallet.TrySpend(CurrentDue))
            {
                return PaymentOutcome.Insufficient;
            }

            Round++;
            SpinsRemaining = balance.SpinsPerRound;
            ForfeitedSpins = 0;
            return PaymentOutcome.Paid;
        }
    }
}
