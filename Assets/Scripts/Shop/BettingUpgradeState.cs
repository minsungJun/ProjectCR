namespace ProjectCR
{
    /// <summary>
    /// 상점 집중 강화(이번 게임 동안). 음료 중첩과 따로 저장한다.
    /// - 지급: 묶음별 증가량을 더한다(+10%씩). 정산 때 (1 + 집중 + 교차)로 곱한다.
    /// - 확률: 대상별 효과(같은 대상이면 잔 수 +1). 가중치 계산에서 음료 확률 효과와 합친다.
    /// 납부 지원금(납부할 때마다 낸 납부금의 일부 환급)도 이번 게임 동안 이어지는 강화라 여기 둔다.
    /// </summary>
    public sealed class BettingUpgradeState
    {
        private readonly decimal[] payoutIncrease = new decimal[DrinkGroups.Count];
        private readonly int[] stacks = new int[DrinkGroups.Count];

        /// <summary>대상별 확률 효과(음료와 같은 구조, 따로 보관).</summary>
        public ProbabilityDrinkRunState Probabilities { get; } = new ProbabilityDrinkRunState();

        public decimal PayoutIncrease(DrinkGroup group) => payoutIncrease[(int)group];

        /// <summary>납부할 때 돌려받는 비율(산 납부 지원금의 합). 0.25 = 낸 납부금의 25%.</summary>
        public decimal PaymentRefund { get; private set; }

        /// <summary>산 납부 지원금 수.</summary>
        public int PaymentRefundStacks { get; private set; }

        /// <summary>특수 강화 추가 공: 이번 게임 동안 매 회전 공 +n(최대 공 수까지, GameFlowController.BuildSpinConfig).</summary>
        public int ExtraBalls { get; private set; }

        public void AddExtraBall() => ExtraBalls++;

        /// <summary>납부 지원금 한 개(상점 구매, 테스트). 환급 비율을 더한다.</summary>
        public void AddPaymentRefund(decimal ratio)
        {
            PaymentRefund += ratio;
            PaymentRefundStacks++;
        }

        /// <summary>그 묶음 집중 강화를 산 수(대상 무관).</summary>
        public int StacksOf(DrinkGroup group) => stacks[(int)group];

        public int TotalStacks
        {
            get
            {
                int total = 0;
                foreach (int count in stacks)
                {
                    total += count;
                }

                return total;
            }
        }

        /// <summary>집중 강화 한 개(상점 구매, 테스트·시뮬레이션). 지급 증가와 대상 확률 효과를 함께 더한다.</summary>
        public void AddFocus(DrinkGroup group, BetTarget target, decimal value)
        {
            payoutIncrease[(int)group] += value;
            stacks[(int)group]++;
            Probabilities.AddStack(target, value);
        }
    }
}
